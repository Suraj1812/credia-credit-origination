using System.Security.Claims;
using Credia.Infrastructure;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;
namespace Credia.Web;
public sealed class SessionRevalidation(ILoggerFactory loggerFactory, IDbContextFactory<CrediaDbContext> factory) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        if (!Guid.TryParse(state.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ||
            !long.TryParse(state.User.FindFirstValue("expires"), out var expiry) || DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiry) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return user is not null && user.Role == state.User.FindFirstValue(ClaimTypes.Role) && user.SecurityStamp == state.User.FindFirstValue("security_stamp");
    }
}
