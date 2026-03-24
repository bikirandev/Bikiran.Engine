using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bikiran.Engine.Extensions;

/// <summary>
/// Extension methods for mapping admin API endpoints in the application pipeline.
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Maps all Bikiran.Engine admin REST endpoints under /api/bikiran-engine/*.
    /// When RequireAuthentication is enabled in BikiranEngineOptions, endpoints
    /// are protected with the configured authorization policy.
    /// Call this from app.MapBikiranEngineEndpoints() in Program.cs.
    /// </summary>
    public static IEndpointRouteBuilder MapBikiranEngineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetService<BikiranEngineOptions>();

        if (options?.RequireAuthentication == true)
        {
            var builder = endpoints.MapControllers();

            if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
                builder.RequireAuthorization(options.AuthorizationPolicy);
            else
                builder.RequireAuthorization();
        }
        else
        {
            endpoints.MapControllers();
        }

        return endpoints;
    }
}
