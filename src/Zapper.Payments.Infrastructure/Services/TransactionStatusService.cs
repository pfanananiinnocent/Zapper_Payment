
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zapper.Payments.Application.Notifications;
using Zapper.Payments.Application.Notifications.Contracts;
using Zapper.Payments.Domain.Entities;
using Zapper.Payments.Domain.Enums;
using Zapper.Payments.Infrastructure.Persistence;

namespace Zapper.Payments.Infrastructure.Services;

public sealed class TransactionStatusService
    : ITransactionStatusService
{
    private readonly ZapperPaymentsDbContext _db;
    private readonly ILogger<TransactionStatusService> _logger;

    public TransactionStatusService(
        ZapperPaymentsDbContext db,
        ILogger<TransactionStatusService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(
        AcmeTransactionStatusRequest request,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate the required webhook fields.

        if (!Guid.TryParse(
                request.TransactionUuid,
                out var eventId)
            || eventId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Rrn)
            || request.Rrn.Length > 12
            || request.TransactionAmount is null or <= 0
            || string.IsNullOrWhiteSpace(request.CurrencyCode)
            || request.CurrencyCode.Length != 3
            || string.IsNullOrWhiteSpace(request.TxResultCode)
            || request.IsApproved is null)
        {
            return false;
        }

        var isReversal = string.Equals(
            request.TransactionType,
            "Reversal",
            StringComparison.OrdinalIgnoreCase);

        var isRefund = string.Equals(
            request.TransactionType,
            "Refund",
            StringComparison.OrdinalIgnoreCase);

        if (!isReversal && !isRefund)
        {
            return false;
        }

        // Acme timestamps are supplied in SAST.

        if (!DateTimeOffset.TryParseExact(
                request.TransactionDateTime + "+02:00",
                "yyyy-MM-dd'T'HH:mm:ss.fffzzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var occurredAt))
        {
            return false;
        }

        var externalEventId = eventId.ToString();

        var fingerprint = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"TransactionStatus:{externalEventId}")));

        var now = DateTime.UtcNow;

        var amount = request.TransactionAmount.Value;

        // 2. Start an atomic database transaction.

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        // 3. Find and lock the original payment using RRN.

        var payment = await _db.Payments
            .FromSqlInterpolated($@"
                SELECT *
                FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK)
                WHERE RetrievalReferenceNumber = {request.Rrn}")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            _logger.LogWarning(
                "Transaction status webhook references an unknown RRN.");

            return false;
        }

        // 4. Ignore an already processed event.

        var existing = await _db.AcmeNotifications
            .SingleOrDefaultAsync(
                x => x.ExternalEventId == externalEventId,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.PayloadJson != payloadJson)
            {
                _logger.LogWarning(
                    "Conflicting payload received for event {EventId}",
                    externalEventId);
            }

            await transaction.CommitAsync(cancellationToken);

            return true;
        }

        // 5. Validate payment currency.

        if (!string.Equals(
                request.CurrencyCode,
                payment.CurrencyCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var notification = new AcmeNotification
        {
            NotificationType = isReversal
                ? "Reversal"
                : "Refund",

            ExternalEventId = externalEventId,
            Fingerprint = fingerprint,
            PayloadJson = payloadJson,
            ReceivedAtUtc = now,
            IsProcessed = false
        };

        var previousStatus = payment.Status;

        var authorisedAmount =
            payment.AuthorisedAmount ?? 0;

        // 6. Unsuccessful provider events are audited only.

        if (request.IsApproved != true
            || request.TxResultCode != "00")
        {
            notification.ProcessingError =
                "Provider did not approve the transaction.";
        }
        else if (isReversal)
        {
            // Reversal applies to an approved,
            // previously unrefunded payment.

            if (payment.Status == PaymentStatus.Approved
                && payment.RefundedAmount == 0
                && amount == authorisedAmount)
            {
                payment.Status = PaymentStatus.Reversed;
            }
            else if (
                payment.Status == PaymentStatus.Pending
                || payment.Status ==
                    PaymentStatus.PendingReconciliation)
            {
                // Original payment outcome is not yet known.
                // Do not acknowledge an unapplied financial event.

                return false;
            }
            else
            {
                notification.ProcessingError =
                    $"Reversal ignored. Current status: {payment.Status}.";
            }
        }
        else if (isRefund)
        {
            // Refunds apply to approved or partially
            // refunded payments only.

            if (payment.Status == PaymentStatus.Approved
                || payment.Status ==
                    PaymentStatus.PartiallyRefunded)
            {
                var remainingAmount =
                    authorisedAmount - payment.RefundedAmount;

                if (amount > remainingAmount)
                {
                    notification.ProcessingError =
                        "Refund exceeds remaining authorised amount.";
                }
                else
                {
                    payment.RefundedAmount += amount;

                    payment.Status =
                        payment.RefundedAmount == authorisedAmount
                            ? PaymentStatus.Refunded
                            : PaymentStatus.PartiallyRefunded;
                }
            }
            else if (
                payment.Status == PaymentStatus.Pending
                || payment.Status ==
                    PaymentStatus.PendingReconciliation)
            {
                return false;
            }
            else
            {
                notification.ProcessingError =
                    $"Refund ignored. Current status: {payment.Status}.";
            }
        }

        // 7. Create a financial event only when
        // a reversal or refund was actually applied.

        if (notification.ProcessingError is null)
        {
            payment.UpdatedAtUtc = now;

            _db.PaymentEvents.Add(new PaymentEvent
            {
                PaymentId = payment.Id,

                EventType = isReversal
                    ? PaymentEventType.PaymentReversed
                    : PaymentEventType.PaymentRefunded,

                PreviousStatus = previousStatus,
                NewStatus = payment.Status,
                Amount = amount,
                ExternalReference = externalEventId,

                Description = isReversal
                    ? "Acme reversal notification"
                    : "Acme refund notification",

                OccurredAtUtc = occurredAt.UtcDateTime,
                RecordedAtUtc = now
            });
        }

        // 8. Persist the notification and financial changes.

        notification.IsProcessed = true;
        notification.ProcessedAtUtc = now;

        _db.AcmeNotifications.Add(notification);

        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Transaction status event {EventId} processed. " +
            "Payment {PaymentId}: {PreviousStatus} to {CurrentStatus}",
            externalEventId,
            payment.Id,
            previousStatus,
            payment.Status);

        return true;
    }
}
