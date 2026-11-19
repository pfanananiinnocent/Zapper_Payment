using Microsoft.EntityFrameworkCore;
using Zapper.Payments.Domain.Entities;

namespace Zapper.Payments.Infrastructure.Persistence;

public class ZapperPaymentsDbContext : DbContext
{
    public ZapperPaymentsDbContext(
        DbContextOptions<ZapperPaymentsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<StoredCard> StoredCards => Set<StoredCard>();
    public DbSet<SubMerchant> SubMerchants => Set<SubMerchant>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<AcmeNotification> AcmeNotifications => Set<AcmeNotification>();
    public DbSet<AcmeRequestLog> AcmeRequestLogs => Set<AcmeRequestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ZapperPaymentsDbContext).Assembly);
    }
}