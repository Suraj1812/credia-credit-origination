# Credia

Credia is an independent credit-origination portfolio project inspired by Dufo's freelance .NET/Blazor role. It is **not** a Dufo product or a live lender. The demo uses synthetic people and an illustrative lending policy; it never makes a real credit decision, contacts a real bureau, or signs a legal document.

The application covers the path from commercial intake to review: consent, validated application forms, document upload and assisted extraction, a versioned policy evaluation with evidence hash, analyst decisions with separation of duties, a signature handoff stub, a role-scoped audit trail, and an outbox for webhook integrations. The interface offers Spanish and English, responsive layouts, accessible reduced-motion behavior, and locally bundled scroll animations.

## Stack

- .NET 10 / C# / Blazor Server, ASP.NET Core REST endpoints
- Domain, Application, Infrastructure, and Web projects
- EF Core with SQLite for local demo and SQL Server configuration for hosted environments
- Cookie authentication, Identity password hashing, antiforgery, role checks, rate limits, CSP, and security headers
- Azure Document Intelligence adapter for configured OCR; local TXT parser for the demo
- xUnit workflow and security tests

## Run locally

Install .NET SDK 10 and run:

```bash
dotnet restore Dufo.Credia.slnx
dotnet test Dufo.Credia.slnx
dotnet run --project src/Credia.Web --urls http://127.0.0.1:5080
```

Open `http://127.0.0.1:5080/login`. The Development configuration seeds synthetic cases and demo accounts. The analyst login is `analista@credia.demo` with `Credia.Demo2026!`; advisor and auditor accounts are listed on the login page. Never use these credentials or sample data outside Development.

## Production configuration

The app fails closed unless `Database:Provider=SqlServer`, `ConnectionStrings:Credia` points to a reachable SQL Server/Azure SQL database, and `DataProtection:KeyPath` points to a persistent volume writable by the app user. `ASPNETCORE_ENVIRONMENT` must be `Production`, HTTPS must be terminated at the trusted edge, and `AllowedHosts` must be set to the deployment hostname. Provide app users through a secure provisioning workflow before exposing the portal. The real bureau and e-signature integrations intentionally remain unconfigured and fail closed; do not represent the demo adapter as a production integration. Configure `DocumentIntelligence:Endpoint` and `DocumentIntelligence:ApiKey` to enable PDF/image extraction. A protected webhook may be configured through `Integrations:WebhookUrl` and `Integrations:WebhookSecret`.

The production database schema currently requires a controlled provisioning step; automated EF migrations and first-user provisioning are still needed before a real rollout. The Dockerfile packages the application, but a running container alone does not satisfy that prerequisite.

## Architecture and safeguards

`CreditWorkspace` owns workflow transitions and enforces permissions and version checks. Advisors see only their cases. Reviewers cannot decide their own case. An approval requires a current evaluation and an eligible recommendation; an analyst supplies a reason. Files are limited to 5 MB with signature checks, bounded count, and no binary payload in list views. OCR proposals require human confirmation. Audit entries and outbox events are written in the same transaction as state changes.

`docs/` contains design notes. The policy thresholds, interest rate, bureau score, and sample document are illustrative. The UI photographs are locally bundled assets; no customer data is sent to a translation service. The English interface translates product chrome while preserving stored case data as entered.
