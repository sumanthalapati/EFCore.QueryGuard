using EFCore.QueryGuard.Abstractions;

namespace EFCore.QueryGuard.Detectors;

/// <summary>
/// Flags the moment a query pattern repeats for the
/// <see cref="QueryGuardOptions.NPlusOneThreshold"/>-th time within a single scope.
/// Fires <em>exactly once</em> per pattern — at the threshold crossing — to avoid
/// log spam on every subsequent repeat.
/// </summary>
internal sealed class NPlusOneDetector : ViolationDetectorBase
{
    /// <inheritdoc/>
    public override QueryViolation? Detect(DetectionContext context, QueryGuardOptions options)
    {
        if (!options.DetectNPlusOne)
            return null;

        // Only fire at the exact threshold crossing, not on every subsequent repeat.
        if (context.PatternRepeatCount != options.NPlusOneThreshold)
            return null;

        return new QueryViolation
        {
            Type        = ViolationType.NPlusOne,
            Sql         = context.Sql,
            ElapsedMs   = context.ElapsedMs,
            RepeatCount = context.PatternRepeatCount,
            TotalQueriesInScope = context.TotalQueriesInScope,
            Message     = $"N+1 query detected: the same SQL pattern has executed " +
                          $"{context.PatternRepeatCount} times (threshold: {options.NPlusOneThreshold}). " +
                          $"SQL: {TruncateSql(context.Sql)}"
        };
    }
}
