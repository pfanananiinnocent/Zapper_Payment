using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zapper.Payments.Application.Payments.AcmePay;
using Zapper.Payments.Infrastructure.AcmePay;
using Zapper.Payments.Application.Payments;
using Zapper.Payments.Infrastructure.Services;
using Zapper.Payments.Application.Payments.Reconciliation;
using Zapper.Payments.Infrastructure.BackgroundServices;
using Zapper.Payments.Infrastructure.AcmePay.Resilience;

namespace Zapper.Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AcmePayOptions>(configuration.GetSection(
                AcmePayOptions.SectionName));

        services.AddOptions<AcmeResilienceOptions>().Bind(configuration.GetSection(AcmeResilienceOptions.SectionName))
            .Validate(
                options =>
                    options.RequestsPerSecond > 0 &&
                    options.RequestsPerSecond <= 20 &&
                    options.MaxConcurrentRequests > 0 &&
                    options.MaxConcurrentRequests <= 100 &&
                    options.QueueLimit >= 0 &&
                    options.QueueLimit <= 1000 &&
                    options.QueueTimeoutSeconds > 0 &&
                    options.QueueTimeoutSeconds <= 30 &&
                    options.DefaultRetryAfterSeconds > 0,
                "Invalid Acme resilience configuration.")
            .ValidateOnStart();

        //Shared limiter across all Acme HTTP clients.
        services.AddSingleton<AcmeRequestLimiter>();

        //Outbound HTTP resilience handler.
        services.AddTransient<AcmeResilienceHandler>();
        services.AddHttpClient<IAcmePayClient,AcmePayClient>((serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<
                        IOptions<AcmePayOptions>>()
                    .Value;

                client.BaseAddress =
                    new Uri(options.BaseUrl);

                client.Timeout =
                    TimeSpan.FromSeconds(
                        options.TimeoutSeconds);
            })
            .AddHttpMessageHandler<
                AcmeResilienceHandler>();

        //payment service.
        services.AddScoped<IPaymentService,PaymentService>();

        //reconciliation service.
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();

        //background worker configuration.
        services.AddOptions<ReconciliationOptions>().Bind(configuration.GetSection(ReconciliationOptions.SectionName))
            .Validate(
                options =>
                    options.PollIntervalSeconds > 0 &&
                    options.BatchSize > 0 &&
                    options.BatchSize <= 100 &&
                    options.RetryDelaySeconds > 0 &&
                    options.InitialDelaySeconds >= 0,
                "Invalid reconciliation configuration.")
            .ValidateOnStart();

        //background reconciliation worker.
        services.AddHostedService<PaymentReconciliationWorker>();

        return services;
    }
}