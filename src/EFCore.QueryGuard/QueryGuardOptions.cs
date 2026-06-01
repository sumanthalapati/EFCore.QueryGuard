using EFCore.QueryGuard.Abstractions;

namespace EFCore.QueryGuard;

/// <summary>
/// Configuration for QueryGuard violation detection.
/// All properties have safe defaults that enable detection without being too noisy.
/// </summary>
public sealed class QueryGuardOptions
{
    /// <summary>
    /// Warn if a single query takes longer than this many milliseconds.
    /// Set to <c>0</c> to flag every query. Must be ≥ 0.
    /// Default: <c>500</c>.
    /// </summary>
    public int SlowQueryThresholdMs { get; set; } = 500;

    /// <summary>
    /// Warn if the total number of queries in a scope exceeds this count.
    /// Must be ≥ 1.
    /// Default: <c>10</c>.
    /// </summary>
    public int MaxQueriesPerScope { get; set; } = 10;

    /// <summary>
    /// Enable N+1 query detection.
    /// Default: <c>true</c>.
    /// </summary>
    public bool DetectNPlusOne { get; set; } = true;

    /// <summary>
    /// Number of times the same query pattern must repeat within a scope before
    /// being flagged as N+1. Must be ≥ 2.
    /// Default: <c>3</c>.
    /// </summary>
    public int NPlusOneThreshold { get; set; } = 3;

    /// <summary>
    /// When <see langword="true"/>, throws a <see cref="QueryGuardException"/> on
    /// the first violation instead of only logging.
    /// Default: <c>false</c>.
    /// </summary>
    public bool ThrowOnViolation { get; set; } = false;

    /// <summary>
    /// Optional callback invoked synchronously for every violation detected.
    /// Runs before <see cref="ThrowOnViolation"/> is evaluated, so you can record
    /// the violation even when an exception is about to be thrown.
    /// Multiple delegates may be combined using <c>+=</c>.
    /// </summary>
    public Action<QueryViolation>? OnViolation { get; set; }

    /// <summary>
    /// Additional <see cref="IViolationDetector"/> strategies appended after the three
    /// built-in detectors (slow query, N+1, excessive count).
    /// Use this to add domain-specific detection without forking the library.
    /// </summary>
    public IList<IViolationDetector> AdditionalDetectors { get; } = [];

    /// <summary>
    /// Validates the current option values and throws <see cref="InvalidOperationException"/>
    /// if any constraint is violated.
    /// Called automatically by <see cref="QueryGuardDbContextExtensions"/> and
    /// <see cref="QueryGuardOptionsBuilder.Build"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">One or more option values are invalid.</exception>
    public void EnsureValid()
    {
        var errors = new List<string>(3);

        if (SlowQueryThresholdMs < 0)
            errors.Add($"{nameof(SlowQueryThresholdMs)} must be ≥ 0 (got {SlowQueryThresholdMs}).");

        if (MaxQueriesPerScope < 1)
            errors.Add($"{nameof(MaxQueriesPerScope)} must be ≥ 1 (got {MaxQueriesPerScope}).");

        if (NPlusOneThreshold < 2)
            errors.Add($"{nameof(NPlusOneThreshold)} must be ≥ 2 (got {NPlusOneThreshold}).");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"QueryGuard configuration is invalid:{Environment.NewLine}" +
                string.Join(Environment.NewLine, errors.Select(e => $"  • {e}")));
    }
}
