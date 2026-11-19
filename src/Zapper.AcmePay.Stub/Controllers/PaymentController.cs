using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Zapper.AcmePay.Stub.Models;

namespace Zapper.AcmePay.Stub.Controllers;

[ApiController]
[Route("api/payment")]
public class PaymentController : ControllerBase
{
    private readonly IConfiguration _configuration;

    private static readonly object SyncRoot = new();

    private static readonly Dictionary<string, StoredTransaction>
        Transactions = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string>
        RetrievalReferences = new(StringComparer.Ordinal);

    private sealed record StoredTransaction(
        AcmePaymentResponseEnvelope Response,
        DateTime CreatedAtUtc);

    public PaymentController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> SubmitPayment( [FromBody] AcmePaymentEnvelope request)
    {
        var payment = request.PaymentRequest;

        // 1. Authenticate the request.
        if (!IsAuthenticated())
        {
            return CreateError(
                401,
                payment.TransactionID,
                payment.OrderNumber,
                "E02",
                "Invalid API key",
                "Payment could not be processed.");
        }

        // 2. Validate the merchant account.
        if (!IsValidMerchant(payment.MerchantID))
        {
            return CreateError(
                400,
                payment.TransactionID,
                payment.OrderNumber,
                "E01",
                "Invalid merchant identifier",
                "Payment details are invalid.");
        }

        // 3. Validate required fields.
        if (payment.Amount <= 0 ||
            string.IsNullOrWhiteSpace(payment.TransactionID) ||
            payment.TransactionID.Length > 50 ||
            string.IsNullOrWhiteSpace(payment.OrderNumber) ||
            payment.OrderNumber.Length > 50 ||
            !payment.OrderNumber.All(char.IsAsciiLetterOrDigit) ||
            string.IsNullOrWhiteSpace(payment.Card?.Token) ||
            string.IsNullOrWhiteSpace(
                request.MessageHeader?.MessageID))
        {
            return CreateError(
                400,
                payment.TransactionID,
                payment.OrderNumber,
                "E01",
                "Required field missing or invalid",
                "Payment details are invalid.");
        }

        // Simulate provider throttling before processing.
        if (payment.Card.Token == "TEST_TOKEN_RATE_LIMIT")
        {
            Response.Headers["Retry-After"] = "10";

            return CreateError(
                429,
                payment.TransactionID,
                payment.OrderNumber,
                "E29",
                "Rate limit exceeded",
                "Please try again later.");
        }

        var retrievalReference = payment.OrderNumber.Length > 12
                ? payment.OrderNumber[..12]
                : payment.OrderNumber;

        AcmePaymentResponseEnvelope response;

        // 4. Atomically check and store the transaction.
        lock (SyncRoot)
        {
            // Replay the original outcome within 24 hours.
            if (Transactions.TryGetValue(
                    payment.TransactionID,
                    out var existingTransaction))
            {
                if (DateTime.UtcNow -
                    existingTransaction.CreatedAtUtc
                    < TimeSpan.FromHours(24))
                {
                    return Ok(existingTransaction.Response);
                }

                // Simulator safety decision:
                // Do not charge an expired transaction again.
                return CreateError(
                    409,
                    payment.TransactionID,
                    payment.OrderNumber,
                    "E09",
                    "Transaction replay window expired",
                    "Query the original transaction.");
            }

            // Detect a different transaction using the same RRN.
            if (RetrievalReferences.TryGetValue(
                    retrievalReference,
                    out var existingTransactionId))
            {
                var originalOrderNumber =
                    Transactions[existingTransactionId]
                        .Response.PaymentResponse.OrderNumber;

                return CreateError(
                    409,
                    existingTransactionId,
                    originalOrderNumber,
                    "E09",
                    "Retrieval Reference Number already in use",
                    "We could not process that payment.");
            }

            // 5. Generate a simulated acquirer outcome.
            response = BuildPaymentResponse(
                payment,
                retrievalReference);

            // 6. Persist the simulated outcome BEFORE
            // introducing an artificial response delay.
            Transactions.Add(
                payment.TransactionID,
                new StoredTransaction(
                    response,
                    DateTime.UtcNow));

            RetrievalReferences.Add(
                retrievalReference,
                payment.TransactionID);
        }

        // Simulate a provider error after the
        // financial transaction has been processed.
        if (payment.Card.Token == "TEST_TOKEN_SERVER_ERROR")
        {
            return CreateError(
                500,
                payment.TransactionID,
                payment.OrderNumber,
                "E50",
                "Internal server error",
                "Payment outcome could not be confirmed.");
        }

        // 7. Simulate a lost or delayed payment response.
        // Never hold the lock while waiting.
        if (payment.Card.Token == "TEST_TOKEN_TIMEOUT")
        {
            await Task.Delay(
                TimeSpan.FromSeconds(40),
                CancellationToken.None);
        }

        return Ok(response);
    }

