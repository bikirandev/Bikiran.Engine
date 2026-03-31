namespace Bikiran.Engine.Core;

/// <summary>
/// Identifies the type of a flow node.
/// </summary>
public enum FlowNodeType
{
    /// <summary>Pauses flow execution for a configured duration.</summary>
    Wait,

    /// <summary>Makes an outbound HTTP request.</summary>
    HttpRequest,

    /// <summary>Sends an email via SMTP.</summary>
    EmailSend,

    /// <summary>Evaluates a condition and executes one of two branches.</summary>
    IfElse,

    /// <summary>Repeats steps while a condition is true.</summary>
    WhileLoop,

    /// <summary>Runs an EF Core query against the host database.</summary>
    DatabaseQuery,

    /// <summary>Reshapes or derives values from existing context data.</summary>
    Transform,

    /// <summary>Wraps any node with retry logic.</summary>
    Retry,

    /// <summary>Runs multiple branches concurrently.</summary>
    Parallel,

    /// <summary>A user-defined custom node type.</summary>
    Custom
}
