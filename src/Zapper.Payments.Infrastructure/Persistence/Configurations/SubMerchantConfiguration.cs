using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zapper.Payments.Domain.Entities;

namespace Zapper.Payments.Infrastructure.Persistence.Configurations;

public class SubMerchantConfiguration : IEntityTypeConfiguration<SubMerchant>
{
    public void Configure(EntityTypeBuilder<SubMerchant> builder)
    {
        builder.ToTable("SubMerchants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ZapperMerchantId)
            .HasMaxLength(15)
            .IsRequired();

        builder.HasIndex(x => x.ZapperMerchantId)
            .IsUnique();

        builder.Property(x => x.TradingName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.City).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Region).HasMaxLength(10).IsRequired();
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.MerchantCategoryCode).HasMaxLength(4).IsRequired();
    }
}