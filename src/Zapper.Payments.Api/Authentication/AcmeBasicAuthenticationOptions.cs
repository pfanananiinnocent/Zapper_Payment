
using Microsoft.AspNetCore.Authentication;

namespace Zapper.Payments.Api.Authentication;

public sealed class AcmeBasicAuthenticationOptions
    : AuthenticationSchemeOptions
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
