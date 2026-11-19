using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zapper.Payments.Domain.Entities;

namespace Zapper.Payments.Infrastructure.Persistence.Configurations;

public class AcmeNotificationConfiguration
    : IEntityTypeConfiguration<AcmeNotification>
{
    public void Configure(EntityTypeBuilder<AcmeNotification> builder)
    {
        builder.ToTable("AcmeNotifications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.NotificationType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ExternalEventId).HasMaxLength(100);
        builder.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();

        builder.Property(x => x.PayloadJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        // Prevent duplicate deliveries from creating duplicate records.
        builder.HasIndex(x => x.Fingerprint).IsUnique();

        // Some notification types have an Acme event identifier.
        builder.HasIndex(x => x.ExternalEventId)
            .IsUnique()
            .HasFilter("[ExternalEventId] IS NOT NULL");
    }
}