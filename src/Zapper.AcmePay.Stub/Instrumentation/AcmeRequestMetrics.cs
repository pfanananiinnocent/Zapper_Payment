using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Zapper.AcmePay.Stub.Instrumentation;

public sealed class AcmeRequestMetrics
{
    private readonly object _sync = new();

    private readonly Queue<long> _arrivalTimestamps = new();

    private readonly AcmeMetricsOptions _options;

    private DateTimeOffset _metricsStartedAtUtc =
        DateTimeOffset.UtcNow;

    private long _totalRequests;
    private long _paymentRequests;
    private long _rttqRequests;
    private long _completedRequests;
    private long _abortedRequests;

    private int _activeRequests;
    private int _peakConcurrentRequests;
    private int _peakRollingRequestsPerSecond;

    private long _rateLimitViolations;
    private long _concurrencyLimitViolations;

    private long _http2xxResponses;
    private long _http429Responses;
    private long _http4xxResponses;
    private long _http5xxResponses;

    private double _totalDurationMs;
    private double _maximumDurationMs;

    public AcmeRequestMetrics(
        IOptions<AcmeMetricsOptions> options)
    {
        _options = options.Value;
    }

    public long BeginRequest(bool isRttq)
    {
        lock (_sync)
        {
            var now = Stopwatch.GetTimestamp();

            RemoveExpiredTimestamps(now);

            _arrivalTimestamps.Enqueue(now);

            _totalRequests++;

            if (isRttq)
            {
                _rttqRequests++;
            }
            else
            {
                _paymentRequests++;
            }

            _activeRequests++;

            _peakConcurrentRequests = Math.Max(
                _peakConcurrentRequests,
                _activeRequests);

            _peakRollingRequestsPerSecond = Math.Max(
                _peakRollingRequestsPerSecond,
                _arrivalTimestamps.Count);

            if (_arrivalTimestamps.Count >
                _options.RequestsPerSecondLimit)
            {
                _rateLimitViolations++;
            }

            if (_activeRequests >
                _options.ExpectedMaxConcurrency)
            {
                _concurrencyLimitViolations++;
            }

            return now;
        }
    }

    public void EndRequest(
        long startedAt,
        int statusCode,
        bool aborted)
    {
        var duration = Stopwatch
            .GetElapsedTime(startedAt)
            .TotalMilliseconds;

        lock (_sync)
        {
            _activeRequests--;
            _completedRequests++;

            _totalDurationMs += duration;

            _maximumDurationMs = Math.Max(
                _maximumDurationMs,
                duration);

            if (aborted)
            {
                _abortedRequests++;
                return;
            }

            if (statusCode >= 200 &&
                statusCode < 300)
            {
                _http2xxResponses++;
            }
            else if (statusCode == 429)
            {
                _http429Responses++;
            }
            else if (statusCode >= 400 &&
                     statusCode < 500)
            {
                _http4xxResponses++;
            }
            else if (statusCode >= 500)
            {
                _http5xxResponses++;
            }
        }
    }

    public AcmeMetricsSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            RemoveExpiredTimestamps(
                Stopwatch.GetTimestamp());

            return new AcmeMetricsSnapshot
            {
                MetricsStartedAtUtc =
                    _metricsStartedAtUtc,

                CapturedAtUtc =
                    DateTimeOffset.UtcNow,

                TotalRequests = _totalRequests,

                PaymentRequests = _paymentRequests,

                RttqRequests = _rttqRequests,

                CompletedRequests = _completedRequests,

                AbortedRequests = _abortedRequests,

                ActiveRequests = _activeRequests,

                PeakConcurrentRequests =
                    _peakConcurrentRequests,

                CurrentRollingRequestsPerSecond =
                    _arrivalTimestamps.Count,

                PeakRollingRequestsPerSecond =
                    _peakRollingRequestsPerSecond,

                ConfiguredRequestsPerSecondLimit =
                    _options.RequestsPerSecondLimit,

                ExpectedMaxConcurrency =
                    _options.ExpectedMaxConcurrency,

                RateLimitViolations =
                    _rateLimitViolations,

                ConcurrencyLimitViolations =
                    _concurrencyLimitViolations,

                IsRateLimitCompliant =
                    _rateLimitViolations == 0,

                IsConcurrencyLimitCompliant =
                    _concurrencyLimitViolations == 0,

                Http2xxResponses = _http2xxResponses,

                Http429Responses = _http429Responses,

                Http4xxResponses = _http4xxResponses,

                Http5xxResponses = _http5xxResponses,

                AverageRequestDurationMs =
                    _completedRequests == 0
                        ? 0
                        : Math.Round(
                            _totalDurationMs /
                            _completedRequests, 2),

                MaximumRequestDurationMs =
                    Math.Round(_maximumDurationMs, 2),

                PaymentDelayMilliseconds =
                    _options.PaymentDelayMilliseconds,

                RttqDelayMilliseconds =
                    _options.RttqDelayMilliseconds
            };
        }
    }

    public bool TryReset()
    {
        lock (_sync)
        {
            if (_activeRequests > 0)
            {
                return false;
            }

            _arrivalTimestamps.Clear();

            _metricsStartedAtUtc =
                DateTimeOffset.UtcNow;

            _totalRequests = 0;
            _paymentRequests = 0;
            _rttqRequests = 0;
            _completedRequests = 0;
            _abortedRequests = 0;

            _activeRequests = 0;
            _peakConcurrentRequests = 0;
            _peakRollingRequestsPerSecond = 0;

            _rateLimitViolations = 0;
            _concurrencyLimitViolations = 0;

            _http2xxResponses = 0;
            _http429Responses = 0;
            _http4xxResponses = 0;
            _http5xxResponses = 0;

            _totalDurationMs = 0;
            _maximumDurationMs = 0;

            return true;
        }
    }

    private void RemoveExpiredTimestamps(long now)
    {
        while (_arrivalTimestamps.Count > 0)
        {
            var oldest = _arrivalTimestamps.Peek();

            if (now - oldest < Stopwatch.Frequency)
            {
                break;
            }

            _arrivalTimestamps.Dequeue();
        }
    }
}