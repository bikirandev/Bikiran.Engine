using Bikiran.Engine.Database;
using Bikiran.Engine.Database.Entities;
using Bikiran.Engine.Nodes;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Bikiran.Engine.Core;

/// <summary>
/// Provides database persistence, serialization, and failure-recovery helpers for FlowRunner.
/// Caches the FlowRun entity to eliminate redundant lookups.
/// </summary>
internal class FlowRunnerHelper
{
    private readonly EngineDbContext? _db;
    private FlowRun? _cachedRun;

    /// <summary>
    /// Maps built-in node types to their FlowNodeType enum value.
    /// Custom nodes (any type not in this map) get FlowNodeType.Custom.
    /// </summary>
    private static readonly Dictionary<Type, FlowNodeType> _builtInNodeTypes = new()
    {
        { typeof(WaitNode), FlowNodeType.Wait },
        { typeof(HttpRequestNode), FlowNodeType.HttpRequest },
        { typeof(EmailSendNode), FlowNodeType.EmailSend },
        { typeof(IfElseNode), FlowNodeType.IfElse },
        { typeof(WhileLoopNode), FlowNodeType.WhileLoop },
        { typeof(TransformNode), FlowNodeType.Transform },
        { typeof(RetryNode), FlowNodeType.Retry },
        { typeof(ParallelNode), FlowNodeType.Parallel },
        { typeof(StartingNode), FlowNodeType.Starting },
        { typeof(EndingNode), FlowNodeType.Ending },
    };

    /// <summary>
    /// Resolves the FlowNodeType for a node instance.
    /// Returns the specific type for built-in nodes, or Custom for user-defined nodes.
    /// </summary>
    internal static FlowNodeType ResolveNodeType(IFlowNode node)
    {
        var nodeType = node.GetType();
        if (_builtInNodeTypes.TryGetValue(nodeType, out var type))
            return type;

        // Handle generic types like DatabaseQueryNode<TContext>
        if (nodeType.IsGenericType && nodeType.GetGenericTypeDefinition() == typeof(DatabaseQueryNode<>))
            return FlowNodeType.DatabaseQuery;

        return FlowNodeType.Custom;
    }

    internal FlowRunnerHelper(EngineDbContext? db)
    {
        _db = db;
    }

    // --- FlowRun entity cache ---

    /// <summary>
    /// Loads and caches the FlowRun entity for the given ServiceId.
    /// Must be called once before any update methods.
    /// </summary>
    internal async Task LoadRunAsync(string serviceId)
    {
        if (_db == null) return;
        _cachedRun = await _db.FlowRun.FirstOrDefaultAsync(r => r.ServiceId == serviceId);
    }

    // --- FlowRun CRUD ---

