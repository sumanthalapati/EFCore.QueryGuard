using EFCore.QueryGuard.Abstractions;

namespace EFCore.QueryGuard.Detectors;

/// <summary>
/// Flags the moment the total query count in a scope crosses
/// <see cref="QueryGuardOptions.MaxQueriesPerScope"/>.
/// Fires <em>exactly once</em> — at the crossing point — to avoid log spam.
/// </summary>
internal sealed class ExcessiveQueryCountDetector : ViolationDetectorBase
{
    /// <inheritdoc/>
    public override QueryViolation? Detect(DetectionContext context, QueryGuardOptions options)
    {
        // Fire only at the exact crossing point (MaxQueriesPerScope + 1).
        if (context.TotalQueriesInScope != options.MaxQueriesPerScope + 1)
            return null;

        return new QueryViolation
        {
            Type        = ViolationType.ExcessiveQueryCount,
            Sql         = context.Sql,
            ElapsedMs   = context.ElapsedMs,
            TotalQueriesInScope = context.TotalQueriesInScope,
            Message     = $"Excessive query count: {context.TotalQueriesInScope} queries " +
                          $"executed in this scope (max allowed: {options.MaxQueriesPerScope})."
        };
    }
}
