using EFCore.QueryGuard;
using EFCore.QueryGuard.Sample.Models;
using Microsoft.EntityFrameworkCore;

namespace EFCore.QueryGuard.Sample;

public sealed class SampleDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    public SampleDbContext(DbContextOptions<SampleDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseQueryGuard(o =>
            {
                o.SlowQueryThresholdMs = 200;
                o.DetectNPlusOne = true;
                o.NPlusOneThreshold = 2;
            });
        }
    }
}
