using System.Text.RegularExpressions;

namespace Bikiran.Engine.Core;

/// <summary>
/// Evaluates simple boolean expressions against FlowContext values.
/// Supports $ctx.key references, comparison and logical operators.
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>
    /// Evaluates an expression string against a FlowContext.
    /// Supports $ctx.key references, ==, !=, >, &lt;, >=, &lt;=, &amp;&amp;, || operators.
    /// </summary>
    public static bool Evaluate(string expression, FlowContext context)
    {
        // Replace $ctx.key references with actual values from context
        var resolved = Regex.Replace(expression, @"\$ctx\.(\w+(?:\.\w+)*)", match =>
        {
            var key = match.Groups[1].Value;
            var val = context.Get<object>(key);
            if (val == null) return "null";

            return val switch
            {
                string s => $"\"{s}\"",
                bool b => b ? "true" : "false",
                int or long or float or double or decimal => val.ToString() ?? "null",
                _ => $"\"{val}\""
            };
        });

        return EvaluateCondition(resolved);
    }

    /// <summary>
    /// Evaluates a simple expression against a parsed JSON response.
    /// Uses $val.field references. Returns null on success or an error message on failure.
    /// </summary>
    public static string? ValidateJsonExpression(string json, string expression)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var resolved = Regex.Replace(expression, @"\$val\.(\w+(?:\.\w+)*)", match =>
            {
                var path = match.Groups[1].Value.Split('.');
                var current = root;
                foreach (var segment in path)
                {
                    if (current.ValueKind != System.Text.Json.JsonValueKind.Object ||
                        !current.TryGetProperty(segment, out current))
                        return "null";
                }

                return current.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => $"\"{current.GetString()}\"",
                    System.Text.Json.JsonValueKind.Number => current.GetRawText(),
                    System.Text.Json.JsonValueKind.True => "true",
                    System.Text.Json.JsonValueKind.False => "false",
                    _ => "null"
                };
            });

            if (!EvaluateCondition(resolved))
                return $"ExpectValue assertion failed: {expression}";

            return null;
        }
        catch (Exception ex)
        {
            return $"ExpectValue evaluation error: {ex.Message}";
        }
    }

    /// <summary>Evaluates a simplified boolean expression string.</summary>
    internal static bool EvaluateCondition(string condition)
    {
        if (condition.Contains("&&"))
        {
            var parts = condition.Split("&&", 2);
            return EvaluateCondition(parts[0].Trim()) && EvaluateCondition(parts[1].Trim());
        }
        if (condition.Contains("||"))
        {
            var parts = condition.Split("||", 2);
            return EvaluateCondition(parts[0].Trim()) || EvaluateCondition(parts[1].Trim());
        }

        foreach (var op in new[] { ">=", "<=", "!=", "==", ">", "<" })
        {
            var idx = condition.IndexOf(op, StringComparison.Ordinal);
            if (idx < 0) continue;

            var left = condition[..idx].Trim().Trim('"');
            var right = condition[(idx + op.Length)..].Trim().Trim('"');

            if (double.TryParse(left, out var l) && double.TryParse(right, out var r))
            {
                return op switch
                {
                    ">=" => l >= r,
                    "<=" => l <= r,
                    ">" => l > r,
                    "<" => l < r,
                    "==" => Math.Abs(l - r) < 1e-10,
                    "!=" => Math.Abs(l - r) > 1e-10,
                    _ => false
                };
            }

            return op switch
            {
                "==" => left == right,
                "!=" => left != right,
                _ => false
            };
        }

        return condition.Trim().ToLowerInvariant() == "true";
    }
}
