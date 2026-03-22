namespace Bikiran.Engine.Core;

/// <summary>
/// Contract that every flow node must implement.
/// </summary>
public interface IFlowNode
{
    /// <summary>Unique name for this node within the flow (lowercase_underscore recommended).</summary>
    string Name { get; }

    /// <summary>Type label used for logging and JSON definitions (PascalCase).</summary>
    string NodeType { get; }

    /// <summary>Executes the node's logic and returns a result.</summary>
    Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken);
}
