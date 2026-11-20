using Microsoft.EntityFrameworkCore;
using Zapper.Payments.Infrastructure.Persistence;
using Zapper.Payments.Infrastructure;
using Zapper.Payments.Api.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Add services to the container.
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
                    AcmeAuthenticationDefaults
                        .ConfigurationSection)
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


using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider
        .GetRequiredService<ZapperPaymentsDbContext>();

    await DatabaseSeeder.SeedAsync(dbContext);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();

app.UseAuthorization();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();