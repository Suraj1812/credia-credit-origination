namespace Credia.Domain;

public sealed record RuleOutcome(string Code, string Label, bool Passed, string Detail, bool HardStop);
public sealed record PolicyResult(decimal Installment, decimal DebtRatio, string Recommendation, IReadOnlyList<RuleOutcome> Rules);
/// <summary>Illustrative policy, not a regulatory or lending recommendation.</summary>
public static class CreditPolicy
{
    public const string Version = "COL-DEMO-2026.1";
    public const decimal AnnualEffectiveRate = 0.24m;
    public static decimal CalculateInstallment(decimal principal, int months)
    {
        if (principal <= 0 || months is < 6 or > 60) throw new ArgumentOutOfRangeException(nameof(principal));
        var monthly = Math.Pow(1 + (double)AnnualEffectiveRate, 1d / 12) - 1;
        return Math.Round(principal * (decimal)(monthly / (1 - Math.Pow(1 + monthly, -months))), 2, MidpointRounding.AwayFromZero);
    }
    public static PolicyResult Evaluate(CreditApplication app, int score, bool documentReviewed)
    {
        var installment = CalculateInstallment(app.Amount, app.TermMonths);
        var ratio = app.MonthlyIncome > 0 ? (app.ExistingDebtPayment + installment) / app.MonthlyIncome : 1;
        var rules = new List<RuleOutcome>
        {
            new("CONSENT", "Autorización de datos", app.Consent, "Autorización explícita del solicitante", true),
            new("INCOME", "Ingreso mínimo", app.MonthlyIncome >= 1_500_000, "Ingreso mensual ≥ $1.500.000", true),
            new("CAPACITY", "Capacidad de pago", ratio <= .40m, $"Endeudamiento {ratio:P1}; máximo 40%", true),
            new("DISPOSABLE", "Ingreso disponible", app.MonthlyIncome - app.MonthlyExpenses - app.ExistingDebtPayment >= installment, "Cuota cubierta después de gastos y obligaciones", true),
            new("SCORE", "Perfil de riesgo", score >= 650, $"Puntaje sintético {score}; umbral 650", false),
            new("DOCUMENT", "Soporte de ingresos", documentReviewed, "Documento extraído y confirmado por una persona", false)
        };
        var decision = rules.Any(r => !r.Passed && r.HardStop) ? "No elegible" : rules.Any(r => !r.Passed) ? "Revisión manual" : "Elegible";
        return new(installment, Math.Round(ratio, 4), decision, rules);
    }
}
