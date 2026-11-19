namespace Zapper.Payments.Domain.Entities;

public class StoredCard
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string AcmeToken { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Customer Customer { get; set; } = null!;
}