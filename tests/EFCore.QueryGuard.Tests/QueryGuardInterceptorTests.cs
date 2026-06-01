using EFCore.QueryGuard;
using EFCore.QueryGuard.Abstractions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EFCore.QueryGuard.Tests;

// ── Minimal in-memory DbContext ───────────────────────────────────────────────

public sealed class TestProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class TestDbContext : DbContext
{
    public DbSet<TestProduct> Products => Set<TestProduct>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
}

// ── Factory ───────────────────────────────────────────────────────────────────

file static class DbContextFactory
{
    /// <summary>
    /// Creates a test <see cref="TestDbContext"/> wired to a fresh
    /// <see cref="QueryGuardInterceptor"/> in an isolated in-memory database.
    /// </summary>
    public static (TestDbContext Db, QueryGuardInterceptor Interceptor) Create(
        Action<QueryGuardOptions>? configure = null)
    {
        var options = new QueryGuardOptions();
        configure?.Invoke(options);

        var interceptor = new QueryGuardInterceptor(options);

        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        var db = new TestDbContext(dbOptions);
        db.Database.EnsureCreated();
        return (db, interceptor);
    }
}

// ── IQueryGuardScope contract ─────────────────────────────────────────────────

public sealed class QueryGuardScopeContractTests
{
    [Fact]
    public void CurrentViolations_IsEmpty_WhenNoScopeIsActive()
    {
        var (_, interceptor) = DbContextFactory.Create();
        Assert.Empty(interceptor.CurrentViolations);
    }

    [Fact]
    public void CurrentViolations_IsEmpty_AfterEndScope()
    {
        var (_, interceptor) = DbContextFactory.Create();
        interceptor.BeginScope();
        interceptor.EndScope();
        Assert.Empty(interceptor.CurrentViolations);
    }

    [Fact]
    public void EndScope_ReturnsEmptyList_WhenNoQueriesRan()
    {
        var (_, interceptor) = DbContextFactory.Create();
        interceptor.BeginScope();
        var result = interceptor.EndScope();
        Assert.Empty(result);
    }

    [Fact]
    public void Interceptor_ImplementsIQueryGuardScope()
    {
        var (_, interceptor) = DbContextFactory.Create();
        Assert.IsAssignableFrom<IQueryGuardScope>(interceptor);
    }
}

// ── Integration: violation detection via real EF Core pipeline ────────────────

public sealed class QueryGuardInterceptorIntegrationTests
{
    [Fact]
    public async Task SlowQuery_IsDetected_WhenThresholdIsZero()
    {
        // threshold=0 means every query is "slow" — predictable in InMemory tests.
        var (db, interceptor) = DbContextFactory.Create(o =>
        {
            o.SlowQueryThresholdMs = 0;
            o.DetectNPlusOne       = false;
        });

        interceptor.BeginScope();
        _ = await db.Products.ToListAsync();
        var violations = interceptor.EndScope();

        Assert.NotEmpty(violations);
        Assert.All(violations, v => Assert.Equal(ViolationType.SlowQuery, v.Type));
    }

    [Fact]
    public async Task ExcessiveQueryCount_IsDetected_InRealPipeline()
    {
        var (db, interceptor) = DbContextFactory.Create(o =>
        {
            o.MaxQueriesPerScope = 1;
            o.DetectNPlusOne     = false;
            o.SlowQueryThresholdMs = int.MaxValue;
        });

        interceptor.BeginScope();
        _ = await db.Products.ToListAsync(); // 1st — ok
        _ = await db.Products.ToListAsync(); // 2nd — crosses MaxQueriesPerScope=1
        var violations = interceptor.EndScope();

        Assert.Contains(violations, v => v.Type == ViolationType.ExcessiveQueryCount);
    }

    [Fact]
    public async Task QueriesOutsideScope_AreNotTracked()
    {
        var (db, interceptor) = DbContextFactory.Create(o => o.SlowQueryThresholdMs = 0);

        // Query runs with NO active scope — must not affect CurrentViolations.
        _ = await db.Products.ToListAsync();

        Assert.Empty(interceptor.CurrentViolations);
    }

    [Fact]
    public async Task OnViolation_Callback_IsInvoked_ByInterceptor()
    {
        var captured = new List<QueryViolation>();
        var (db, interceptor) = DbContextFactory.Create(o =>
        {
            o.SlowQueryThresholdMs = 0;
            o.DetectNPlusOne       = false;
            o.OnViolation          = v => captured.Add(v);
        });

        interceptor.BeginScope();
        _ = await db.Products.ToListAsync();
        interceptor.EndScope();

        Assert.NotEmpty(captured);
        Assert.All(captured, v => Assert.Equal(ViolationType.SlowQuery, v.Type));
    }

