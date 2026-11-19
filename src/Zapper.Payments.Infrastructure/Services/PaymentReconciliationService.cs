using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zapper.Payments.Application.Payments.AcmePay;
using Zapper.Payments.Application.Payments.AcmePay.Contracts;
using Zapper.Payments.Application.Payments.Reconciliation;
using Zapper.Payments.Domain.Entities;
using Zapper.Payments.Domain.Enums;
using Zapper.Payments.Infrastructure.Persistence;

namespace Zapper.Payments.Infrastructure.Services;

public sealed class PaymentReconciliationService
    : IPaymentReconciliationService
{
    private readonly ZapperPaymentsDbContext _db;
    private readonly IAcmePayClient _acmePayClient;

    private readonly ILogger<PaymentReconciliationService>
        _logger;

    public PaymentReconciliationService(
        ZapperPaymentsDbContext db,
        IAcmePayClient acmePayClient,
        ILogger<PaymentReconciliationService> logger)
    {
        _db = db;
        _acmePayClient = acmePayClient;
        _logger = logger;
    }

    public async Task<ReconciliationResult?> ReconcileAsync(
        string appPaymentId,
        CancellationToken cancellationToken = default)
    {
        // 1. Retrieve the original payment.
        var payment = await _db.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.AppPaymentId == appPaymentId,
                cancellationToken);

        if (payment is null)
        {
            return null;
        }

        var previousStatus = payment.Status;

        // 2. Only reconcile uncertain payments.
        if (!RequiresReconciliation(payment.Status))
        {
            return BuildResult(
                payment,
                previousStatus,
                false,
                "Payment does not require reconciliation.");
        }

        var requestLog = new AcmeRequestLog
        {
            PaymentId = payment.Id,
            TransactionId = payment.TransactionId.ToString(),
            RequestType = "RTTQ",
            StartedAtUtc = DateTime.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();

        AcmeQueryResult? queryResult = null;

        AcmePaymentResponseBody? response = null;

        PaymentStatus? targetStatus = null;

        var message = "Payment outcome remains uncertain.";

        // 3. Query Acme using the original TransactionID.
        try
        {
            queryResult =
                await _acmePayClient.QueryTransactionAsync(
                    payment.TransactionId,
                    cancellationToken);

            requestLog.HttpStatusCode =
                (int)queryResult.StatusCode;

            requestLog.ResponseCode =
                queryResult.Response?
                    .PaymentResponse?
                    .ResponseDetail?
                    .Code;

            requestLog.IsSuccessful =
                queryResult.IsSuccessful;

            if (queryResult.IsNotFound)
            {
                message =
                    "Transaction not yet found at Acme.";

                requestLog.ErrorMessage = message;
            }
            else if (queryResult.IsRateLimited)
            {
                message =
                    "Acme rate limit reached.";

                requestLog.ErrorMessage = message;
            }
            else if (!queryResult.IsSuccessful)
            {
                message =
                    "Acme returned an unsuccessful HTTP response.";

                requestLog.ErrorMessage = message;
            }
            else
            {
                response =
                    queryResult.Response?.PaymentResponse;

                // 4. Validate that Acme returned the
                // correct original transaction.
                if (response is null ||
                    !Guid.TryParse(
                        response.TransactionID,
                        out var returnedTransactionId) ||
                    returnedTransactionId !=
                        payment.TransactionId ||
                    response.OrderNumber !=
                        payment.OrderNumber)
                {
                    requestLog.IsSuccessful = false;

                    message =
                        "Acme returned an unexpected transaction.";

                    requestLog.ErrorMessage = message;
                }
                else
                {
                    // 5. Interpret the financial outcome.
                    targetStatus = DetermineStatus(
                        payment,
                        response);

                    message = targetStatus switch
                    {
                        PaymentStatus.Approved =>
                            "Original payment approval confirmed.",

                        PaymentStatus.Declined =>
                            "Original payment decline confirmed.",

                        _ =>
                            "Acme payment outcome remains unresolved."
                    };
                }
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            requestLog.IsSuccessful = false;

            message = "Acme RTTQ request timed out.";

            requestLog.ErrorMessage = message;
        }
        catch (HttpRequestException ex)
        {
            requestLog.IsSuccessful = false;

            requestLog.HttpStatusCode =
                ex.StatusCode.HasValue
                    ? (int)ex.StatusCode.Value
                    : null;

            message = "Acme RTTQ communication failed.";

            requestLog.ErrorMessage = message;
        }
        catch (Exception ex)
            when (ex is JsonException
                  or InvalidOperationException
                  or NotSupportedException)
        {
            requestLog.IsSuccessful = false;

            message = "Acme returned an invalid RTTQ response.";

            requestLog.ErrorMessage = message;
        }
        finally
        {
            stopwatch.Stop();

            requestLog.CompletedAtUtc = DateTime.UtcNow;

            requestLog.DurationMilliseconds =
                (int)stopwatch.ElapsedMilliseconds;
        }

        // 6. Start a short database transaction AFTER
        // the external HTTP request has completed.
        using var persistCts =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(10));

        var persistToken = persistCts.Token;

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                persistToken);

        // SQL Server update lock prevents two simultaneous
        // reconciliation requests applying the same outcome.
        var currentPayment = await _db.Payments
            .FromSqlInterpolated($@"
                SELECT *
                FROM dbo.Payments WITH (UPDLOCK, HOLDLOCK)
                WHERE Id = {payment.Id}")
            .AsTracking()
            .SingleOrDefaultAsync(persistToken);

        if (currentPayment is null)
        {
            return null;
        }

        var currentPreviousStatus = currentPayment.Status;

        var statusChanged = false;

        // 7. Recheck the latest database status.
        // Another reconciliation request may have already
        // resolved the payment while we were calling Acme.
        if (RequiresReconciliation(currentPayment.Status) &&
            targetStatus.HasValue &&
            response is not null)
        {
            // 8. Apply the confirmed provider outcome.
            currentPayment.Status = targetStatus.Value;

            currentPayment.AcmeResponseCode =
                response.ResponseDetail?.Code;

            currentPayment.AcmeResponseDescription =
                response.ResponseDetail?.Description;

            currentPayment.AcmeReconId =
                response.ReconID;

            currentPayment.AuthorisationCode =
                response.AuthCode;

            currentPayment.AuthorisedAmount =
                targetStatus == PaymentStatus.Approved
                    ? response.AuthAmount
                    : 0;

            currentPayment.UpdatedAtUtc =
                DateTime.UtcNow;

            // 9. Record exactly one financial transition.
            _db.PaymentEvents.Add(new PaymentEvent
            {
                PaymentId = currentPayment.Id,

                EventType =
                    targetStatus == PaymentStatus.Approved
                        ? PaymentEventType.PaymentApproved
                        : PaymentEventType.PaymentDeclined,

                PreviousStatus =
                    currentPreviousStatus,

                NewStatus =
                    currentPayment.Status,

                Amount =
                    targetStatus == PaymentStatus.Approved
                        ? currentPayment.AuthorisedAmount
                        : currentPayment.Amount,

                Description =
                    currentPayment.AcmeResponseDescription,

                OccurredAtUtc =
                    GetTransactionDateUtc(
                        response.TransactionDateTime),

                RecordedAtUtc =
                    DateTime.UtcNow
            });

            statusChanged = true;
        }
        else if (!RequiresReconciliation(
                     currentPayment.Status))
        {
            message =
                "Payment was already resolved by another operation.";
        }

        // 10. Record every completed RTTQ attempt.
        _db.AcmeRequestLogs.Add(requestLog);

        // 11. Save the outcome, event and diagnostics together.
        await _db.SaveChangesAsync(persistToken);

        await transaction.CommitAsync(persistToken);

        _logger.LogInformation(
            "RTTQ completed for payment {PaymentId}, " +
            "transaction {TransactionId}, " +
            "status {Status}, changed {StatusChanged}",
            currentPayment.Id,
            currentPayment.TransactionId,
            currentPayment.Status,
            statusChanged);

        return BuildResult(
            currentPayment,
            currentPreviousStatus,
            statusChanged,
            message,
            queryResult);
    }

    private static bool RequiresReconciliation(
        PaymentStatus status)
    {
        return status == PaymentStatus.PendingReconciliation;
    }

    private static PaymentStatus? DetermineStatus(
        Payment payment,
        AcmePaymentResponseBody response)
    {
        // Processing or failure is not a confirmed outcome.
        if (response.TransactionStatus != "Success")
        {
            return null;
        }

        var responseCode =
            response.ResponseDetail?.Code;

        if (responseCode == "00" &&
            response.AuthAmount is > 0 &&
            response.AuthAmount <= payment.Amount)
        {
            return PaymentStatus.Approved;
        }

        if (response.ResponseDetail?.Source == "Acquirer" &&
            responseCode is "05" or "51" or "54")
        {
            return PaymentStatus.Declined;
        }

        return null;
    }

    private static DateTime GetTransactionDateUtc(
        string? transactionDateTime)
    {
        // Acme timestamps represent South African time
        // without an explicit timezone offset.
        if (DateTime.TryParseExact(
                transactionDateTime,
                "yyyy-MM-ddTHH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            var unspecifiedDate =
                DateTime.SpecifyKind(
                    parsedDate,
                    DateTimeKind.Unspecified);

            return new DateTimeOffset(
                    unspecifiedDate,
                    TimeSpan.FromHours(2))
                .UtcDateTime;
        }

        // Fall back to the observation time when the
        // provider timestamp is missing or malformed.
        return DateTime.UtcNow;
    }

    private static ReconciliationResult BuildResult(
        Payment payment,
        PaymentStatus previousStatus,
        bool statusChanged,
        string message,
        AcmeQueryResult? queryResult = null)
    {
        return new ReconciliationResult
        {
            PaymentId = payment.Id,

            AppPaymentId = payment.AppPaymentId,

            TransactionId = payment.TransactionId,

            PreviousStatus = previousStatus.ToString(),

            CurrentStatus = payment.Status.ToString(),

            StatusChanged = statusChanged,

            IsResolved =
                payment.Status != PaymentStatus.Pending &&
                !RequiresReconciliation(payment.Status),

            AcmeHttpStatusCode =
                queryResult is null
                    ? null
                    : (int)queryResult.StatusCode,

            AcmeResponseCode =
                payment.AcmeResponseCode,

            RetryAfterSeconds =
                queryResult?.RetryAfter?.TotalSeconds,

            Message = message
        };
    }
}