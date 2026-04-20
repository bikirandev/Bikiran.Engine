using Bikiran.Engine.Core;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// A lightweight marker node that signals the beginning of a flow.
/// Optionally pauses for a brief moment before handing off to the next node.
/// </summary>
public class StartingNode : IFlowNode
{
    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

    /// <inheritdoc />
    public TimeSpan ApproxExecutionTime { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long to pause before proceeding. Default is 1 second.</summary>
    public TimeSpan WaitTime { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Creates a new StartingNode.
    /// </summary>
    /// <param name="name">PascalCase node name. Defaults to "StartingNode".</param>
    public StartingNode(string name = "StartingNode")
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    /// <inheritdoc />
    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        if (WaitTime > TimeSpan.Zero)
            await Task.Delay(WaitTime, cancellationToken);

        return NodeResult.Ok("Flow started.");
    }
}
