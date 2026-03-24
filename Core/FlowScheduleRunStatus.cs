namespace Bikiran.Engine.Core;

/// <summary>
/// Outcome of a scheduled flow trigger attempt.
/// </summary>
public enum FlowScheduleRunStatus
{
    /// <summary>Flow definition was triggered successfully.</summary>
    Triggered,

    /// <summary>Failed to trigger the flow definition.</summary>
    Error
}
