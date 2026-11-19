using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zapper.Payments.Application.Exceptions;
using Zapper.Payments.Application.Payments;
using Zapper.Payments.Application.Payments.AcmePay;
using Zapper.Payments.Application.Payments.AcmePay.Contracts;
using Zapper.Payments.Domain.Entities;
using Zapper.Payments.Domain.Enums;
using Zapper.Payments.Infrastructure.AcmePay;
using Zapper.Payments.Infrastructure.Persistence;
using Zapper.Payments.Infrastructure.AcmePay.Resilience;

namespace Zapper.Payments.Infrastructure.Services;

public class PaymentService : IPaymentService
{
    private readonly ZapperPaymentsDbContext _db;
    private readonly IAcmePayClient _acmePayClient;
    private readonly AcmePayOptions _options;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(ZapperPaymentsDbContext db, IAcmePayClient acmePayClient, IOptions<AcmePayOptions> options,ILogger<PaymentService> logger)
    {
        _db = db;
        _acmePayClient = acmePayClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PaymentResponseDto> CreatePaymentAsync(CreatePaymentRequest request,CancellationToken cancellationToken = default)
    {
        // 1. Check whether this payment already exists.
        var existingPayment = await FindPaymentAsync(
            request.AppPaymentId,
            cancellationToken);

        if (existingPayment is not null)
        {
            return HandleExistingPayment(
                existingPayment, request);
        }

        // 2. Retrieve customer.
        var customer = await _db.Customers
            .SingleOrDefaultAsync(
                x => x.ExternalCustomerId == request.CustomerId,
                cancellationToken);

        if (customer is null)
        {
            throw new PaymentValidationException(
                "Customer was not found.");
        }

        // 3. Retrieve the customer's active stored card.
        var storedCard = await _db.StoredCards
            .Where(x => x.CustomerId == customer.Id
                     && x.IsActive)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (storedCard is null)
        {
            throw new PaymentValidationException(
                "Customer does not have an active stored card.");
        }

        // 4. Retrieve the sub-merchant.
        var merchant = await _db.SubMerchants
            .SingleOrDefaultAsync(
                x => x.ZapperMerchantId == request.SubMerchantId
                     && x.IsActive,
                cancellationToken);

        if (merchant is null)
        {
            throw new PaymentValidationException(
                "Sub-merchant was not found or is inactive.");
        }

        // 5. Generate stable transaction references.
        var transactionId = Guid.NewGuid();

        var orderNumber = transactionId
            .ToString("N")[..12]
            .ToUpperInvariant();

        var now = DateTime.UtcNow;

        // 6. Construct the provider request before saving.
        var acmeRequest = new AcmePaymentRequest
        {
            MessageHeader = new AcmeMessageHeader
            {
                MessageDateTime = GetAcmeTimestamp(),
                MessageID = Guid.NewGuid().ToString(),
                Channel = "App"
            },

            PaymentRequest = new AcmePaymentRequestBody
            {
                MerchantID = _options.MerchantId,
                TransactionID = transactionId.ToString(),
                OrderNumber = orderNumber,
                Amount = request.Amount,
                CurrencyCode = request.CurrencyCode,
                PaymentService = "CardNotPresent",

                Card = new AcmeCard
                {
                    Token = storedCard.AcmeToken
                },

                CardOnFileScenario = "CIT",
                SubMerchantID = merchant.ZapperMerchantId,
                MerchantCategoryCode =
                    merchant.MerchantCategoryCode,

                CardAcceptorName =
                    CardAcceptorNameFormatter.Format(
                        merchant.TradingName,
                        merchant.City,
                        merchant.Region,
                        merchant.CountryCode),

                Email = customer.Email
            }
        };

        // 7. Create the local payment.
        var payment = new Payment
        {
            CustomerId = customer.Id,
            StoredCardId = storedCard.Id,
            SubMerchantId = merchant.Id,
            AppPaymentId = request.AppPaymentId,
            TransactionId = transactionId,
            OrderNumber = orderNumber,
            RetrievalReferenceNumber = orderNumber,
            Amount = request.Amount,
            CurrencyCode = request.CurrencyCode,
            Status = PaymentStatus.Pending,
            RefundedAmount = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        payment.Events.Add(new PaymentEvent
        {
            EventType = PaymentEventType.PaymentSubmitted,
            PreviousStatus = null,
            NewStatus = PaymentStatus.Pending,
            Amount = payment.Amount,
            Description = "Payment registered for submission.",
            OccurredAtUtc = now,
            RecordedAtUtc = now
        });

        _db.Payments.Add(payment);

        // 8. Persist before contacting Acme.
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (IsUniqueConstraintViolation(ex))
        {
            // Another concurrent request may have inserted
            // the same AppPaymentId first.
            _db.ChangeTracker.Clear();

            var duplicate = await FindPaymentAsync(
                request.AppPaymentId,
                cancellationToken);

            if (duplicate is not null)
            {
                return HandleExistingPayment(
                    duplicate, request);
            }

            // An unrelated reference collision or DB issue.
            throw;
        }

        _logger.LogInformation(
            "Submitting payment {PaymentId}, transaction {TransactionId}",
            payment.Id,
            payment.TransactionId);

        var requestLog = new AcmeRequestLog
        {
            PaymentId = payment.Id,
            TransactionId = transactionId.ToString(),
            RequestType = "Payment",
            StartedAtUtc = DateTime.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();

        var previousStatus = payment.Status;

        // 9. Call Acme Pay.
        try
        {
            var response =
                await _acmePayClient.SubmitPaymentAsync(
                    acmeRequest,
                    cancellationToken);

            var result = response.PaymentResponse
                ?? throw new InvalidOperationException(
                    "Acme response body is missing.");

            requestLog.HttpStatusCode = 200;
            requestLog.ResponseCode =
                result.ResponseDetail?.Code;

            // Validate that the response belongs to this payment.
            if (!Guid.TryParse(
                    result.TransactionID, out var returnedId)
                || returnedId != payment.TransactionId)
            {
                throw new InvalidOperationException(
                    "Acme returned an unexpected transaction ID.");
            }

            if (result.OrderNumber != payment.OrderNumber)
            {
                throw new InvalidOperationException(
                    "Acme returned an unexpected order number.");
            }

            ApplyAcmeResponse(payment, result);

            requestLog.IsSuccessful = true;
        }
        catch (AcmeRequestNotDispatchedException ex)
        {
            payment.Status = PaymentStatus.NotDispatched;

            payment.AcmeResponseCode = null;

            payment.AcmeResponseDescription =
                "Payment was not dispatched to Acme.";

            requestLog.IsSuccessful = false;

            requestLog.HttpStatusCode = null;

            requestLog.ErrorMessage = ex.Message;

            _logger.LogWarning(
                "Payment {PaymentId}, transaction {TransactionId} " +
                "was rejected before dispatch.",
                payment.Id,
                payment.TransactionId);
        }
        catch (OperationCanceledException)
        {
            MarkForReconciliation(payment);

            requestLog.IsSuccessful = false;
            requestLog.ErrorMessage =
                "Acme request timed out or was cancelled.";

            _logger.LogWarning(
                "Uncertain payment outcome for {TransactionId}",
                transactionId);
        }
        catch (HttpRequestException ex)
        {
            MarkForReconciliation(payment);

            requestLog.IsSuccessful = false;

            requestLog.HttpStatusCode =
                ex.StatusCode.HasValue
                    ? (int)ex.StatusCode.Value
                    : null;

            requestLog.ErrorMessage =
                "Acme HTTP communication error.";

            _logger.LogWarning(
                "Acme HTTP error for {TransactionId}. HTTP {StatusCode}",
                transactionId,
                requestLog.HttpStatusCode);
        }
        catch (Exception ex)
            when (ex is InvalidOperationException
                  or JsonException
                  or NotSupportedException)
        {
            MarkForReconciliation(payment);

            requestLog.IsSuccessful = false;
            requestLog.ErrorMessage =
                "Unexpected Acme response.";

            _logger.LogWarning(
                "Unexpected Acme response for {TransactionId}",
                transactionId);
        }

        stopwatch.Stop();

        requestLog.CompletedAtUtc = DateTime.UtcNow;
        requestLog.DurationMilliseconds =
            (int)stopwatch.ElapsedMilliseconds;

        payment.UpdatedAtUtc = DateTime.UtcNow;

        // 10. Record the lifecycle transition.
        _db.PaymentEvents.Add(new PaymentEvent
        {
            PaymentId = payment.Id,

            EventType = payment.Status switch
            {
                PaymentStatus.Approved =>
                    PaymentEventType.PaymentApproved,

                PaymentStatus.Declined =>
                    PaymentEventType.PaymentDeclined,

                PaymentStatus.NotDispatched =>
                    PaymentEventType.PaymentNotDispatched,

                _ => PaymentEventType.PaymentPending
            },

            PreviousStatus = previousStatus,
            NewStatus = payment.Status,

            Amount = payment.Status == PaymentStatus.Approved
                ? payment.AuthorisedAmount
                : payment.Amount,

            Description = payment.AcmeResponseDescription,
            OccurredAtUtc = DateTime.UtcNow,
            RecordedAtUtc = DateTime.UtcNow
        });

        _db.AcmeRequestLogs.Add(requestLog);

        // 11. Persist the outcome, event and request log together.
        // Use a separate bounded token because the caller may
        // have disconnected during the provider request.
        using var persistCts =
            new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await _db.SaveChangesAsync(persistCts.Token);

        _logger.LogInformation(
            "Payment {PaymentId} completed with status {Status}",
            payment.Id,
            payment.Status);

        return MapResponse(payment);
    }

    public async Task<PaymentResponseDto?> GetPaymentAsync(
        string appPaymentId,
        CancellationToken cancellationToken = default)
    {
        var payment = await _db.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.AppPaymentId == appPaymentId,
                cancellationToken);

        return payment is null
            ? null
            : MapResponse(payment);
    }

    private Task<Payment?> FindPaymentAsync(
        string appPaymentId,
        CancellationToken cancellationToken)
    {
        return _db.Payments
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.SubMerchant)
            .SingleOrDefaultAsync(
                x => x.AppPaymentId == appPaymentId,
                cancellationToken);
    }

    private static PaymentResponseDto HandleExistingPayment(
        Payment payment,
        CreatePaymentRequest request)
    {
        if (payment.Amount != request.Amount ||
            payment.CurrencyCode != request.CurrencyCode ||
            !string.Equals(
                payment.Customer.ExternalCustomerId,
                request.CustomerId,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                payment.SubMerchant.ZapperMerchantId,
                request.SubMerchantId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentConflictException(
                "AppPaymentId has already been used " +
                "with different payment details.");
        }

        return MapResponse(payment);
    }

    private static void ApplyAcmeResponse(
        Payment payment,
        AcmePaymentResponseBody result)
    {
        payment.AcmeResponseCode =
            result.ResponseDetail?.Code;

        payment.AcmeResponseDescription =
            result.ResponseDetail?.Description;

        payment.AcmeReconId = result.ReconID;

        payment.AuthorisationCode = result.AuthCode;

        if (result.TransactionStatus == "Success" &&
            result.ResponseDetail?.Code == "00" &&
            result.AuthAmount is > 0 &&
            result.AuthAmount <= payment.Amount)
        {
            payment.Status = PaymentStatus.Approved;
            payment.AuthorisedAmount = result.AuthAmount;
        }
        else if (result.TransactionStatus == "Success" &&
                 result.ResponseDetail?.Source == "Acquirer" &&
                 result.ResponseDetail?.Code is
                     "05" or "51" or "54")
        {
            payment.Status = PaymentStatus.Declined;
            payment.AuthorisedAmount = 0;
        }
        else
        {
            MarkForReconciliation(payment);
        }
    }

    private static void MarkForReconciliation(Payment payment)
    {
        payment.Status = PaymentStatus.PendingReconciliation;

        payment.AcmeResponseDescription =
            "Payment outcome requires reconciliation.";
    }

    private static bool IsUniqueConstraintViolation(
        DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException
            && sqlException.Number is 2601 or 2627;
    }

    private static string GetAcmeTimestamp()
    {
        // Acme expects SAST timestamps without an offset.
        return DateTimeOffset.UtcNow
            .ToOffset(TimeSpan.FromHours(2))
            .ToString(
                "yyyy-MM-ddTHH:mm:ss.fff",
                CultureInfo.InvariantCulture);
    }

    private static PaymentResponseDto MapResponse(
        Payment payment)
    {
        return new PaymentResponseDto
        {
            PaymentId = payment.Id,
            AppPaymentId = payment.AppPaymentId,
            TransactionId = payment.TransactionId,
            OrderNumber = payment.OrderNumber,
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            CurrencyCode = payment.CurrencyCode,
            AcmeResponseCode = payment.AcmeResponseCode,
            AcmeResponseDescription =
                payment.AcmeResponseDescription
        };
    }
}