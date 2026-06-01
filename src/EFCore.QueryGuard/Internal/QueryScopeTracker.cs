using EFCore.QueryGuard.Abstractions;

namespace EFCore.QueryGuard.Internal;

/// <summary>
/// Tracks all query activity within a single scope.
/// Runs each registered <see cref="IViolationDetector"/> after every query
/// and accumulates the resulting violations.
/// <para>
/// <b>Design invariant:</b> this class is intentionally free of side effects.
/// It does not log, throw, or invoke callbacks. All such cross-cutting concerns
/// are handled by the caller (<see cref="QueryGuardInterceptor"/>), keeping the
/// tracker a pure state machine that is easy to reason about and test in isolation.
/// </para>
/// <para>
/// <b>Options snapshot:</b> the <see cref="QueryGuardOptions"/> instance is
/// captured at scope creation (inside <see cref="QueryGuardInterceptor.BeginScope"/>),
/// so mid-request option changes do not affect an in-flight scope.
/// </para>
/// </summary>
internal sealed class QueryScopeTracker
{
    private readonly IReadOnlyList<IViolationDetector> _detectors;
    private readonly QueryGuardOptions _options;
    private readonly Dictionary<string, int> _patternCounts = new(StringComparer.Ordinal);
    private readonly List<QueryViolation> _violations = [];
    private int _totalQueries;

    /// <param name="detectors">Ordered set of detectors applied to every query.</param>
    /// <param name="options">
    /// Snapshotted configuration for this scope.
    /// Mutations after construction do not affect this scope.
    /// </param>
    public QueryScopeTracker(IReadOnlyList<IViolationDetector> detectors, QueryGuardOptions options)
    {
        _detectors = detectors;
        _options = options;
    }

    /// <summary>All violations accumulated in this scope so far.</summary>
    public IReadOnlyList<QueryViolation> Violations => _violations;

    /// <summary>
    /// Records one query execution and returns <em>only the violations produced by this call</em>.
    /// Previously detected violations are not repeated in the returned list.
    /// </summary>
    /// <param name="sql">Raw SQL text sent to the database.</param>
    /// <param name="elapsedMs">Wall-clock duration of the query in milliseconds.</param>
    /// <returns>
    /// A list of zero or more new <see cref="QueryViolation"/> instances,
    /// or <see cref="Array.Empty{T}"/> when nothing was detected.
    /// </returns>
    public IReadOnlyList<QueryViolation> RecordQuery(string sql, long elapsedMs)
    {
        var normalized = SqlNormalizer.Normalize(sql);

        _patternCounts.TryGetValue(normalized, out var repeatCount);
        _patternCounts[normalized] = ++repeatCount;
        _totalQueries++;

        var context = new DetectionContext(
            Sql:                 sql,
            NormalizedSql:       normalized,
            ElapsedMs:           elapsedMs,
            TotalQueriesInScope: _totalQueries,
            PatternRepeatCount:  repeatCount);

        List<QueryViolation>? newViolations = null;

        foreach (var detector in _detectors)
        {
            var violation = detector.Detect(context, _options);
            if (violation is null) continue;

            _violations.Add(violation);
            (newViolations ??= []).Add(violation);
        }

        return newViolations ?? (IReadOnlyList<QueryViolation>)[];
    }
}
