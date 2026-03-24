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
        string triggerSource = "",
        bool dryRun = false)
    {
        // Load the latest active version
        var definition = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == definitionKey && d.IsActive && d.TimeDeleted == 0)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync();

        if (definition == null)
            throw new InvalidOperationException(
                $"Flow definition '{definitionKey}' was not found or is not active.");

        return await ExecuteDefinitionAsync(definition, parameters, contextSetup, triggerSource, dryRun);
    }

    /// <summary>
    /// Triggers a specific version of a flow definition.
    /// </summary>
    public async Task<string> TriggerVersionAsync(
        string definitionKey,
        int version,
        Dictionary<string, string>? parameters = null,
        Action<FlowContext>? contextSetup = null,
        string triggerSource = "")
    {
        var definition = await _db.FlowDefinition
            .Where(d => d.DefinitionKey == definitionKey && d.Version == version && d.TimeDeleted == 0)
            .FirstOrDefaultAsync();

        if (definition == null)
            throw new InvalidOperationException(
                $"Flow definition '{definitionKey}' v{version} was not found.");

        return await ExecuteDefinitionAsync(definition, parameters, contextSetup, triggerSource, false);
    }

    /// <summary>
    /// Validates runtime parameters against the definition's ParameterSchema.
    /// Returns a list of validation errors (empty if valid).
    /// </summary>
    public static List<string> ValidateParameters(FlowDefinition definition, Dictionary<string, string>? parameters)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(definition.ParameterSchema))
            return errors;

        JsonElement schema;
        try
        {
            schema = JsonDocument.Parse(definition.ParameterSchema).RootElement;
        }
        catch
        {
            return errors; // Malformed schema — skip validation
        }

        if (schema.ValueKind != JsonValueKind.Object)
            return errors;

        var allParams = parameters ?? new Dictionary<string, string>();

        foreach (var prop in schema.EnumerateObject())
        {
            var paramName = prop.Name;
            var spec = prop.Value;

            var isRequired = spec.TryGetProperty("required", out var req) && req.GetBoolean();
            var hasDefault = spec.TryGetProperty("default", out _);
            var paramType = spec.TryGetProperty("type", out var tp) ? tp.GetString() ?? "string" : "string";

            if (!allParams.ContainsKey(paramName))
            {
                if (isRequired && !hasDefault)
                    errors.Add($"Required parameter '{paramName}' is missing.");
                continue;
            }

            var value = allParams[paramName];

            // Type validation
            switch (paramType)
            {
                case "number":
                    if (!double.TryParse(value, out _))
                        errors.Add($"Parameter '{paramName}' must be a number, got '{value}'.");
                    break;
                case "boolean":
                    if (value != "true" && value != "false")
                        errors.Add($"Parameter '{paramName}' must be 'true' or 'false', got '{value}'.");
                    break;
                // "string" — always valid
            }
        }

        return errors;
    }

    private async Task<string> ExecuteDefinitionAsync(
        FlowDefinition definition,
        Dictionary<string, string>? parameters,
        Action<FlowContext>? contextSetup,
        string triggerSource,
        bool dryRun)
    {
        // Validate parameters against schema
        var paramErrors = ValidateParameters(definition, parameters);
        if (paramErrors.Count > 0)
            throw new InvalidOperationException(
                $"Parameter validation failed: {string.Join("; ", paramErrors)}");

        // Add built-in scheduling placeholders (don't overwrite user-supplied values)
        var allParams = new Dictionary<string, string>(parameters ?? new());
        allParams.TryAdd("today_date", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        allParams.TryAdd("unix_now", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        allParams.TryAdd("year", DateTime.UtcNow.ToString("yyyy"));
        allParams.TryAdd("month", DateTime.UtcNow.ToString("MM"));

        // Apply defaults from parameter schema
        if (!string.IsNullOrWhiteSpace(definition.ParameterSchema))
        {
            try
            {
                using var schemaDoc = JsonDocument.Parse(definition.ParameterSchema);
                foreach (var prop in schemaDoc.RootElement.EnumerateObject())
                {
                    if (!allParams.ContainsKey(prop.Name) &&
                        prop.Value.TryGetProperty("default", out var def))
                    {
                        allParams[prop.Name] = def.ToString();
                    }
                }
            }
            catch { /* skip defaults on malformed schema */ }
        }

        // Parse and configure the flow
        var builder = _parser.Parse(definition.FlowJson, allParams);
        builder.Configure(cfg => { cfg.TriggerSource = triggerSource; });

        if (contextSetup != null)
            builder.WithContext(contextSetup);

        // Dry-run: parse and validate only, don't execute
        if (dryRun)
            return $"dry-run:{definition.DefinitionKey}:v{definition.Version}";

        var serviceId = await builder.StartAsync();

        // Record the definition → run linkage
        _db.FlowDefinitionRun.Add(new FlowDefinitionRun
        {
            FlowRunServiceId = serviceId,
            DefinitionId = definition.Id,
            DefinitionKey = definition.DefinitionKey,
            DefinitionVersion = definition.Version,
            Parameters = JsonSerializer.Serialize(allParams),
            TriggerSource = triggerSource,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        await _db.SaveChangesAsync();

        _logger?.LogInformation(
            "Triggered definition '{Key}' v{Version} → ServiceId {ServiceId}",
            definition.DefinitionKey, definition.Version, serviceId);

        return serviceId;
    }
}
