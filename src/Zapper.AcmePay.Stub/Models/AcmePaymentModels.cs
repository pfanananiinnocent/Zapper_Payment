namespace Zapper.AcmePay.Stub.Models;

public class AcmePaymentEnvelope
{
    public AcmeMessageHeader MessageHeader { get; set; } = new();
    public AcmePaymentRequestBody PaymentRequest { get; set; } = new();
}

public class AcmeMessageHeader
{
    public string MessageDateTime { get; set; } = string.Empty;
    public string MessageID { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
}

public class AcmePaymentRequestBody
{
    public string MerchantID { get; set; } = string.Empty;
    public string TransactionID { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string CurrencyCode { get; set; } = "ZAR";
    public string PaymentService { get; set; } = string.Empty;
    public AcmeCard Card { get; set; } = new();
    public string CardOnFileScenario { get; set; } = string.Empty;
    public string SubMerchantID { get; set; } = string.Empty;
    public string MerchantCategoryCode { get; set; } = string.Empty;
    public string CardAcceptorName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class AcmeCard
{
    public string Token { get; set; } = string.Empty;
}

public class AcmePaymentResponseEnvelope
{
    public AcmeMessageHeader MessageHeader { get; set; } = new();
    public AcmePaymentResponseBody PaymentResponse { get; set; } = new();
}

public class AcmePaymentResponseBody
{
    public string TransactionID { get; set; } = string.Empty;
    public string TransactionDateTime { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string ReconID { get; set; } = string.Empty;
    public string RetrievalReferenceNumber { get; set; } = string.Empty;
    public string AuthCode { get; set; } = string.Empty;
    public string TransactionStatus { get; set; } = string.Empty;
    public AcmeResponseDetail ResponseDetail { get; set; } = new();
    public long AuthAmount { get; set; }
}

public class AcmeResponseDetail
{
    public string Source { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ClientMessage { get; set; } = string.Empty;
}