namespace Bikiran.Engine.Core;

/// <summary>
/// Execution status of an individual node within a flow run.
/// </summary>
public enum FlowNodeStatus
{
    /// <summary>Queued but not yet executed.</summary>
    Pending,

    /// <summary>Currently executing.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Completed,

    /// <summary>Finished with an error.</summary>
    Failed,

    /// <summary>Node was bypassed.</summary>
    Skipped
}
