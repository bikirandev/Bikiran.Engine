using Bikiran.Engine.Core;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// Pauses flow execution for a configured number of milliseconds.
/// </summary>
public class WaitNode : IFlowNode
{
    public string Name { get; }

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

    /// <summary>Duration to pause in milliseconds. Default is 1000 ms.</summary>
    public int DelayMs { get; set; } = 1000;

    public WaitNode(string name)
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(DelayMs, cancellationToken);
        return NodeResult.Ok($"Waited {DelayMs}ms");
    }
}
