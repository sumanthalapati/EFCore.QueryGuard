using EFCore.QueryGuard.Abstractions;

namespace EFCore.QueryGuard.Detectors;

/// <summary>
/// Base class for built-in <see cref="IViolationDetector"/> implementations.
/// Provides shared utilities (e.g. SQL truncation) via the
/// <em>Template Method</em> pattern so concrete detectors stay focused on their
/// single detection concern without duplicating infrastructure code.
/// </summary>
internal abstract class ViolationDetectorBase : IViolationDetector
{
    /// <summary>Maximum SQL characters included in a violation message.</summary>
    protected const int MaxSqlDisplayLength = 120;

    /// <inheritdoc/>
    public abstract QueryViolation? Detect(DetectionContext context, QueryGuardOptions options);

    /// <summary>
    /// Returns <paramref name="sql"/> truncated to <see cref="MaxSqlDisplayLength"/> characters,
    /// appending <c>…</c> when truncation occurs.
    /// Uses <see cref="string.Concat(ReadOnlySpan{char}, ReadOnlySpan{char})"/> to avoid
    /// an intermediate allocation.
    /// </summary>
    protected static string TruncateSql(string sql) =>
        sql.Length > MaxSqlDisplayLength
            ? string.Concat(sql.AsSpan(0, MaxSqlDisplayLength), "…")
            : sql;
}
