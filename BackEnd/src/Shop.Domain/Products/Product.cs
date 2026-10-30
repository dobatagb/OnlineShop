using Shop.Domain.Common;

namespace Shop.Domain.Products;

public class Product
{
    private Product() { }

    public Product(string sku, string name, string description, decimal price, int stockQuantity, string category, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new DomainException("product.sku_required", "SKU is required.");
        }

        if (price < 0)
        {
            throw new DomainException("product.invalid_price", "Price cannot be negative.");
        }

        if (Money.Round(price) != price)
        {
            throw new DomainException("product.invalid_price", "Price must have at most two decimal places.");
        }

        if (stockQuantity < 0)
        {
            throw new DomainException("product.invalid_stock", "Stock quantity cannot be negative.");
        }

        Id = Guid.CreateVersion7();
        Sku = sku.Trim().ToUpperInvariant();
        Name = name;
        Description = description;
        Price = price;
        StockQuantity = stockQuantity;
        Category = category;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public decimal Price { get; private set; }
    public int StockQuantity { get; private set; }
    public string Category { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    internal void Reserve(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (StockQuantity < quantity)
        {
            throw new DomainConflictException(
                "product.insufficient_stock",
                $"Not enough stock for '{Sku}': requested {quantity} more, {StockQuantity} available.");
        }

        StockQuantity -= quantity;
        Touch(now);
    }

    internal void Release(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        StockQuantity += quantity;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
