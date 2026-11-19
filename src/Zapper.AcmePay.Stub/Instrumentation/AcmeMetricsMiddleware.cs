using Microsoft.Extensions.Options;

namespace Zapper.AcmePay.Stub.Instrumentation;

public sealed class AcmeMetricsMiddleware
{
    private readonly RequestDelegate _next;

    public AcmeMetricsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        AcmeRequestMetrics metrics,
        IOptions<AcmeMetricsOptions> options)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;

        var isPayment =
            HttpMethods.IsPost(context.Request.Method) &&
            string.Equals(
                path.Value?.TrimEnd('/'),
                "/api/payment",
                StringComparison.OrdinalIgnoreCase);

        var isRttq =
            HttpMethods.IsGet(context.Request.Method) &&
            path.StartsWithSegments("/apirttq/query");

        if (!isPayment && !isRttq)
        {
            await _next(context);
            return;
        }

        var startedAt = metrics.BeginRequest(isRttq);

        var statusCode = 500;
        var aborted = false;

        try
        {
            var delay = isRttq
                ? settings.RttqDelayMilliseconds
                : settings.PaymentDelayMilliseconds;

            if (delay > 0)
            {
                await Task.Delay(
                    delay,
                    context.RequestAborted);
            }

            await _next(context);

            statusCode = context.Response.StatusCode;
        }
        catch (OperationCanceledException)
            when (context.RequestAborted
                .IsCancellationRequested)
        {
            aborted = true;
            throw;
        }
        finally
        {
            metrics.EndRequest(
                startedAt,
                statusCode,
                aborted ||
                context.RequestAborted
                    .IsCancellationRequested);
        }
    }
}