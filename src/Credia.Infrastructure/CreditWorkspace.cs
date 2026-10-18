using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Credia.Application;
using Credia.Domain;
using Microsoft.EntityFrameworkCore;
namespace Credia.Infrastructure;

public sealed class CreditWorkspace(IDbContextFactory<CrediaDbContext> factory, IRiskBureau bureau, IDocumentExtractor extractor) : ICreditWorkspace
{
    public async Task<IReadOnlyList<CreditApplication>> ListAsync(Actor actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Visible(db, actor).AsNoTracking();
        return await query.Include(x => x.Evaluations).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }
    public async Task<CreditApplication> GetAsync(Guid id, Actor actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var app = await Visible(db, actor).AsNoTracking().Include(x => x.Evaluations).Include(x => x.Audit)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        // Never load binary document payloads into the UI/application list.
        app.Documents = await db.Documents.Where(x => x.ApplicationId == id).Select(x => new DocumentRecord {
            Id = x.Id, ApplicationId = x.ApplicationId, FileName = x.FileName, ContentType = x.ContentType,
            Sha256 = x.Sha256, ExtractionJson = x.ExtractionJson, ExtractionProvider = x.ExtractionProvider,
            Status = x.Status, CreatedAt = x.CreatedAt }).ToListAsync(ct);
        return app;
    }
    public async Task<Guid> CreateAsync(ApplicationInput input, Actor actor, CancellationToken ct = default)
    {
        WriteAccess(actor);
        var validation = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new(input), validation, true))
            throw new AppException("validation", string.Join(" ", validation.Select(x => x.ErrorMessage)));
        if (!new[] { "Libre inversión", "Capital de trabajo", "Compra de cartera" }.Contains(input.Product) ||
            !new[] { "Empleado", "Independiente", "Pensionado" }.Contains(input.Employment))
            throw new AppException("validation", "Producto u ocupación no válido.");
        var app = new CreditApplication {
            Reference = $"CR-{DateTime.UtcNow:yy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            OwnerId = actor.Id, BorrowerName = input.BorrowerName.Trim(), DocumentNumber = input.DocumentNumber,
            Email = input.Email.Trim(), Phone = input.Phone, City = input.City.Trim(), Employment = input.Employment,
            Product = input.Product, Purpose = input.Purpose.Trim(), Amount = input.Amount, TermMonths = input.TermMonths,
            MonthlyIncome = input.MonthlyIncome, MonthlyExpenses = input.MonthlyExpenses,
            ExistingDebtPayment = input.ExistingDebtPayment, Consent = input.Consent
        };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Applications.Add(app);
        AddAudit(db, app, actor, "Solicitud creada", "Registro inicial; autorización de tratamiento de datos confirmada.");
        await Save(db, ct);
        return app.Id;
    }
    public async Task SubmitAsync(Guid id, int version, Actor actor, CancellationToken ct = default)
    {
        WriteAccess(actor);
        await using var db = await factory.CreateDbContextAsync(ct);
        var app = await Load(db, id, version, actor, ct);
        Require(app.Status == ApplicationStatus.Draft, "Solo se pueden enviar borradores.");
        Require(app.Consent, "Se requiere autorización del titular.");
        app.Status = ApplicationStatus.Submitted;
        Change(db, app, actor, "Solicitud enviada", "Disponible en la bandeja de análisis.");
        await Save(db, ct);
    }
    public async Task EvaluateAsync(Guid id, int version, Actor actor, CancellationToken ct = default)
    {
        ReviewAccess(actor);
        await using var db = await factory.CreateDbContextAsync(ct);
        var app = await Load(db, id, version, actor, ct);
        Require(app.Status is ApplicationStatus.Submitted or ApplicationStatus.InReview, "La solicitud no está disponible para evaluación.");
        var score = await bureau.GetScoreAsync(app.DocumentNumber, ct);
        var confirmed = await db.Documents.AnyAsync(x => x.ApplicationId == id && x.Status == "Confirmed", ct);
        var result = CreditPolicy.Evaluate(app, score, confirmed);
        var evidence = JsonSerializer.Serialize(new { app.Id, app.Version, app.Amount, app.TermMonths, app.MonthlyIncome,
            app.MonthlyExpenses, app.ExistingDebtPayment, app.Consent, score, confirmed, policy = CreditPolicy.Version, provider = bureau.Name });
        db.Set<Evaluation>().Add(new Evaluation { ApplicationId = id, ApplicationVersion = version + 1,
            Score = score, DebtRatio = result.DebtRatio, Installment = result.Installment,
            Recommendation = result.Recommendation, RulesJson = JsonSerializer.Serialize(result.Rules), Provider = bureau.Name,
            EvidenceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))).ToLowerInvariant() });
        app.Status = ApplicationStatus.InReview;
        Change(db, app, actor, "Política evaluada", $"{CreditPolicy.Version} · {result.Recommendation} · {bureau.Name}");
        await Save(db, ct);
    }
    public async Task DecideAsync(Guid id, DecisionInput input, Actor actor, CancellationToken ct = default)
    {
        ReviewAccess(actor);
        Require(input.Reason?.Trim().Length is >= 15 and <= 1000, "Registra una justificación de 15 a 1.000 caracteres.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var app = await Load(db, id, input.Version, actor, ct);
        Require(app.OwnerId != actor.Id, "Control de doble intervención: no puedes decidir tu propia solicitud.");
        Require(app.Status == ApplicationStatus.InReview, "La solicitud debe estar en análisis.");
        var evaluation = app.Evaluations.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        Require(evaluation is not null && evaluation.ApplicationVersion == app.Version, "Evalúa nuevamente: la evidencia cambió desde la última evaluación.");
        if (input.Approve) Require(evaluation!.Recommendation == "Elegible", "Resuelve las reglas pendientes antes de aprobar.");
        app.Status = input.Approve ? ApplicationStatus.Approved : ApplicationStatus.Rejected;
        Change(db, app, actor, input.Approve ? "Crédito aprobado" : "Crédito rechazado", input.Reason!.Trim());
        await Save(db, ct);
    }
    public async Task RequestSignatureAsync(Guid id, int version, Actor actor, CancellationToken ct = default)
    {
        ReviewAccess(actor);
        await using var db = await factory.CreateDbContextAsync(ct);
        var app = await Load(db, id, version, actor, ct);
        Require(app.Status == ApplicationStatus.Approved, "Solo los créditos aprobados pueden pasar a firma.");
        app.Status = ApplicationStatus.SignaturePending;
        Change(db, app, actor, "Paquete de firma preparado", "Demostración: pagaré pendiente. No se ha realizado una firma legal ni enviado al cliente.");
        await Save(db, ct);
    }
    public async Task<Guid> UploadAsync(Guid id, int version, string filename, string contentType, byte[] data, Actor actor, CancellationToken ct = default)
    {
        WriteAccess(actor);
        ValidateFile(contentType, data);
        // Authorize and check state before invoking any external extractor.
        await using var db = await factory.CreateDbContextAsync(ct);
        var app = await Load(db, id, version, actor, ct);
        Require(app.Status is ApplicationStatus.Draft or ApplicationStatus.Submitted or ApplicationStatus.InReview, "El expediente está cerrado para documentos.");
        if (await db.Documents.CountAsync(x => x.ApplicationId == id, ct) >= 10) throw new AppException("limit", "Máximo 10 documentos por solicitud.");
        var extraction = await extractor.ExtractAsync(data, contentType, ct);
        var document = new DocumentRecord { ApplicationId = id, FileName = Path.GetFileName(filename)[..Math.Min(Path.GetFileName(filename).Length, 180)],
            ContentType = contentType, Content = data, Sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
            ExtractionJson = JsonSerializer.Serialize(extraction), ExtractionProvider = extraction.Provider,
            Status = extraction.Fields.Count > 0 ? "Extracted" : "NeedsReview" };
        db.Documents.Add(document);
        Change(db, app, actor, "Documento recibido", $"{document.FileName} · SHA-256 {document.Sha256} · {extraction.Provider}");
        await Save(db, ct);
        return document.Id;
    }
    public async Task ConfirmDocumentAsync(Guid id, Guid documentId, int version, Actor actor, CancellationToken ct = default)
    {
        WriteAccess(actor);
        await using var db = await factory.CreateDbContextAsync(ct);
        var app = await Load(db, id, version, actor, ct);
        Require(app.Status is ApplicationStatus.Draft or ApplicationStatus.Submitted or ApplicationStatus.InReview, "El expediente está cerrado.");
        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == documentId && x.ApplicationId == id, ct) ?? throw Missing();
        Require(doc.Status == "Extracted", "El documento no tiene una extracción disponible para confirmar.");
        var extraction = JsonSerializer.Deserialize<ExtractionResult>(doc.ExtractionJson)!;
        var income = extraction.Fields.FirstOrDefault(x => x.Name == "monthlyIncome");
        var documentNumber = extraction.Fields.FirstOrDefault(x => x.Name == "documentNumber");
        Require(documentNumber?.Value == app.DocumentNumber, "La identificación del documento no coincide con el solicitante.");
        Require(decimal.TryParse(income?.Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount)
            && amount > 0 && amount <= 1_000_000_000, "No se encontró un ingreso válido; adjunta un soporte completo.");
        app.MonthlyIncome = amount;
        doc.Status = "Confirmed";
        Change(db, app, actor, "Extracción confirmada", "Identificación verificada e ingreso actualizado por revisión humana. Requiere nueva evaluación.");
        await Save(db, ct);
    }
    public async Task<IReadOnlyList<AuditEntry>> AuditAsync(Actor actor, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var ids = Visible(db, actor).Select(x => x.Id);
        return await db.AuditEntries.AsNoTracking().Where(x => ids.Contains(x.ApplicationId)).OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct);
    }
    static IQueryable<CreditApplication> Visible(CrediaDbContext db, Actor actor)
    {
        if (actor.Role is not (Roles.Advisor or Roles.Analyst or Roles.Admin or Roles.Auditor)) throw new AppException("forbidden", "No tienes permisos.", 403);
        return actor.Role == Roles.Advisor ? db.Applications.Where(x => x.OwnerId == actor.Id) : db.Applications;
    }
    static async Task<CreditApplication> Load(CrediaDbContext db, Guid id, int version, Actor actor, CancellationToken ct)
    {
        var app = await Visible(db, actor).Include(x => x.Evaluations).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (app.Version != version) throw new AppException("conflict", "Otro usuario actualizó la solicitud. Recarga antes de continuar.", 409);
        return app;
    }
    static void Change(CrediaDbContext db, CreditApplication app, Actor actor, string action, string detail)
    {
        app.Version++; app.UpdatedAt = DateTime.UtcNow;
        AddAudit(db, app, actor, action, detail);
        db.Outbox.Add(new OutboxMessage { ApplicationId = app.Id, Type = "credit.application.changed", Payload = JsonSerializer.Serialize(new {
            applicationId = app.Id, app.Reference, status = app.Status.ToString(), app.Version, action }) });
    }
    static void AddAudit(CrediaDbContext db, CreditApplication app, Actor actor, string action, string detail) => db.AuditEntries.Add(new() { ApplicationId = app.Id, Actor = actor.Name, Action = action, Detail = detail });
    static async Task Save(CrediaDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AppException("conflict", "La solicitud cambió. Recarga y vuelve a intentarlo.", 409); }
    }
    static AppException Missing() => new("not_found", "No se encontró la solicitud o no tienes acceso.", 404);
    static void Require(bool valid, string message) { if (!valid) throw new AppException("workflow", message, 422); }
    static void WriteAccess(Actor actor) { if (!Roles.CanWrite(actor.Role)) throw new AppException("forbidden", "Tu rol es de solo lectura.", 403); }
    static void ReviewAccess(Actor actor) { if (!Roles.CanReview(actor.Role)) throw new AppException("forbidden", "Se requiere el rol de analista.", 403); }
    public static void ValidateFile(string contentType, byte[] data)
    {
        if (data.Length is 0 or > 5 * 1024 * 1024) throw new AppException("file_size", "Selecciona un archivo de hasta 5 MB.");
        var valid = contentType switch {
            "application/pdf" => data.AsSpan().StartsWith("%PDF-"u8),
            "image/png" => data.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}),
            "image/jpeg" => data.AsSpan().StartsWith(new byte[] {255,216,255}),
            "text/plain" => !data.Contains((byte)0), _ => false };
        if (!valid) throw new AppException("file_type", "Formato no válido. Usa PDF, PNG, JPG o un soporte TXT UTF-8.");
    }
}
