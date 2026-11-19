using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zapper.Payments.Domain.Entities;

namespace Zapper.Payments.Infrastructure.Persistence.Configurations;

public class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("PaymentEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventType).HasConversion<int>();
        builder.Property(x => x.PreviousStatus).HasConversion<int?>();
        builder.Property(x => x.NewStatus).HasConversion<int>();

        builder.Property(x => x.ExternalReference).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(500);

        builder.HasOne(x => x.Payment)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.PaymentId, x.OccurredAtUtc });
    }
}