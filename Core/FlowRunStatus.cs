namespace Bikiran.Engine.Core;

/// <summary>
/// Execution status of a flow run.
/// </summary>
public enum FlowRunStatus
{
    /// <summary>Created but not yet started.</summary>
    Pending,

    /// <summary>Currently executing nodes.</summary>
    Running,

    /// <summary>All main nodes finished successfully.</summary>
    Completed,

    /// <summary>Stopped due to an error or timeout.</summary>
    Failed,

    /// <summary>Cancelled externally.</summary>
    Cancelled
}
