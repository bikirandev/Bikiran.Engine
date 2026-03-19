using Bikiran.Engine.Definitions;
using Bikiran.Engine.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using System.Text.Json;

namespace Bikiran.Engine.Scheduling;

/// <summary>
/// Quartz.NET job that fires on schedule and triggers the linked flow definition.
/// </summary>
[DisallowConcurrentExecution]
public class ScheduledFlowJob : IJob
{
    public const string ScheduleKeyData = "ScheduleKey";

    private readonly IServiceProvider _services;
    private readonly ILogger<ScheduledFlowJob>? _logger;

    public ScheduledFlowJob(IServiceProvider services, ILogger<ScheduledFlowJob>? logger = null)
    {
        _services = services;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext quartzContext)
    {
        var scheduleKey = quartzContext.JobDetail.JobDataMap.GetString(ScheduleKeyData);
        if (string.IsNullOrEmpty(scheduleKey)) return;

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EngineDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<FlowDefinitionRunner>();

        var schedule = await db.FlowSchedule
            .FirstOrDefaultAsync(s => s.ScheduleKey == scheduleKey && s.IsActive && s.TimeDeleted == 0);

        if (schedule == null)
        {
            _logger?.LogWarning("ScheduledFlowJob: Schedule '{Key}' not found or inactive.", scheduleKey);
            return;
        }

        Dictionary<string, string> parameters;
        try
        {
            parameters = string.IsNullOrEmpty(schedule.DefaultParameters) || schedule.DefaultParameters == "{}"
                ? new()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(schedule.DefaultParameters) ?? new();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ScheduledFlowJob: Failed to deserialize DefaultParameters for schedule '{Key}'. Using empty parameters.", scheduleKey);
            parameters = new();
        }

        try
        {
            var serviceId = await runner.TriggerAsync(
                schedule.DefinitionKey,
                parameters,
                triggerSource: $"FlowSchedule:{scheduleKey}");

            schedule.LastRunAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            schedule.LastRunServiceId = serviceId;
            schedule.LastRunStatus = "triggered";
            schedule.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await db.SaveChangesAsync();

            _logger?.LogInformation(
                "Schedule '{Key}' triggered definition '{Def}' → {ServiceId}",
                scheduleKey, schedule.DefinitionKey, serviceId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Schedule '{Key}' failed to trigger.", scheduleKey);

            schedule.LastRunAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            schedule.LastRunStatus = "error";
            schedule.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await db.SaveChangesAsync();
        }
    }
}
