using EFCore.QueryGuard;
using EFCore.QueryGuard.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EFCore.QueryGuard.Tests;

// ── Minimal DbContext ─────────────────────────────────────────────────────────

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

// ── TestDbScope ───────────────────────────────────────────────────────────────

/// <summary>
/// Encapsulates a SQLite in-memory connection, DbContext, and interceptor for one test.
/// SQLite in-memory databases are tied to a single connection; this scope keeps it alive
/// for the test's lifetime and disposes everything together.
/// </summary>
internal sealed class TestDbScope : IAsyncDisposable
{
    public TestDbContext Db { get; }
    public QueryGuardInterceptor Interceptor { get; }
    private readonly SqliteConnection _connection;

    public TestDbScope(Action<QueryGuardOptions>? configure = null)
    {
        var options = new QueryGuardOptions();
        configure?.Invoke(options);
        Interceptor = new QueryGuardInterceptor(options);

        // A named in-memory database tied to this connection.
        // Without keeping the connection open the database disappears between queries.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(Interceptor)
            .Options;

        Db = new TestDbContext(dbOptions);
        Db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

// ── IQueryGuardScope contract ─────────────────────────────────────────────────

public sealed class QueryGuardScopeContractTests
{
    [Fact]
    public void CurrentViolations_IsEmpty_WhenNoScopeIsActive()
    {
        var interceptor = new QueryGuardInterceptor(new QueryGuardOptions());
        Assert.Empty(interceptor.CurrentViolations);
    }

    [Fact]
    public void CurrentViolations_IsEmpty_AfterEndScope()
    {
        var interceptor = new QueryGuardInterceptor(new QueryGuardOptions());
        interceptor.BeginScope();
        interceptor.EndScope();
        Assert.Empty(interceptor.CurrentViolations);
    }

    [Fact]
    public void EndScope_ReturnsEmptyList_WhenNoQueriesRan()
    {
        var interceptor = new QueryGuardInterceptor(new QueryGuardOptions());
        interceptor.BeginScope();
        var result = interceptor.EndScope();
        Assert.Empty(result);
    }

    [Fact]
    public void Interceptor_ImplementsIQueryGuardScope()
    {
        var interceptor = new QueryGuardInterceptor(new QueryGuardOptions());
        Assert.IsAssignableFrom<IQueryGuardScope>(interceptor);
    }
}

// ── Integration: real SQLite pipeline ────────────────────────────────────────

/// <summary>
/// Integration tests that exercise the full EF Core → SQLite → DbCommandInterceptor
/// pipeline. All violation triggers are <em>deterministic</em> (query count / repeat
/// count) rather than timing-based so results are stable on any CI runner.
/// </summary>
public sealed class QueryGuardInterceptorIntegrationTests : IAsyncLifetime
{
    // xUnit IAsyncLifetime lets us dispose the scope cleanly after each test.
    private TestDbScope? _scope;

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() { if (_scope is not null) await _scope.DisposeAsync(); }

    // ── ExcessiveQueryCount — used as the deterministic violation trigger ──────

    [Fact]
    public async Task ExcessiveQueryCount_IsDetected_InRealPipeline()
    {
        _scope = new TestDbScope(o =>
        {
            o.MaxQueriesPerScope   = 1;
            o.DetectNPlusOne       = false;
            o.SlowQueryThresholdMs = int.MaxValue;
        });

        _scope.Interceptor.BeginScope();
        _ = await _scope.Db.Products.ToListAsync(); // 1st — within limit
        _ = await _scope.Db.Products.ToListAsync(); // 2nd — crosses MaxQueriesPerScope
        var violations = _scope.Interceptor.EndScope();

        Assert.Contains(violations, v => v.Type == ViolationType.ExcessiveQueryCount);
    }

    [Fact]
    public async Task OnViolation_Callback_IsInvoked_WhenViolationDetected()
    {
        var captured = new List<QueryViolation>();
        _scope = new TestDbScope(o =>
        {
            o.MaxQueriesPerScope   = 1;
            o.DetectNPlusOne       = false;
            o.SlowQueryThresholdMs = int.MaxValue;
            o.OnViolation          = v => captured.Add(v);
        });

        _scope.Interceptor.BeginScope();
        _ = await _scope.Db.Products.ToListAsync(); // 1st
        _ = await _scope.Db.Products.ToListAsync(); // 2nd — triggers callback
        _scope.Interceptor.EndScope();

        Assert.NotEmpty(captured);
        Assert.All(captured, v => Assert.Equal(ViolationType.ExcessiveQueryCount, v.Type));
    }

    [Fact]
    public async Task ThrowOnViolation_ThrowsQueryGuardException_WithCorrectViolation()
    {
        _scope = new TestDbScope(o =>
        {
            o.MaxQueriesPerScope   = 1;
            o.ThrowOnViolation     = true;
            o.DetectNPlusOne       = false;
            o.SlowQueryThresholdMs = int.MaxValue;
        });

        _scope.Interceptor.BeginScope();
        _ = await _scope.Db.Products.ToListAsync(); // 1st — ok

        // 2nd query crosses MaxQueriesPerScope=1 and throws.
        var ex = await Assert.ThrowsAsync<QueryGuardException>(
            () => _scope.Db.Products.ToListAsync());

        Assert.Equal(ViolationType.ExcessiveQueryCount, ex.Violation.Type);
        Assert.NotEmpty(ex.Violation.Message);
    }

    // ── N+1 detection ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NPlusOne_IsDetected_WhenSameQueryRepeatsAboveThreshold()
    {
        _scope = new TestDbScope(o =>
        {
            o.DetectNPlusOne       = true;
            o.NPlusOneThreshold    = 2;
            o.SlowQueryThresholdMs = int.MaxValue;
            o.MaxQueriesPerScope   = int.MaxValue;
        });

        _scope.Interceptor.BeginScope();
        _ = await _scope.Db.Products.ToListAsync(); // pattern seen once
        _ = await _scope.Db.Products.ToListAsync(); // threshold crossed → N+1
        var violations = _scope.Interceptor.EndScope();

        Assert.Contains(violations, v => v.Type == ViolationType.NPlusOne);
    }

    // ── Scope isolation ───────────────────────────────────────────────────────

    [Fact]
    public async Task QueriesOutsideScope_AreNotTracked()
    {
        _scope = new TestDbScope(o =>
        {
            o.SlowQueryThresholdMs = int.MaxValue;
            o.DetectNPlusOne       = false;
            o.MaxQueriesPerScope   = 1;
        });

        // Query runs with NO active scope — must not affect CurrentViolations.
        _ = await _scope.Db.Products.ToListAsync();

        Assert.Empty(_scope.Interceptor.CurrentViolations);
    }

    [Fact]
    public async Task EndScope_ClearsCurrentViolations()
    {
        _scope = new TestDbScope(o =>
        {
            o.MaxQueriesPerScope   = 1;
            o.DetectNPlusOne       = false;
            o.SlowQueryThresholdMs = int.MaxValue;
        });

        _scope.Interceptor.BeginScope();
        _ = await _scope.Db.Products.ToListAsync();
        _ = await _scope.Db.Products.ToListAsync(); // triggers violation

        var violations = _scope.Interceptor.EndScope();

        Assert.NotEmpty(violations);
        Assert.Empty(_scope.Interceptor.CurrentViolations); // cleared
    }

    [Fact]
    public async Task IQueryGuardScope_Interface_WorksInterchangeably()
    {
        _scope = new TestDbScope(o =>
        {
            o.MaxQueriesPerScope   = 1;
            o.DetectNPlusOne       = false;
            o.SlowQueryThresholdMs = int.MaxValue;
        });

        IQueryGuardScope scope = _scope.Interceptor; // depend on abstraction

        scope.BeginScope();
        _ = await _scope.Db.Products.ToListAsync();
        _ = await _scope.Db.Products.ToListAsync();
        var violations = scope.EndScope();

        Assert.NotEmpty(violations);
        Assert.Empty(scope.CurrentViolations);
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
    public void EnsureValid_Throws_ForNegativeSlowQueryThreshold()
    {
        var options = new QueryGuardOptions { SlowQueryThresholdMs = -1 };
        Assert.Throws<InvalidOperationException>(() => options.EnsureValid());
    }

    [Fact]
    public void EnsureValid_Throws_ForMaxQueriesPerScopeZero()
    {
        var options = new QueryGuardOptions { MaxQueriesPerScope = 0 };
        Assert.Throws<InvalidOperationException>(() => options.EnsureValid());
    }

    [Fact]
    public void EnsureValid_Throws_ForNPlusOneThresholdLessThanTwo()
    {
        var options = new QueryGuardOptions { NPlusOneThreshold = 1 };
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
