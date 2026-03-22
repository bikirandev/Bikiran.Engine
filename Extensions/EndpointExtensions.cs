using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Bikiran.Engine.Extensions;

/// <summary>
/// Extension methods for mapping admin API endpoints in the application pipeline.
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Maps all Bikiran.Engine admin REST endpoints under /api/bikiran-engine/*.
    /// Call this from app.MapBikiranEngineEndpoints() in Program.cs.
    /// </summary>
    public static IEndpointRouteBuilder MapBikiranEngineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllers();
        return endpoints;
    }
}
