using Bikiran.Engine.Credentials;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bikiran.Engine.Core;

/// <summary>
/// Shared state that every node in a flow can read from and write to.
/// Also provides access to injected services and caller metadata.
/// </summary>
public class FlowContext
{
    private readonly Dictionary<string, object?> _data = new();

    /// <summary>Unique identifier for this flow run (UUID).</summary>
    public string ServiceId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Name of the running flow.</summary>
    public string FlowName { get; init; } = "";

    // --- Injected services ---

    /// <summary>The host application's DbContext, available to DatabaseQueryNode and custom nodes.</summary>
    public object? DbContext { get; set; }

    /// <summary>The HTTP context of the originating request, used for capturing caller metadata.</summary>
    public HttpContext? HttpContext { get; set; }

    /// <summary>Structured logger available to nodes.</summary>
    public ILogger? Logger { get; set; }

    /// <summary>General-purpose DI service provider.</summary>
    public IServiceProvider? Services { get; set; }

    // --- Internal state used by FlowRunner ---
    internal Dictionary<string, IEngineCredential> Credentials { get; set; } = new();
    internal FlowConfig Config { get; set; } = new();

    // --- Flow outcome (set by FlowRunner after main nodes complete) ---

    /// <summary>Final status of the flow after main nodes complete: "completed" or "failed".</summary>
    public string? FlowStatus { get; internal set; }

    /// <summary>Error message if the flow failed; null on success.</summary>
    public string? FlowError { get; internal set; }

    // --- Context data methods ---

    /// <summary>Stores a value in the shared context under the given key.</summary>
    public void Set(string key, object? value) => _data[key] = value;

    /// <summary>Retrieves a typed value from the shared context. Returns default if the key is not found.</summary>
    public T? Get<T>(string key) =>
        _data.TryGetValue(key, out var val) && val is T typed ? typed : default;

    /// <summary>Returns true if the given key exists in the shared context.</summary>
    public bool Has(string key) => _data.ContainsKey(key);

    /// <summary>
    /// Retrieves a named credential registered at engine startup.
    /// Throws if the credential is not found or does not match the expected type.
    /// </summary>
    public T GetCredential<T>(string name) where T : IEngineCredential
    {
        if (!Credentials.TryGetValue(name, out var cred))
            throw new InvalidOperationException($"Credential '{name}' is not registered.");

        if (cred is not T typed)
            throw new InvalidOperationException(
                $"Credential '{name}' is of type '{cred.GetType().Name}', not '{typeof(T).Name}'.");

        return typed;
    }
}
