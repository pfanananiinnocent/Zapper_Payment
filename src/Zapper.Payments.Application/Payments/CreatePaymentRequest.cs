using System.ComponentModel.DataAnnotations;

namespace Zapper.Payments.Application.Payments;

public class CreatePaymentRequest
{
    [Required]
    [StringLength(50)]
    public string CustomerId { get; set; } = null!;

    [Required]
    [StringLength(15)]
    public string SubMerchantId { get; set; } = null!;

    [Range(1, long.MaxValue)]
    public long Amount { get; set; }

    [Required]
    [RegularExpression("^[A-Z]{3}$")]
    public string CurrencyCode { get; set; } = "ZAR";

    [Required]
    [StringLength(100)]
    public string AppPaymentId { get; set; } = null!;
}