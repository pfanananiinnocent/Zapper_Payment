using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zapper.Payments.Domain.Entities;

namespace Zapper.Payments.Infrastructure.Persistence.Configurations;

public class StoredCardConfiguration : IEntityTypeConfiguration<StoredCard>
{
    public void Configure(EntityTypeBuilder<StoredCard> builder)
    {
        builder.ToTable("StoredCards");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AcmeToken)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(x => x.AcmeToken)
            .IsUnique();

        builder.HasOne(x => x.Customer)
            .WithMany(x => x.StoredCards)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}