# Cloudflare deployment readiness

The Blazor application is a stateful .NET server process. Cloudflare Workers or Pages static hosting cannot run it directly. The matching Cloudflare product is Containers, fronted by a Worker that forwards HTTP and Blazor WebSocket traffic. Cloudflare documents Containers as available on the Workers Paid plan. Do not publish a static export and present it as the working portal.

Before deployment:

1. Enable Workers Paid on the target account, verify Containers access, and connect a Docker-compatible build environment or a container image registry.
2. Provision an external SQL Server/Azure SQL database with encrypted transport, backups, and a database principal scoped to Credia. The container filesystem is not the source of truth for cases or auth keys.
3. Set the production configuration as secrets: `ConnectionStrings__Credia`, `Bootstrap__AdminEmail`, and a 16+ character `Bootstrap__AdminPassword` for the first start. Set `Database__Provider=SqlServer`, `ASPNETCORE_ENVIRONMENT=Production`, and `AllowedHosts` to the public hostname. Remove the bootstrap password after the first administrator exists.
4. Configure the Worker and container to forward the original HTTPS scheme and host; verify cookie login, antiforgery, and Blazor WebSocket reconnect through the edge.
5. Run the migration script in a staging database, then deploy the container with one instance initially. Verify `/health`, login, application creation, role separation, document upload, evaluation fail-closed behavior, audit entries, and outbox delivery.
6. Configure real bureau and signature adapters before a genuine lending rollout. The current ones deliberately fail closed or record a demonstration handoff only. Set Azure Document Intelligence secrets only when OCR is required.

The repository includes a container build and EF Core SQL Server migrations. Account setup, database provisioning, secret configuration, and live smoke testing must occur in the target Cloudflare account; there is no live deployment URL until those steps succeed.
