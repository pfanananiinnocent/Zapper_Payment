using Microsoft.EntityFrameworkCore;
using Zapper.Payments.Domain.Entities;

namespace Zapper.Payments.Infrastructure.Persistence;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        ZapperPaymentsDbContext dbContext)
    {
        if (!await dbContext.Customers.AnyAsync())
        {
            var customers = new[]
            {
                new Customer
                {
                    //Id = 1,
                    ExternalCustomerId = "CUST-1001",
                    Email = "alice@example.com",
                    CreatedAtUtc = DateTime.UtcNow
                },
                new Customer
                {
                    //Id = 2,
                    ExternalCustomerId = "CUST-1002",
                    Email = "john@example.com",
                    CreatedAtUtc = DateTime.UtcNow
                }
            };

            await dbContext.Customers.AddRangeAsync(customers);
            await dbContext.SaveChangesAsync();
        }

        if (!await dbContext.StoredCards.AnyAsync())
        {
            var cards = new[]
            {
                new StoredCard
                {
                    //Id = 1,
                    CustomerId = 1,
                    AcmeToken = "TEST_TOKEN_ALICE_001",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                },
                new StoredCard
                {
                    //Id = 2,
                    CustomerId = 2,
                    AcmeToken = "TEST_TOKEN_JOHN_001",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                }
            };

            await dbContext.StoredCards.AddRangeAsync(cards);
            await dbContext.SaveChangesAsync();
        }

        if (!await dbContext.SubMerchants.AnyAsync())
        {
            var merchants = new[]
            {
                new SubMerchant
                {
                    //Id = 1,
                    ZapperMerchantId = "ZAP0004182",
                    TradingName = "CAFE DEL SOL",
                    City = "CAPE TOWN",
                    Region = "WC",
                    CountryCode = "ZA",
                    MerchantCategoryCode = "5812",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                },
                new SubMerchant
                {
                    //Id = 2,
                    ZapperMerchantId = "ZAP0004183",
                    TradingName = "JOHNNY'S CAFE",
                    City = "PRETORIA",
                    Region = "GP",
                    CountryCode = "ZA",
                    MerchantCategoryCode = "5812",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                }
            };

            await dbContext.SubMerchants.AddRangeAsync(merchants);
            await dbContext.SaveChangesAsync();
        }
    }
}