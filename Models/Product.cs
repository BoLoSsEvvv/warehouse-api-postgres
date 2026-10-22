namespace WarehouseApi.Models;

/// <summary>
/// Складская позиция — сущность CRUD API, хранится в таблице products в PostgreSQL.
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public int MinQuantity { get; set; }
    public decimal Price { get; set; }

    /// <summary>Остаток ниже минимального — пора заказывать. В БД не хранится, считается на лету.</summary>
    public bool LowStock => Quantity < MinQuantity;
}

/// <summary>Тело запроса на создание и полное обновление товара (POST / PUT).</summary>
public record ProductRequest(string Sku, string Name, int Quantity, int MinQuantity, decimal Price);

/// <summary>Тело запроса на приход/расход.</summary>
public record StockMovementRequest(int Amount);
