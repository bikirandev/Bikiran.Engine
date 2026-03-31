using Bikiran.Engine.Database;
using Microsoft.Extensions.Logging;

namespace Bikiran.Engine.Core;

/// <summary>
/// Executes a flow's nodes in sequence, handling timeouts, failure strategies, and lifecycle events.
/// Delegates all database persistence and serialization to <see cref="FlowRunnerHelper"/>.
/// </summary>
internal class FlowRunner
{
    private readonly FlowRunnerHelper _helper;

    internal FlowRunner(EngineDbContext? db)
    {
        _helper = new FlowRunnerHelper(db);
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

        // Cache the FlowRun entity once to avoid repeated lookups
        await _helper.LoadRunAsync(context.ServiceId);

        var runStartedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _helper.UpdateRunStatusAsync(context.ServiceId, FlowRunStatus.Running,
            startedAt: runStartedAtMs / 1000);

        var (completedNodes, flowError) = await ExecuteMainNodesAsync(context, nodes, ct);

        var runCompletedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var status = flowError == null ? FlowRunStatus.Completed : FlowRunStatus.Failed;

        // Expose flow outcome to lifecycle event nodes
        context.FlowStatus = status;
        context.FlowError = flowError;

        // Persist final flow status BEFORE running lifecycle event nodes,
        // so OnSuccess/OnFail/OnFinish nodes see the committed status.
        await _helper.UpdateRunStatusAsync(context.ServiceId, status,
            completedAt: runCompletedAtMs / 1000,
            durationMs: runCompletedAtMs - runStartedAtMs,
            errorMessage: flowError);

        // Execute lifecycle event nodes with a fresh timeout (not the expired main token)
        await ExecuteLifecyclePhaseAsync(context, nodes.Count + 1,
            flowError, onSuccessNodes, onFailNodes, onFinishNodes, externalToken);
    }

    // --- Main node execution ---

