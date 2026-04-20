using Bikiran.Engine.Core;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// Repeats a set of steps while a condition remains true, up to MaxIterations times.
/// </summary>
public class WhileLoopNode : IFlowNode
{
    public string Name { get; }

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

    /// <inheritdoc />
    public TimeSpan ApproxExecutionTime { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Continue iterating while this returns true.</summary>
    public required Func<FlowContext, bool> Condition { get; set; }

    /// <summary>Steps to execute on each iteration.</summary>
    public List<IFlowNode> Body { get; set; } = new();

    /// <summary>Hard cap to prevent infinite loops. Default is 10.</summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>Milliseconds to wait between iterations. Default is 0.</summary>
    public int IterationDelayMs { get; set; }

    public WhileLoopNode(string name)
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        var iteration = 0;

        while (Condition(context))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (iteration >= MaxIterations)
                return NodeResult.Fail($"WhileLoop '{Name}' exceeded MaxIterations ({MaxIterations}).");

            iteration++;
            context.Set($"{Name}_iteration_count", iteration);

            foreach (var node in Body)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await node.ExecuteAsync(context, cancellationToken);
                if (!result.Success)
                    return NodeResult.Fail($"WhileLoop body failed at iteration {iteration}, node '{node.Name}': {result.ErrorMessage}");
            }

            if (IterationDelayMs > 0)
                await Task.Delay(IterationDelayMs, cancellationToken);
        }

        return NodeResult.Ok($"Loop completed after {iteration} iteration(s)");
    }
}
