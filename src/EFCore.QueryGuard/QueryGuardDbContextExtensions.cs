using Microsoft.EntityFrameworkCore;

namespace EFCore.QueryGuard;

/// <summary>
/// Extension methods for attaching QueryGuard to a <see cref="DbContextOptionsBuilder"/>
/// without using the ASP.NET Core DI container.
/// </summary>
public static class QueryGuardDbContextExtensions
{
    /// <summary>
    /// Attaches a <see cref="QueryGuardInterceptor"/> configured via a delegate.
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="configure">
    /// Optional delegate that mutates a <see cref="QueryGuardOptions"/> instance.
    /// When omitted, all defaults apply.
    /// </param>
    /// <returns><paramref name="builder"/> for call chaining.</returns>
    /// <example>
    /// <code>
    /// optionsBuilder.UseQueryGuard(o =>
    /// {
    ///     o.SlowQueryThresholdMs = 300;
    ///     o.NPlusOneThreshold    = 2;
    /// });
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UseQueryGuard(
        this DbContextOptionsBuilder builder,
        Action<QueryGuardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new QueryGuardOptions();
        configure?.Invoke(options);
        options.EnsureValid();

        return builder.AddInterceptors(new QueryGuardInterceptor(options));
    }

    /// <summary>
    /// Attaches a <see cref="QueryGuardInterceptor"/> configured via the fluent
    /// <see cref="QueryGuardOptionsBuilder"/>.
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="build">
    /// Delegate that receives a fresh <see cref="QueryGuardOptionsBuilder"/> and returns
    /// the same instance after applying configuration.
    /// </param>
    /// <returns><paramref name="builder"/> for call chaining.</returns>
    /// <example>
    /// <code>
    /// optionsBuilder.UseQueryGuard(b => b
    ///     .WithSlowQueryThreshold(300)
    ///     .WithNPlusOneDetection(threshold: 2));
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UseQueryGuard(
        this DbContextOptionsBuilder builder,
        Func<QueryGuardOptionsBuilder, QueryGuardOptionsBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(build);

        var options = build(new QueryGuardOptionsBuilder()).Build();
        return builder.AddInterceptors(new QueryGuardInterceptor(options));
    }
}
