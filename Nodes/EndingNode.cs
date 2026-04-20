using Bikiran.Engine.Core;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// A lightweight marker node that signals the successful completion of a flow.
/// Produces a final output message and returns immediately.
/// </summary>
public class EndingNode : IFlowNode
{
    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

    /// <inheritdoc />
    public TimeSpan ApproxExecutionTime { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Creates a new EndingNode.
    /// </summary>
    /// <param name="name">PascalCase node name. Defaults to "EndingNode".</param>
    public EndingNode(string name = "EndingNode")
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    /// <inheritdoc />
    public Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(NodeResult.Ok("Flow completed."));
    }
}
