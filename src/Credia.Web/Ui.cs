using System.Globalization;
using Credia.Domain;
namespace Credia.Web;
public static class Ui
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-CO");
    public static string Money(decimal value) => value.ToString("C0", Culture);
    public static string Compact(decimal value) => value >= 1_000_000 ? $"${value / 1_000_000:0.#} M" : Money(value);
    public static string Initials(string value) => string.Concat(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[0]));
    public static string Status(ApplicationStatus value) => value switch {
        ApplicationStatus.Draft => "Borrador", ApplicationStatus.Submitted => "Recibida", ApplicationStatus.InReview => "En análisis",
        ApplicationStatus.Approved => "Aprobada", ApplicationStatus.Rejected => "Rechazada", ApplicationStatus.SignaturePending => "En firma", _ => value.ToString() };
    public static string Role(string role) => role switch { Roles.Analyst => "Analista de crédito", Roles.Advisor => "Asesor comercial", Roles.Admin => "Administrador", _ => "Auditor" };
    public static DateTime Colombia(DateTime value) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("America/Bogota"));
}