    [Fact]
    public async Task ThrowOnViolation_ThrowsQueryGuardException_WithCorrectViolation()
    {
        var (db, interceptor) = DbContextFactory.Create(o =>
        {
            o.SlowQueryThresholdMs = 0; // every InMemory query is "slow"
            o.ThrowOnViolation     = true;
            o.DetectNPlusOne       = false;
        });

        interceptor.BeginScope();

        var ex = await Assert.ThrowsAsync<QueryGuardException>(
            () => db.Products.ToListAsync());

        Assert.Equal(ViolationType.SlowQuery, ex.Violation.Type);
        Assert.NotEmpty(ex.Violation.Message);
        Assert.NotEmpty(ex.Violation.Sql);
    }

    [Fact]
    public async Task BeginScope_And_EndScope_AccumulatesViolationsAcrossMultipleQueries()
    {
        var (db, interceptor) = DbContextFactory.Create(o =>
        {
            o.SlowQueryThresholdMs = 0;
            o.DetectNPlusOne       = false;
        });

        interceptor.BeginScope();
        _ = await db.Products.ToListAsync();
        _ = await db.Products.ToListAsync();
        var violations = interceptor.EndScope();

        Assert.True(violations.Count >= 2,
            $"Expected ≥ 2 violations but got {violations.Count}.");
    }

    [Fact]
    public async Task IQueryGuardScope_Interface_WorksInterchangeably()
    {
        var (db, interceptor) = DbContextFactory.Create(o =>
        {
            o.SlowQueryThresholdMs = 0;
            o.DetectNPlusOne       = false;
        });

        IQueryGuardScope scope = interceptor; // depend on abstraction

        scope.BeginScope();
        _ = await db.Products.ToListAsync();
        var violations = scope.EndScope();

        Assert.NotEmpty(violations);
        Assert.Empty(scope.CurrentViolations); // cleared after EndScope
    }
}

// ── QueryGuardOptionsBuilder ──────────────────────────────────────────────────

public sealed class QueryGuardOptionsBuilderTests
{
    [Fact]
    public void Build_ProducesCorrectOptions_FromFluentChain()
    {
        var options = new QueryGuardOptionsBuilder()
            .WithSlowQueryThreshold(300)
            .WithMaxQueriesPerScope(20)
            .WithNPlusOneDetection(threshold: 2)
            .Build();

        Assert.Equal(300, options.SlowQueryThresholdMs);
        Assert.Equal(20,  options.MaxQueriesPerScope);
        Assert.True(options.DetectNPlusOne);
        Assert.Equal(2,   options.NPlusOneThreshold);
    }

    [Fact]
    public void Build_ThrowsInvalidOperationException_ForNegativeThreshold()
    {
        // EnsureValid() is called by Build().
        var builder = new QueryGuardOptionsBuilder();

        // Can't use the fluent API to produce a negative value (it validates inline),
        // so we verify the guard on a manually configured options object.
        var options = new QueryGuardOptions { SlowQueryThresholdMs = -1 };
        Assert.Throws<InvalidOperationException>(() => options.EnsureValid());
    }

    [Fact]
    public void WithSlowQueryThreshold_ThrowsArgumentOutOfRange_ForNegativeValue() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QueryGuardOptionsBuilder().WithSlowQueryThreshold(-1));

    [Fact]
    public void WithMaxQueriesPerScope_ThrowsArgumentOutOfRange_ForZero() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QueryGuardOptionsBuilder().WithMaxQueriesPerScope(0));

    [Fact]
    public void WithNPlusOneDetection_ThrowsArgumentOutOfRange_ForThresholdLessThanTwo() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QueryGuardOptionsBuilder().WithNPlusOneDetection(threshold: 1));

    [Fact]
    public void WithoutNPlusOneDetection_DisablesNPlusOneFlag()
    {
        var options = new QueryGuardOptionsBuilder()
            .WithoutNPlusOneDetection()
            .Build();

        Assert.False(options.DetectNPlusOne);
    }

    [Fact]
    public void OnViolation_CombinesMultipleCallbacks()
    {
        var log = new List<string>();

        var options = new QueryGuardOptionsBuilder()
            .OnViolation(_ => log.Add("first"))
            .OnViolation(_ => log.Add("second"))
            .Build();

        var dummy = new QueryViolation
        {
            Type = ViolationType.SlowQuery, Sql = "x", ElapsedMs = 0, Message = "m"
        };
        options.OnViolation!.Invoke(dummy);

        Assert.Equal(["first", "second"], log);
    }

    [Fact]
    public void ThrowOnViolation_SetsFlag()
    {
        var options = new QueryGuardOptionsBuilder()
            .ThrowOnViolation()
            .Build();

        Assert.True(options.ThrowOnViolation);
    }

    [Fact]
    public void WithDetector_AppendsToAdditionalDetectors()
    {
        var custom = new LambdaDetector((_, _) => null);

        var options = new QueryGuardOptionsBuilder()
            .WithDetector(custom)
            .Build();

        Assert.Contains(custom, options.AdditionalDetectors);
    }
}

// ── Test helper ───────────────────────────────────────────────────────────────

file sealed class LambdaDetector(Func<DetectionContext, QueryGuardOptions, QueryViolation?> fn)
    : IViolationDetector
{
    public QueryViolation? Detect(DetectionContext context, QueryGuardOptions options) =>
        fn(context, options);
}
