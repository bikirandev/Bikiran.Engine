namespace Bikiran.Engine.Core;

/// <summary>
/// Represents the outcome of a single node execution.
/// </summary>
public class NodeResult
{
    /// <summary>Whether the node completed successfully.</summary>
    public bool Success { get; private init; }

    /// <summary>Optional output value produced by the node.</summary>
    public object? Output { get; private init; }

    /// <summary>Error message when the node fails.</summary>
    public string? ErrorMessage { get; private init; }

    private NodeResult() { }

    /// <summary>Creates a successful result with an optional output value.</summary>
    public static NodeResult Ok(object? output = null) =>
        new() { Success = true, Output = output };

    /// <summary>Creates a failure result with a descriptive error message.</summary>
    public static NodeResult Fail(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}
