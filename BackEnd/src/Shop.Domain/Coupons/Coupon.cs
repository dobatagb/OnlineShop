using Shop.Domain.Common;

namespace Shop.Domain.Coupons;

public enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2,
}

public class Coupon
{
    private Coupon() { }

    public Coupon(
        string code,
        string description,
        DiscountType type,
        decimal value,
        decimal minimumSubtotal = 0,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validUntil = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("coupon.code_required", "Coupon code is required.");
        }

        if (value <= 0)
        {
            throw new DomainException("coupon.invalid_value", "Coupon value must be positive.");
        }

        if (type == DiscountType.Percentage && value > 100)
        {
            throw new DomainException("coupon.invalid_value", "A percentage coupon cannot exceed 100%.");
        }

        if (minimumSubtotal < 0)
        {
            throw new DomainException("coupon.invalid_minimum", "Minimum subtotal cannot be negative.");
        }

        Id = Guid.CreateVersion7();
        Code = NormalizeCode(code);
        Description = description;
        Type = type;
        Value = value;
        MinimumSubtotal = minimumSubtotal;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public DiscountType Type { get; private set; }
    public decimal Value { get; private set; }
    public decimal MinimumSubtotal { get; private set; }
    public DateTimeOffset? ValidFrom { get; private set; }
    public DateTimeOffset? ValidUntil { get; private set; }
    public bool IsActive { get; private set; }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public void EnsureApplicable(decimal subtotal, DateTimeOffset now)
    {
        if (!IsActive || (ValidFrom is { } from && now < from) || (ValidUntil is { } until && now > until))
        {
            throw new DomainException("coupon.not_valid", $"Coupon '{Code}' is not valid at this time.");
        }

        if (subtotal < MinimumSubtotal)
        {
            throw new DomainException(
                "coupon.minimum_not_met",
                $"Coupon '{Code}' requires a subtotal of at least {MinimumSubtotal:0.00}.");
        }
    }

    public decimal CalculateDiscount(decimal subtotal)
    {
        decimal discount = Type switch
        {
            DiscountType.Percentage => Money.Round(subtotal * Value / 100m),
            DiscountType.FixedAmount => Value,
            _ => throw new InvalidOperationException($"Unknown discount type {Type}."),
        };

        return Math.Min(discount, subtotal);
    }
}
