using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zapper.Payments.Domain.Entities;

namespace Zapper.Payments.Infrastructure.Persistence.Configurations;

public class AcmeRequestLogConfiguration
    : IEntityTypeConfiguration<AcmeRequestLog>
{
    public void Configure(EntityTypeBuilder<AcmeRequestLog> builder)
    {
        builder.ToTable("AcmeRequestLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TransactionId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RequestType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ResponseCode).HasMaxLength(10);
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);

        builder.HasIndex(x => x.TransactionId);
        builder.HasIndex(x => x.StartedAtUtc);
    }
}