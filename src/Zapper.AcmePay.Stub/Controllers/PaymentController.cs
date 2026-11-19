using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Zapper.AcmePay.Stub.Models;

namespace Zapper.AcmePay.Stub.Controllers;

[ApiController]
[Route("api/payment")]
public class PaymentController : ControllerBase
{
    private readonly IConfiguration _configuration;

    private static readonly ConcurrentDictionary<string,
        AcmePaymentResponseEnvelope> Transactions = new();

    private static readonly ConcurrentDictionary<string, string>
        RetrievalReferences = new();

    public PaymentController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> SubmitPayment(
        [FromBody] AcmePaymentEnvelope request)
    {
        var expectedApiKey =
            _configuration["AcmePay:ApiKey"];

        var authorization = Request.Headers.Authorization.ToString();

        if (authorization != $"Bearer {expectedApiKey}")
        {
            return Unauthorized(new
            {
                MessageHeader = new
                {
                    MessageDateTime = DateTime.Now.ToString(
                        "yyyy-MM-ddTHH:mm:ss.fff"),
                    MessageID = Guid.NewGuid().ToString(),
                    Channel = "App"
                },
                PaymentResponse = new
                {
                    TransactionID = request.PaymentRequest.TransactionID,
                    TransactionStatus = "Failure",
                    ResponseDetail = new
                    {
                        Source = "Application",
                        Code = "E02",
                        Description = "Invalid API key",
                        ClientMessage = "Payment could not be processed."
                    }
                }
            });
        }

        var payment = request.PaymentRequest;

        if (payment.Amount <= 0 ||
            string.IsNullOrWhiteSpace(payment.TransactionID) ||
            string.IsNullOrWhiteSpace(payment.OrderNumber) ||
            string.IsNullOrWhiteSpace(payment.Card?.Token))
        {
            return BadRequest(new
            {
                MessageHeader = new
                {
                    MessageDateTime = DateTime.Now.ToString(
                        "yyyy-MM-ddTHH:mm:ss.fff"),
                    MessageID = Guid.NewGuid().ToString(),
                    Channel = "App"
                },
                PaymentResponse = new
                {
                    TransactionID = payment.TransactionID,
                    TransactionStatus = "Failure",
                    ResponseDetail = new
                    {
                        Source = "Application",
                        Code = "E01",
                        Description = "Required field missing or invalid",
                        ClientMessage = "Payment details are invalid."
                    }
                }
            });
        }

        var retrievalReference = payment.OrderNumber.Length >= 12
            ? payment.OrderNumber[..12]
            : payment.OrderNumber.PadRight(12);

        if (RetrievalReferences.TryGetValue(
            retrievalReference, out var existingTransactionId))
        {
            return Conflict(new
            {
                MessageHeader = new
                {
                    MessageDateTime = DateTime.Now.ToString(
                        "yyyy-MM-ddTHH:mm:ss.fff"),
                    MessageID = Guid.NewGuid().ToString(),
                    Channel = "App"
                },
                PaymentResponse = new
                {
                    TransactionID = existingTransactionId,
                    TransactionStatus = "Failure",
                    ResponseDetail = new
                    {
                        Source = "Application",
                        Code = "E09",
                        Description = "Retrieval Reference Number already in use",
                        ClientMessage = "We could not process that payment."
                    }
                }
            });
        }

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
                clientMessage = "Your bank declined this payment.";
                authCode = string.Empty;
                status = "Success";
                break;

            case "TEST_TOKEN_PROCESSING":
                responseCode = "91";
                description = "Issuer or switch inoperative";
                clientMessage = "We are waiting for your bank.";
                authCode = string.Empty;
                status = "Processing";
                break;

            default:
                responseCode = "00";
                description = "Approved or completed successfully";
                clientMessage = "Approved.";
                authCode = "238699";
                status = "Success";
                break;
        }

        var response = new AcmePaymentResponseEnvelope
        {
            MessageHeader = new AcmeMessageHeader
            {
                MessageDateTime = DateTime.Now.ToString(
                    "yyyy-MM-ddTHH:mm:ss.fff"),
                MessageID = Guid.NewGuid().ToString(),
                Channel = "App"
            },
            PaymentResponse = new AcmePaymentResponseBody
            {
                TransactionID = payment.TransactionID,
                TransactionDateTime = DateTime.Now.ToString(
                    "yyyy-MM-ddTHH:mm:ss.fff"),
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
                AuthAmount = responseCode == "00" ? payment.Amount : 0
            }
        };

        Transactions[payment.TransactionID] = response;
        RetrievalReferences[retrievalReference] = payment.TransactionID;

        // Simulate a slow issuer. The transaction is recorded before
        // the delay, so the caller can later reconcile it.
        if (payment.Card.Token == "TEST_TOKEN_TIMEOUT")
        {
            await Task.Delay(TimeSpan.FromSeconds(40), CancellationToken.None);
        }

        return Ok(response);
    }
}