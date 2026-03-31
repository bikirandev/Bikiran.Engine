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
    public FlowNodeType NodeType => FlowNodeType.HttpRequest;

    /// <inheritdoc />
    public string? ProgressMessage { get; set; }

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

    /// <summary>Context key where the response body is stored. Defaults to "{Name}_Result".</summary>
    public string? OutputKey { get; set; }

    /// <summary>If set, the response must match this HTTP status code or the node fails.</summary>
    public int? ExpectStatusCode { get; set; }

    /// <summary>
    /// JSON validation expression evaluated against the parsed response.
    /// Supports $val references with ==, !=, >=, <=, >, &lt;, &amp;&amp;, || operators.
    /// Example: "$val.status == \"ok\""
    /// </summary>
    public string? ExpectValue { get; set; }

    public HttpRequestNode(string name)
    {
        FlowNodeNameValidator.Validate(name);
        Name = name;
    }

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken)
    {
        var outputKey = OutputKey ?? $"{Name}_Result";

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
                    var validationError = ExpressionEvaluator.ValidateJsonExpression(responseBody, ExpectValue);
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
}