    // Real Time Transaction Query endpoint.
    [HttpGet("/apirttq/query/Merchant/{merchantId}")]
    public IActionResult QueryTransaction( string merchantId, [FromQuery(Name = "TransactionID")] string? transactionId,
        [FromQuery(Name = "OrderNumber")] string? orderNumber)
    {
        // 1. Authenticate the query.
        if (!IsAuthenticated())
        {
            return Unauthorized();
        }

        // 2. Check the merchant account.
        if (!IsValidMerchant(merchantId))
        {
            return NotFound();
        }

        // 3. Require one lookup identifier.
        if (string.IsNullOrWhiteSpace(transactionId) &&
            string.IsNullOrWhiteSpace(orderNumber))
        {
            return BadRequest(new
            {
                Message = "TransactionID or OrderNumber is required."
            });
        }

        // Development-only fault injection for RTTQ.
        if (_configuration.GetValue<bool>(
                "FaultInjection:ThrottleRttq"))
        {
            Response.Headers["Retry-After"] = "10";

            return CreateError(
                429,
                transactionId ?? string.Empty,
                orderNumber ?? string.Empty,
                "E29",
                "Rate limit exceeded",
                "Please try again later.");
        }

        lock (SyncRoot)
        {
            // 4. Find by transaction identifier.
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                if (Transactions.TryGetValue(
                        transactionId,
                        out var transaction))
                {
                    return Ok(transaction.Response);
                }

                return NotFound();
            }

            // 5. Alternatively, find by order number.
            var matchingTransaction = Transactions.Values
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Response.PaymentResponse.OrderNumber,
                        orderNumber,
                        StringComparison.Ordinal));

            if (matchingTransaction is null)
            {
                return NotFound();
            }

            return Ok(matchingTransaction.Response);
        }
    }

    private static AcmePaymentResponseEnvelope BuildPaymentResponse( AcmePaymentRequestBody payment, string retrievalReference)
    {
        string responseCode;
        string description;
        string clientMessage;
        string authCode;
        string status;

        switch (payment.Card.Token)
        {
            case "TEST_TOKEN_DECLINED":

                responseCode = "05";
                description = "Do not honour";
                clientMessage =
                    "Your bank declined this payment.";
                authCode = string.Empty;
                status = "Success";

                break;

            case "TEST_TOKEN_INSUFFICIENT_FUNDS":

                responseCode = "51";
                description = "Insufficient funds";
                clientMessage = "Insufficient funds.";
                authCode = string.Empty;
                status = "Success";

                break;

            case "TEST_TOKEN_PROCESSING":

                responseCode = "91";
                description = "Issuer or switch inoperative";
                clientMessage =
                    "We are waiting for your bank.";
                authCode = string.Empty;
                status = "Processing";

                break;

            default:

                responseCode = "00";
                description =
                    "Approved or completed successfully";
                clientMessage = "Approved.";
                authCode = "238699";
                status = "Success";

                break;
        }

        return new AcmePaymentResponseEnvelope
        {
            MessageHeader = CreateMessageHeader(),

            PaymentResponse = new AcmePaymentResponseBody
            {
                TransactionID = payment.TransactionID,

                TransactionDateTime = GetAcmeTimestamp(),

                OrderNumber = payment.OrderNumber,

                ReconID = $"RECON{Guid.NewGuid():N}"[..15],

                RetrievalReferenceNumber = retrievalReference,

                AuthCode = authCode,

                TransactionStatus = status,

                ResponseDetail = new AcmeResponseDetail
                {
                    Source = "Acquirer",
                    Code = responseCode,
                    Description = description,
                    ClientMessage = clientMessage
                },

                AuthAmount = responseCode == "00"
                    ? payment.Amount
                    : 0
            }
        };
    }

    private bool IsAuthenticated()
    {
        var expectedApiKey =
            _configuration["AcmePay:ApiKey"];

        var authorization =
            Request.Headers.Authorization.ToString();

        return !string.IsNullOrWhiteSpace(expectedApiKey) &&
               string.Equals(
                   authorization,
                   $"Bearer {expectedApiKey}",
                   StringComparison.Ordinal);
    }

    private bool IsValidMerchant(string merchantId)
    {
        var configuredMerchantId =
            _configuration["AcmePay:MerchantId"];

        return Guid.TryParse(
                   configuredMerchantId, out var expectedId)
               && Guid.TryParse(merchantId, out var suppliedId)
               && expectedId == suppliedId;
    }

    private IActionResult CreateError(
        int httpStatusCode,
        string transactionId,
        string orderNumber,
        string responseCode,
        string description,
        string clientMessage)
    {
        return StatusCode(httpStatusCode, new
        {
            MessageHeader = CreateMessageHeader(),

            PaymentResponse = new
            {
                TransactionID = transactionId,
                OrderNumber = orderNumber,
                TransactionStatus = "Failure",

                ResponseDetail = new
                {
                    Source = "Application",
                    Code = responseCode,
                    Description = description,
                    ClientMessage = clientMessage
                }
            }
        });
    }

    private static AcmeMessageHeader CreateMessageHeader()
    {
        return new AcmeMessageHeader
        {
            MessageDateTime = GetAcmeTimestamp(),
            MessageID = Guid.NewGuid().ToString(),
            Channel = "App"
        };
    }

    private static string GetAcmeTimestamp()
    {
        return DateTimeOffset.UtcNow
            .ToOffset(TimeSpan.FromHours(2))
            .ToString(
                "yyyy-MM-ddTHH:mm:ss.fff",
                CultureInfo.InvariantCulture);
    }
}