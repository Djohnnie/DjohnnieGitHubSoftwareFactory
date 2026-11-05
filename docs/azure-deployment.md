# Azure deployment

The `Build and release` workflow (`.github/workflows/release.yml`) builds, tests and publishes `Djohnnie.SoftwareFactory.Web` on every push and pull request to `main`. On every **push to `main`** (or a manual run on `main`), the `deploy` job:

1. Logs in to Azure with OpenID Connect (no Azure secret stored in GitHub).
2. Opens a temporary Azure SQL firewall rule for the runner's IP.
3. Applies EF Core migrations with a self-contained migrations bundle (`efbundle`).
4. Removes the firewall rule (always, even on failure).
5. Deploys the published web app to Azure App Service.

The deployment is skipped when migrations fail.

## Azure resources (prepare beforehand)

- A resource group containing:
  - An App Service (Web App) with the **.NET 10** runtime stack (Linux or Windows).
  - An Azure SQL logical server and database.
- A SQL login/user that can change the schema (for example `db_owner`) for migrations. The app can use the same or a less-privileged user at runtime.

## Connect GitHub to Azure

Sign in with `az login` and `gh auth login`, then run:

```powershell
./scripts/setup-azure-github.ps1 -SubscriptionId <subscription-id> -ResourceGroup <rg> `
    -WebAppName <web-app> -SqlServerName <sql-server> -SqlDatabaseName <database> -ConfigureWebApp
```

The script creates the Entra app registration `github-DjohnnieGitHubSoftwareFactory` and a federated credential for the `production` environment. It reads the subject from the repository's OIDC settings. This repository uses immutable subjects, so the subject is `repo:Djohnnie@7031043/DjohnnieGitHubSoftwareFactory@1437143137:environment:production`, not `repo:Djohnnie/DjohnnieGitHubSoftwareFactory:...`. Check it with `gh api repos/Djohnnie/DjohnnieGitHubSoftwareFactory/actions/oidc/customization/sub`. The script also grants **Website Contributor** on the Web App and **SQL Server Contributor** on the SQL server so the workflow can manage firewall rules. It also creates the `production` GitHub environment and prompts for the SQL connection string without echoing it. With `-ConfigureWebApp`, it also stores that connection string on the Web App as `ConnectionStrings:Database`. The script can be re-run safely.

It sets these on the `production` environment:

| Name | Kind | Value |
| --- | --- | --- |
| `AZURE_CLIENT_ID` | variable | App registration client ID |
| `AZURE_TENANT_ID` | variable | Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | variable | Subscription ID |
| `AZURE_RESOURCE_GROUP` | variable | Resource group of the Web App and SQL server |
| `AZURE_WEBAPP_NAME` | variable | Web App name |
| `AZURE_SQL_SERVER_NAME` | variable | SQL server name (without `.database.windows.net`) |
| `AZURE_SQL_CONNECTION_STRING` | secret | Connection string used to run migrations |

To configure this manually, create the same app registration, federated credential, role assignments and environment values in the Azure portal and in the repository's environment settings.

## Web App settings

Configure these on the Web App under **Settings > Environment variables**. Use `__` instead of `:` in app setting names.

| Setting | Notes |
| --- | --- |
| `ConnectionStrings:Database` | Connection string (type *SQLAzure*); set by `-ConfigureWebApp` |
| `GitHub__ClientId`, `GitHub__ClientSecret` | GitHub OAuth app. Set its callback URL to `https://<web-app>.azurewebsites.net/signin-github` |
| `GitHub__AllowedLogin` | Optional; defaults to `Djohnnie` |
| `OpenAI__Endpoint`, `OpenAI__Model`, `OpenAI__ApiKey` | Optional; enables DGHSF Copilot |
| `ImageUploads__ConnectionString` | Optional; storage account connection string that enables image upload in the work item editors. See [Image uploads](#image-uploads) |
| `ImageUploads__ContainerName`, `ImageUploads__PublicBaseUrl`, `ImageUploads__MaxImageSizeInMegabytes` | Optional; default to `dghsf-uploads`, the blob container URL and `10` |

Also turn on **Web sockets** (needed for Blazor Interactive Server) and **Session affinity (ARR affinity)** if you scale out to more than one instance. Authentication tickets are kept in memory, so users sign in again after a restart or redeployment.

## Image uploads

Images pasted, dropped or picked in the work item and work item template editors are uploaded to an Azure Storage blob container and linked by URL in the Markdown. GitHub (and the Copilot cloud agent) must be able to load them from the issue, so the container must allow anonymous read access to blobs:

```powershell
az storage account create -n <account> -g <resource-group> --sku Standard_LRS --allow-blob-public-access true
az storage container create -n dghsf-uploads --account-name <account> --public-access blob
az storage account show-connection-string -n <account> -g <resource-group> -o tsv
```

Use `--public-access blob`, not `container`, so the container cannot be listed. Blob names are random (`yyyy/MM/<guid>-<name>.png`) and never contain the repository name, but anyone with a link can view the image, also for private repositories. Only PNG, JPEG, GIF and WebP are accepted (detected from the content; SVG is refused). Without configuration, the editors refuse images instead of embedding them as base64.

## Releasing

Push or merge to `main`, or run **Build and release** manually from the Actions tab. Pull requests only build and test. To require approval before production deployments, add required reviewers to the `production` environment.
