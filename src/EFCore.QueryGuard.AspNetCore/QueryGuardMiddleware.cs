using EFCore.QueryGuard.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EFCore.QueryGuard.AspNetCore;

/// <summary>
/// ASP.NET Core middleware that wraps each HTTP request in a QueryGuard tracking scope.
/// <para>
/// Violations are logged at <c>Warning</c> level and also surfaced via the
/// <c>X-QueryGuard-Violations</c> response header so they are visible in browser dev tools
/// and API clients without requiring log access.
/// </para>
/// </summary>
public sealed class QueryGuardMiddleware
{
    private const string ViolationCountHeader = "X-QueryGuard-Violations";

    private readonly RequestDelegate _next;
    private readonly IQueryGuardScope _scope;
    private readonly ILogger<QueryGuardMiddleware> _logger;

    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="scope">
    /// The QueryGuard scope manager (registered as a singleton by <see cref="QueryGuardServiceExtensions"/>).
    /// </param>
    /// <param name="logger">Logger for per-request violation summaries.</param>
    public QueryGuardMiddleware(
        RequestDelegate next,
        IQueryGuardScope scope,
        ILogger<QueryGuardMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Begins a scope, invokes the rest of the pipeline, then collects and reports violations.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        _scope.BeginScope();

        try
        {
            await _next(context);
        }
        finally
        {
            // EndScope is called in finally so violations are always collected,
            // even when the downstream middleware throws.
            var violations = _scope.EndScope();

            if (violations.Count > 0)
            {
                var path = context.Request.Path.Value ?? "/";

                _logger.LogWarning(
                    "[QueryGuard] {Count} violation(s) on {Method} {Path}",
                    violations.Count,
                    context.Request.Method,
                    path);

                foreach (var violation in violations)
                    _logger.LogWarning("  {Violation}", violation);

                // Only mutate headers when the response has not yet started streaming.
                if (!context.Response.HasStarted)
                    context.Response.Headers[ViolationCountHeader] = violations.Count.ToString();
            }
        }
    }
}
