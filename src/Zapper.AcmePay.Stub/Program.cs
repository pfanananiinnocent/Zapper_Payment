using Zapper.AcmePay.Stub.Instrumentation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddOptions<AcmeMetricsOptions>()
    .Bind(builder.Configuration.GetSection(
        AcmeMetricsOptions.SectionName))
    .Validate(options =>
        options.RequestsPerSecondLimit > 0 &&
        options.ExpectedMaxConcurrency > 0 &&
        options.PaymentDelayMilliseconds >= 0 &&
        options.RttqDelayMilliseconds >= 0,
        "Invalid Acme metrics configuration.")
    .ValidateOnStart();

builder.Services.AddSingleton<AcmeRequestMetrics>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<AcmeMetricsMiddleware>();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Service = "Local Acme Pay Simulator"
}));

app.Run();