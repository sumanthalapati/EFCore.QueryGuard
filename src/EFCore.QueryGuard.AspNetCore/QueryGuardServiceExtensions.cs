using EFCore.QueryGuard;
using EFCore.QueryGuard.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EFCore.QueryGuard.AspNetCore;

/// <summary>
/// Extension methods for registering QueryGuard with the ASP.NET Core DI container
/// and middleware pipeline.
/// </summary>
public static class QueryGuardServiceExtensions
{
    /// <summary>
    /// Registers QueryGuard services:
    /// <list type="bullet">
    ///   <item><see cref="QueryGuardOptions"/> via the Options Pattern.</item>
    ///   <item><see cref="QueryGuardInterceptor"/> as a singleton (also exposed as <see cref="IQueryGuardScope"/>).</item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Optional delegate to customise <see cref="QueryGuardOptions"/>.</param>
    /// <returns><paramref name="services"/> for call chaining.</returns>
    public static IServiceCollection AddQueryGuard(
        this IServiceCollection services,
        Action<QueryGuardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register options via the Options Pattern.
        // The validator surfaces misconfigured values at startup (or first resolution).
        services.AddOptions<QueryGuardOptions>();
        if (configure is not null)
            services.Configure<QueryGuardOptions>(configure);

        // IValidateOptions<T> — called by the Options infrastructure before the
        // first IOptions<QueryGuardOptions>.Value access.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<QueryGuardOptions>,
                QueryGuardOptionsValidator>());

        // Validate eagerly at startup so misconfiguration surfaces immediately,
        // not silently at the first request.
        services.AddOptions<QueryGuardOptions>().ValidateOnStart();

        // Register the interceptor as a singleton so it can be shared between
        // the EF Core interceptor pipeline (per-DbContext) and the middleware (per-request).
        services.TryAddSingleton<QueryGuardInterceptor>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<QueryGuardOptions>>().Value;
            var logger = sp.GetService<ILogger<QueryGuardInterceptor>>();
            return new QueryGuardInterceptor(options, logger);
        });

        // Expose via interface so consumers (e.g. middleware, tests) depend on the
        // abstraction rather than the concrete EF Core interceptor type.
        services.TryAddSingleton<IQueryGuardScope>(
            sp => sp.GetRequiredService<QueryGuardInterceptor>());

        return services;
    }

    /// <summary>
    /// Adds <see cref="QueryGuardMiddleware"/> to the request pipeline.
    /// Must be called after <see cref="AddQueryGuard"/> has been invoked on the service collection.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns><paramref name="app"/> for call chaining.</returns>
    public static IApplicationBuilder UseQueryGuard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<QueryGuardMiddleware>();
    }
}
