using Microsoft.EntityFrameworkCore;
using WarehouseApi.Data;
using WarehouseApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Строка подключения берётся из конфигурации.
// В Docker Compose её задаёт переменная окружения ConnectionStrings__Default (Host=db — имя сервиса).
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not set");

builder.Services.AddDbContext<WarehouseDb>(o => o.UseNpgsql(connectionString));

var app = builder.Build();

await DbInitializer.InitAsync(app);

static string? Validate(ProductRequest r)
{
    if (string.IsNullOrWhiteSpace(r.Sku) || string.IsNullOrWhiteSpace(r.Name))
        return "Sku and Name are required";
    if (r.Quantity < 0 || r.MinQuantity < 0 || r.Price < 0)
        return "Quantity, MinQuantity and Price must be >= 0";
    return null;
}

app.MapGet("/health", async (WarehouseDb db) =>
{
    var dbOk = await db.Database.CanConnectAsync();
    return dbOk
        ? Results.Ok(new { status = "ok", database = "connected" })
        : Results.Json(new { status = "degraded", database = "unavailable" }, statusCode: 503);
});

// ---------- CRUD: товары ----------

// READ: все товары
app.MapGet("/products", async (WarehouseDb db) =>
    Results.Ok(await db.Products.OrderBy(p => p.Id).ToListAsync()));

// READ: товары с остатком ниже минимального
app.MapGet("/products/low-stock", async (WarehouseDb db) =>
    Results.Ok(await db.Products.Where(p => p.Quantity < p.MinQuantity).OrderBy(p => p.Id).ToListAsync()));

// READ: один товар
app.MapGet("/products/{id:int}", async (int id, WarehouseDb db) =>
    await db.Products.FindAsync(id) is { } product
        ? Results.Ok(product)
        : Results.NotFound(new { error = $"Product {id} not found" }));

// CREATE
app.MapPost("/products", async (ProductRequest req, WarehouseDb db, ILogger<Program> log) =>
{
    if (Validate(req) is { } error) return Results.BadRequest(new { error });
    if (await db.Products.AnyAsync(p => p.Sku == req.Sku))
        return Results.Conflict(new { error = $"Sku '{req.Sku}' already exists" });

    var product = new Product
    {
        Sku = req.Sku, Name = req.Name, Quantity = req.Quantity,
        MinQuantity = req.MinQuantity, Price = req.Price
    };
    db.Products.Add(product);
    await db.SaveChangesAsync();

    log.LogInformation("CREATE product {Sku} (id={Id})", product.Sku, product.Id);
    return Results.Created($"/products/{product.Id}", product);
});

// UPDATE (полная замена полей)
app.MapPut("/products/{id:int}", async (int id, ProductRequest req, WarehouseDb db, ILogger<Program> log) =>
{
    if (Validate(req) is { } error) return Results.BadRequest(new { error });

    var product = await db.Products.FindAsync(id);
    if (product is null) return Results.NotFound(new { error = $"Product {id} not found" });

    if (await db.Products.AnyAsync(p => p.Sku == req.Sku && p.Id != id))
        return Results.Conflict(new { error = $"Sku '{req.Sku}' already exists" });

    product.Sku = req.Sku;
    product.Name = req.Name;
    product.Quantity = req.Quantity;
    product.MinQuantity = req.MinQuantity;
    product.Price = req.Price;
    await db.SaveChangesAsync();

    log.LogInformation("UPDATE product {Id} -> {Sku}, qty={Quantity}", id, product.Sku, product.Quantity);
    return Results.Ok(product);
});

// DELETE
app.MapDelete("/products/{id:int}", async (int id, WarehouseDb db, ILogger<Program> log) =>
{
    var product = await db.Products.FindAsync(id);
    if (product is null) return Results.NotFound(new { error = $"Product {id} not found" });

    db.Products.Remove(product);
    await db.SaveChangesAsync();

    log.LogInformation("DELETE product {Id} ({Sku})", id, product.Sku);
    return Results.NoContent();
});

// ---------- Складские операции ----------

// Приход: увеличить остаток
app.MapPost("/products/{id:int}/receive", async (int id, StockMovementRequest req, WarehouseDb db, ILogger<Program> log) =>
{
    if (req.Amount <= 0) return Results.BadRequest(new { error = "Amount must be > 0" });

    var product = await db.Products.FindAsync(id);
    if (product is null) return Results.NotFound(new { error = $"Product {id} not found" });

    product.Quantity += req.Amount;
    await db.SaveChangesAsync();

    log.LogInformation("RECEIVE {Sku}: +{Amount} -> {Quantity}", product.Sku, req.Amount, product.Quantity);
    return Results.Ok(product);
});

// Расход: уменьшить остаток, в минус нельзя
app.MapPost("/products/{id:int}/issue", async (int id, StockMovementRequest req, WarehouseDb db, ILogger<Program> log) =>
{
    if (req.Amount <= 0) return Results.BadRequest(new { error = "Amount must be > 0" });

    var product = await db.Products.FindAsync(id);
    if (product is null) return Results.NotFound(new { error = $"Product {id} not found" });

    if (product.Quantity < req.Amount)
    {
        log.LogWarning("ISSUE {Sku}: requested {Amount}, only {Quantity} in stock", product.Sku, req.Amount, product.Quantity);
        return Results.BadRequest(new { error = $"Not enough stock: {product.Quantity} available, {req.Amount} requested" });
    }

    product.Quantity -= req.Amount;
    await db.SaveChangesAsync();

    log.LogInformation("ISSUE {Sku}: -{Amount} -> {Quantity}", product.Sku, req.Amount, product.Quantity);
    return Results.Ok(product);
});

app.Logger.LogInformation("Warehouse API started, connected to PostgreSQL");
app.Run();
