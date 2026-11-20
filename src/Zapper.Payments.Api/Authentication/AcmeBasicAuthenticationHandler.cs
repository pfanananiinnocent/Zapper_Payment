
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Zapper.Payments.Api.Authentication;

public sealed class AcmeBasicAuthenticationHandler
    : AuthenticationHandler<AcmeBasicAuthenticationOptions>
{
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    public AcmeBasicAuthenticationHandler(
        IOptionsMonitor<AcmeBasicAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult>
        HandleAuthenticateAsync()
    {
        // Fail closed when credentials are not configured.

        if (string.IsNullOrWhiteSpace(Options.Username)
            || string.IsNullOrWhiteSpace(Options.Password)
            || Options.Username.Contains(':'))
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "Acme authentication is not configured."));
        }

        // Reject missing or duplicate Authorization headers.

        if (!Request.Headers.TryGetValue(
                HeaderNames.Authorization,
                out var authorizationHeaders))
        {
            return Task.FromResult(
                AuthenticateResult.NoResult());
        }

        if (authorizationHeaders.Count != 1)
        {
            return InvalidCredentials();
        }

        // Parse the Authorization header.

        if (!AuthenticationHeaderValue.TryParse(
                authorizationHeaders[0],
                out var authorizationHeader))
        {
            return InvalidCredentials();
        }

        if (!string.Equals(
                authorizationHeader.Scheme,
                "Basic",
                StringComparison.OrdinalIgnoreCase))
        {
            return InvalidCredentials();
        }

        var encodedCredentials =
            authorizationHeader.Parameter;

        if (string.IsNullOrWhiteSpace(encodedCredentials)
            || encodedCredentials.Length > 4096)
        {
            return InvalidCredentials();
        }

        byte[]? credentialBytes = null;

        try
        {
            // Decode Base64 credentials.

            credentialBytes =
                Convert.FromBase64String(
                    encodedCredentials);

            var credentials =
                StrictUtf8.GetString(credentialBytes);

            // Basic authentication uses username:password.
            // Passwords may contain additional colons.

            var separatorIndex =
                credentials.IndexOf(':');

            if (separatorIndex <= 0)
            {
                return InvalidCredentials();
            }

            var username =
                credentials[..separatorIndex];

            var password =
                credentials[(separatorIndex + 1)..];

            // Evaluate both comparisons without
            // short-circuiting.

            var usernameMatches = SecureEquals(
                username,
                Options.Username);

            var passwordMatches = SecureEquals(
                password,
                Options.Password);

            if (!(usernameMatches & passwordMatches))
            {
                return InvalidCredentials();
            }

            // Create the authenticated service identity.

            var claims = new[]
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    Options.Username),

                new Claim(
                    ClaimTypes.Name,
                    Options.Username)
            };

            var identity = new ClaimsIdentity(
                claims,
                Scheme.Name);

            var principal =
                new ClaimsPrincipal(identity);

            var ticket = new AuthenticationTicket(
                principal,
                Scheme.Name);

            return Task.FromResult(
                AuthenticateResult.Success(ticket));
        }
        catch (FormatException)
        {
            return InvalidCredentials();
        }
        catch (DecoderFallbackException)
        {
            return InvalidCredentials();
        }
        finally
        {
            if (credentialBytes is not null)
            {
                CryptographicOperations.ZeroMemory(
                    credentialBytes);
            }
        }
    }

    protected override Task HandleChallengeAsync(
        AuthenticationProperties properties)
    {
        Response.StatusCode =
            StatusCodes.Status401Unauthorized;

        Response.Headers[HeaderNames.WWWAuthenticate] =
            $"Basic realm=\"{AcmeAuthenticationDefaults.Realm}\", charset=\"UTF-8\"";

        return Task.CompletedTask;
    }

    private static Task<AuthenticateResult>
        InvalidCredentials()
    {
        return Task.FromResult(
            AuthenticateResult.Fail(
                "Invalid Acme authentication credentials."));
    }

    private static bool SecureEquals(
        string supplied,
        string expected)
    {
        var suppliedBytes =
            Encoding.UTF8.GetBytes(supplied);

        var expectedBytes =
            Encoding.UTF8.GetBytes(expected);

        var suppliedHash =
            SHA256.HashData(suppliedBytes);

        var expectedHash =
            SHA256.HashData(expectedBytes);

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                suppliedHash,
                expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                suppliedBytes);

            CryptographicOperations.ZeroMemory(
                expectedBytes);

            CryptographicOperations.ZeroMemory(
                suppliedHash);

            CryptographicOperations.ZeroMemory(
                expectedHash);
        }
    }
}
