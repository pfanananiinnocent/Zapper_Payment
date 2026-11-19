using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zapper.Payments.Application.Payments.AcmePay;
using Zapper.Payments.Infrastructure.AcmePay;
using Zapper.Payments.Application.Payments;
using Zapper.Payments.Infrastructure.Services;

namespace Zapper.Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AcmePayOptions>(
            configuration.GetSection(AcmePayOptions.SectionName));

        services.AddHttpClient<IAcmePayClient, AcmePayClient>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<AcmePayOptions>>()
                    .Value;

                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(
                    options.TimeoutSeconds);
            });

        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}