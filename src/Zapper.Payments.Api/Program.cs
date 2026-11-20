
using Microsoft.EntityFrameworkCore;
using Zapper.Payments.Infrastructure.Persistence;
using Zapper.Payments.Infrastructure;
using Zapper.Payments.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddDbContext<ZapperPaymentsDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("ZapperPayments")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddAuthentication()
    .AddScheme<
        AcmeBasicAuthenticationOptions,
        AcmeBasicAuthenticationHandler>(
        AcmeAuthenticationDefaults.Scheme,
        options =>
        {
            builder.Configuration
                .GetSection(
                    AcmeAuthenticationDefaults.ConfigurationSection)
                .Bind(options);
        });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AcmeAuthenticationDefaults.Policy,
        policy =>
        {
            policy.AddAuthenticationSchemes(
                AcmeAuthenticationDefaults.Scheme);

            policy.RequireAuthenticatedUser();
        });
});

var app = builder.Build();

// Initialise the database and seed demonstration data.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider
        .GetRequiredService<ZapperPaymentsDbContext>();

    await dbContext.Database.MigrateAsync();

    await DatabaseSeeder.SeedAsync(dbContext);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Docker uses HTTP locally.
// Production HTTPS would terminate at the load balancer.
if (app.Configuration.GetValue<bool>("EnableHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Service = "Zapper Payments API"
}));

app.Run();
