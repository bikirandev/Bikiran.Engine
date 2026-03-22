using Bikiran.Engine.Core;
using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Bikiran.Engine.Definitions;

/// <summary>
/// Loads a flow definition from the database, resolves parameters, builds the flow, and starts execution.
/// </summary>
public class FlowDefinitionRunner
{
    private readonly EngineDbContext _db;
    private readonly FlowDefinitionParser _parser;
    private readonly ILogger<FlowDefinitionRunner>? _logger;

    public FlowDefinitionRunner(EngineDbContext db, ILogger<FlowDefinitionRunner>? logger = null)
    {
        _db = db;
        _parser = new FlowDefinitionParser();
        _logger = logger;
    }

    /// <summary>
    /// Triggers the latest active version of the named flow definition.
    /// Returns the ServiceId of the resulting run.
    /// </summary>
    public async Task<string> TriggerAsync(
        string definitionKey,
        Dictionary<string, string>? parameters = null,
        Action<FlowContext>? contextSetup = null,
        string triggerSource = "")
    {
        // Load the latest active version
        var definition = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == definitionKey && d.IsActive && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        if (definition == null)
            throw new InvalidOperationException(
                $"Flow definition '{definitionKey}' was not found or is not active.");

        // Add built-in scheduling placeholders
        var allParams = new Dictionary<string, string>(parameters ?? new())
        {
            ["today_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["unix_now"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["year"] = DateTime.UtcNow.ToString("yyyy"),
            ["month"] = DateTime.UtcNow.ToString("MM")
        };

        // Parse and configure the flow
        var builder = _parser.Parse(definition.FlowJson, allParams);
        builder.Configure(cfg => { cfg.TriggerSource = triggerSource; });

        if (contextSetup != null)
            builder.WithContext(contextSetup);

        var serviceId = await builder.StartAsync();

        // Record the definition → run linkage
        _db.FlowDefinitionRun.Add(new FlowDefinitionRun
        {
            FlowRunServiceId = serviceId,
            DefinitionId = definition.Id,
            DefinitionKey = definitionKey,
            DefinitionVersion = definition.Version,
            Parameters = JsonSerializer.Serialize(allParams),
            TriggerSource = triggerSource,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        await _db.SaveChangesAsync();

        _logger?.LogInformation(
            "Triggered definition '{Key}' v{Version} → ServiceId {ServiceId}",
            definitionKey, definition.Version, serviceId);

        return serviceId;
    }
}
