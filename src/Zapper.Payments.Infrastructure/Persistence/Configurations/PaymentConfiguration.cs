using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zapper.Payments.Domain.Entities;

namespace Zapper.Payments.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AppPaymentId)
            .HasMaxLength(100)
            .IsRequired();

        // A repeated app payment identifier must not create a second payment.
        builder.HasIndex(x => x.AppPaymentId)
            .IsUnique();

        builder.Property(x => x.TransactionId)
            .IsRequired();

        builder.HasIndex(x => x.TransactionId)
            .IsUnique();

        builder.Property(x => x.OrderNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => x.OrderNumber)
            .IsUnique();

        builder.Property(x => x.RetrievalReferenceNumber)
            .HasMaxLength(12);

        builder.Property(x => x.AcmeReconId)
            .HasMaxLength(50);

        builder.Property(x => x.Amount).HasColumnType("bigint");
        builder.Property(x => x.AuthorisedAmount).HasColumnType("bigint");
        builder.Property(x => x.RefundedAmount).HasColumnType("bigint");

        builder.Property(x => x.CurrencyCode)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>();

        builder.Property(x => x.AcmeResponseCode).HasMaxLength(3);
        builder.Property(x => x.AcmeResponseDescription).HasMaxLength(100);
        builder.Property(x => x.AuthorisationCode).HasMaxLength(6);

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Customer)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StoredCard)
            .WithMany()
            .HasForeignKey(x => x.StoredCardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SubMerchant)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.SubMerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.SubMerchantId, x.CurrencyCode, x.CreatedAtUtc });

        builder.HasIndex(x => x.RetrievalReferenceNumber)
            .IsUnique()
            .HasFilter("[RetrievalReferenceNumber] IS NOT NULL");
    }
}