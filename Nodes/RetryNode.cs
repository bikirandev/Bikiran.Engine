using Bikiran.Engine.Core;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// Wraps any node and retries it on failure with configurable delay and optional exponential backoff.
/// </summary>
public class RetryNode : IFlowNode
{
    public string Name { get; }

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

    /// <summary>The node to wrap and retry.</summary>
    public required IFlowNode Inner { get; set; }

    /// <summary>Total number of attempts (1 means no retry). Default is 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base delay between attempts in milliseconds. Default is 2000.</summary>
    public int DelayMs { get; set; } = 2000;

    /// <summary>
    /// Custom condition to decide whether to retry. If null, retries on any failure.
    /// </summary>
    public Func<NodeResult, bool>? RetryOn { get; set; }

    /// <summary>
    /// Multiplier applied to DelayMs on each successive retry.
    /// Use 2.0 for exponential backoff. Default is 1.0 (constant delay).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 1.0;

    public RetryNode(string name)
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        NodeResult? lastResult = null;
        var delay = (double)DelayMs;
        var attemptsUsed = 0;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 1)
            {
                await Task.Delay((int)delay, cancellationToken);
                delay *= BackoffMultiplier;
            }

            lastResult = await Inner.ExecuteAsync(context, cancellationToken);
            attemptsUsed = attempt;

            if (lastResult.Success)
            {
                context.Set($"{Name}_retry_count", attempt - 1);
                return lastResult;
            }

            var shouldRetry = RetryOn?.Invoke(lastResult) ?? !lastResult.Success;
            if (!shouldRetry)
                break;
        }

        context.Set($"{Name}_retry_count", attemptsUsed - 1);
        return lastResult ?? NodeResult.Fail("RetryNode: No attempts were made.");
    }
}
