using Credia.Application;
using Credia.Domain;
using Credia.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Credia.Tests;

public sealed class CreditWorkflowTests
{
    [Fact]
    public void Installment_and_policy_are_deterministic()
    {
        var app = Application();
        var result = CreditPolicy.Evaluate(app, 720, true);
        Assert.True(result.Installment > 0);
        Assert.Equal("Elegible", result.Recommendation);
        Assert.All(result.Rules, r => Assert.True(r.Passed));
        Assert.Equal(CreditPolicy.CalculateInstallment(app.Amount, app.TermMonths), result.Installment);
    }

    [Fact]
    public void Blocking_rule_prevents_eligibility()
    {
        var app = Application(); app.MonthlyIncome = 1_000_000;
        var result = CreditPolicy.Evaluate(app, 800, true);
        Assert.Equal("No elegible", result.Recommendation);
        Assert.Contains(result.Rules, r => r.Code == "INCOME" && !r.Passed && r.HardStop);
    }

    [Fact]
    public async Task Approval_requires_current_evidence_and_a_second_person()
    {
        await using var fixture = await Fixture.Create();
        var owner = new Actor(Guid.NewGuid(), "Asesora", Roles.Advisor);
        var analyst = new Actor(Guid.NewGuid(), "Analista", Roles.Analyst);
        var id = await fixture.Workspace.CreateAsync(Input(), owner);
        var draft = await fixture.Workspace.GetAsync(id, owner);
        await Assert.ThrowsAsync<AppException>(() => fixture.Workspace.DecideAsync(id, new(draft.Version, true, "Aprobado con todo el soporte"), analyst));
        await fixture.Workspace.SubmitAsync(id, draft.Version, owner);
        var submitted = await fixture.Workspace.GetAsync(id, analyst);
        await fixture.Workspace.EvaluateAsync(id, submitted.Version, analyst);
        var reviewed = await fixture.Workspace.GetAsync(id, analyst);
        Assert.Equal("Revisión manual", reviewed.Evaluations.Single().Recommendation);
        var rejected = await Assert.ThrowsAsync<AppException>(() => fixture.Workspace.DecideAsync(id, new(reviewed.Version, true, "Aprobado sin soporte documental"), analyst));
        Assert.Equal("workflow", rejected.Code);
        var sample = System.Text.Encoding.UTF8.GetBytes("Nombre: Persona Demo\nDocumento: 1234567890\nIngreso mensual: 6500000\n");
        var docId = await fixture.Workspace.UploadAsync(id, reviewed.Version, "soporte.txt", "text/plain", sample, owner);
        var afterUpload = await fixture.Workspace.GetAsync(id, analyst);
        await fixture.Workspace.ConfirmDocumentAsync(id, docId, afterUpload.Version, analyst);
        var afterConfirm = await fixture.Workspace.GetAsync(id, analyst);
        var stale = await Assert.ThrowsAsync<AppException>(() => fixture.Workspace.DecideAsync(id, new(reviewed.Version, true, "Aprobado con soporte confirmado"), analyst));
        Assert.Equal("conflict", stale.Code);
        await fixture.Workspace.EvaluateAsync(id, afterConfirm.Version, analyst);
        var fresh = await fixture.Workspace.GetAsync(id, analyst);
        Assert.Equal("Elegible", fresh.Evaluations.OrderByDescending(x => x.CreatedAt).First().Recommendation);
        await fixture.Workspace.DecideAsync(id, new(fresh.Version, true, "Ingresos y políticas verificados"), analyst);
        var approved = await fixture.Workspace.GetAsync(id, analyst);
        Assert.Equal(ApplicationStatus.Approved, approved.Status);
        Assert.Contains(approved.Audit, x => x.Action == "Crédito aprobado" && x.Actor == analyst.Name);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        Assert.Contains(await db.Outbox.ToListAsync(), x => x.ApplicationId == id && x.Type == "credit.application.changed");
    }

    [Fact]
    public async Task Advisor_cannot_read_another_advisors_case()
    {
        await using var fixture = await Fixture.Create();
        var owner = new Actor(Guid.NewGuid(), "Asesora", Roles.Advisor);
        var other = new Actor(Guid.NewGuid(), "Otro", Roles.Advisor);
        var id = await fixture.Workspace.CreateAsync(Input(), owner);
        var error = await Assert.ThrowsAsync<AppException>(() => fixture.Workspace.GetAsync(id, other));
        Assert.Equal(404, error.Status);
        Assert.Empty(await fixture.Workspace.ListAsync(other));
    }

    [Theory]
    [InlineData("application/pdf", "hello")]
    [InlineData("image/png", "not a png")]
    [InlineData("text/html", "<script>bad</script>")]
    public void Upload_rejects_type_spoofing(string type, string content)
        => Assert.Throws<AppException>(() => CreditWorkspace.ValidateFile(type, System.Text.Encoding.UTF8.GetBytes(content)));

    static CreditApplication Application() => new() { Amount = 15_000_000, TermMonths = 36, MonthlyIncome = 6_500_000, MonthlyExpenses = 1_500_000, ExistingDebtPayment = 200_000, Consent = true };
    static ApplicationInput Input() => new() { BorrowerName = "Persona Demo", DocumentNumber = "1234567890", Email = "demo@example.com", Phone = "3001234567", City = "Bogotá", Purpose = "Financiar un proyecto personal", Amount = 15_000_000, TermMonths = 36, MonthlyIncome = 6_500_000, MonthlyExpenses = 1_500_000, ExistingDebtPayment = 200_000, Consent = true };
    sealed class Fixture(SqliteConnection connection, IDbContextFactory<CrediaDbContext> factory, CreditWorkspace workspace) : IAsyncDisposable
    {
        public IDbContextFactory<CrediaDbContext> Factory => factory;
        public CreditWorkspace Workspace => workspace;
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<CrediaDbContext>().UseSqlite(connection).Options;
            var factory = new TestFactory(options);
            await using var db = factory.CreateDbContext(); await db.Database.EnsureCreatedAsync();
            return new(connection, factory, new(factory, new DemoRiskBureau(), new StubExtractor()));
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
        sealed class TestFactory(DbContextOptions<CrediaDbContext> options) : IDbContextFactory<CrediaDbContext>
        { public CrediaDbContext CreateDbContext() => new(options); }
        sealed class StubExtractor : IDocumentExtractor
        { public Task<ExtractionResult> ExtractAsync(byte[] data, string contentType, CancellationToken ct) => Task.FromResult(DocumentExtractor.ParseText(System.Text.Encoding.UTF8.GetString(data), "Test reader", true)); }
    }
}
