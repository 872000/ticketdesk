using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TicketDesk.Api.Data;

namespace TicketDesk.Api.Auth;

/// <summary>
/// Demo authentication for a self-contained portfolio app: the caller passes
/// X-Demo-User with a seeded user id and receives that user's roles.
/// Swap for JWT/OIDC in a real deployment; the [Authorize(Roles=...)]
/// attributes on controllers stay exactly the same.
///
/// Note: the DbContext is resolved per-request from the request scope because
/// authentication handlers are cached per scheme — constructor injection of a
/// scoped service would be a captive dependency.
/// </summary>
public sealed class DemoAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string HeaderName = "X-Demo-User";

    public DemoAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) ||
            !Guid.TryParse(values.FirstOrDefault(), out var userId))
        {
            return AuthenticateResult.NoResult();
        }

        var db = Context.RequestServices.GetRequiredService<TicketDeskContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return AuthenticateResult.Fail("Unknown demo user.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
