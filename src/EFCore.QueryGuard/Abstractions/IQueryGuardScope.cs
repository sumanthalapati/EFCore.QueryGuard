namespace EFCore.QueryGuard.Abstractions;

/// <summary>
/// Manages a QueryGuard tracking scope for the current async execution context.
/// In ASP.NET Core, each HTTP request runs inside one scope.
/// </summary>
public interface IQueryGuardScope
{
    /// <summary>
    /// Opens a new tracking scope in the current async context.
    /// Any previously open scope for this context is discarded.
    /// </summary>
    void BeginScope();

    /// <summary>
    /// Closes the current scope and returns all violations collected during it.
    /// Subsequent calls to <see cref="CurrentViolations"/> will return an empty list.
    /// </summary>
    IReadOnlyList<QueryViolation> EndScope();

    /// <summary>
    /// All violations detected so far in the current scope.
    /// Returns an empty list when no scope is active.
    /// </summary>
    IReadOnlyList<QueryViolation> CurrentViolations { get; }
}
