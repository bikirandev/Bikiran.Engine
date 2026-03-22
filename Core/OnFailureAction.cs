namespace Bikiran.Engine.Core;

/// <summary>
/// Defines what happens when a node fails during flow execution.
/// </summary>
public enum OnFailureAction
{
    /// <summary>Cancel the entire flow immediately.</summary>
    Stop,

    /// <summary>Skip the failed node and continue with the next one.</summary>
    Continue,

    /// <summary>Let the node handle retries using its own MaxRetries setting.</summary>
    Retry
}
