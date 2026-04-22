using Bikiran.Engine.Core;
using Bikiran.Engine.Nodes;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Bikiran.Engine.Definitions;

/// <summary>
/// Parses a flow definition JSON string into a FlowBuilder ready for execution.
/// Supports built-in node types (including IfElse, Parallel, Retry, WhileLoop)
/// and custom-registered node types.
/// </summary>
public class FlowDefinitionParser
{
    // Registry of custom node types added via BikiranEngineOptions.RegisterNode<T>()
    private static readonly ConcurrentDictionary<string, Type> _customNodeTypes = new();

    /// <summary>Registers a custom node type for use in JSON flow definitions.</summary>
    public static void RegisterNode<T>(string typeName) where T : IFlowNode
    {
        _customNodeTypes[typeName] = typeof(T);
    }

    /// <summary>Returns true if a custom node type is registered with the given name.</summary>
    public static bool IsCustomNodeRegistered(string typeName) => _customNodeTypes.ContainsKey(typeName);

    /// <summary>
    /// Parses the flow JSON and returns a configured FlowBuilder.
    /// Placeholders in the form {{key}} are replaced with values from the parameters dictionary.
    /// </summary>
    public FlowBuilder Parse(string flowJson, Dictionary<string, string>? parameters = null)
    {
        // Replace all {{key}} placeholders with actual values
        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
                flowJson = flowJson.Replace($"{{{{{key}}}}}", value);
        }

        using var doc = JsonDocument.Parse(flowJson);
        var root = doc.RootElement;

        var flowName = root.TryGetProperty("name", out var nameProp)
            ? nameProp.GetString() ?? "unnamed_flow"
            : "unnamed_flow";

        var builder = FlowBuilder.Create(flowName);

        // Apply optional config block
        if (root.TryGetProperty("config", out var configEl))
        {
            builder.Configure(cfg =>
            {
                if (configEl.TryGetProperty("maxExecutionTimeSeconds", out var maxTime))
                    cfg.MaxExecutionTime = TimeSpan.FromSeconds(maxTime.GetInt32());
                if (configEl.TryGetProperty("onFailure", out var onFailure))
                    cfg.OnFailure = onFailure.GetString() == "Continue"
                        ? OnFailureAction.Continue : OnFailureAction.Stop;
                if (configEl.TryGetProperty("enableNodeLogging", out var logging))
                    cfg.EnableNodeLogging = logging.GetBoolean();
            });
        }

        // Build nodes
        if (root.TryGetProperty("nodes", out var nodesEl))
        {
            foreach (var nodeEl in nodesEl.EnumerateArray())
            {
                var node = BuildNode(nodeEl);
                if (node != null)
                    builder.AddNode(node);
            }
        }

