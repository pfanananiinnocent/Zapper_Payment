using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zapper.Payments.Application.Payments.AcmePay;
using Zapper.Payments.Infrastructure.AcmePay;
using Zapper.Payments.Application.Payments;
using Zapper.Payments.Infrastructure.Services;
using Zapper.Payments.Application.Payments.Reconciliation;
using Zapper.Payments.Infrastructure.BackgroundServices;

namespace Zapper.Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure( this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AcmePayOptions>(
            configuration.GetSection(AcmePayOptions.SectionName));

        services.AddHttpClient<IAcmePayClient, AcmePayClient>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<AcmePayOptions>>().Value;

                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(
                    options.TimeoutSeconds);
            });

        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentReconciliationService,PaymentReconciliationService>();

        services.AddOptions<ReconciliationOptions>().Bind(configuration.GetSection(
        ReconciliationOptions.SectionName))
        .Validate(
            options =>
                options.PollIntervalSeconds > 0 &&
                options.BatchSize > 0 &&
                options.BatchSize <= 100 &&
                options.RetryDelaySeconds > 0 &&
                options.InitialDelaySeconds >= 0,
            "Invalid reconciliation configuration.")
        .ValidateOnStart();

        services.AddHostedService<PaymentReconciliationWorker>();

        return services;
    }
}