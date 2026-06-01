using EFCore.QueryGuard;
using EFCore.QueryGuard.Abstractions;
using EFCore.QueryGuard.AspNetCore;
using EFCore.QueryGuard.Sample;
using EFCore.QueryGuard.Sample.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register QueryGuard (singleton interceptor + options)
builder.Services.AddQueryGuard(options =>
{
    options.SlowQueryThresholdMs = 200;
    options.DetectNPlusOne = true;
    options.NPlusOneThreshold = 2;
});

// Register DbContext using InMemory for demo purposes
builder.Services.AddDbContext<SampleDbContext>((sp, options) =>
{
    var interceptor = sp.GetRequiredService<QueryGuardInterceptor>();
    options.UseInMemoryDatabase("SampleDb")
           .AddInterceptors(interceptor);
});

var app = builder.Build();

// Seed some data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
    db.Database.EnsureCreated();

    if (!db.Categories.Any())
    {
        var electronics = new Category { Name = "Electronics" };
        var clothing = new Category { Name = "Clothing" };
        db.Categories.AddRange(electronics, clothing);
        db.Products.AddRange(
            new Product { Name = "Laptop", Price = 999.99m, Category = electronics },
            new Product { Name = "Phone", Price = 599.99m, Category = electronics },
            new Product { Name = "T-Shirt", Price = 19.99m, Category = clothing },
            new Product { Name = "Jeans", Price = 49.99m, Category = clothing }
        );
        db.SaveChanges();
    }
}

// Add QueryGuard middleware (wraps each request in a scope)
app.UseQueryGuard();

// GET /products — intentionally triggers N+1 by loading categories in a loop
app.MapGet("/products", async (SampleDbContext db) =>
{
    var products = await db.Products.ToListAsync();

    // N+1: separate query per product to load its category
    var result = new List<object>();
    foreach (var product in products)
    {
        var category = await db.Categories.FindAsync(product.CategoryId);
        result.Add(new
        {
            product.Id,
            product.Name,
            product.Price,
            Category = category?.Name
        });
    }
    return Results.Ok(result);
});

// GET /products/safe — uses Include to avoid N+1
app.MapGet("/products/safe", async (SampleDbContext db) =>
{
    var products = await db.Products
        .Include(p => p.Category)
        .Select(p => new { p.Id, p.Name, p.Price, Category = p.Category!.Name })
        .ToListAsync();

    return Results.Ok(products);
});

// GET /violations — returns current scope violations as JSON.
// Depends on IQueryGuardScope, not the concrete type, following the Dependency Inversion Principle.
app.MapGet("/violations", (IQueryGuardScope scope) =>
    Results.Ok(scope.CurrentViolations));

app.Run();
