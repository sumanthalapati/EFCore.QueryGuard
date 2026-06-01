using EFCore.QueryGuard;
using EFCore.QueryGuard.Abstractions;
using EFCore.QueryGuard.Detectors;
using EFCore.QueryGuard.Internal;
using Xunit;

namespace EFCore.QueryGuard.Tests;

/// <summary>
/// Unit tests for <see cref="QueryScopeTracker"/> and the built-in
/// <see cref="IViolationDetector"/> strategies in isolation.
/// No EF Core or database involved.
/// </summary>
public sealed class QueryScopeTrackerTests
{
    // ── Factory helpers ───────────────────────────────────────────────────────

    private static QueryScopeTracker CreateTracker(QueryGuardOptions options) =>
        new(BuildDetectors(options), options);

    private static IReadOnlyList<IViolationDetector> BuildDetectors(QueryGuardOptions options) =>
    [
        new SlowQueryDetector(),
        new NPlusOneDetector(),
        new ExcessiveQueryCountDetector(),
        .. options.AdditionalDetectors
    ];

    // ── SlowQuery ─────────────────────────────────────────────────────────────

    [Fact]
    public void SlowQuery_IsDetected_WhenElapsedExceedsThreshold()
    {
        var options = new QueryGuardOptions { SlowQueryThresholdMs = 100 };
        var tracker = CreateTracker(options);

        var violations = tracker.RecordQuery("SELECT * FROM Products", elapsedMs: 200);

        Assert.Single(violations);
        Assert.Equal(ViolationType.SlowQuery, violations[0].Type);
        Assert.Equal(200, violations[0].ElapsedMs);
        Assert.Equal(1, violations[0].TotalQueriesInScope);
    }

    [Fact]
    public void SlowQuery_IsNotDetected_WhenElapsedEqualsThreshold()
    {
        // Boundary: exactly at the threshold is NOT a violation (strictly greater-than).
        var options = new QueryGuardOptions { SlowQueryThresholdMs = 100 };
        var tracker = CreateTracker(options);

        var violations = tracker.RecordQuery("SELECT 1", elapsedMs: 100);

        Assert.DoesNotContain(violations, v => v.Type == ViolationType.SlowQuery);
    }

    [Fact]
    public void NoViolation_WhenQueryIsFastAndUnique()
    {
        var options = new QueryGuardOptions
        {
            SlowQueryThresholdMs = 500,
            DetectNPlusOne       = true,
            NPlusOneThreshold    = 3,
            MaxQueriesPerScope   = 10
        };
        var tracker = CreateTracker(options);

        var violations = tracker.RecordQuery("SELECT * FROM Products", elapsedMs: 10);

        Assert.Empty(violations);
    }

    // ── N+1 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void NPlusOne_IsDetected_ExactlyAtThreshold()
    {
        var options = new QueryGuardOptions { DetectNPlusOne = true, NPlusOneThreshold = 3 };
        var tracker = CreateTracker(options);
        const string sql = "SELECT * FROM Products WHERE CategoryId = 1";

        tracker.RecordQuery(sql, 10); // 1st
        tracker.RecordQuery(sql, 10); // 2nd — no violation yet
        var violations = tracker.RecordQuery(sql, 10); // 3rd — triggers

        Assert.Single(violations);
        Assert.Equal(ViolationType.NPlusOne, violations[0].Type);
        Assert.Equal(3, violations[0].RepeatCount);
    }

    [Fact]
    public void NPlusOne_FiresOnlyOnce_NotOnSubsequentRepeats()
    {
        var options = new QueryGuardOptions { DetectNPlusOne = true, NPlusOneThreshold = 3 };
        var tracker = CreateTracker(options);
        const string sql = "SELECT * FROM Products WHERE CategoryId = 1";

        for (var i = 0; i < 6; i++)
            tracker.RecordQuery(sql, 10);

        // Exactly one N+1 violation, even though the pattern appeared 6 times.
        Assert.Single(tracker.Violations, v => v.Type == ViolationType.NPlusOne);
    }

    [Fact]
    public void NPlusOne_NormalizesWhitespaceForPatternMatching()
    {
        var options = new QueryGuardOptions { DetectNPlusOne = true, NPlusOneThreshold = 2 };
        var tracker = CreateTracker(options);

        tracker.RecordQuery("SELECT * FROM Products",      10);
        var violations = tracker.RecordQuery("SELECT  *  FROM  Products", 10); // same pattern

        Assert.Contains(violations, v => v.Type == ViolationType.NPlusOne);
    }

