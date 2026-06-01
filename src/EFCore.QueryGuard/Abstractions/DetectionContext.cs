namespace EFCore.QueryGuard.Abstractions;

/// <summary>
/// Immutable snapshot of a single query execution passed to every <see cref="IViolationDetector"/>.
/// </summary>
/// <param name="Sql">The original SQL text sent to the database.</param>
/// <param name="NormalizedSql">Whitespace-collapsed, lowercased SQL used for pattern matching.</param>
/// <param name="ElapsedMs">Wall-clock time the query took in milliseconds.</param>
/// <param name="TotalQueriesInScope">Total queries executed in the current scope, including this one.</param>
/// <param name="PatternRepeatCount">
///   How many times <paramref name="NormalizedSql"/> has been seen in the current scope (≥ 1).
/// </param>
public sealed record DetectionContext(
    string Sql,
    string NormalizedSql,
    long ElapsedMs,
    int TotalQueriesInScope,
    int PatternRepeatCount);
