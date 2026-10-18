namespace Credia.Domain;

public enum ApplicationStatus { Draft, Submitted, InReview, Approved, Rejected, SignaturePending }
public static class Roles
{
    public const string Advisor = "Advisor", Analyst = "Analyst", Admin = "Admin", Auditor = "Auditor";
    public static bool CanReview(string role) => role is Analyst or Admin;
    public static bool CanWrite(string role) => role is Advisor or Analyst or Admin;
}
public sealed class CreditApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Reference { get; set; } = "";
    public Guid OwnerId { get; set; }
    public string BorrowerName { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string City { get; set; } = "Bogotá";
    public string Employment { get; set; } = "Empleado";
    public string Product { get; set; } = "Libre inversión";
    public string Purpose { get; set; } = "";
    public decimal MonthlyIncome { get; set; }
    public decimal MonthlyExpenses { get; set; }
    public decimal ExistingDebtPayment { get; set; }
    public decimal Amount { get; set; }
    public int TermMonths { get; set; } = 24;
    public bool Consent { get; set; }
    public ApplicationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; } = 1;
    public List<Evaluation> Evaluations { get; set; } = [];
    public List<DocumentRecord> Documents { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];
}
public sealed class Evaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public int ApplicationVersion { get; set; }
    public string PolicyVersion { get; set; } = CreditPolicy.Version;
    public int Score { get; set; }
    public decimal DebtRatio { get; set; }
    public decimal Installment { get; set; }
    public string Recommendation { get; set; } = "";
    public string RulesJson { get; set; } = "[]";
    public string EvidenceHash { get; set; } = "";
    public string Provider { get; set; } = "Demo bureau · synthetic";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class DocumentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string Sha256 { get; set; } = "";
    public string ExtractionJson { get; set; } = "{}";
    public string ExtractionProvider { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = Roles.Advisor;
    public string PasswordHash { get; set; } = "";
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
}
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
