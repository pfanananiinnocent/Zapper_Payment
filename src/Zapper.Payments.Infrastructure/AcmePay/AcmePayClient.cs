using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    public async Task<AcmePaymentResponse> SubmitPaymentAsync(
        AcmePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "api/payment");

        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _options.ApiKey);

        httpRequest.Content = JsonContent.Create(request);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(
                cancellationToken);

            throw new HttpRequestException(
                $"Acme Pay returned HTTP {(int)response.StatusCode}. " +
                $"Response: {errorContent}",
                null,
                response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync
            <AcmePaymentResponse>(
                cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException(
            "Acme Pay returned an empty payment response.");
    }
}