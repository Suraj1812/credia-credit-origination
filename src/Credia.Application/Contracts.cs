using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Credia.Domain;
namespace Credia.Application;

public sealed class ApplicationInput
{
    [Required(ErrorMessage = "Ingresa el nombre completo."), StringLength(120, MinimumLength = 3)] public string BorrowerName { get; set; } = "";
    [Required, RegularExpression(@"\d{6,12}", ErrorMessage = "La identificación debe tener entre 6 y 12 dígitos.")] public string DocumentNumber { get; set; } = "";
    [Required, EmailAddress, StringLength(160)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"[+\d ()-]{7,20}", ErrorMessage = "Ingresa un teléfono válido.")] public string Phone { get; set; } = "";
    [Required, StringLength(80)] public string City { get; set; } = "Bogotá";
    [Required] public string Employment { get; set; } = "Empleado";
    [Required] public string Product { get; set; } = "Libre inversión";
    [Required, StringLength(500, MinimumLength = 10)] public string Purpose { get; set; } = "";
    [Range(500000, 100000000)] public decimal Amount { get; set; } = 15000000;
    [Range(6, 60)] public int TermMonths { get; set; } = 24;
    [Range(1, 1000000000)] public decimal MonthlyIncome { get; set; } = 4500000;
    [Range(0, 1000000000)] public decimal MonthlyExpenses { get; set; } = 1500000;
    [Range(0, 1000000000)] public decimal ExistingDebtPayment { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Se requiere autorización del titular.")] public bool Consent { get; set; }
}
public sealed record Actor(Guid Id, string Name, string Role)
{
    public static Actor From(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id))
            throw new AppException("unauthenticated", "Inicia sesión para continuar.", 401);
        return new(id, principal.Identity.Name ?? "Usuario", principal.FindFirst(ClaimTypes.Role)?.Value ?? "");
    }
}
public sealed class AppException(string code, string message, int status = 400) : Exception(message)
{ public string Code { get; } = code; public int Status { get; } = status; }
public sealed record VersionInput(int Version);
public sealed record DecisionInput(int Version, bool Approve, string Reason);
public sealed record ExtractionField(string Name, string Value, decimal Confidence, string Source);
public sealed record ExtractionResult(string Provider, bool IsDemo, IReadOnlyList<ExtractionField> Fields, string? Limitation);
public sealed record IntegrationStatus(string Name, string Mode, string Description);
public interface IRiskBureau { Task<int> GetScoreAsync(string documentNumber, CancellationToken cancellationToken); string Name { get; } }
public interface IDocumentExtractor { Task<ExtractionResult> ExtractAsync(byte[] data, string contentType, CancellationToken cancellationToken); }
public interface ICreditWorkspace
{
    Task<IReadOnlyList<CreditApplication>> ListAsync(Actor actor, CancellationToken ct = default);
    Task<CreditApplication> GetAsync(Guid id, Actor actor, CancellationToken ct = default);
    Task<Guid> CreateAsync(ApplicationInput input, Actor actor, CancellationToken ct = default);
    Task SubmitAsync(Guid id, int version, Actor actor, CancellationToken ct = default);
    Task EvaluateAsync(Guid id, int version, Actor actor, CancellationToken ct = default);
    Task DecideAsync(Guid id, DecisionInput input, Actor actor, CancellationToken ct = default);
    Task RequestSignatureAsync(Guid id, int version, Actor actor, CancellationToken ct = default);
    Task<Guid> UploadAsync(Guid id, int version, string filename, string contentType, byte[] data, Actor actor, CancellationToken ct = default);
    Task ConfirmDocumentAsync(Guid id, Guid documentId, int version, Actor actor, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> AuditAsync(Actor actor, CancellationToken ct = default);
}
