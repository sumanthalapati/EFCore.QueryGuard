namespace EFCore.QueryGuard.Abstractions;

/// <summary>
/// Strategy for detecting a specific class of query violation.
/// Implement this interface to plug custom detection logic into QueryGuard.
/// </summary>
public interface IViolationDetector
{
    /// <summary>
    /// Inspect the current query execution and return a violation if one is detected,
    /// or <see langword="null"/> if the query is acceptable.
    /// </summary>
    /// <param name="context">Snapshot of the query execution and surrounding scope state.</param>
    /// <param name="options">The active QueryGuard configuration.</param>
    QueryViolation? Detect(DetectionContext context, QueryGuardOptions options);
}