    [Fact]
    public void NPlusOne_IsSkipped_WhenDetectNPlusOneIsFalse()
    {
        var options = new QueryGuardOptions { DetectNPlusOne = false, NPlusOneThreshold = 2 };
        var tracker = CreateTracker(options);

        for (var i = 0; i < 5; i++)
            tracker.RecordQuery("SELECT * FROM Products", 10);

        Assert.DoesNotContain(tracker.Violations, v => v.Type == ViolationType.NPlusOne);
    }

    // ── ExcessiveQueryCount ───────────────────────────────────────────────────

    [Fact]
    public void ExcessiveQueryCount_IsDetected_WhenTotalExceedsMax()
    {
        var options = new QueryGuardOptions { MaxQueriesPerScope = 3, DetectNPlusOne = false };
        var tracker = CreateTracker(options);

        for (var i = 0; i < 4; i++)
            tracker.RecordQuery($"SELECT {i}", 5);

        Assert.Contains(tracker.Violations, v => v.Type == ViolationType.ExcessiveQueryCount);
    }

    [Fact]
    public void ExcessiveQueryCount_FiresOnlyOnce_ForManyQueries()
    {
        var options = new QueryGuardOptions { MaxQueriesPerScope = 2, DetectNPlusOne = false };
        var tracker = CreateTracker(options);

        for (var i = 0; i < 10; i++)
            tracker.RecordQuery($"SELECT {i}", 5);

        Assert.Single(tracker.Violations, v => v.Type == ViolationType.ExcessiveQueryCount);
    }

    // ── Tracker scope isolation ───────────────────────────────────────────────

    [Fact]
    public void RecordQuery_ReturnsOnlyNewViolations_NotPreviousOnes()
    {
        var options = new QueryGuardOptions { SlowQueryThresholdMs = 0, DetectNPlusOne = false };
        var tracker = CreateTracker(options);

        var first  = tracker.RecordQuery("SELECT 1", elapsedMs: 1);
        var second = tracker.RecordQuery("SELECT 2", elapsedMs: 1);

        // Each call returns only its own new violations, not an accumulation.
        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(2, tracker.Violations.Count); // accumulator grows
    }

    // ── AdditionalDetectors extensibility ─────────────────────────────────────

    [Fact]
    public void AdditionalDetectors_AreInvoked_AfterBuiltIns()
    {
        var invoked = false;
        var options = new QueryGuardOptions
        {
            AdditionalDetectors =
            {
                new LambdaDetector((_, _) =>
                {
                    invoked = true;
                    return null;
                })
            }
        };

        var tracker = CreateTracker(options);
        tracker.RecordQuery("SELECT 1", 10);

        Assert.True(invoked);
    }

    [Fact]
    public void AdditionalDetectors_CanProduceCustomViolations()
    {
        var customViolation = new QueryViolation
        {
            Type    = ViolationType.SlowQuery,
            Sql     = "custom",
            ElapsedMs = 0,
            Message = "custom detector fired"
        };
        var options = new QueryGuardOptions
        {
            AdditionalDetectors = { new LambdaDetector((_, _) => customViolation) }
        };

        var tracker    = CreateTracker(options);
        var violations = tracker.RecordQuery("SELECT 1", 10);

        Assert.Contains(violations, v => v.Message == "custom detector fired");
    }

    // ── SqlNormalizer ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM t",       "select * from t")]
    [InlineData("  SELECT  *  FROM t ",  "select * from t")]
    [InlineData("SELECT\n*\nFROM\tt",    "select * from t")]
    [InlineData("SELECT\r\n*\r\nFROM t", "select * from t")]
    public void SqlNormalizer_CollapseAndLowercase(string input, string expected) =>
        Assert.Equal(expected, SqlNormalizer.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void SqlNormalizer_ReturnsEmpty_ForBlankInput(string input) =>
        Assert.Equal(string.Empty, SqlNormalizer.Normalize(input));
}

// ── Test helper ───────────────────────────────────────────────────────────────

/// <summary>
/// Wraps a lambda as an <see cref="IViolationDetector"/> to avoid creating
/// a named class for every one-off custom-detector scenario in tests.
/// Scoped to this file via the <c>file</c> access modifier (C# 11).
/// </summary>
file sealed class LambdaDetector(Func<DetectionContext, QueryGuardOptions, QueryViolation?> fn)
    : IViolationDetector
{
    public QueryViolation? Detect(DetectionContext context, QueryGuardOptions options) =>
        fn(context, options);
}
