using Microsoft.EntityFrameworkCore;
using WarehouseApi.Models;

namespace WarehouseApi.Data;

/// <summary>
/// Контекст EF Core. Через него API читает и пишет таблицу products в PostgreSQL.
/// </summary>
public class WarehouseDb(DbContextOptions<WarehouseDb> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Имена таблицы и колонок в snake_case — чтобы в psql писать без кавычек:
        // SELECT * FROM products;
        var p = model.Entity<Product>();
        p.ToTable("products");
        p.HasKey(x => x.Id);
        p.Property(x => x.Id).HasColumnName("id");
        p.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(50).IsRequired();
        p.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        p.Property(x => x.Quantity).HasColumnName("quantity");
        p.Property(x => x.MinQuantity).HasColumnName("min_quantity");
        p.Property(x => x.Price).HasColumnName("price").HasPrecision(12, 2);
        p.Ignore(x => x.LowStock);
        p.HasIndex(x => x.Sku).IsUnique();
    }
}

public static class DbInitializer
{
    /// <summary>
    /// Создаёт таблицу, если её нет, и кладёт стартовые товары, только если таблица пустая.
    /// Поэтому после docker compose down/up данные не перезатираются — берутся из volume db_data.
    /// </summary>
    public static async Task InitAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDb>();
        var log = app.Logger;

        // depends_on + healthcheck уже ждут готовности db,
        // но на всякий случай несколько попыток подключения.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync();
                break;
            }
            catch (Exception ex) when (attempt < 10)
            {
                log.LogWarning("PostgreSQL not ready (attempt {Attempt}/10): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        if (await db.Products.AnyAsync())
        {
            log.LogInformation("Database already has {Count} products, seed skipped", await db.Products.CountAsync());
            return;
        }

        db.Products.AddRange(
            new Product { Sku = "BOLT-M8",  Name = "Болт M8x40",         Quantity = 1200, MinQuantity = 500, Price = 3.50m },
            new Product { Sku = "NUT-M8",   Name = "Гайка M8",           Quantity = 300,  MinQuantity = 500, Price = 1.20m },
            new Product { Sku = "PAINT-WH", Name = "Краска белая, 10 л", Quantity = 15,   MinQuantity = 10,  Price = 2400m },
            new Product { Sku = "TAPE-50",  Name = "Скотч 50 мм",        Quantity = 0,    MinQuantity = 20,  Price = 85m });
        await db.SaveChangesAsync();
        log.LogInformation("Empty database, seeded 4 products");
    }
}
