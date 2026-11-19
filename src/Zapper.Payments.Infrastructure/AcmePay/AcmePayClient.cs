using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zapper.Payments.Application.Payments.AcmePay;
using Zapper.Payments.Application.Payments.AcmePay.Contracts;

namespace Zapper.Payments.Infrastructure.AcmePay;

public class AcmePayClient : IAcmePayClient
{
    private readonly HttpClient _httpClient;
    private readonly AcmePayOptions _options;

    public AcmePayClient(
        HttpClient httpClient,
        IOptions<AcmePayOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    // Existing payment submission method.
    public async Task<AcmePaymentResponse> SubmitPaymentAsync(
        AcmePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "api/payment");

        ApplyAuthentication(httpRequest);

        httpRequest.Content = JsonContent.Create(request);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Acme Pay returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        var result = await response.Content
            .ReadFromJsonAsync<AcmePaymentResponse>(
                cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException(
            "Acme Pay returned an empty payment response.");
    }

    // New Real Time Transaction Query method.
    public async Task<AcmeQueryResult> QueryTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Transaction ID cannot be empty.",
                nameof(transactionId));
        }

        // Construct the RTTQ endpoint.
        var merchantId = Uri.EscapeDataString(
            _options.MerchantId);

        var encodedTransactionId = Uri.EscapeDataString(
            transactionId.ToString());

        var requestUrl =
            $"apirttq/query/Merchant/{merchantId}" +
            $"?TransactionID={encodedTransactionId}";

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get,
            requestUrl);

        ApplyAuthentication(httpRequest);

        // Execute the request using the existing HttpClient.
        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        AcmePaymentResponse? paymentResponse = null;

        // Successful HTTP responses must contain a valid
        // Acme payment response.
        if (response.IsSuccessStatusCode)
        {
            paymentResponse = await response.Content
                .ReadFromJsonAsync<AcmePaymentResponse>(
                    cancellationToken: cancellationToken);

            if (paymentResponse?.PaymentResponse is null)
            {
                throw new InvalidOperationException(
                    "Acme returned an invalid RTTQ response.");
            }
        }
        else
        {
            // Error responses may contain the Acme envelope,
            // an empty body or an unrelated error format.
            paymentResponse = await TryReadErrorResponseAsync(
                response,
                cancellationToken);
        }

        return new AcmeQueryResult
        {
            StatusCode = response.StatusCode,

            Response = paymentResponse,

            RetryAfter = GetRetryAfter(response)
        };
    }

    private void ApplyAuthentication(
        HttpRequestMessage request)
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _options.ApiKey);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));
    }

    private static async Task<AcmePaymentResponse?>
    TryReadErrorResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content
                .ReadFromJsonAsync<AcmePaymentResponse>(
                    cancellationToken: cancellationToken);

            return result?.PaymentResponse is null
                ? null
                : result;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static TimeSpan? GetRetryAfter(
        HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter is null)
        {
            return null;
        }

        // Retry-After can be expressed in seconds.
        if (retryAfter.Delta.HasValue)
        {
            return retryAfter.Delta.Value < TimeSpan.Zero
                ? TimeSpan.Zero
                : retryAfter.Delta.Value;
        }

        // Alternatively, it can be an HTTP date.
        if (retryAfter.Date.HasValue)
        {
            var delay =
                retryAfter.Date.Value - DateTimeOffset.UtcNow;

            return delay < TimeSpan.Zero
                ? TimeSpan.Zero
                : delay;
        }

        return null;
    }
}