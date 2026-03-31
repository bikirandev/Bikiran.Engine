using System.Text.RegularExpressions;

namespace Bikiran.Engine.Core;

/// <summary>
/// Contract that every flow node must implement.
/// </summary>
public interface IFlowNode
{
    /// <summary>Unique name for this node within the flow (PascalCase, no spaces).</summary>
    string Name { get; }

    /// <summary>
    /// The node type enum value. Defaults to <see cref="FlowNodeType.Custom"/> for user-defined nodes.
    /// Built-in nodes override this internally. Do not override in custom node implementations.
    /// </summary>
    FlowNodeType NodeType => FlowNodeType.Custom;

    /// <summary>
    /// Optional progress message shown while this node is executing.
    /// Example: "Waiting for DNS propagation".
    /// </summary>
    string? ProgressMessage { get; set; }

    /// <summary>Executes the node's logic and returns a result.</summary>
    Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Validates node naming conventions.
/// </summary>
public static class FlowNodeNameValidator
{
    private static readonly Regex PascalCaseRegex = new(@"^[A-Z][a-zA-Z0-9]*$", RegexOptions.Compiled);

    /// <summary>
    /// Validates that the node name follows PascalCase convention (no spaces, starts with uppercase).
    /// Throws <see cref="ArgumentException"/> if the name is invalid.
    /// </summary>
    public static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Node name cannot be null or empty.", nameof(name));

        if (!PascalCaseRegex.IsMatch(name))
            throw new ArgumentException(
                $"Node name '{name}' must be PascalCase (start with uppercase, no spaces or special characters). Example: 'FetchOrder', 'SendEmail'.",
                nameof(name));
    }
}
