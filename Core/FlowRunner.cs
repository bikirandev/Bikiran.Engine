using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bikiran.Engine.Core;

/// <summary>
/// Executes a flow's nodes in sequence, handling logging, timeouts, and failure strategies.
/// </summary>
internal class FlowRunner
{
    private readonly EngineDbContext? _db;

    internal FlowRunner(EngineDbContext? db)
    {
        _db = db;
    }

    /// <summary>
    /// Runs all nodes in sequence with timeout protection, per-node logging, and failure handling.
    /// After main nodes complete, executes lifecycle event nodes (OnSuccess/OnFail/OnFinish).
    /// </summary>
    internal async Task RunAsync(
        FlowContext context,
        IReadOnlyList<IFlowNode> nodes,
        IReadOnlyList<IFlowNode> onSuccessNodes,
        IReadOnlyList<IFlowNode> onFailNodes,
        IReadOnlyList<IFlowNode> onFinishNodes,
        CancellationToken externalToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        cts.CancelAfter(context.Config.MaxExecutionTime);
        var ct = cts.Token;

        var runStartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await UpdateRunStatusAsync(context.ServiceId, FlowRunStatus.Running,
            startedAt: runStartedAtMs / 1000);

        var completedNodes = 0;
        string? flowError = null;

        try
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var node = nodes[i];
                var sequence = i + 1;

                var nodeStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (context.Config.EnableNodeLogging)
                    await CreateNodeLogAsync(context.ServiceId, node, sequence, FlowNodeStatus.Running, nodeStartedAt);

                NodeResult result;
                string? branchTaken = null;
                int retryCount = 0;

                try
                {
                    result = await node.ExecuteAsync(context, ct);

                    // Some nodes (IfElseNode) return branch info via context
                    if (context.Has($"{node.Name}_branch_taken"))
                        branchTaken = context.Get<string>($"{node.Name}_branch_taken");

                    if (context.Has($"{node.Name}_retry_count"))
                        retryCount = context.Get<int>($"{node.Name}_retry_count");
                }
                catch (OperationCanceledException)
                {
                    result = NodeResult.Fail("max_execution_time_exceeded");
                }
                catch (Exception ex)
                {
                    result = NodeResult.Fail(ex.Message);
                    context.Logger?.LogError(ex, "Unhandled exception in node {NodeName}", node.Name);
                }

                var nodeCompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var durationMs = nodeCompletedAt - nodeStartedAt;

                if (context.Config.EnableNodeLogging)
                {
                    await UpdateNodeLogAsync(context.ServiceId, node.Name, sequence,
                        result.Success ? FlowNodeStatus.Completed : FlowNodeStatus.Failed,
                        result.ErrorMessage,
                        branchTaken,
                        retryCount,
                        nodeCompletedAt / 1000,
                        durationMs);
                }

                if (result.Success)
                {
                    completedNodes++;
                    await UpdateRunProgressAsync(context.ServiceId, completedNodes);
                }
                else
                {
                    if (context.Config.OnFailure == OnFailureAction.Stop)
                    {
                        flowError = result.ErrorMessage;
                        break;
                    }
                    // OnFailureAction.Continue: log the failure but keep going
                    completedNodes++;
                    await UpdateRunProgressAsync(context.ServiceId, completedNodes);
                }
            }
        }
        catch (OperationCanceledException)
        {
            flowError = "max_execution_time_exceeded";
        }
        catch (Exception ex)
        {
            flowError = ex.Message;
            context.Logger?.LogError(ex, "Unhandled exception in flow {FlowName}", context.FlowName);
        }

        var runCompletedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var status = flowError == null ? FlowRunStatus.Completed : FlowRunStatus.Failed;

        // Expose flow outcome to lifecycle event nodes
        context.FlowStatus = status;
        context.FlowError = flowError;

        // Persist final flow status BEFORE running lifecycle event nodes,
        // so OnSuccess/OnFail/OnFinish nodes see the committed status.
        // Lifecycle nodes must not affect completedNodes, durationMs, completedAt, or errorMessage.
        await UpdateRunStatusAsync(context.ServiceId, status,
            completedAt: runCompletedAtMs / 1000,
            durationMs: runCompletedAtMs - runStartedAtMs,
            errorMessage: flowError);

        // Execute lifecycle event nodes (after status is committed)
        var lifecycleSequenceStart = nodes.Count + 1;

        if (flowError == null && onSuccessNodes.Count > 0)
        {
            lifecycleSequenceStart = await ExecuteLifecycleNodesAsync(
                context, onSuccessNodes, "OnSuccess", lifecycleSequenceStart, ct);
        }
        else if (flowError != null && onFailNodes.Count > 0)
        {
            lifecycleSequenceStart = await ExecuteLifecycleNodesAsync(
                context, onFailNodes, "OnFail", lifecycleSequenceStart, ct);
        }

        if (onFinishNodes.Count > 0)
        {
            await ExecuteLifecycleNodesAsync(
                context, onFinishNodes, "OnFinish", lifecycleSequenceStart, ct);
        }
    }

    // --- Lifecycle event helpers ---

    /// <summary>
    /// Executes lifecycle event nodes sequentially. Failures are logged but do not change flow status.
    /// Returns the next available sequence number.
    /// </summary>
    private async Task<int> ExecuteLifecycleNodesAsync(
        FlowContext context,
        IReadOnlyList<IFlowNode> nodes,
        string phase,
        int sequenceStart,
        CancellationToken ct)
    {
        var sequence = sequenceStart;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var nodeStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (context.Config.EnableNodeLogging)
                await CreateNodeLogAsync(context.ServiceId, node, sequence, FlowNodeStatus.Running, nodeStartedAt);

            NodeResult result;
            try
            {
                result = await node.ExecuteAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                result = NodeResult.Fail("max_execution_time_exceeded");
            }
            catch (Exception ex)
            {
                result = NodeResult.Fail(ex.Message);
                context.Logger?.LogError(ex, "{Phase} node '{NodeName}' failed", phase, node.Name);
            }

            var nodeCompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var durationMs = nodeCompletedAt - nodeStartedAt;

            if (context.Config.EnableNodeLogging)
            {
                await UpdateNodeLogAsync(context.ServiceId, node.Name, sequence,
                    result.Success ? FlowNodeStatus.Completed : FlowNodeStatus.Failed,
                    result.ErrorMessage,
                    branchTaken: null,
                    retryCount: 0,
                    nodeCompletedAt / 1000,
                    durationMs);
            }

            sequence++;
        }

        return sequence;
    }

    // --- Database helpers ---

    private async Task UpdateRunStatusAsync(string serviceId, FlowRunStatus status,
        long? startedAt = null, long? completedAt = null, long? durationMs = null, string? errorMessage = null)
    {
        if (_db == null) return;

        var run = await _db.FlowRun.FirstOrDefaultAsync(r => r.ServiceId == serviceId);
        if (run == null) return;

        run.Status = status.ToString().ToLowerInvariant();
        run.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (startedAt.HasValue) run.StartedAt = startedAt.Value;
        if (completedAt.HasValue) run.CompletedAt = completedAt.Value;
        if (durationMs.HasValue) run.DurationMs = durationMs.Value;
        if (errorMessage != null) run.ErrorMessage = errorMessage[..Math.Min(errorMessage.Length, 500)];

        await _db.SaveChangesAsync();
    }

    private async Task UpdateRunProgressAsync(string serviceId, int completedNodes)
    {
        if (_db == null) return;

        var run = await _db.FlowRun.FirstOrDefaultAsync(r => r.ServiceId == serviceId);
        if (run == null) return;

        run.CompletedNodes = completedNodes;
        run.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SaveChangesAsync();
    }

    private async Task CreateNodeLogAsync(string serviceId, IFlowNode node, int sequence, FlowNodeStatus status, long startedAtMs)
    {
        if (_db == null) return;

        var log = new FlowNodeLog
        {
            ServiceId = serviceId,
            NodeName = node.Name,
            NodeType = node.NodeType,
            Sequence = sequence,
            Status = status.ToString().ToLowerInvariant(),
            StartedAt = startedAtMs / 1000,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await _db.FlowNodeLog.AddAsync(log);
        await _db.SaveChangesAsync();
    }

    private async Task UpdateNodeLogAsync(string serviceId, string nodeName, int sequence,
        FlowNodeStatus status, string? errorMessage, string? branchTaken, int retryCount,
        long completedAt, long durationMs)
    {
        if (_db == null) return;

        var log = await _db.FlowNodeLog
            .FirstOrDefaultAsync(l => l.ServiceId == serviceId && l.Sequence == sequence);
        if (log == null) return;

        log.Status = status.ToString().ToLowerInvariant();
        log.CompletedAt = completedAt;
        log.DurationMs = durationMs;
        log.RetryCount = retryCount;
        log.BranchTaken = branchTaken;
        log.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (errorMessage != null)
            log.ErrorMessage = errorMessage[..Math.Min(errorMessage.Length, 500)];

        await _db.SaveChangesAsync();
    }
}
