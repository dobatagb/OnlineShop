using Shop.Domain.Common;
using Shop.Domain.Products;

namespace Shop.Domain.Orders;

public class OrderItem
{
    private OrderItem() { }

    internal OrderItem(Order order, Product product, int quantity)
    {
        Id = Guid.CreateVersion7();
        OrderId = order.Id;
        ProductId = product.Id;
        Product = product;
        Sku = product.Sku;
        ProductName = product.Name;
        Quantity = quantity;
        RefreshPrice();
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;

    public string Sku { get; private set; } = null!;
    public string ProductName { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal { get; private set; }

    internal void ChangeQuantity(int quantity)
    {
        Quantity = quantity;
        RefreshPrice();
    }

    internal void RefreshPrice()
    {
        Sku = Product.Sku;
        ProductName = Product.Name;
        UnitPrice = Product.Price;
        LineTotal = Money.Round(UnitPrice * Quantity);
    }
}
