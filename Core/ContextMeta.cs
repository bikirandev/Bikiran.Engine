using Microsoft.AspNetCore.Http;

namespace Bikiran.Engine.Core;

/// <summary>
/// Captures caller metadata from the originating HTTP request.
/// Stored as JSON in FlowRun.ContextMeta.
/// </summary>
internal class ContextMeta
{
    public string IpAddress { get; set; } = "";
    public long UserId { get; set; }
    public string RequestPath { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public long Timestamp { get; set; }

    /// <summary>Builds a ContextMeta snapshot from an HTTP context.</summary>
    internal static ContextMeta FromHttpContext(HttpContext? httpContext)
    {
        if (httpContext == null)
            return new ContextMeta { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

        return new ContextMeta
        {
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            RequestPath = httpContext.Request.Path.ToString(),
            UserAgent = httpContext.Request.Headers["User-Agent"].ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }
}
