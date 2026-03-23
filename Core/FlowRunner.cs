using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Bikiran.Engine.Core;

/// <summary>
/// Executes a flow's nodes in sequence, handling logging, timeouts, and failure strategies.
/// </summary>
internal class FlowRunner
{
    private readonly IServiceProvider? _serviceProvider;

    internal FlowRunner(IServiceProvider? serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Runs all nodes in sequence with timeout protection, per-node logging, and failure handling.
    /// </summary>
    internal async Task RunAsync(FlowContext context, IReadOnlyList<IFlowNode> nodes, CancellationToken externalToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        cts.CancelAfter(context.Config.MaxExecutionTime);
        var ct = cts.Token;

        await UpdateRunStatusAsync(context.ServiceId, "running",
            startedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

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
                    await CreateNodeLogAsync(context.ServiceId, node, sequence, "running", nodeStartedAt);

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
                        result.Success ? "completed" : "failed",
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

        var completedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var status = flowError == null ? "completed" : "failed";

        await UpdateRunStatusAsync(context.ServiceId, status,
            completedAt: completedAt,
            errorMessage: flowError);
    }

    // --- Database helpers ---

    private async Task UpdateRunStatusAsync(string serviceId, string status,
        long? startedAt = null, long? completedAt = null, string? errorMessage = null)
    {
        using var scope = _serviceProvider?.CreateScope();
        var db = scope?.ServiceProvider.GetService<EngineDbContext>();
        if (db == null) return;

        var run = await db.FlowRun.FirstOrDefaultAsync(r => r.ServiceId == serviceId);
        if (run == null) return;

        run.Status = status;
        run.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (startedAt.HasValue) run.StartedAt = startedAt.Value;
        if (completedAt.HasValue)
        {
            run.CompletedAt = completedAt.Value;
            run.DurationMs = (run.CompletedAt - run.StartedAt) * 1000;
        }
        if (errorMessage != null) run.ErrorMessage = errorMessage[..Math.Min(errorMessage.Length, 500)];

        await db.SaveChangesAsync();
    }

    private async Task UpdateRunProgressAsync(string serviceId, int completedNodes)
    {
        using var scope = _serviceProvider?.CreateScope();
        var db = scope?.ServiceProvider.GetService<EngineDbContext>();
        if (db == null) return;

        var run = await db.FlowRun.FirstOrDefaultAsync(r => r.ServiceId == serviceId);
        if (run == null) return;

        run.CompletedNodes = completedNodes;
        run.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await db.SaveChangesAsync();
    }

    private async Task CreateNodeLogAsync(string serviceId, IFlowNode node, int sequence, string status, long startedAtMs)
    {
        using var scope = _serviceProvider?.CreateScope();
        var db = scope?.ServiceProvider.GetService<EngineDbContext>();
        if (db == null) return;

        var log = new FlowNodeLog
        {
            ServiceId = serviceId,
            NodeName = node.Name,
            NodeType = node.NodeType,
            Sequence = sequence,
            Status = status,
            StartedAt = startedAtMs / 1000,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await db.FlowNodeLog.AddAsync(log);
        await db.SaveChangesAsync();
    }

    private async Task UpdateNodeLogAsync(string serviceId, string nodeName, int sequence,
        string status, string? errorMessage, string? branchTaken, int retryCount,
        long completedAt, long durationMs)
    {
        using var scope = _serviceProvider?.CreateScope();
        var db = scope?.ServiceProvider.GetService<EngineDbContext>();
        if (db == null) return;

        var log = await db.FlowNodeLog
            .FirstOrDefaultAsync(l => l.ServiceId == serviceId && l.Sequence == sequence);
        if (log == null) return;

        log.Status = status;
        log.CompletedAt = completedAt;
        log.DurationMs = durationMs;
        log.RetryCount = retryCount;
        log.BranchTaken = branchTaken;
        log.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (errorMessage != null)
            log.ErrorMessage = errorMessage[..Math.Min(errorMessage.Length, 500)];

        await db.SaveChangesAsync();
    }
}
