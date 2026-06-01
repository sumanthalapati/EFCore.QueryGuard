namespace EFCore.QueryGuard;

/// <summary>Classification of a detected query problem.</summary>
public enum ViolationType
{
    /// <summary>A single query exceeded the configured time threshold.</summary>
    SlowQuery,

    /// <summary>The same query pattern repeated above the N+1 threshold.</summary>
    NPlusOne,

    /// <summary>Total queries in the current scope exceeded the allowed maximum.</summary>
    ExcessiveQueryCount
}

/// <summary>
/// Immutable value object describing a single query violation detected by QueryGuard.
/// </summary>
public sealed record QueryViolation
{
    /// <summary>The category of violation.</summary>
    public required ViolationType Type { get; init; }

    /// <summary>The raw SQL that triggered the violation.</summary>
    public required string Sql { get; init; }

    /// <summary>Elapsed wall-clock time for this query in milliseconds.</summary>
    public required long ElapsedMs { get; init; }

    /// <summary>Human-readable description of the violation.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// How many times this query pattern has been seen in the current scope.
    /// Non-zero only for <see cref="ViolationType.NPlusOne"/>.
    /// </summary>
    public int RepeatCount { get; init; }

    /// <summary>Total queries executed in the scope when this violation was raised.</summary>
    public int TotalQueriesInScope { get; init; }

    /// <summary>UTC timestamp when the violation was detected.</summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public override string ToString() => $"[QueryGuard:{Type}] {Message}";
}
