using EFCore.QueryGuard.Abstractions;

namespace EFCore.QueryGuard;

/// <summary>
/// Fluent <em>Builder</em> for <see cref="QueryGuardOptions"/>.
/// Provides a discoverable, compile-time-checked alternative to the
/// <c>Action&lt;QueryGuardOptions&gt;</c> configure delegate.
/// </summary>
/// <example>
/// <code>
/// var options = new QueryGuardOptionsBuilder()
///     .WithSlowQueryThreshold(milliseconds: 300)
///     .WithMaxQueriesPerScope(20)
///     .WithNPlusOneThreshold(2)
///     .OnViolation(v => Console.WriteLine(v))
///     .Build();
/// </code>
/// </example>
public sealed class QueryGuardOptionsBuilder
{
    private readonly QueryGuardOptions _options = new();

    /// <summary>
    /// Sets the slow query threshold.
    /// Queries whose elapsed time exceeds this value will be flagged.
    /// </summary>
    /// <param name="milliseconds">Must be ≥ 0. Default is 500.</param>
    public QueryGuardOptionsBuilder WithSlowQueryThreshold(int milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
        _options.SlowQueryThresholdMs = milliseconds;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of queries allowed per scope before an
    /// <see cref="ViolationType.ExcessiveQueryCount"/> violation is raised.
    /// </summary>
    /// <param name="max">Must be ≥ 1. Default is 10.</param>
    public QueryGuardOptionsBuilder WithMaxQueriesPerScope(int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        _options.MaxQueriesPerScope = max;
        return this;
    }

    /// <summary>
    /// Enables N+1 detection (on by default) and sets the repeat threshold.
    /// </summary>
    /// <param name="threshold">
    /// How many times a pattern must repeat before being flagged. Must be ≥ 2.
    /// Default is 3.
    /// </param>
    public QueryGuardOptionsBuilder WithNPlusOneDetection(int threshold = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 2);
        _options.DetectNPlusOne = true;
        _options.NPlusOneThreshold = threshold;
        return this;
    }

    /// <summary>Disables N+1 query detection entirely.</summary>
    public QueryGuardOptionsBuilder WithoutNPlusOneDetection()
    {
        _options.DetectNPlusOne = false;
        return this;
    }

    /// <summary>
    /// Configures QueryGuard to throw a <see cref="QueryGuardException"/> on the
    /// first detected violation rather than only logging it.
    /// </summary>
    public QueryGuardOptionsBuilder ThrowOnViolation(bool enabled = true)
    {
        _options.ThrowOnViolation = enabled;
        return this;
    }

    /// <summary>
    /// Registers a callback invoked synchronously for every violation.
    /// Multiple calls append to any previously registered callback.
    /// </summary>
    /// <param name="callback">Must not be null.</param>
    public QueryGuardOptionsBuilder OnViolation(Action<QueryViolation> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _options.OnViolation = _options.OnViolation is null
            ? callback
            : _options.OnViolation + callback; // combine delegates
        return this;
    }

    /// <summary>
    /// Appends a custom <see cref="IViolationDetector"/> to the detection pipeline.
    /// Custom detectors run after the three built-in ones.
    /// </summary>
    /// <param name="detector">Must not be null.</param>
    public QueryGuardOptionsBuilder WithDetector(IViolationDetector detector)
    {
        ArgumentNullException.ThrowIfNull(detector);
        _options.AdditionalDetectors.Add(detector);
        return this;
    }

    /// <summary>Builds and returns the configured <see cref="QueryGuardOptions"/> instance.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the resulting options fail validation.
    /// </exception>
    public QueryGuardOptions Build()
    {
        _options.EnsureValid();
        return _options;
    }
}
