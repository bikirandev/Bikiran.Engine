using Bikiran.Engine.Credentials;
using Bikiran.Engine.Definitions;

namespace Bikiran.Engine.Extensions;

/// <summary>
/// Configuration options passed to AddBikiranEngine() in Program.cs.
/// </summary>
public class BikiranEngineOptions
{
    /// <summary>Database connection string used for engine tables.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Default maximum execution time for flows that don't override it.
    /// Default is 10 minutes.
    /// </summary>
    public TimeSpan DefaultMaxExecutionTime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Default setting for per-node logging. Default is true.
    /// </summary>
    public bool EnableNodeLogging { get; set; } = true;

    /// <summary>
    /// When true, engine API endpoints require authentication. Default is false.
    /// </summary>
    public bool RequireAuthentication { get; set; }

    /// <summary>
    /// Name of the ASP.NET Core authorization policy to apply to engine endpoints.
    /// Only used when RequireAuthentication is true.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    // Internal credential registry
    internal Dictionary<string, IEngineCredential> Credentials { get; } = new();

    /// <summary>
    /// Registers a named credential for use by EmailSendNode and custom nodes.
    /// </summary>
    public BikiranEngineOptions AddCredential(string name, IEngineCredential credential)
    {
        Credentials[name] = credential;
        return this;
    }

    /// <summary>
    /// Registers a custom node type for use in JSON flow definitions.
    /// </summary>
    public BikiranEngineOptions RegisterNode<T>(string typeName) where T : Core.IFlowNode
    {
        FlowDefinitionParser.RegisterNode<T>(typeName);
        return this;
    }
}
