# Sales Reporting deployment

The production workflow deploys `main` to Azure Container Apps through GHCR.
Do not merge this deployment setup into `main` until the application source and
the prerequisites below are present there.

## Reuse the existing MEDZO Azure architecture

- Resource group: `rg-MEDZO-NEW`
- Container Apps environment: `medzo-env-01`
- SQL server: `medzo-new-sql`
- SQL database: `medzo-sales-db`
- Container app: `medzo-sales-reporting-service`
- Container registry: `ghcr.io`

Create only the Sales Reporting container app. Reuse the existing resource
group, Container Apps environment, SQL server, and logging workspace.

For the Consumption plan, use 0.5 CPU, 1 GiB memory, minimum replicas 0,
maximum replicas 1, and target port 8080. Scaling to zero avoids container
compute charges while the service is idle.

## Application prerequisites

Before promoting the deployment files to `main`, verify that the application:

- builds and tests with .NET 10;
- supports `Database__Provider=SqlServer`;
- reads `ConnectionStrings__Sales`;
- applies a controlled EF Core migration for Azure SQL;
- exposes `/health`;
- listens on port 8080;
- reads the Catalogue service URL from configuration;
- validates Auth service JWTs using environment-provided settings; and
- can start with `Kafka__Enabled=false` until Kafka infrastructure is approved.

## GitHub Actions configuration

Create these repository Actions variables:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

The Azure identity must have a GitHub OIDC federated credential for:

```text
repo:MEDZO-pharmacy/medzo-sales-reporting-service:ref:refs/heads/main
```

Scope its Azure role to the Sales container app, or to `rg-MEDZO-NEW` only if
the deployment action requires resource-group-level access. Do not store an
Azure client secret in GitHub.

If the GHCR package is private, configure the container app with a GitHub token
that has only `read:packages`. The workflow's `GITHUB_TOKEN` publishes the image
but is not automatically available to Azure when Azure pulls it.

## Container App secrets and environment variables

Store the SQL connection string and JWT signing secret as Container App
secrets. Reference them from environment variables; never commit them.

```text
ASPNETCORE_ENVIRONMENT=Production
Database__Provider=SqlServer
ConnectionStrings__Sales=secretref:sales-db-connection
Services__CatalogueInventory__BaseUrl=<catalogue-service-url>
Jwt__Secret=secretref:jwt-signing-secret
Jwt__Issuer=MedzoAuthService
Jwt__Audience=MedzoClient
Kafka__Enabled=false
Cors__AllowedOrigins__0=https://<your-frontend>.azurestaticapps.net
```

Configure HTTP health probes on port 8080 using `/health` for startup,
liveness, and readiness. The application currently exposes that single health
endpoint.

## First deployment order

1. Configure an Azure budget and cost alerts.
2. Verify the exact `medzo-sales-db` name and its low-cost compute tier.
3. Verify the application locally against Azure SQL using a developer IP
   firewall rule.
4. Create `medzo-sales-reporting-service` once in `medzo-env-01` with a
   temporary public image and scale-to-zero settings.
5. Add Container App secrets, environment variables, ingress, and probes.
6. Configure GHCR pull access and GitHub-to-Azure OIDC.
7. Merge the tested application and deployment setup into `main`.
8. Verify the SHA-tagged GHCR image, Azure revision, `/health`, and a database
   read/write operation.
9. Review Azure Cost Analysis after the first deployment.

Do not create Event Hubs/Kafka, Azure Managed Grafana, another Container Apps
environment, another SQL server, ACR, a private endpoint, or a NAT gateway until
the requirement and expected cost are approved.
