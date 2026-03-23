namespace Bikiran.Engine.Scheduling.DTOs;

/// <summary>Summary of a flow schedule including next fire time.</summary>
public class FlowScheduleSummaryDTO
{
    public long Id { get; set; }
    public string ScheduleKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DefinitionKey { get; set; } = "";
    public string ScheduleType { get; set; } = "";
    public bool IsActive { get; set; }
    public long LastRunAt { get; set; }
    public string? LastRunStatus { get; set; }
    public string? LastRunServiceId { get; set; }
    public long NextRunAt { get; set; }
}
