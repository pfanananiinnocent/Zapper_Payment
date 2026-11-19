using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zapper.Payments.Infrastructure.AcmePay;

namespace Zapper.Payments.Infrastructure.AcmePay.Resilience;

public sealed class AcmeResilienceHandler
    : DelegatingHandler
{
    private readonly AcmeRequestLimiter _limiter;

    private readonly AcmeResilienceOptions _options;

    private readonly ILogger<AcmeResilienceHandler>  _logger;
    private readonly AcmePayOptions _gatewayOptions;

    public AcmeResilienceHandler( AcmeRequestLimiter limiter, IOptions<AcmeResilienceOptions> options, IOptions<AcmePayOptions> gatewayOptions, ILogger<AcmeResilienceHandler> logger)
    {
        _limiter = limiter;
        _options = options.Value;
        _gatewayOptions = gatewayOptions.Value;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage>SendAsync( HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var queueTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        queueTimeout.CancelAfter(
            TimeSpan.FromSeconds(
                _options.QueueTimeoutSeconds));

        var queueToken = queueTimeout.Token;

        System.Threading.RateLimiting.RateLimitLease?
            concurrencyLease = null;

        try
        {
            // 1. Acquire a concurrency permit.
            concurrencyLease =
                await _limiter.AcquireConcurrencyAsync(
                    queueToken);

            if (!concurrencyLease.IsAcquired)
            {
                throw CreateLocalThrottleException(
                    "Acme outbound concurrency queue is full.");
            }

            // 2. Respect cooldown and request pacing.
            while (true)
            {
                await _limiter.WaitForCooldownAsync(
                    queueToken);

                using var rateLease =
                    await _limiter.AcquireRateAsync(
                        queueToken);

                if (!rateLease.IsAcquired)
                {
                    throw CreateLocalThrottleException(
                        "Acme outbound rate queue is full.");
                }

                // Another request might have received HTTP 429
                // while this request was waiting for a permit.
                if (_limiter.GetRemainingCooldown()
                    > TimeSpan.Zero)
                {
                    continue;
                }

                break;
            }

            queueToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            concurrencyLease?.Dispose();

            _logger.LogWarning(
                "Acme outbound request exceeded " +
                "the local queue timeout.");

            throw CreateLocalThrottleException(
                "Acme outbound queue timeout. " +
                "Request was not sent to the provider.");
        }
        catch
        {
            concurrencyLease?.Dispose();

            throw;
        }

        // Hold the concurrency permit until the response
        // content has been received.
        using (concurrencyLease)
        {
            _logger.LogDebug(
                "Dispatching Acme HTTP request. " +
                "Method: {Method}",
                request.Method.Method);

            using var gatewayTimeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken);

            gatewayTimeout.CancelAfter( TimeSpan.FromSeconds( _gatewayOptions.TimeoutSeconds));

            var response = await base.SendAsync( request, gatewayTimeout.Token);

            try
            {
                // 3. Apply a shared cooldown on provider 429.
                if (response.StatusCode ==
                    HttpStatusCode.TooManyRequests)
                {
                    var retryAfter =
                        GetRetryAfter(response)
                        ?? TimeSpan.FromSeconds(
                            _options.DefaultRetryAfterSeconds);

                    _limiter.BlockFor(retryAfter);

                    _logger.LogWarning(
                        "Acme returned HTTP 429. " +
                        "Shared cooldown: {RetryAfterSeconds}s.",
                        retryAfter.TotalSeconds);
                }

                // 4. Buffer the small Acme JSON response
                // before releasing the concurrency permit.
                if (response.Content is not null)
                {
                    await response.Content.ReadAsByteArrayAsync( gatewayTimeout.Token);
                }

                // No automatic resubmission or retry.
                return response;
            }
            catch
            {
                response.Dispose();

                throw;
            }
        }
    }

    private static AcmeRequestNotDispatchedException CreateLocalThrottleException(string message)
    {
        return new AcmeRequestNotDispatchedException(message);
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retryAfter =
            response.Headers.RetryAfter;

        if (retryAfter?.Delta is TimeSpan delta)
        {
            return delta < TimeSpan.Zero
                ? TimeSpan.Zero
                : delta;
        }

        if (retryAfter?.Date is DateTimeOffset date)
        {
            var delay = date - DateTimeOffset.UtcNow;

            return delay < TimeSpan.Zero
                ? TimeSpan.Zero
                : delay;
        }

        return null;
    }
}