using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace EFCore.QueryGuard.Internal;

/// <summary>
/// Associates a running <see cref="Stopwatch"/> with a <see cref="DbCommand"/> instance
/// without leaking memory when the command is abandoned before <see cref="Stop"/> is called.
/// Uses <see cref="ConditionalWeakTable{TKey,TValue}"/> so entries are GC-eligible as soon
/// as the command itself is collected.
/// Thread-safe.
/// </summary>
internal static class CommandTimerStore
{
    private static readonly ConditionalWeakTable<DbCommand, Stopwatch> _timers = new();

    /// <summary>Starts (or restarts) the stopwatch bound to <paramref name="command"/>.</summary>
    public static void Start(DbCommand command)
    {
        var sw = _timers.GetOrCreateValue(command);
        sw.Restart();
    }

    /// <summary>
    /// Stops the stopwatch bound to <paramref name="command"/> and returns elapsed milliseconds.
    /// Returns 0 if <see cref="Start"/> was never called for this command.
    /// </summary>
    public static long Stop(DbCommand command)
    {
        if (!_timers.TryGetValue(command, out var sw))
            return 0L;

        sw.Stop();
        _timers.Remove(command);
        return sw.ElapsedMilliseconds;
    }
}
