
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zapper.Payments.Api.Authentication;
using Xunit;

namespace Zapper.Payments.IntegrationTests.Authentication;

public sealed class AcmeBasicAuthenticationHandlerTests
{
    private const string Username = "test-acme-client";

    private const string Password = "test-password:123";

    [Fact]
    public async Task Authenticate_ValidCredentials_Succeeds()
    {
        var header = CreateBasicHeader(
            Username,
            Password);

        var handler = await CreateHandlerAsync(header);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);

        Assert.NotNull(result.Principal);

        Assert.Equal(
            Username,
            result.Principal!.Identity!.Name);

        Assert.Equal(
            AcmeAuthenticationDefaults.Scheme,
            result.Principal.Identity.AuthenticationType);
    }

    [Fact]
    public async Task Authenticate_MissingHeader_ReturnsNoResult()
    {
        var handler = await CreateHandlerAsync(null);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Theory]
    [InlineData("incorrect-user", Password)]
    [InlineData(Username, "incorrect-password")]
    [InlineData("incorrect-user", "incorrect-password")]
    [InlineData(Username, "")]
    [InlineData("", Password)]
    public async Task Authenticate_InvalidCredentials_Fails(
        string username,
        string password)
    {
        var header = CreateBasicHeader(
            username,
            password);

        var handler = await CreateHandlerAsync(header);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Theory]
    [InlineData("Basic INVALID_BASE64!!!")]
    [InlineData("Basic")]
    [InlineData("Bearer sample-token")]
    [InlineData("Basic bm9zZXBhcmF0b3I=")]
    public async Task Authenticate_InvalidHeader_Fails(
        string header)
    {
        var handler = await CreateHandlerAsync(header);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Authenticate_MissingConfiguration_Fails()
    {
        var header = CreateBasicHeader(
            Username,
            Password);

        var handler = await CreateHandlerAsync(
            header,
            new AcmeBasicAuthenticationOptions());

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Authenticate_DuplicateHeaders_Fails()
    {
        var header = CreateBasicHeader(
            Username,
            Password);

        var context = new DefaultHttpContext();

        context.Request.Headers.Append(
            "Authorization",
            header);

        context.Request.Headers.Append(
            "Authorization",
            header);

        var handler = await CreateHandlerAsync(
            null,
            context: context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Authenticate_SchemeIsCaseInsensitive()
    {
        var header = CreateBasicHeader(
            Username,
            Password)
            .Replace("Basic ", "basic ");

        var handler = await CreateHandlerAsync(header);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Authenticate_PasswordContainingColon_Succeeds()
    {
        var header = CreateBasicHeader(
            Username,
            Password);

        var handler = await CreateHandlerAsync(header);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Authenticate_OversizedHeader_Fails()
    {
        var header =
            "Basic " + new string('A', 5000);

        var handler = await CreateHandlerAsync(header);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Authenticate_InvalidUtf8_Fails()
    {
        var malformedBytes = new byte[]
        {
            0xC3, 0x28, 0x3A, 0x61
        };

        var header = "Basic " +
            Convert.ToBase64String(malformedBytes);

        var handler = await CreateHandlerAsync(header);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Challenge_Returns401WithAuthenticateHeader()
    {
        var context = new DefaultHttpContext();

        var handler = await CreateHandlerAsync(
            null,
            context: context);

        await handler.ChallengeAsync(
            new AuthenticationProperties());

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            context.Response.StatusCode);

        Assert.Contains(
            "Basic",
            context.Response
                .Headers["WWW-Authenticate"]
                .ToString());

        Assert.Contains(
            AcmeAuthenticationDefaults.Realm,
            context.Response
                .Headers["WWW-Authenticate"]
                .ToString());
    }

    private static string CreateBasicHeader(
        string username,
        string password)
    {
        var credentials = $"{username}:{password}";

        return "Basic " +
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(credentials));
    }

    private static async Task<AcmeBasicAuthenticationHandler>
        CreateHandlerAsync(
            string? authorizationHeader,
            AcmeBasicAuthenticationOptions? options = null,
            DefaultHttpContext? context = null)
    {
        context ??= new DefaultHttpContext();

        options ??= new AcmeBasicAuthenticationOptions
        {
            Username = Username,
            Password = Password
        };

        if (authorizationHeader is not null)
        {
            context.Request.Headers["Authorization"] =
                authorizationHeader;
        }

        var optionsMonitor =
            new TestOptionsMonitor<
                AcmeBasicAuthenticationOptions>(options);

        var handler =
            new AcmeBasicAuthenticationHandler(
                optionsMonitor,
                NullLoggerFactory.Instance,
                UrlEncoder.Default);

        var scheme = new AuthenticationScheme(
            AcmeAuthenticationDefaults.Scheme,
            AcmeAuthenticationDefaults.Scheme,
            typeof(AcmeBasicAuthenticationHandler));

        await handler.InitializeAsync(
            scheme,
            context);

        return handler;
    }

    private sealed class TestOptionsMonitor<T>
        : IOptionsMonitor<T>
    {
        private readonly T _options;

        public TestOptionsMonitor(T options)
        {
            _options = options;
        }

        public T CurrentValue => _options;

        public T Get(string? name)
        {
            return _options;
        }

        public IDisposable? OnChange(
            Action<T, string?> listener)
        {
            return null;
        }
    }
}