        return builder;
    }

    /// <summary>Recursively builds a node from a JSON element, including nested structures.</summary>
    internal IFlowNode? BuildNode(JsonElement el)
    {
        var type = el.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var paramsEl = el.TryGetProperty("params", out var p) ? p : (JsonElement?)null;

        var node = type switch
        {
            "Starting" => (IFlowNode)BuildStartingNode(name, paramsEl),
            "Ending" => BuildEndingNode(name, paramsEl),
            "Wait" => BuildWaitNode(name, paramsEl),
            "HttpRequest" => BuildHttpRequestNode(name, paramsEl),
            "EmailSend" => BuildEmailSendNode(name, paramsEl),
            "Transform" => BuildTransformNode(name, paramsEl),
            "IfElse" => (IFlowNode?)BuildIfElseNode(name, paramsEl, el),
            "Parallel" => BuildParallelNode(name, paramsEl, el),
            "Retry" => BuildRetryNode(name, paramsEl, el),
            "WhileLoop" => BuildWhileLoopNode(name, paramsEl, el),
            _ => BuildCustomNode(type, name, paramsEl)
        };

        // Apply optional progressMessage from JSON params
        if (node != null && paramsEl.HasValue &&
            paramsEl.Value.TryGetProperty("progressMessage", out var pm))
        {
            node.ProgressMessage = pm.GetString();
        }

        return node;
    }

    /// <summary>Parses a JSON array of node definitions into a list of IFlowNode.</summary>
    private List<IFlowNode> ParseNodeList(JsonElement arrayEl)
    {
        var nodes = new List<IFlowNode>();
        foreach (var nodeEl in arrayEl.EnumerateArray())
        {
            var node = BuildNode(nodeEl);
            if (node != null)
                nodes.Add(node);
        }
        return nodes;
    }

    private static StartingNode BuildStartingNode(string name, JsonElement? p)
    {
        var node = new StartingNode(string.IsNullOrEmpty(name) ? "StartingNode" : name);
        if (p.HasValue && p.Value.TryGetProperty("waitTimeMs", out var wt))
            node.WaitTime = TimeSpan.FromMilliseconds(wt.GetInt32());
        return node;
    }

    private static EndingNode BuildEndingNode(string name, JsonElement? p)
    {
        return new EndingNode(string.IsNullOrEmpty(name) ? "EndingNode" : name);
    }

    private static WaitNode BuildWaitNode(string name, JsonElement? p)
    {
        var node = new WaitNode(name);
        if (p.HasValue && p.Value.TryGetProperty("delayMs", out var delay))
            node.Delay = TimeSpan.FromMilliseconds(delay.GetInt32());
        return node;
    }

    private static HttpRequestNode BuildHttpRequestNode(string name, JsonElement? p)
    {
        var node = new HttpRequestNode(name);
        if (!p.HasValue) return node;

        if (p.Value.TryGetProperty("url", out var url)) node.Url = url.GetString() ?? "";
        if (p.Value.TryGetProperty("method", out var method))
            node.Method = new HttpMethod(method.GetString() ?? "GET");
        if (p.Value.TryGetProperty("maxRetries", out var retries)) node.MaxRetries = retries.GetInt32();
        if (p.Value.TryGetProperty("timeoutSeconds", out var timeout)) node.TimeoutSeconds = timeout.GetInt32();
        if (p.Value.TryGetProperty("outputKey", out var ok)) node.OutputKey = ok.GetString();
        if (p.Value.TryGetProperty("body", out var body)) node.Body = body.GetString();
        if (p.Value.TryGetProperty("expectStatusCode", out var sc)) node.ExpectStatusCode = sc.GetInt32();
        if (p.Value.TryGetProperty("expectValue", out var ev)) node.ExpectValue = ev.GetString();

        if (p.Value.TryGetProperty("headers", out var headers))
        {
            foreach (var h in headers.EnumerateObject())
                node.Headers[h.Name] = h.Value.GetString() ?? "";
        }

        // SSRF protection: allowedHosts validation
        if (p.Value.TryGetProperty("allowedHosts", out var allowedHosts) &&
            !string.IsNullOrEmpty(node.Url))
        {
            if (Uri.TryCreate(node.Url, UriKind.Absolute, out var uri))
            {
                var hosts = allowedHosts.EnumerateArray()
                    .Select(h => h.GetString() ?? "")
                    .Where(h => !string.IsNullOrEmpty(h))
                    .ToList();

                if (hosts.Count > 0 && !hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"SSRF protection: host '{uri.Host}' is not in the allowedHosts list for node '{name}'.");
                }
            }
        }

        return node;
    }

    private static EmailSendNode BuildEmailSendNode(string name, JsonElement? p)
    {
        var node = new EmailSendNode(name);
        if (!p.HasValue) return node;

        if (p.Value.TryGetProperty("toEmail", out var to)) node.ToEmail = to.GetString() ?? "";
        if (p.Value.TryGetProperty("toName", out var toName)) node.ToName = toName.GetString() ?? "";
        if (p.Value.TryGetProperty("subject", out var subj)) node.Subject = subj.GetString() ?? "";
        if (p.Value.TryGetProperty("credentialName", out var cred)) node.CredentialName = cred.GetString();
        if (p.Value.TryGetProperty("template", out var tmpl)) node.Template = tmpl.GetString();
        if (p.Value.TryGetProperty("htmlBody", out var html)) node.HtmlBody = html.GetString();
        if (p.Value.TryGetProperty("textBody", out var text)) node.TextBody = text.GetString();

        if (p.Value.TryGetProperty("placeholders", out var ph))
        {
            foreach (var item in ph.EnumerateObject())
                node.Placeholders[item.Name] = item.Value.GetString() ?? "";
        }

        return node;
    }

    private static TransformNode BuildTransformNode(string name, JsonElement? p)
    {
        var node = new TransformNode(name);
        if (!p.HasValue) return node;

        if (p.Value.TryGetProperty("outputKey", out var ok)) node.OutputKey = ok.GetString();

        // Static value transform: set a fixed value from the JSON params
        if (p.Value.TryGetProperty("value", out var val))
        {
            var staticValue = val.GetString();
            node.Transform = _ => staticValue;
        }

        return node;
    }

    private IfElseNode BuildIfElseNode(string name, JsonElement? p, JsonElement el)
    {
        var condition = p.HasValue && p.Value.TryGetProperty("condition", out var c)
            ? c.GetString() ?? "" : "";

        var node = new IfElseNode(name)
        {
            Condition = ctx => ExpressionEvaluator.Evaluate(condition, ctx)
        };

        if (el.TryGetProperty("trueBranch", out var trueBranch))
            node.TrueBranch = ParseNodeList(trueBranch);
        if (el.TryGetProperty("falseBranch", out var falseBranch))
            node.FalseBranch = ParseNodeList(falseBranch);

        return node;
    }

    private ParallelNode BuildParallelNode(string name, JsonElement? p, JsonElement el)
    {
        var node = new ParallelNode(name);

        if (p.HasValue)
        {
            if (p.Value.TryGetProperty("waitAll", out var wa)) node.WaitAll = wa.GetBoolean();
            if (p.Value.TryGetProperty("abortOnBranchFailure", out var aobf)) node.AbortOnBranchFailure = aobf.GetBoolean();
        }

        if (el.TryGetProperty("branches", out var branchesEl))
        {
            foreach (var branchEl in branchesEl.EnumerateArray())
                node.Branches.Add(ParseNodeList(branchEl));
        }

        return node;
    }

    private RetryNode BuildRetryNode(string name, JsonElement? p, JsonElement el)
    {
        IFlowNode? inner = null;
        if (el.TryGetProperty("inner", out var innerEl))
            inner = BuildNode(innerEl);

        var node = new RetryNode(name)
        {
            Inner = inner ?? new WaitNode(name + "_noop") { Delay = TimeSpan.Zero }
        };

        if (p.HasValue)
        {
            if (p.Value.TryGetProperty("maxAttempts", out var ma)) node.MaxAttempts = ma.GetInt32();
            if (p.Value.TryGetProperty("delayMs", out var d)) node.DelayMs = d.GetInt32();
            if (p.Value.TryGetProperty("backoffMultiplier", out var bm)) node.BackoffMultiplier = bm.GetDouble();
        }

        return node;
    }

    private WhileLoopNode BuildWhileLoopNode(string name, JsonElement? p, JsonElement el)
    {
        var condition = p.HasValue && p.Value.TryGetProperty("condition", out var c)
            ? c.GetString() ?? "" : "";

        var node = new WhileLoopNode(name)
        {
            Condition = ctx => ExpressionEvaluator.Evaluate(condition, ctx)
        };

        if (p.HasValue)
        {
            if (p.Value.TryGetProperty("maxIterations", out var mi)) node.MaxIterations = mi.GetInt32();
            if (p.Value.TryGetProperty("iterationDelayMs", out var id)) node.IterationDelayMs = id.GetInt32();
        }

        if (el.TryGetProperty("body", out var bodyEl))
            node.Body = ParseNodeList(bodyEl);

        return node;
    }

    private IFlowNode? BuildCustomNode(string type, string name, JsonElement? p)
    {
        if (!_customNodeTypes.TryGetValue(type, out var nodeType))
            return null;

        if (Activator.CreateInstance(nodeType, name) is not IFlowNode node)
            return null;

        // Populate matching public properties via reflection
        if (p.HasValue)
        {
            foreach (var prop in p.Value.EnumerateObject())
            {
                if (string.IsNullOrEmpty(prop.Name)) continue;

                var property = nodeType.GetProperty(
                    char.ToUpperInvariant(prop.Name[0]) + (prop.Name.Length > 1 ? prop.Name[1..] : ""));

                if (property == null || !property.CanWrite) continue;

                try
                {
                    var value = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => (object?)prop.Value.GetString(),
                        JsonValueKind.Number when property.PropertyType == typeof(int) => prop.Value.GetInt32(),
                        JsonValueKind.Number when property.PropertyType == typeof(double) => prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => prop.Value.GetString()
                    };

                    if (value != null)
                        property.SetValue(node, value);
                }
                catch
                {
                    // Skip properties that can't be set via reflection
                }
            }
        }

        return node;
    }
}