    /// <summary>
    /// Executes main nodes in sequence. Returns the count of completed nodes and any error.
    /// </summary>
    private async Task<(int completedNodes, string? flowError)> ExecuteMainNodesAsync(
        FlowContext context,
        IReadOnlyList<IFlowNode> nodes,
        CancellationToken ct)
    {
        var completedNodes = 0;
        string? flowError = null;

        // Track current node state so the outer catch can create a failure log
        int currentSequence = 0;
        long currentNodeStartedAt = 0;
        bool nodeLogCreated = false;
        IFlowNode? currentNode = null;

        try
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var node = nodes[i];
                var sequence = i + 1;
                currentSequence = sequence;
                currentNode = node;
                nodeLogCreated = false;

                var nodeStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                currentNodeStartedAt = nodeStartedAt;

                if (context.Config.EnableNodeLogging)
                {
                    await _helper.CreateNodeLogAsync(context.ServiceId, node, sequence,
                        FlowNodeStatus.Running, nodeStartedAt,
                        FlowRunnerHelper.SerializeNodeInput(node));
                    nodeLogCreated = true;
                }

                // Only update progress message when one is set
                if (node.ProgressMessage != null)
                    await _helper.UpdateRunProgressMessageAsync(context.ServiceId, node.ProgressMessage);

                NodeResult result;
                string? branchTaken = null;
                int retryCount = 0;

                try
                {
                    result = await node.ExecuteAsync(context, ct);

                    if (context.Has($"{node.Name}_branch_taken"))
                        branchTaken = context.Get<string>($"{node.Name}_branch_taken");

                    if (context.Has($"{node.Name}_retry_count"))
                        retryCount = context.Get<int>($"{node.Name}_retry_count");
                }
                catch (OperationCanceledException)
                {
                    result = NodeResult.Fail($"Node '{node.Name}' was cancelled because the flow exceeded MaxExecutionTime ({context.Config.MaxExecutionTime.TotalMinutes} minutes).");
                }
                catch (Exception ex)
                {
                    result = NodeResult.Fail($"Node '{node.Name}' threw an unhandled exception: {ex.Message}");
                    context.Logger?.LogError(ex, "Unhandled exception in node {NodeName}", node.Name);
                }

                var nodeCompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var durationMs = nodeCompletedAt - nodeStartedAt;

                if (context.Config.EnableNodeLogging)
                {
                    await _helper.UpdateNodeLogAsync(context.ServiceId, node.Name, sequence,
                        result.Success ? FlowNodeStatus.Completed : FlowNodeStatus.Failed,
                        result.ErrorMessage,
                        branchTaken,
                        retryCount,
                        nodeCompletedAt / 1000,
                        durationMs,
                        FlowRunnerHelper.SafeSerialize(result.Output));
                }

                if (result.Success)
                {
                    completedNodes++;
                    await _helper.UpdateRunProgressAsync(context.ServiceId, completedNodes);
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
                    await _helper.UpdateRunProgressAsync(context.ServiceId, completedNodes);
                }
            }
        }
        catch (OperationCanceledException)
        {
            flowError = $"Flow '{context.FlowName}' exceeded MaxExecutionTime ({context.Config.MaxExecutionTime.TotalMinutes} minutes). Last active node: '{currentNode?.Name ?? "unknown"}'.";
            await _helper.TryRecordFailingNodeLogAsync(context, currentNode, currentSequence,
                nodeLogCreated, currentNodeStartedAt, flowError);
        }
        catch (Exception ex)
        {
            flowError = $"Flow '{context.FlowName}' failed at node '{currentNode?.Name ?? "unknown"}': {ex.Message}";
            context.Logger?.LogError(ex, "Unhandled exception in flow {FlowName}", context.FlowName);
            await _helper.TryRecordFailingNodeLogAsync(context, currentNode, currentSequence,
                nodeLogCreated, currentNodeStartedAt, flowError);
        }

        return (completedNodes, flowError);
    }

    // --- Lifecycle event orchestration ---

    /// <summary>
    /// Orchestrates OnSuccess/OnFail/OnFinish lifecycle phases.
    /// Uses a fresh CancellationToken so lifecycle nodes are not affected by an expired main timeout.
    /// </summary>
    private async Task ExecuteLifecyclePhaseAsync(
        FlowContext context,
        int lifecycleSequenceStart,
        string? flowError,
        IReadOnlyList<IFlowNode> onSuccessNodes,
        IReadOnlyList<IFlowNode> onFailNodes,
        IReadOnlyList<IFlowNode> onFinishNodes,
        CancellationToken externalToken)
    {
        // Lifecycle nodes get a fresh 5-minute timeout independent of the main flow timeout
        using var lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        lifecycleCts.CancelAfter(TimeSpan.FromMinutes(5));
        var lifecycleCt = lifecycleCts.Token;

        var nextSequence = lifecycleSequenceStart;

        if (flowError == null && onSuccessNodes.Count > 0)
        {
            nextSequence = await ExecuteLifecycleNodesAsync(
                context, onSuccessNodes, "OnSuccess", nextSequence, lifecycleCt);
        }
        else if (flowError != null && onFailNodes.Count > 0)
        {
            nextSequence = await ExecuteLifecycleNodesAsync(
                context, onFailNodes, "OnFail", nextSequence, lifecycleCt);
        }

        if (onFinishNodes.Count > 0)
        {
            await ExecuteLifecycleNodesAsync(
                context, onFinishNodes, "OnFinish", nextSequence, lifecycleCt);
        }
    }

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
                await _helper.CreateNodeLogAsync(context.ServiceId, node, sequence,
                    FlowNodeStatus.Running, nodeStartedAt,
                    FlowRunnerHelper.SerializeNodeInput(node));

            NodeResult result;
            try
            {
                result = await node.ExecuteAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                result = NodeResult.Fail($"{phase} node '{node.Name}' was cancelled because the lifecycle phase exceeded its 5-minute timeout.");
            }
            catch (Exception ex)
            {
                result = NodeResult.Fail($"{phase} node '{node.Name}' threw an unhandled exception: {ex.Message}");
                context.Logger?.LogError(ex, "{Phase} node '{NodeName}' failed", phase, node.Name);
            }

            var nodeCompletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var durationMs = nodeCompletedAt - nodeStartedAt;

            if (context.Config.EnableNodeLogging)
            {
                await _helper.UpdateNodeLogAsync(context.ServiceId, node.Name, sequence,
                    result.Success ? FlowNodeStatus.Completed : FlowNodeStatus.Failed,
                    result.ErrorMessage,
                    branchTaken: null,
                    retryCount: 0,
                    nodeCompletedAt / 1000,
                    durationMs,
                    FlowRunnerHelper.SafeSerialize(result.Output));
            }

            sequence++;
        }

        return sequence;
    }
}
