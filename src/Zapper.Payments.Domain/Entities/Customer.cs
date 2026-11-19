namespace Zapper.Payments.Domain.Entities;

public class Customer
{
    public int Id { get; set; }
    public string ExternalCustomerId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<StoredCard> StoredCards { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}