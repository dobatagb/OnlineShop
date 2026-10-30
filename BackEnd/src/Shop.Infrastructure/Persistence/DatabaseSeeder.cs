using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shop.Application.Abstractions;
using Shop.Domain.Coupons;
using Shop.Domain.Products;
using Shop.Domain.Users;

namespace Shop.Infrastructure.Persistence;

public sealed class DatabaseSeeder(
    ShopDbContext db,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    ILogger<DatabaseSeeder> logger)
{
    public const string DemoEmail = "demo@shop.test";
    public const string DemoPassword = "Demo123!";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        int users = await SeedUsersAsync(now, ct);
        int products = await SeedProductsAsync(now, ct);
        int coupons = await SeedCouponsAsync(ct);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeding finished: {Users} user(s), {Products} product(s), {Coupons} coupon(s) inserted", users, products, coupons);
    }

    private async Task<int> SeedUsersAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Email == DemoEmail, ct))
        {
            return 0;
        }

        User user = new User(DemoEmail, "Demo Customer", now);
        user.SetPasswordHash(passwordHasher.Hash(user, DemoPassword));
        db.Users.Add(user);
        return 1;
    }

    private async Task<int> SeedProductsAsync(DateTimeOffset now, CancellationToken ct)
    {
        HashSet<string> existing = await db.Products.Select(p => p.Sku).ToHashSetAsync(ct);
        List<Product> missing = Catalogue(now).Where(p => !existing.Contains(p.Sku)).ToList();
        db.Products.AddRange(missing);
        return missing.Count;
    }

    private async Task<int> SeedCouponsAsync(CancellationToken ct)
    {
        Coupon[] coupons =
        [
            new("WELCOME10", "10% off your order", DiscountType.Percentage, 10),
            new("SAVE20", "20.00 off orders of 100.00 or more", DiscountType.FixedAmount, 20, minimumSubtotal: 100),
            new("SPRING2024", "Expired seasonal promotion (15% off)", DiscountType.Percentage, 15,
                validFrom: new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero),
                validUntil: new DateTimeOffset(2024, 5, 31, 23, 59, 59, TimeSpan.Zero)),
        ];

        HashSet<string> existing = await db.Coupons.Select(c => c.Code).ToHashSetAsync(ct);
        List<Coupon> missing = coupons.Where(c => !existing.Contains(c.Code)).ToList();
        db.Coupons.AddRange(missing);
        return missing.Count;
    }

    private static IEnumerable<Product> Catalogue(DateTimeOffset now) =>
    [
        new("KB-MECH-001", "Mechanical Keyboard TKL", "Tenkeyless mechanical keyboard with hot-swappable brown switches and PBT keycaps.", 89.99m, 40, "Peripherals", now),
        new("MS-WL-002", "Wireless Ergonomic Mouse", "Vertical wireless mouse with silent clicks and 70-day battery life.", 39.50m, 75, "Peripherals", now),
        new("MON-27-4K", "27\" 4K IPS Monitor", "27-inch UHD IPS panel, 60 Hz, factory calibrated, USB-C with 65 W power delivery.", 349.00m, 15, "Displays", now),
        new("HUB-USBC-7", "USB-C 7-in-1 Hub", "HDMI 4K, 3x USB-A 3.0, SD/microSD readers and 100 W pass-through charging.", 34.99m, 120, "Accessories", now),
        new("HS-NC-300", "Noise-Cancelling Headset", "Over-ear Bluetooth headset with active noise cancelling and a boom microphone.", 129.00m, 30, "Audio", now),
        new("CAM-1080-PRO", "1080p Webcam Pro", "Full HD webcam with autofocus, dual microphones and a privacy shutter.", 59.90m, 50, "Video", now),
        new("SSD-NVME-1TB", "NVMe SSD 1 TB", "PCIe 4.0 M.2 2280 drive, up to 7000 MB/s sequential read.", 74.95m, 60, "Storage", now),
        new("SSD-EXT-2TB", "Portable SSD 2 TB", "Rugged USB 3.2 Gen 2 external SSD, drop resistant up to 2 metres.", 149.00m, 25, "Storage", now),
        new("DESK-LAMP-LED", "LED Desk Lamp", "Dimmable LED lamp with adjustable colour temperature and a wireless charging base.", 45.00m, 35, "Office", now),
        new("CHAIR-ERGO-01", "Ergonomic Office Chair", "Mesh-back chair with lumbar support, 4D armrests and seat depth adjustment.", 279.00m, 10, "Office", now),
        new("STAND-LAP-AL", "Aluminium Laptop Stand", "Foldable, height-adjustable stand for 10–17 inch laptops.", 29.99m, 90, "Accessories", now),
        new("CBL-USBC-2M", "USB-C Cable 2 m (240 W)", "Braided USB-C to USB-C cable supporting 240 W charging and 480 Mbps data.", 12.49m, 200, "Accessories", now),
        new("SPK-BT-MINI", "Mini Bluetooth Speaker", "Pocket-sized waterproof speaker with 12-hour battery life.", 24.99m, 0, "Audio", now),
    ];
}
