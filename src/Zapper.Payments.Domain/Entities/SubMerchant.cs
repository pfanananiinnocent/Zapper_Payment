namespace Zapper.Payments.Domain.Entities;

public class SubMerchant
{
    public int Id { get; set; }
    public string ZapperMerchantId { get; set; } = null!;
    public string TradingName { get; set; } = null!;
    public string City { get; set; } = null!;
    public string Region { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
    public string MerchantCategoryCode { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<Payment> Payments { get; set; } = [];
}