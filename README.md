# EFCore.QueryGuard

[![NuGet Version](https://img.shields.io/nuget/v/EFCore.QueryGuard?style=flat-square)](https://www.nuget.org/packages/EFCore.QueryGuard)
[![NuGet Downloads](https://img.shields.io/nuget/dt/EFCore.QueryGuard?style=flat-square)](https://www.nuget.org/packages/EFCore.QueryGuard)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)
[![Build](https://img.shields.io/github/actions/workflow/status/your-org/EFCore.QueryGuard/ci.yml?branch=main&style=flat-square)](https://github.com/your-org/EFCore.QueryGuard/actions)

A lightweight EF Core interceptor that detects common query problems at runtime — before they hit production.

- **N+1 detection** — flags when the same query pattern repeats above a configurable threshold
- **Slow query alerts** — warns when individual queries exceed a time threshold
- **Excessive query count** — catches scopes with too many queries (e.g., loops that should be batched)

---

## Installation

```bash
dotnet add package EFCore.QueryGuard
```

For ASP.NET Core middleware integration:

```bash
dotnet add package EFCore.QueryGuard.AspNetCore
```

---

## Quick Start

### In your DbContext

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    optionsBuilder
        .UseSqlServer(connectionString)
        .UseQueryGuard(options =>
        {
            options.SlowQueryThresholdMs = 300;
            options.DetectNPlusOne = true;
            options.NPlusOneThreshold = 3;
        });
}
```

### In ASP.NET Core (Program.cs)

```csharp
// Register the interceptor and options
builder.Services.AddQueryGuard(options =>
{
    options.SlowQueryThresholdMs = 200;
    options.DetectNPlusOne = true;
});

// Register your DbContext, wiring in the interceptor
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var interceptor = sp.GetRequiredService<QueryGuardInterceptor>();
    options.UseSqlServer(connectionString)
           .AddInterceptors(interceptor);
});

// Add middleware — wraps each HTTP request in a QueryGuard scope
app.UseQueryGuard();
```

Each HTTP request is automatically wrapped in a scope. Violations are logged as warnings and surfaced via the `X-QueryGuard-Violations: {count}` response header.

---

## Configuration

All options are set via `QueryGuardOptions`:

| Property | Default | Description |
|---|---|---|
| `SlowQueryThresholdMs` | `500` | Warn if a query takes longer than this many milliseconds |
| `MaxQueriesPerScope` | `10` | Warn if total queries within a scope exceed this count |
| `DetectNPlusOne` | `true` | Enable N+1 query pattern detection |
| `NPlusOneThreshold` | `3` | Number of times a query pattern must repeat before flagging as N+1 |
| `ThrowOnViolation` | `false` | Throw `QueryGuardException` instead of just logging |
| `OnViolation` | `null` | Custom `Action<QueryViolation>` callback invoked on each violation |

---

## What Violations Look Like

QueryGuard logs violations at `Warning` level via `ILogger`:

```
warn: EFCore.QueryGuard.QueryGuardInterceptor
      [QueryGuard:NPlusOne] N+1 query detected: same query pattern executed 3 times (threshold: 3): SELECT "p"."Id", "p"."Name" FROM "Products" AS "p" WHERE "p"."CategoryId" = @__id_0

warn: EFCore.QueryGuard.QueryGuardInterceptor
      [QueryGuard:SlowQuery] Slow query detected (612ms > threshold 500ms): SELECT "p"."Id", "p"."Name", "p"."Price" FROM "Products" AS "p"

warn: EFCore.QueryGuard.QueryGuardInterceptor
      [QueryGuard:ExcessiveQueryCount] Excessive query count: 11 queries executed in scope (max: 10)
```

When using the ASP.NET Core middleware, violations are also reflected in the response header:

```
X-QueryGuard-Violations: 3
```

---

## License

MIT — see [LICENSE](LICENSE).
