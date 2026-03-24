namespace Bikiran.Engine.Definitions.DTOs;

/// <summary>Request model for creating or updating a flow definition.</summary>
public class FlowDefinitionSaveRequestDTO
{
    /// <summary>Unique slug identifying this definition (e.g., "order_notification").</summary>
    public string DefinitionKey { get; set; } = "";

    /// <summary>Human-readable label shown in the admin UI.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Optional description of what this flow does.</summary>
    public string Description { get; set; } = "";

    /// <summary>JSON string describing the flow and its nodes.</summary>
    public string FlowJson { get; set; } = "{}";

    /// <summary>Comma-separated tags for categorisation (e.g., "email,auth").</summary>
    public string Tags { get; set; } = "";

    /// <summary>
    /// Optional JSON schema describing accepted runtime parameters.
    /// Format: { "paramName": { "type": "string|number|boolean", "required": true, "default": "..." } }
    /// </summary>
    public string? ParameterSchema { get; set; }
}
