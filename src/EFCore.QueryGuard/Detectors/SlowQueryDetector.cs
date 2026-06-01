using EFCore.QueryGuard.Abstractions;

namespace EFCore.QueryGuard.Detectors;

/// <summary>
/// Flags a query whose elapsed wall-clock time exceeds
/// <see cref="QueryGuardOptions.SlowQueryThresholdMs"/>.
/// </summary>
internal sealed class SlowQueryDetector : ViolationDetectorBase
{
    /// <inheritdoc/>
    public override QueryViolation? Detect(DetectionContext context, QueryGuardOptions options)
    {
        if (context.ElapsedMs <= options.SlowQueryThresholdMs)
            return null;

        return new QueryViolation
        {
            Type        = ViolationType.SlowQuery,
            Sql         = context.Sql,
            ElapsedMs   = context.ElapsedMs,
            TotalQueriesInScope = context.TotalQueriesInScope,
            Message     = $"Slow query detected: {context.ElapsedMs}ms " +
                          $"exceeds threshold of {options.SlowQueryThresholdMs}ms. " +
                          $"SQL: {TruncateSql(context.Sql)}"
        };
    }
}
