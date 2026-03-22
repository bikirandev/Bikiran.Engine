namespace Bikiran.Engine.Definitions.DTOs;

/// <summary>Request model for triggering a flow definition with runtime parameters.</summary>
public class FlowDefinitionTriggerRequestDTO
{
    /// <summary>Key-value pairs that replace {{placeholders}} in the flow JSON.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new();

    /// <summary>Label identifying where this trigger originated.</summary>
    public string TriggerSource { get; set; } = "";
}
