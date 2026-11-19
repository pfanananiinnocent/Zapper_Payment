using System.Net.Http;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Zapper.Payments.Infrastructure.AcmePay.Resilience;

public sealed class AcmeRequestLimiter : IDisposable
{
    private readonly ConcurrencyLimiter _concurrencyLimiter;

    private readonly TokenBucketRateLimiter _rateLimiter;

    private long _blockedUntilUtcTicks;

    public AcmeRequestLimiter(IOptions<AcmeResilienceOptions> options)
    {
        var settings = options.Value;

        _concurrencyLimiter = new ConcurrencyLimiter(
            new ConcurrencyLimiterOptions
            {
                PermitLimit = settings.MaxConcurrentRequests,

                QueueLimit = settings.QueueLimit,

                QueueProcessingOrder =
                    QueueProcessingOrder.OldestFirst
            });

        _rateLimiter = new TokenBucketRateLimiter(
            new TokenBucketRateLimiterOptions
            {
                TokenLimit = 1,

                TokensPerPeriod = 1,

                ReplenishmentPeriod =
                    TimeSpan.FromMilliseconds(
                        Math.Ceiling(
                            1000.0 / settings.RequestsPerSecond)),

                AutoReplenishment = true,

                QueueLimit = settings.MaxConcurrentRequests,

                QueueProcessingOrder =
                    QueueProcessingOrder.OldestFirst
            });
    }

    public ValueTask<RateLimitLease>AcquireConcurrencyAsync( CancellationToken cancellationToken)
    {
        return _concurrencyLimiter.AcquireAsync(
            1,
            cancellationToken);
    }

    public ValueTask<RateLimitLease> AcquireRateAsync(CancellationToken cancellationToken)
    {
        return _rateLimiter.AcquireAsync(
            1,
            cancellationToken);
    }

    public TimeSpan GetRemainingCooldown()
    {
        var blockedUntil =
            Interlocked.Read(ref _blockedUntilUtcTicks);

        var remainingTicks =
            blockedUntil - DateTime.UtcNow.Ticks;

        return remainingTicks > 0
            ? TimeSpan.FromTicks(remainingTicks)
            : TimeSpan.Zero;
    }

    public void BlockFor(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var now = DateTime.UtcNow.Ticks;

        var target = now + Math.Min(
            duration.Ticks,
            DateTime.MaxValue.Ticks - now);

        // Never shorten an existing provider cooldown.
        while (true)
        {
            var current =
                Interlocked.Read(
                    ref _blockedUntilUtcTicks);

            if (target <= current)
            {
                return;
            }

            var original =
                Interlocked.CompareExchange(
                    ref _blockedUntilUtcTicks,
                    target,
                    current);

            if (original == current)
            {
                return;
            }
        }
    }

    public async Task WaitForCooldownAsync( CancellationToken cancellationToken)
    {
        while (true)
        {
            var remaining = GetRemainingCooldown();

            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            // Bounded delay followed by another check.
            var delay = remaining > TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : remaining;

            await Task.Delay(delay, cancellationToken);
        }
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();

        _concurrencyLimiter.Dispose();
    }
}