    /// <summary>
    /// Updates the FlowRun status and optional timing/error fields.
    /// Uses the cached entity to avoid a redundant query.
    /// </summary>
    internal async Task UpdateRunStatusAsync(string serviceId, FlowRunStatus status,
        long? startedAt = null, long? completedAt = null, long? durationMs = null, string? errorMessage = null)
    {
        if (_db == null || _cachedRun == null) return;

        _cachedRun.Status = status.ToString().ToLowerInvariant();
        _cachedRun.CurrentProgressMessage = null;
        _cachedRun.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (startedAt.HasValue) _cachedRun.StartedAt = startedAt.Value;
        if (completedAt.HasValue) _cachedRun.CompletedAt = completedAt.Value;
        if (durationMs.HasValue) _cachedRun.DurationMs = durationMs.Value;
        if (errorMessage != null) _cachedRun.ErrorMessage = TruncateError(errorMessage);

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Updates the completed-nodes counter and optionally clears the progress message.
    /// </summary>
    internal async Task UpdateRunProgressAsync(string serviceId, int completedNodes)
    {
        if (_db == null || _cachedRun == null) return;

        _cachedRun.CompletedNodes = completedNodes;
        _cachedRun.CurrentProgressMessage = null;
        _cachedRun.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Persists the node's progress message to the FlowRun record.
    /// Skips the call entirely when progressMessage is null.
    /// </summary>
    internal async Task UpdateRunProgressMessageAsync(string serviceId, string? progressMessage)
    {
        if (_db == null || _cachedRun == null || progressMessage == null) return;

        _cachedRun.CurrentProgressMessage = progressMessage;
        _cachedRun.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _db.SaveChangesAsync();
    }

    // --- FlowNodeLog CRUD ---

    /// <summary>
    /// Creates a new FlowNodeLog record with "running" status.
    /// </summary>
    internal async Task CreateNodeLogAsync(string serviceId, IFlowNode node, int sequence,
        FlowNodeStatus status, long startedAtMs, string inputData)
    {
        if (_db == null) return;

        var log = new FlowNodeLog
        {
            ServiceId = serviceId,
            NodeName = node.Name,
            NodeType = ResolveNodeType(node).ToString(),
            Sequence = sequence,
            Status = status.ToString().ToLowerInvariant(),
            InputData = inputData,
            StartedAt = startedAtMs / 1000,
            TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await _db.FlowNodeLog.AddAsync(log);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Updates an existing FlowNodeLog record with completion details.
    /// </summary>
    internal async Task UpdateNodeLogAsync(string serviceId, string nodeName, int sequence,
        FlowNodeStatus status, string? errorMessage, string? branchTaken, int retryCount,
        long completedAt, long durationMs, string outputData)
    {
        if (_db == null) return;

        var log = await _db.FlowNodeLog
            .FirstOrDefaultAsync(l => l.ServiceId == serviceId && l.Sequence == sequence);
        if (log == null) return;

        log.Status = status.ToString().ToLowerInvariant();
        log.OutputData = outputData;
        log.CompletedAt = completedAt;
        log.DurationMs = durationMs;
        log.RetryCount = retryCount;
        log.BranchTaken = branchTaken;
        log.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (errorMessage != null)
            log.ErrorMessage = TruncateError(errorMessage);

        await _db.SaveChangesAsync();
    }

    // --- Failure recovery ---

    /// <summary>
    /// Best-effort attempt to create or update a node log entry when the outer catch
    /// is hit during node processing. Ensures the failing node always has a log record.
    /// </summary>
    internal async Task TryRecordFailingNodeLogAsync(
        FlowContext context, IFlowNode? node, int sequence, bool logAlreadyCreated,
        long startedAtMs, string errorMessage)
    {
        if (!context.Config.EnableNodeLogging || sequence <= 0 || _db == null) return;

        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var truncatedError = TruncateError(errorMessage);

            if (logAlreadyCreated)
            {
                var log = await _db.FlowNodeLog
                    .FirstOrDefaultAsync(l => l.ServiceId == context.ServiceId && l.Sequence == sequence);
                if (log != null)
                {
                    log.Status = FlowNodeStatus.Failed.ToString().ToLowerInvariant();
                    log.ErrorMessage = truncatedError;
                    log.CompletedAt = now / 1000;
                    log.DurationMs = now - startedAtMs;
                    log.TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await _db.SaveChangesAsync();
                }
            }
            else
            {
                var nodeName = "unknown";
                var nodeType = FlowNodeType.Custom.ToString();
                try
                {
                    if (node != null)
                    {
                        nodeName = node.Name;
                        nodeType = ResolveNodeType(node).ToString();
                    }
                }
                catch { /* Name/NodeType getter may itself be the source of the exception */ }

                var log = new FlowNodeLog
                {
                    ServiceId = context.ServiceId,
                    NodeName = nodeName,
                    NodeType = nodeType,
                    Sequence = sequence,
                    Status = FlowNodeStatus.Failed.ToString().ToLowerInvariant(),
                    InputData = SerializeNodeInput(node),
                    ErrorMessage = truncatedError,
                    StartedAt = startedAtMs / 1000,
                    CompletedAt = now / 1000,
                    DurationMs = now - startedAtMs,
                    TimeCreated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    TimeUpdated = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                await _db.FlowNodeLog.AddAsync(log);
                await _db.SaveChangesAsync();
            }
        }
        catch
        {
            // Best effort — swallow if the database is unavailable
        }
    }

    // --- Serialization ---

    private static readonly JsonSerializerOptions _logJsonOptions = new()
    {
        WriteIndented = false,
        MaxDepth = 5
    };

    private static readonly HashSet<string> _skipProperties = new()
    {
        "Name", "ProgressMessage"
    };

    /// <summary>
    /// Serializes a node's public properties (excluding Name, ProgressMessage) to JSON.
    /// Used for the InputData column of FlowNodeLog.
    /// </summary>
    internal static string SerializeNodeInput(IFlowNode? node)
    {
        if (node == null) return "{}";
        try
        {
            var dict = new Dictionary<string, object?>();

            foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || _skipProperties.Contains(prop.Name)) continue;
                if (!IsSerializableType(prop.PropertyType)) continue;

                var value = prop.GetValue(node);
                if (value is HttpMethod httpMethod)
                    value = httpMethod.Method;

                if (value != null)
                    dict[prop.Name] = value;
            }

            return dict.Count > 0
                ? JsonSerializer.Serialize(dict, _logJsonOptions)
                : "{}";
        }
        catch
        {
            return "{}";
        }
    }

    /// <summary>
    /// Safely serializes any object to JSON, falling back to ToString on failure.
    /// Used for the OutputData column of FlowNodeLog.
    /// </summary>
    internal static string SafeSerialize(object? value)
    {
        if (value == null) return "{}";
        try
        {
            return JsonSerializer.Serialize(value, _logJsonOptions);
        }
        catch
        {
            return value.ToString() ?? "{}";
        }
    }

    /// <summary>
    /// Determines whether a Type is safe to serialize for node input logging.
    /// Supports primitives, strings, enums, HttpMethod, Dictionary, List, and arrays.
    /// </summary>
    internal static bool IsSerializableType(Type type)
    {
        if (type == typeof(string)) return true;
        if (type.IsPrimitive || type == typeof(decimal)) return true;
        if (type.IsEnum) return true;
        if (type == typeof(HttpMethod)) return true;
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return IsSerializableType(underlying);
        if (type.IsArray)
            return IsSerializableType(type.GetElementType()!);
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(Dictionary<,>))
            {
                var args = type.GetGenericArguments();
                return args[0] == typeof(string) && IsSerializableType(args[1]);
            }
            if (genericDef == typeof(List<>) || genericDef == typeof(IList<>) ||
                genericDef == typeof(IReadOnlyList<>))
            {
                return IsSerializableType(type.GetGenericArguments()[0]);
            }
        }
        return false;
    }

    // --- Utilities ---

    /// <summary>
    /// Truncates an error message to 500 characters.
    /// </summary>
    internal static string TruncateError(string error) =>
        error.Length > 500 ? error[..500] : error;
}
