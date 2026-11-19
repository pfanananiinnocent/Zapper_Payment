using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zapper.Payments.Application.Payments.Reconciliation;
using Zapper.Payments.Domain.Enums;
using Zapper.Payments.Infrastructure.Persistence;

namespace Zapper.Payments.Infrastructure.BackgroundServices;

public sealed class PaymentReconciliationWorker: BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ReconciliationOptions _options;

    private readonly ILogger<PaymentReconciliationWorker> _logger;

    private DateTimeOffset _gatewayRetryAfter = DateTimeOffset.MinValue;

    private readonly Dictionary<long, DateTimeOffset> _nextAttempt = new();

    public PaymentReconciliationWorker( IServiceScopeFactory scopeFactory, IOptions<ReconciliationOptions> options, ILogger<PaymentReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync( CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Payment reconciliation worker is disabled.");

            return;
        }

        _logger.LogInformation(
            "Payment reconciliation worker started. " +
            "Polling interval: {IntervalSeconds}s. " +
            "Batch size: {BatchSize}.",
            _options.PollIntervalSeconds,
            _options.BatchSize);

        using var timer = new PeriodicTimer( TimeSpan.FromSeconds( _options.PollIntervalSeconds));

        try
        {
            do
            {
                try
                {
                    await ProcessPendingPaymentsAsync(
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Payment reconciliation cycle failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(
                       stoppingToken));
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        finally
        {
            _logger.LogInformation(
                "Payment reconciliation worker stopped.");
        }
    }

    private async Task ProcessPendingPaymentsAsync( CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // Respect provider throttling before issuing
        // any additional reconciliation requests.
        if (now < _gatewayRetryAfter)
        {
            return;
        }

        // Remove expired retry entries.
        foreach (var paymentId in _nextAttempt
                     .Where(x => x.Value <= now)
                     .Select(x => x.Key)
                     .ToArray())
        {
            _nextAttempt.Remove(paymentId);
        }

        var deferredPaymentIds =
            _nextAttempt.Keys.ToArray();

        var eligibleBefore = DateTime.UtcNow.AddSeconds(
            -_options.InitialDelaySeconds);

        // 1. Discover eligible unresolved payments.
        await using var discoveryScope =
            _scopeFactory.CreateAsyncScope();

        var db = discoveryScope.ServiceProvider
            .GetRequiredService<ZapperPaymentsDbContext>();

        var pendingPayments = await db.Payments
            .AsNoTracking()
            .Where(x =>
                x.Status ==
                    PaymentStatus.PendingReconciliation &&
                x.UpdatedAtUtc <= eligibleBefore &&
                !deferredPaymentIds.Contains(x.Id))
            .OrderBy(x => x.UpdatedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.AppPaymentId
            })
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (pendingPayments.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Discovered {PaymentCount} payments " +
            "requiring reconciliation.",
            pendingPayments.Count);

        // 2. Reconcile each payment independently.
        foreach (var payment in pendingPayments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow < _gatewayRetryAfter)
            {
                break;
            }

            try
            {
                // A fresh scope per payment prevents
                // sharing a DbContext across operations.
                await using var paymentScope =
                    _scopeFactory.CreateAsyncScope();

                var reconciliationService =
                    paymentScope.ServiceProvider
                        .GetRequiredService<
                            IPaymentReconciliationService>();

                var result =
                    await reconciliationService.ReconcileAsync(
                        payment.AppPaymentId,
                        cancellationToken);

                if (result is null)
                {
                    _nextAttempt.Remove(payment.Id);

                    continue;
                }

                if (result.IsResolved)
                {
                    _nextAttempt.Remove(payment.Id);

                    _logger.LogInformation(
                        "Payment {PaymentId} resolved. " +
                        "Status: {Status}. " +
                        "StatusChanged: {StatusChanged}.",
                        payment.Id,
                        result.CurrentStatus,
                        result.StatusChanged);

                    continue;
                }

                // 3. Schedule unresolved payments
                // for a later reconciliation attempt.
                var delaySeconds = Math.Max(
                    _options.RetryDelaySeconds,
                    result.RetryAfterSeconds ?? 0);

                var nextAttempt =
                    DateTimeOffset.UtcNow.AddSeconds(
                        delaySeconds);

                _nextAttempt[payment.Id] = nextAttempt;

                _logger.LogInformation(
                    "Payment {PaymentId} remains unresolved. " +
                    "Next eligible attempt: {NextAttemptUtc}. " +
                    "Acme HTTP status: {HttpStatus}.",
                    payment.Id,
                    nextAttempt,
                    result.AcmeHttpStatusCode);

                // 4. Pause all reconciliation requests
                // from this worker if Acme throttles us.
                if (result.AcmeHttpStatusCode == 429 ||
                    result.RetryAfterSeconds.HasValue)
                {
                    _gatewayRetryAfter = nextAttempt;

                    _logger.LogWarning(
                        "Acme reconciliation requests paused " +
                        "until {RetryAfterUtc}.",
                        _gatewayRetryAfter);

                    break;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _nextAttempt[payment.Id] =
                    DateTimeOffset.UtcNow.AddSeconds(
                        _options.RetryDelaySeconds);

                _logger.LogError(
                    ex,
                    "Automatic reconciliation failed " +
                    "for payment {PaymentId}.",
                    payment.Id);
            }
        }
    }
}