using System.Security.Claims;
using Credia.Application;
using Credia.Domain;
using Credia.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace Credia.Web;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var error = exception as AppException;
        if (error is null) logger.LogError(exception, "Unhandled request {TraceId}", context.TraceIdentifier);
        await Results.Problem(statusCode: error?.Status ?? 500, title: error?.Message ?? "Ocurrió un error. Intenta nuevamente.",
            extensions: new Dictionary<string, object?> { ["code"] = error?.Code ?? "internal", ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        return true;
    }
}
public sealed class CsrfFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Request.Method is not ("GET" or "HEAD" or "OPTIONS")) {
            try { await antiforgery.ValidateRequestAsync(context.HttpContext); }
            catch (AntiforgeryValidationException) { return Results.Problem("Actualiza la página y vuelve a intentarlo.", statusCode: 400, title: "Token de seguridad inválido"); }
        }
        return await next(context);
    }
}
public sealed class DatabaseHealthCheck(IDbContextFactory<CrediaDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
    }
}
public static class Api
{
    public static void MapCrediaEndpoints(this WebApplication app, bool demo)
    {
        app.MapPost("/auth/login", async (HttpContext context, IAntiforgery antiforgery, IDbContextFactory<CrediaDbContext> factory, IPasswordHasher<AppUser> hasher) => {
            try { await antiforgery.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.Redirect("/login?error=security"); }
            var form = await context.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim().ToLowerInvariant(); var password = form["password"].ToString();
            await using var db = await factory.CreateDbContextAsync();
            var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
            if (user is null || user.LockedUntil > DateTime.UtcNow) return Results.Redirect("/login?error=credentials");
            var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (result == PasswordVerificationResult.Failed) {
                user.FailedAttempts++; if (user.FailedAttempts >= 5) user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
                await db.SaveChangesAsync(); return Results.Redirect("/login?error=credentials");
            }
            user.FailedAttempts = 0; user.LockedUntil = null;
            if (result == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = hasher.HashPassword(user, password);
            await db.SaveChangesAsync();
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Email, user.Email), new Claim(ClaimTypes.Role, user.Role), new Claim("security_stamp", user.SecurityStamp), new Claim("expires", DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString()) };
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.LocalRedirect("/");
        }).RequireRateLimiting("login");
        app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) => {
            await antiforgery.ValidateRequestAsync(context); await context.SignOutAsync(); return Results.LocalRedirect("/login");
        }).RequireAuthorization();
        var api = app.MapGroup("/api/v1").RequireAuthorization().RequireRateLimiting("api").AddEndpointFilter<CsrfFilter>();
        api.MapGet("/csrf", (HttpContext c, IAntiforgery a) => Results.Ok(new { token = a.GetAndStoreTokens(c).RequestToken }));
        api.MapGet("/me", (ClaimsPrincipal user) => Actor.From(user));
        api.MapGet("/applications", async (ICreditWorkspace w, ClaimsPrincipal p, CancellationToken ct) => await w.ListAsync(Actor.From(p), ct));
        api.MapGet("/applications/{id:guid}", async (Guid id, ICreditWorkspace w, ClaimsPrincipal p, CancellationToken ct) => await w.GetAsync(id, Actor.From(p), ct));
        api.MapPost("/applications", async (ApplicationInput input, ICreditWorkspace w, ClaimsPrincipal p, CancellationToken ct) => {
            var id = await w.CreateAsync(input, Actor.From(p), ct); return Results.Created($"/api/v1/applications/{id}", new { id }); });
        api.MapPost("/applications/{id:guid}/submit", async (Guid id, VersionInput input, ICreditWorkspace w, ClaimsPrincipal p, CancellationToken ct) => { await w.SubmitAsync(id, input.Version, Actor.From(p), ct); return Results.NoContent(); });
        api.MapPost("/applications/{id:guid}/evaluate", async (Guid id, VersionInput input, ICreditWorkspace w, ClaimsPrincipal p, CancellationToken ct) => { await w.EvaluateAsync(id, input.Version, Actor.From(p), ct); return Results.NoContent(); });
        api.MapPost("/applications/{id:guid}/decision", async (Guid id, DecisionInput input, ICreditWorkspace w, ClaimsPrincipal p, CancellationToken ct) => { await w.DecideAsync(id, input, Actor.From(p), ct); return Results.NoContent(); });
        api.MapPost("/applications/{id:guid}/signature", async (Guid id, VersionInput input, ICreditWorkspace w, ClaimsPrincipal p, CancellationToken ct) => { await w.RequestSignatureAsync(id, input.Version, Actor.From(p), ct); return Results.NoContent(); });
        api.MapPost("/applications/{id:guid}/documents", async (Guid id, HttpContext c, ICreditWorkspace w, CancellationToken ct) => {
            var form = await c.Request.ReadFormAsync(ct); var file = form.Files.FirstOrDefault();
            if (file is null || file.Length > 5 * 1024 * 1024 || !int.TryParse(form["version"], out var version)) return Results.BadRequest(new { error = "Archivo o versión inválidos." });
            using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct);
            var docId = await w.UploadAsync(id, version, file.FileName, file.ContentType, buffer.ToArray(), Actor.From(c.User), ct);
            return Results.Created($"/api/v1/applications/{id}/documents/{docId}", new { id = docId });
        });
        api.MapPost("/applications/{id:guid}/documents/{documentId:guid}/confirm", async (Guid id, Guid documentId, VersionInput input, ICreditWorkspace w, ClaimsPrincipal p, CancellationToken ct) => { await w.ConfirmDocumentAsync(id, documentId, input.Version, Actor.From(p), ct); return Results.NoContent(); });
        api.MapGet("/applications/{id:guid}/documents/{documentId:guid}", async (Guid id, Guid documentId, ICreditWorkspace w, ClaimsPrincipal p, IDbContextFactory<CrediaDbContext> factory, CancellationToken ct) => {
            await w.GetAsync(id, Actor.From(p), ct);
            await using var db = await factory.CreateDbContextAsync(ct);
            var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == documentId && x.ApplicationId == id, ct);
            return doc is null ? Results.NotFound() : Results.File(doc.Content, "application/octet-stream", doc.FileName);
        });
        api.MapGet("/audit", async (ICreditWorkspace w, ClaimsPrincipal p, CancellationToken ct) => await w.AuditAsync(Actor.From(p), ct));
        api.MapGet("/outbox", async (ClaimsPrincipal p, IDbContextFactory<CrediaDbContext> factory, CancellationToken ct) => {
            if (Actor.From(p).Role is not (Roles.Admin or Roles.Auditor)) return Results.Forbid();
            await using var db = await factory.CreateDbContextAsync(ct);
            return Results.Ok(await db.Outbox.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct));
        });
        api.MapGet("/export", async (ICreditWorkspace w, ClaimsPrincipal p, CancellationToken ct) => {
            var records = await w.ListAsync(Actor.From(p), ct);
            // Fixed reference/status fields only: no borrower PII or spreadsheet formula injection.
            var csv = "Referencia,Estado,Monto,Plazo\n" + string.Join("\n", records.Select(x => $"{x.Reference},{x.Status},{x.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},{x.TermMonths}"));
            return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "credia-solicitudes.csv");
        });
    }
}
