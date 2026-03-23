namespace Bikiran.Engine.Core;

/// <summary>
/// Runtime configuration for a flow execution.
/// </summary>
public class FlowConfig
{
    /// <summary>Maximum time the flow is allowed to run before being cancelled. Default is 10 minutes.</summary>
    public TimeSpan MaxExecutionTime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>What to do when a node fails. Default is Stop.</summary>
    public OnFailureAction OnFailure { get; set; } = OnFailureAction.Stop;

    /// <summary>Whether to write per-node execution records to the database. Default is true.</summary>
    public bool EnableNodeLogging { get; set; } = true;

    /// <summary>A label indicating where the flow was triggered from (e.g., controller name).</summary>
    public string TriggerSource { get; set; } = "";
}
