namespace EFCore.QueryGuard;

/// <summary>
/// Thrown when a query violation is detected and
/// <see cref="QueryGuardOptions.ThrowOnViolation"/> is <see langword="true"/>.
/// </summary>
public sealed class QueryGuardException : Exception
{
    /// <summary>The violation that caused this exception.</summary>
    public QueryViolation Violation { get; }

    /// <param name="violation">The detected violation.</param>
    public QueryGuardException(QueryViolation violation)
        : base(violation.Message)
    {
        Violation = violation;
    }

    /// <param name="violation">The detected violation.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    public QueryGuardException(QueryViolation violation, Exception innerException)
        : base(violation.Message, innerException)
    {
        Violation = violation;
    }
}
