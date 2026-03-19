namespace Bikiran.Engine.Scheduling.DTOs;

/// <summary>Request model for creating or updating a flow schedule.</summary>
public class FlowScheduleSaveRequestDTO
{
    public string ScheduleKey { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>Must match an existing FlowDefinition key.</summary>
    public string DefinitionKey { get; set; } = "";

    /// <summary>Schedule type: "cron", "interval", or "once".</summary>
    public string ScheduleType { get; set; } = "";

    /// <summary>Quartz 6-field cron expression (for cron type).</summary>
    public string? CronExpression { get; set; }

    /// <summary>Repeat interval in minutes (for interval type).</summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>Unix timestamp for one-time execution (for once type).</summary>
    public long? RunOnceAt { get; set; }

    /// <summary>Key-value pairs passed to the flow definition on each trigger.</summary>
    public Dictionary<string, string> DefaultParameters { get; set; } = new();

    /// <summary>IANA timezone ID (default: "UTC").</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Maximum concurrent runs. 1 means overlapping runs are skipped.</summary>
    public int MaxConcurrent { get; set; } = 1;
}
