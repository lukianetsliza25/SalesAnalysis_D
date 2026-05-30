// SalesAnalysis.Data/SalesDbContext.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SalesAnalysis.Core.Entities;

// Змінюємо успадкування на IdentityDbContext
public class SalesDbContext : IdentityDbContext<IdentityUser<int>, IdentityRole<int>, int>
{
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<SavedAnalysis> SavedAnalyses { get; set; }

    public SalesDbContext(DbContextOptions<SalesDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ОБОВ'ЯЗКОВО викликаємо базовий метод для налаштування таблиць Identity
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var properties = entityType.GetProperties()
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?));

            foreach (var property in properties)
            {
                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                    v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
                    v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
                ));
            }
        }

        modelBuilder.Entity<Transaction>()
            .Property(t => t.Revenue)
            .HasColumnType("decimal(18,2)");

        // Налаштовуємо зв'язок SavedAnalysis з новим типом користувача
        modelBuilder.Entity<SavedAnalysis>()
            .HasOne<IdentityUser<int>>()
            .WithMany()
            .HasForeignKey(a => a.UserId);
    }
}
