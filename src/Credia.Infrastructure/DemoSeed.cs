using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Credia.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Credia.Infrastructure;
public static class DemoSeed
{
    public static async Task InitializeAsync(CrediaDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        if (await db.Users.AnyAsync()) return;
        var hasher = new PasswordHasher<AppUser>();
        var users = new[] {
            new AppUser { Name = "Valentina Torres", Email = "analista@credia.demo", Role = Roles.Analyst },
            new AppUser { Name = "Daniel Rojas", Email = "asesor@credia.demo", Role = Roles.Advisor },
            new AppUser { Name = "Laura Méndez", Email = "auditor@credia.demo", Role = Roles.Auditor },
            new AppUser { Name = "Administrador", Email = "admin@credia.demo", Role = Roles.Admin }
        };
        foreach (var user in users) user.PasswordHash = hasher.HashPassword(user, "Credia.Demo2026!");
        db.Users.AddRange(users);
        string[] names = ["Mariana Rodríguez", "Andrés Martínez", "Camila Restrepo", "Santiago López", "Isabella Gómez", "Juan Sebastián Ruiz", "Valeria Hernández", "Nicolás Castro", "Daniela Moreno", "Mateo Vargas", "Luciana Pérez", "Sebastián Díaz"];
        string[] cities = ["Bogotá", "Medellín", "Cali", "Barranquilla"];
        ApplicationStatus[] statuses = [ApplicationStatus.InReview, ApplicationStatus.Submitted, ApplicationStatus.Approved, ApplicationStatus.InReview, ApplicationStatus.Draft, ApplicationStatus.SignaturePending, ApplicationStatus.Submitted, ApplicationStatus.Rejected, ApplicationStatus.Approved, ApplicationStatus.InReview, ApplicationStatus.Submitted, ApplicationStatus.Draft];
        for (var i = 0; i < names.Length; i++)
        {
            var app = new CreditApplication {
                Reference = $"CR-26-{1042 - i}", BorrowerName = names[i], DocumentNumber = $"10203040{i:00}",
                OwnerId = users[1].Id, Email = $"persona{i + 1}@example.com", Phone = "3001234567", City = cities[i % 4],
                Amount = new decimal[] { 18000000, 32000000, 12000000, 25000000, 8000000, 42000000 }[i % 6],
                Product = i % 3 == 1 ? "Capital de trabajo" : "Libre inversión", Purpose = "Financiar un proyecto personal y consolidar obligaciones.",
                MonthlyIncome = 6500000 + i * 250000, MonthlyExpenses = 1800000, ExistingDebtPayment = 250000,
                TermMonths = i % 2 == 0 ? 36 : 48, Consent = true, Status = statuses[i],
                CreatedAt = DateTime.UtcNow.AddDays(-i - 1), UpdatedAt = DateTime.UtcNow.AddHours(-i * 3), Version = 3
            };
            app.Audit.Add(new() { ApplicationId = app.Id, Actor = users[1].Name, Action = "Solicitud creada", Detail = "Expediente sintético de demostración.", CreatedAt = app.CreatedAt });
            if (app.Status != ApplicationStatus.Draft) app.Audit.Add(new() { ApplicationId = app.Id, Actor = users[1].Name, Action = "Solicitud enviada", Detail = "Autorización de tratamiento registrada.", CreatedAt = app.CreatedAt.AddHours(1) });
            if (app.Status is ApplicationStatus.InReview or ApplicationStatus.Approved or ApplicationStatus.SignaturePending or ApplicationStatus.Rejected)
            {
                var text = $"SOPORTE SINTÉTICO DE DEMOSTRACIÓN\nNombre: {app.BorrowerName}\nDocumento: {app.DocumentNumber}\nIngreso mensual: {app.MonthlyIncome:0}\n";
                var data = Encoding.UTF8.GetBytes(text);
                app.Documents.Add(new() { ApplicationId = app.Id, FileName = "certificado-ingresos-demo.txt", ContentType = "text/plain", Content = data,
                    Sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), Status = "Confirmed", ExtractionProvider = "Lector de soporte TXT",
                    ExtractionJson = JsonSerializer.Serialize(DocumentExtractor.ParseText(text, "Lector de soporte TXT", true)), CreatedAt = app.CreatedAt.AddHours(2) });
                var score = 712 + i * 7;
                var result = CreditPolicy.Evaluate(app, score, true);
                app.Evaluations.Add(new() { ApplicationId = app.Id, ApplicationVersion = 3, Score = score, DebtRatio = result.DebtRatio,
                    Installment = result.Installment, Recommendation = result.Recommendation, RulesJson = JsonSerializer.Serialize(result.Rules),
                    EvidenceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"synthetic-seed:{app.Id}"))).ToLowerInvariant(), CreatedAt = app.CreatedAt.AddHours(3) });
                app.Audit.Add(new() { ApplicationId = app.Id, Actor = users[0].Name, Action = "Política evaluada", Detail = "Evaluación sintética para explorar el portal.", CreatedAt = app.CreatedAt.AddHours(3) });
            }
            db.Applications.Add(app);
        }
        await db.SaveChangesAsync();
    }
}
