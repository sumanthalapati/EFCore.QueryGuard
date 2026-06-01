using EFCore.QueryGuard;
using Microsoft.Extensions.Options;

namespace EFCore.QueryGuard.AspNetCore;

/// <summary>
/// Validates <see cref="QueryGuardOptions"/> at application startup via the
/// ASP.NET Core <see cref="IValidateOptions{TOptions}"/> pipeline.
/// Registered automatically by
/// <see cref="QueryGuardServiceExtensions.AddQueryGuard"/>.
/// </summary>
/// <remarks>
/// Startup-time validation ensures misconfigured options surface immediately
/// (on first DI resolution) rather than at the first request.
/// Enable eager validation by calling
/// <c>services.AddOptions&lt;QueryGuardOptions&gt;().ValidateOnStart()</c>
/// after <c>AddQueryGuard()</c>.
/// </remarks>
internal sealed class QueryGuardOptionsValidator : IValidateOptions<QueryGuardOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, QueryGuardOptions options)
    {
        var failures = new List<string>(3);

        if (options.SlowQueryThresholdMs < 0)
            failures.Add(
                $"{nameof(options.SlowQueryThresholdMs)} must be ≥ 0 " +
                $"(got {options.SlowQueryThresholdMs}).");

        if (options.MaxQueriesPerScope < 1)
            failures.Add(
                $"{nameof(options.MaxQueriesPerScope)} must be ≥ 1 " +
                $"(got {options.MaxQueriesPerScope}).");

        if (options.NPlusOneThreshold < 2)
            failures.Add(
                $"{nameof(options.NPlusOneThreshold)} must be ≥ 2 " +
                $"(got {options.NPlusOneThreshold}).");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
