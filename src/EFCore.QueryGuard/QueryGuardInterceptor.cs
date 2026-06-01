using System.Data.Common;
using EFCore.QueryGuard.Abstractions;
using EFCore.QueryGuard.Detectors;
using EFCore.QueryGuard.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EFCore.QueryGuard;

/// <summary>
/// EF Core <see cref="DbCommandInterceptor"/> that detects slow queries, N+1 patterns,
/// and excessive query counts at runtime.
/// <para>
/// Implements <see cref="IQueryGuardScope"/> so the same singleton instance can be shared
/// between the EF Core interceptor pipeline and ASP.NET Core middleware without duplication.
/// </para>
/// </summary>
public sealed class QueryGuardInterceptor : DbCommandInterceptor, IQueryGuardScope
{
    private readonly QueryGuardOptions _options;
    private readonly IReadOnlyList<IViolationDetector> _detectors;
    private readonly ILogger<QueryGuardInterceptor>? _logger;

    // One tracker per async execution context (one per HTTP request in ASP.NET Core,
    // or one per explicit BeginScope/EndScope call elsewhere).
    private static readonly AsyncLocal<QueryScopeTracker?> _scopeTracker = new();

    /// <summary>
    /// Initialises a new <see cref="QueryGuardInterceptor"/>.
    /// </summary>
    /// <param name="options">Validated detection configuration.</param>
    /// <param name="logger">
    /// Optional logger. When <see langword="null"/> violations are not logged —
    /// useful for tests or console apps where only the callback matters.
    /// </param>
    public QueryGuardInterceptor(
        QueryGuardOptions options,
        ILogger<QueryGuardInterceptor>? logger = null)
    {
        _options   = options ?? throw new ArgumentNullException(nameof(options));
        _logger    = logger;
        _detectors = BuildDetectors(options);
    }

    // ── IQueryGuardScope ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Options are <em>snapshotted</em> into the new <see cref="QueryScopeTracker"/>
    /// at this point, so any post-creation mutations to <see cref="QueryGuardOptions"/>
    /// will not affect the in-flight scope.
    /// </remarks>
    public void BeginScope() =>
        _scopeTracker.Value = new QueryScopeTracker(_detectors, _options);

    /// <inheritdoc/>
    public IReadOnlyList<QueryViolation> EndScope()
    {
        var tracker = _scopeTracker.Value;
        _scopeTracker.Value = null;
        return tracker?.Violations ?? [];
    }

    /// <inheritdoc/>
    public IReadOnlyList<QueryViolation> CurrentViolations =>
        _scopeTracker.Value?.Violations ?? [];

    // ── Executing hooks — start the per-command stopwatch ────────────────────

    /// <inheritdoc/>
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        CommandTimerStore.Start(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        CommandTimerStore.Start(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        CommandTimerStore.Start(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CommandTimerStore.Start(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    // Note: the base class uses non-nullable `object` here (scalar result before execution).
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        CommandTimerStore.Start(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        CommandTimerStore.Start(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    // ── Executed hooks — stop stopwatch, run detectors, handle violations ────

    /// <inheritdoc/>
    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        ObserveCommand(command);
        return base.ReaderExecuted(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        ObserveCommand(command);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        ObserveCommand(command);
        return base.NonQueryExecuted(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        ObserveCommand(command);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        ObserveCommand(command);
        return base.ScalarExecuted(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        ObserveCommand(command);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Stops the command timer, records the query in the current scope, and handles
    /// any resulting violations (structured logging → callback → optional throw).
    /// No-op when no scope is active.
    /// </summary>
    private void ObserveCommand(DbCommand command)
    {
        var tracker = _scopeTracker.Value;
        if (tracker is null) return;

        var elapsed       = CommandTimerStore.Stop(command);
        var newViolations = tracker.RecordQuery(command.CommandText, elapsed);

        // The tracker is a pure state machine — side effects live here only.
        foreach (var violation in newViolations)
        {
            // Structured log: each property is individually addressable by log sinks
            // (Serilog, Application Insights, etc.) without parsing the message string.
            _logger?.LogWarning(
                "[QueryGuard:{ViolationType}] {Message} | ElapsedMs={ElapsedMs} | " +
                "TotalQueries={TotalQueriesInScope} | RepeatCount={RepeatCount}",
                violation.Type,
                violation.Message,
                violation.ElapsedMs,
                violation.TotalQueriesInScope,
                violation.RepeatCount);

            // Callback runs before throw so callers can record the violation even when
            // ThrowOnViolation is enabled.
            _options.OnViolation?.Invoke(violation);

            if (_options.ThrowOnViolation)
                throw new QueryGuardException(violation);
        }
    }

    /// <summary>
    /// Builds the ordered detector pipeline: the three built-ins first, then any
    /// custom detectors from <see cref="QueryGuardOptions.AdditionalDetectors"/>.
    /// Capacity is pre-allocated to avoid resizing.
    /// </summary>
    private static IReadOnlyList<IViolationDetector> BuildDetectors(QueryGuardOptions options)
    {
        var detectors = new List<IViolationDetector>(capacity: 3 + options.AdditionalDetectors.Count)
        {
            new SlowQueryDetector(),
            new NPlusOneDetector(),
            new ExcessiveQueryCountDetector()
        };

        detectors.AddRange(options.AdditionalDetectors);
        return detectors;
    }
}
