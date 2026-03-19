using Bikiran.Engine.Core;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bikiran.Engine.Nodes;

/// <summary>
/// Makes an outbound HTTP request with built-in retry support and optional response validation.
/// Note: a new <see cref="HttpClient"/> is created per execution. For high-throughput scenarios,
/// consider resolving an <c>IHttpClientFactory</c>-managed client via <see cref="FlowContext.Services"/>.
/// </summary>
public class HttpRequestNode : IFlowNode
{
    public string Name { get; }
    public string NodeType => "HttpRequest";

    /// <summary>Target URL (required).</summary>
    public string Url { get; set; } = "";

    /// <summary>HTTP method. Default is GET.</summary>
    public HttpMethod Method { get; set; } = HttpMethod.Get;

    /// <summary>Additional request headers.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>Request body as a JSON string (for POST/PUT/PATCH).</summary>
    public string? Body { get; set; }

    /// <summary>Per-attempt timeout in seconds. Default is 30.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum number of retry attempts. Default is 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Seconds to wait between retries. Default is 2.</summary>
    public int RetryDelaySeconds { get; set; } = 2;

    /// <summary>Context key where the response body is stored. Defaults to "{Name}_response".</summary>
    public string? OutputKey { get; set; }

    /// <summary>If set, the response must match this HTTP status code or the node fails.</summary>
    public int? ExpectStatusCode { get; set; }

    /// <summary>
    /// JSON validation expression evaluated against the parsed response.
    /// Supports $val references with ==, !=, >=, <=, >, &lt;, &amp;&amp;, || operators.
    /// Example: "$val.status == \"ok\""
    /// </summary>
    public string? ExpectValue { get; set; }

    public HttpRequestNode(string name) => Name = name;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        var outputKey = OutputKey ?? $"{Name}_response";

        if (string.IsNullOrWhiteSpace(Url))
            return NodeResult.Fail("HttpRequestNode: Url is required.");

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };

        string? responseBody = null;
        Exception? lastException = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken);

            try
            {
                var request = new HttpRequestMessage(Method, Url);

                foreach (var (key, value) in Headers)
                    request.Headers.TryAddWithoutValidation(key, value);

                if (Body != null)
                    request.Content = new StringContent(Body, Encoding.UTF8, "application/json");

                var response = await httpClient.SendAsync(request, cancellationToken);

                if (ExpectStatusCode.HasValue && (int)response.StatusCode != ExpectStatusCode.Value)
                    return NodeResult.Fail(
                        $"Expected HTTP {ExpectStatusCode}, got {(int)response.StatusCode}.");

                responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                context.Set(outputKey, responseBody);

                if (!string.IsNullOrWhiteSpace(ExpectValue))
                {
                    var validationError = ValidateExpression(responseBody, ExpectValue);
                    if (validationError != null)
                        return NodeResult.Fail(validationError);
                }

                return NodeResult.Ok(responseBody);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
            }
        }

        return NodeResult.Fail($"HTTP request failed after {MaxRetries + 1} attempt(s): {lastException?.Message}");
    }

    /// <summary>
    /// Evaluates a simple expression against the parsed JSON response.
    /// Returns null on success or an error message on failure.
    /// </summary>
    private static string? ValidateExpression(string json, string expression)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Replace $val.field references with their actual values from the JSON
            var resolved = Regex.Replace(expression, @"\$val\.(\w+(?:\.\w+)*)", match =>
            {
                var path = match.Groups[1].Value.Split('.');
                var current = root;
                foreach (var segment in path)
                {
                    if (current.ValueKind != JsonValueKind.Object ||
                        !current.TryGetProperty(segment, out current))
                        return "null";
                }

                return current.ValueKind switch
                {
                    JsonValueKind.String => $"\"{current.GetString()}\"",
                    JsonValueKind.Number => current.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
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
    private static bool EvaluateCondition(string condition)
    {
        // Handle && and || by splitting at the operator
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

        // Check each operator in order (longest first to avoid prefix conflicts)
        foreach (var op in new[] { ">=", "<=", "!=", "==", ">", "<" })
        {
            var idx = condition.IndexOf(op, StringComparison.Ordinal);
            if (idx < 0) continue;

            var left = condition[..idx].Trim().Trim('"');
            var right = condition[(idx + op.Length)..].Trim().Trim('"');

            // Try numeric comparison first
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

            // Fall back to string comparison
            return op switch
            {
                "==" => left == right,
                "!=" => left != right,
                _ => false
            };
        }

        // Treat the whole condition as a boolean literal
        return condition.Trim().ToLowerInvariant() == "true";
    }
}
