using Bikiran.Engine.Core;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bikiran.Engine.Definitions;

/// <summary>
/// Validates flow definition JSON structure before saving.
/// Returns a list of human-readable error messages.
/// </summary>
public class FlowJsonValidator
{
    private static readonly HashSet<string> KnownNodeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Wait", "HttpRequest", "EmailSend", "Transform",
        "IfElse", "Parallel", "Retry", "WhileLoop"
    };

    private static readonly Regex PascalCaseRegex = new(@"^[A-Z][a-zA-Z0-9]*$", RegexOptions.Compiled);

    /// <summary>
    /// Validates a flow JSON string. Returns an empty list if valid.
    /// </summary>
    public List<string> Validate(string flowJson)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(flowJson))
        {
            errors.Add("FlowJson is empty.");
            return errors;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(flowJson);
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON: {ex.Message}");
            return errors;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add("Root must be a JSON object.");
                return errors;
            }

            // name is required
            if (!root.TryGetProperty("name", out var nameProp) ||
                string.IsNullOrWhiteSpace(nameProp.GetString()))
            {
                errors.Add("Missing or empty 'name' property.");
            }

            // config validation (optional)
            if (root.TryGetProperty("config", out var configEl))
                ValidateConfig(configEl, errors);

            // nodes array is required
            if (!root.TryGetProperty("nodes", out var nodesEl))
            {
                errors.Add("Missing 'nodes' array.");
            }
            else if (nodesEl.ValueKind != JsonValueKind.Array)
            {
                errors.Add("'nodes' must be an array.");
            }
            else if (nodesEl.GetArrayLength() == 0)
            {
                errors.Add("'nodes' array must contain at least one node.");
            }
            else
            {
                var nodeNames = new HashSet<string>();
                var idx = 0;
                foreach (var nodeEl in nodesEl.EnumerateArray())
                {
                    ValidateNode(nodeEl, $"nodes[{idx}]", errors, nodeNames);
                    idx++;
                }
            }
        }

        return errors;
    }

    private static void ValidateConfig(JsonElement el, List<string> errors)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            errors.Add("'config' must be an object.");
            return;
        }

        if (el.TryGetProperty("maxExecutionTimeSeconds", out var maxTime))
        {
            if (maxTime.ValueKind != JsonValueKind.Number || maxTime.GetInt32() <= 0)
                errors.Add("config.maxExecutionTimeSeconds must be a positive number.");
        }

        if (el.TryGetProperty("onFailure", out var onFail))
        {
            var val = onFail.GetString();
            if (val != "Stop" && val != "Continue")
                errors.Add("config.onFailure must be 'Stop' or 'Continue'.");
        }
    }

    private void ValidateNode(JsonElement el, string path, List<string> errors, HashSet<string> nodeNames)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}: node must be an object.");
            return;
        }

        // type is required
        if (!el.TryGetProperty("type", out var typeProp) ||
            string.IsNullOrWhiteSpace(typeProp.GetString()))
        {
            errors.Add($"{path}: missing or empty 'type' property.");
            return;
        }

        var type = typeProp.GetString()!;

        // name is required
        if (!el.TryGetProperty("name", out var nameProp) ||
            string.IsNullOrWhiteSpace(nameProp.GetString()))
        {
            errors.Add($"{path}: missing or empty 'name' property.");
            return;
        }

        var name = nameProp.GetString()!;
        if (!nodeNames.Add(name))
            errors.Add($"{path}: duplicate node name '{name}'.");

        if (!PascalCaseRegex.IsMatch(name))
            errors.Add($"{path}: node name '{name}' must be PascalCase (start with uppercase, no spaces or special characters). Example: 'FetchOrder', 'SendEmail'.");

        // Type-specific validation
        switch (type)
        {
            case "Wait":
                ValidateWaitParams(el, path, errors);
                break;
            case "HttpRequest":
                ValidateHttpRequestParams(el, path, errors);
                break;
            case "EmailSend":
                ValidateEmailSendParams(el, path, errors);
                break;
            case "Transform":
                // Transform is flexible, no strict validation required
                break;
            case "IfElse":
                ValidateIfElseNode(el, path, errors, nodeNames);
                break;
            case "Parallel":
                ValidateParallelNode(el, path, errors, nodeNames);
                break;
            case "Retry":
                ValidateRetryNode(el, path, errors, nodeNames);
                break;
            case "WhileLoop":
                ValidateWhileLoopNode(el, path, errors, nodeNames);
                break;
            default:
                // Custom node type — accepted but not deeply validated
                if (!FlowDefinitionParser.IsCustomNodeRegistered(type))
                    errors.Add($"{path}: unknown node type '{type}'. Register it via options.RegisterNode<T>(\"{type}\") or use a built-in type.");
                break;
        }
    }

    private static void ValidateWaitParams(JsonElement el, string path, List<string> errors)
    {
        if (!el.TryGetProperty("params", out var p)) return;
        if (p.TryGetProperty("delayMs", out var delay) &&
            (delay.ValueKind != JsonValueKind.Number || delay.GetInt32() < 0))
            errors.Add($"{path}: params.delayMs must be a non-negative number.");
    }

    private static void ValidateHttpRequestParams(JsonElement el, string path, List<string> errors)
    {
        if (!el.TryGetProperty("params", out var p))
        {
            errors.Add($"{path}: HttpRequest requires 'params' with at least 'url'.");
            return;
        }

        if (!p.TryGetProperty("url", out var url) || string.IsNullOrWhiteSpace(url.GetString()))
            errors.Add($"{path}: params.url is required for HttpRequest.");
    }

    private static void ValidateEmailSendParams(JsonElement el, string path, List<string> errors)
    {
        if (!el.TryGetProperty("params", out var p))
        {
            errors.Add($"{path}: EmailSend requires 'params'.");
            return;
        }

        if (!p.TryGetProperty("toEmail", out var to) || string.IsNullOrWhiteSpace(to.GetString()))
            errors.Add($"{path}: params.toEmail is required for EmailSend.");
    }

    private void ValidateIfElseNode(JsonElement el, string path, List<string> errors, HashSet<string> nodeNames)
    {
        if (!el.TryGetProperty("params", out var p) ||
            !p.TryGetProperty("condition", out var cond) ||
            string.IsNullOrWhiteSpace(cond.GetString()))
        {
            errors.Add($"{path}: IfElse requires params.condition.");
        }

        if (el.TryGetProperty("trueBranch", out var tb))
            ValidateNodeArray(tb, $"{path}.trueBranch", errors, nodeNames);
        if (el.TryGetProperty("falseBranch", out var fb))
            ValidateNodeArray(fb, $"{path}.falseBranch", errors, nodeNames);
    }

    private void ValidateParallelNode(JsonElement el, string path, List<string> errors, HashSet<string> nodeNames)
    {
        if (!el.TryGetProperty("branches", out var branches))
        {
            errors.Add($"{path}: Parallel requires 'branches' array.");
            return;
        }
        if (branches.ValueKind != JsonValueKind.Array || branches.GetArrayLength() == 0)
        {
            errors.Add($"{path}: 'branches' must be a non-empty array of arrays.");
            return;
        }

        var bi = 0;
        foreach (var branch in branches.EnumerateArray())
        {
            ValidateNodeArray(branch, $"{path}.branches[{bi}]", errors, nodeNames);
            bi++;
        }
    }

    private void ValidateRetryNode(JsonElement el, string path, List<string> errors, HashSet<string> nodeNames)
    {
        if (!el.TryGetProperty("inner", out var inner))
        {
            errors.Add($"{path}: Retry requires 'inner' node definition.");
            return;
        }
        ValidateNode(inner, $"{path}.inner", errors, nodeNames);

        if (el.TryGetProperty("params", out var p) && p.TryGetProperty("maxAttempts", out var ma))
        {
            if (ma.ValueKind != JsonValueKind.Number || ma.GetInt32() < 1)
                errors.Add($"{path}: params.maxAttempts must be >= 1.");
        }
    }

    private void ValidateWhileLoopNode(JsonElement el, string path, List<string> errors, HashSet<string> nodeNames)
    {
        if (!el.TryGetProperty("params", out var p) ||
            !p.TryGetProperty("condition", out var cond) ||
            string.IsNullOrWhiteSpace(cond.GetString()))
        {
            errors.Add($"{path}: WhileLoop requires params.condition.");
        }

        if (el.TryGetProperty("body", out var body))
            ValidateNodeArray(body, $"{path}.body", errors, nodeNames);
        else
            errors.Add($"{path}: WhileLoop requires 'body' node array.");

        if (p.TryGetProperty("maxIterations", out var mi) &&
            (mi.ValueKind != JsonValueKind.Number || mi.GetInt32() < 1))
            errors.Add($"{path}: params.maxIterations must be >= 1.");
    }

    private void ValidateNodeArray(JsonElement el, string path, List<string> errors, HashSet<string> nodeNames)
    {
        if (el.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{path}: expected an array of node definitions.");
            return;
        }

        var idx = 0;
        foreach (var nodeEl in el.EnumerateArray())
        {
            ValidateNode(nodeEl, $"{path}[{idx}]", errors, nodeNames);
            idx++;
        }
    }
}
