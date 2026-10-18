using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Credia.Infrastructure;
/// <summary>Single-instance, at-least-once delivery. Consumers deduplicate by event ID.</summary>
public sealed class OutboxDispatcher(IDbContextFactory<CrediaDbContext> factory, IHttpClientFactory clients, IConfiguration config, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = config["Integrations:WebhookUrl"];
        var secret = config["Integrations:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(endpoint)) return; // Events remain pending locally.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrEmpty(secret) || secret.Length < 32)
            throw new InvalidOperationException("Webhook requires HTTPS and a secret of at least 32 characters.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try {
                await using var db = await factory.CreateDbContextAsync(stoppingToken);
                var messages = await db.Outbox.Where(x => x.DeliveredAt == null && x.Attempts < 5).OrderBy(x => x.CreatedAt).Take(10).ToListAsync(stoppingToken);
                foreach (var message in messages) {
                    var payload = Encoding.UTF8.GetBytes(message.Payload);
                    using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(payload) };
                    request.Content.Headers.ContentType = new("application/json");
                    request.Headers.Add("X-Credia-Event-Id", message.Id.ToString());
                    request.Headers.Add("X-Credia-Signature", Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload)).ToLowerInvariant());
                    message.Attempts++;
                    try {
                        using var response = await clients.CreateClient("webhook").SendAsync(request, stoppingToken);
                        if (response.IsSuccessStatusCode) { message.DeliveredAt = DateTime.UtcNow; message.LastError = null; }
                        else message.LastError = $"HTTP {(int)response.StatusCode}";
                    } catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !stoppingToken.IsCancellationRequested) { message.LastError = "Transport unavailable"; }
                    await db.SaveChangesAsync(stoppingToken);
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Outbox dispatch cycle failed; pending events retained"); }
        }
    }
}
