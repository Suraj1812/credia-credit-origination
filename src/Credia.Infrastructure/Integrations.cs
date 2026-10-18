using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Credia.Application;
using Microsoft.Extensions.Configuration;
namespace Credia.Infrastructure;

public sealed class DemoRiskBureau : IRiskBureau
{
    public string Name => "Central simulada · datos sintéticos";
    public Task<int> GetScoreAsync(string documentNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(660 + SHA256.HashData(Encoding.UTF8.GetBytes(documentNumber))[0] % 180);
    }
}
public sealed class DocumentExtractor(HttpClient client, IConfiguration config) : IDocumentExtractor
{
    public async Task<ExtractionResult> ExtractAsync(byte[] data, string contentType, CancellationToken cancellationToken)
    {
        if (contentType == "text/plain") return ParseText(new UTF8Encoding(false, true).GetString(data), "Lector de soporte TXT", true);
        var endpoint = config["DocumentIntelligence:Endpoint"];
        var key = config["DocumentIntelligence:ApiKey"];
        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key))
            return new("OCR sin configurar", false, [], "Configura Azure Document Intelligence para leer imágenes y PDF. El documento se conserva sin inventar resultados.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) || baseUri.Scheme != "https")
            throw new AppException("ocr_config", "La configuración del proveedor OCR no es válida.", 503);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/documentintelligence/documentModels/prebuilt-read:analyze?api-version=2024-11-30");
            request.Headers.Add("Ocp-Apim-Subscription-Key", key);
            request.Content = new ByteArrayContent(data);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            using var response = await client.SendAsync(request, deadline.Token);
            if (!response.IsSuccessStatusCode || !response.Headers.TryGetValues("Operation-Location", out var locations))
                throw new AppException("ocr_unavailable", "El proveedor OCR no aceptó el documento. Intenta nuevamente.", 502);
            if (!Uri.TryCreate(locations.First(), UriKind.Absolute, out var resultUri) || resultUri.Scheme != "https" || resultUri.Authority != baseUri.Authority)
                throw new AppException("ocr_response", "El proveedor devolvió una ubicación no válida.", 502);
            for (var i = 0; i < 15; i++)
            {
                await Task.Delay(1500, deadline.Token);
                using var poll = new HttpRequestMessage(HttpMethod.Get, resultUri);
                poll.Headers.Add("Ocp-Apim-Subscription-Key", key);
                using var result = await client.SendAsync(poll, deadline.Token);
                result.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync(deadline.Token));
                var state = json.RootElement.GetProperty("status").GetString();
                if (state == "succeeded") return ParseText(json.RootElement.GetProperty("analyzeResult").GetProperty("content").GetString() ?? "", "Azure Document Intelligence · prebuilt-read", false);
                if (state == "failed") throw new AppException("ocr_failed", "No fue posible leer el documento. Revisa su calidad.", 502);
            }
            throw new AppException("ocr_timeout", "El documento tardó demasiado en procesarse. Intenta nuevamente.", 504);
        }
        catch (HttpRequestException) { throw new AppException("ocr_unavailable", "No se pudo conectar con el proveedor OCR.", 502); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AppException("ocr_timeout", "Se agotó el tiempo de lectura del documento.", 504); }
    }
    public static ExtractionResult ParseText(string text, string provider, bool demo)
    {
        var fields = new List<ExtractionField>();
        foreach (var (name, pattern) in new[] {
            ("borrowerName", @"(?:Nombre|Name)\s*:\s*([^\r\n]+)"),
            ("documentNumber", @"(?:Documento|Cédula|Cedula)\s*:\s*(\d{6,12})"),
            ("monthlyIncome", @"(?:Ingreso mensual|Salario|Monthly income)\s*:\s*\$?\s*([\d.,]+)") })
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (!match.Success) continue;
            var value = match.Groups[1].Value.Trim();
            if (name == "monthlyIncome") value = value.Replace(".", "").Replace(",", ".");
            fields.Add(new(name, value, .90m, "Campo etiquetado en el documento; confianza heurística, requiere revisión"));
        }
        return new(provider, demo, fields, "Extracción de campos etiquetados; no verifica autenticidad ni constituye una aprobación.");
    }
}

public sealed class UnconfiguredRiskBureau : IRiskBureau { public string Name => "Proveedor sin configurar"; public Task<int> GetScoreAsync(string documentNumber, CancellationToken ct) => throw new AppException("bureau_unconfigured", "Se requiere un proveedor de riesgo real para evaluar fuera del entorno demo.", 503); }
