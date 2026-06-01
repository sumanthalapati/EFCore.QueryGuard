using System.Text.RegularExpressions;

namespace EFCore.QueryGuard.Internal;

/// <summary>
/// Normalizes raw SQL text into a canonical form used for N+1 pattern matching.
/// Thread-safe — all state is immutable or compile-time static.
/// </summary>
internal static class SqlNormalizer
{
    // Compiled once; reused across all calls.
    private static readonly Regex CollapseWhitespace =
        new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns a normalized form of <paramref name="sql"/>:
    /// trimmed, whitespace-collapsed, and lowercased.
    /// Two queries that differ only in parameter values or formatting
    /// will produce the same normalized output.
    /// </summary>
    public static string Normalize(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        return CollapseWhitespace.Replace(sql.Trim(), " ").ToLowerInvariant();
    }
}
