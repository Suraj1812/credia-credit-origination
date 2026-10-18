using System.Security.Claims;
using System.Threading.RateLimiting;
using Credia.Application;
using Credia.Domain;
using Credia.Infrastructure;
using Credia.Web;
using Credia.Web.Components;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var demo = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Demo:Enabled");
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
if (!builder.Environment.IsDevelopment() && provider != "SqlServer") throw new InvalidOperationException("Production requires a configured SQL Server database.");
builder.Services.AddDbContextFactory<CrediaDbContext>(options => {
    if (provider == "SqlServer") options.UseSqlServer(builder.Configuration.GetConnectionString("Credia") ?? throw new InvalidOperationException("Missing SQL connection string."));
    else options.UseSqlite(builder.Configuration.GetConnectionString("Credia") ?? "Data Source=credia.db");
});
builder.Services.AddScoped<ICreditWorkspace, CreditWorkspace>();
if (demo) builder.Services.AddSingleton<IRiskBureau, DemoRiskBureau>();
else builder.Services.AddSingleton<IRiskBureau, UnconfiguredRiskBureau>();
builder.Services.AddHttpClient<IDocumentExtractor, DocumentExtractor>(c => { c.Timeout = TimeSpan.FromSeconds(45); c.MaxResponseContentBufferSize = 8 * 1024 * 1024; })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    options.LoginPath = "/login"; options.AccessDeniedPath = "/forbidden";
    options.Cookie.Name = "Credia.Session"; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(2); options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = c => { if (c.Request.Path.StartsWithSegments("/api")) c.Response.StatusCode = 401; else c.Response.Redirect(c.RedirectUri); return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, SessionRevalidation>();
builder.Services.AddHttpClient("webhook", c => c.Timeout = TimeSpan.FromSeconds(10)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 6 * 1024 * 1024);
builder.Services.AddRazorComponents().AddInteractiveServerComponents(options => { options.DetailedErrors = builder.Environment.IsDevelopment(); });
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
if (!builder.Environment.IsDevelopment()) {
    var keyPath = builder.Configuration["DataProtection:KeyPath"] ?? throw new InvalidOperationException("Configure persistent DataProtection:KeyPath.");
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath)).SetApplicationName("Credia");
}
var app = builder.Build();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api") || !Path.HasExtension(context.Request.Path)) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.UseAntiforgery();
app.MapStaticAssets();
app.MapCrediaEndpoints(demo);
app.MapHealthChecks("/health");
app.MapOpenApi().RequireAuthorization();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
if (demo) { await using var scope = app.Services.CreateAsyncScope(); var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CrediaDbContext>>(); await using var db = await factory.CreateDbContextAsync(); await DemoSeed.InitializeAsync(db); }
app.Run();
public partial class Program { }
