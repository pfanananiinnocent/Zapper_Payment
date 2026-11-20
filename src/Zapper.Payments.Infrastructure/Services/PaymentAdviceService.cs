
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Zapper.Payments.Application.Notifications;
using Zapper.Payments.Application.Notifications.Contracts;
using Zapper.Payments.Application.Notifications.Validation;
using Zapper.Payments.Domain.Entities;
using Zapper.Payments.Domain.Enums;
using Zapper.Payments.Infrastructure.Persistence;

namespace Zapper.Payments.Infrastructure.Services;

public sealed class PaymentAdviceService
    : IPaymentAdviceService
{
    private readonly ZapperPaymentsDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentAdviceService> _logger;

    public PaymentAdviceService(
        ZapperPaymentsDbContext db,
        IConfiguration configuration,
        ILogger<PaymentAdviceService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(
        AcmePaymentAdviceRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation =
            AcmePaymentAdviceValidator.Validate(request);

        if (!validation.IsValid)
        {
            return false;
        }

        // 1. Verify the configured Acme merchant.

        if (!Guid.TryParse(
                _configuration["AcmePay:MerchantId"],
                out var configuredMerchantId)
            || validation.MerchantId != configuredMerchantId)
        {
            _logger.LogWarning(
                "Payment advice contains an unexpected merchant ID.");

            return false;
        }

        var transactionId = validation.TransactionId!.Value;

        var now = DateTime.UtcNow;

        var fingerprint = CreateFingerprint(
            request,
            validation.CurrencyCode,
            transactionId);

        // 2. Start a database transaction.

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        // 3. Retrieve and lock the existing payment.
        // This also protects against concurrent notifications.

        var payment = await _db.Payments
            .FromSqlInterpolated($@"
                SELECT *
                FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK)
                WHERE TransactionId = {transactionId}")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            _logger.LogWarning(
                "Payment advice received for unknown transaction {TransactionId}",
                transactionId);

            return false;
        }

        // 4. Check for an already processed notification.

        var duplicate = await _db.AcmeNotifications
            .AnyAsync(
                x => x.Fingerprint == fingerprint
                    && x.IsProcessed,
                cancellationToken);

        if (duplicate)
        {
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Duplicate payment advice ignored for {TransactionId}",
                transactionId);

            return true;
        }

        // 5. Prepare the notification audit record.

        var notification = new AcmeNotification
        {
            NotificationType = "PaymentAdvice",
            Fingerprint = fingerprint,
            PayloadJson = JsonSerializer.Serialize(request),
            ReceivedAtUtc = now,
            IsProcessed = false
        };

        // 6. Validate the notification against our payment.

        if (request.Amount != payment.Amount
            || !string.Equals(
                validation.CurrencyCode,
                payment.CurrencyCode,
                StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(request.OrderNumber)
                && request.OrderNumber != payment.OrderNumber)
            || request.RetrievalReferenceNumber
                != payment.RetrievalReferenceNumber)
        {
            _logger.LogWarning(
                "Payment advice details do not match payment {PaymentId}",
                payment.Id);

            return false;
        }

        // 7. Determine the confirmed payment outcome.

        var targetStatus = DetermineStatus(request);

        var previousStatus = payment.Status;

        // Only unresolved payments may transition through
        // Payment Advice. Final states are protected.

        var canUpdate =
            payment.Status == PaymentStatus.Pending
            || payment.Status ==
                PaymentStatus.PendingReconciliation;

        if (targetStatus.HasValue && canUpdate)
        {
            payment.Status = targetStatus.Value;

            payment.AcmeResponseCode =
                request.ResponseCode;

            payment.AcmeResponseDescription =
                request.ResponseDescription;

            payment.AcmeReconId =
                request.ReconId ?? payment.AcmeReconId;

            payment.AuthorisationCode =
                request.AuthCode;

            payment.AuthorisedAmount =
                targetStatus.Value == PaymentStatus.Approved
                    ? request.Amount
                    : 0;

            payment.UpdatedAtUtc = now;

            // 8. Record the financial lifecycle event.

            _db.PaymentEvents.Add(new PaymentEvent
            {
                PaymentId = payment.Id,

                EventType =
                    payment.Status == PaymentStatus.Approved
                        ? PaymentEventType.PaymentApproved
                        : PaymentEventType.PaymentDeclined,

                PreviousStatus = previousStatus,

                NewStatus = payment.Status,

                Amount =
                    payment.Status == PaymentStatus.Approved
                        ? payment.AuthorisedAmount
                        : payment.Amount,

                ExternalReference = request.ReconId,

                Description =
                    $"Payment advice: {request.ResponseDescription}",

                OccurredAtUtc =
                    validation.TransactionDateTimeUtc?.UtcDateTime
                    ?? now,

                RecordedAtUtc = now
            });
        }
        else if (!targetStatus.HasValue)
        {
            notification.ProcessingError =
                "Advice does not contain a confirmed payment outcome.";
        }
        else if (targetStatus.Value != payment.Status)
        {
            // Late or conflicting notifications must not
            // overwrite an already resolved payment.

            notification.ProcessingError =
                $"Notification ignored. Current status: {payment.Status}.";
        }

        // 9. Mark the notification as handled.

        notification.IsProcessed = true;
        notification.ProcessedAtUtc = now;

        _db.AcmeNotifications.Add(notification);

        // 10. Save everything atomically.

        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Payment advice processed for payment {PaymentId}. " +
            "Previous status: {PreviousStatus}, Current status: {Status}",
            payment.Id,
            previousStatus,
            payment.Status);

        return true;
    }

    private static PaymentStatus? DetermineStatus(
        AcmePaymentAdviceRequest request)
    {
        // We only submit Payment requests in this assessment.

        if (!string.Equals(
                request.RequestType,
                "Payment",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.Equals(
                request.TransactionStatus,
                "Success",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (request.ResponseCode == "00"
            && request.Amount is > 0)
        {
            return PaymentStatus.Approved;
        }

        if (string.Equals(
                request.ResponseSource,
                "Acquirer",
                StringComparison.OrdinalIgnoreCase)
            && request.ResponseCode is "05" or "51" or "54")
        {
            return PaymentStatus.Declined;
        }

        return null;
    }

    private static string CreateFingerprint(
        AcmePaymentAdviceRequest request,
        string currencyCode,
        Guid transactionId)
    {
        var identity = JsonSerializer.Serialize(new
        {
            NotificationType = "PaymentAdvice",
            TransactionId = transactionId,
            RequestType = request.RequestType?.ToUpperInvariant(),
            TransactionStatus =
                request.TransactionStatus?.ToUpperInvariant(),
            ResponseSource =
                request.ResponseSource?.ToUpperInvariant(),
            request.ResponseCode,
            request.Amount,
            CurrencyCode = currencyCode,
            request.OrderNumber,
            request.RetrievalReferenceNumber
        });

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(identity)));
    }
}
