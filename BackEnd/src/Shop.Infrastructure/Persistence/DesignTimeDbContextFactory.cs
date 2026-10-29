using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shop.Infrastructure.Persistence;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<ShopDbContext> options = new DbContextOptionsBuilder<ShopDbContext>()
            .UseSqlServer("Server=localhost;Database=Shop;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new ShopDbContext(options);
    }
}
