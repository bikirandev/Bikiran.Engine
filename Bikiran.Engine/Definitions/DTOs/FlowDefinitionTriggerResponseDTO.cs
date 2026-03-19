namespace Bikiran.Engine.Definitions.DTOs;

/// <summary>Response returned when a flow definition is successfully triggered.</summary>
public class FlowDefinitionTriggerResponseDTO
{
    /// <summary>Unique identifier for the resulting flow run.</summary>
    public string ServiceId { get; set; } = "";

    /// <summary>The definition key that was triggered.</summary>
    public string DefinitionKey { get; set; } = "";

    /// <summary>Version of the definition used for this run.</summary>
    public int DefinitionVersion { get; set; }

    /// <summary>Name of the flow as defined in the JSON.</summary>
    public string FlowName { get; set; } = "";
}
