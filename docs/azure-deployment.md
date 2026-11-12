# Azure deployment

The `Build and release` workflow (`.github/workflows/release.yml`) builds, tests and publishes `Djohnnie.SoftwareFactory.Web` and `Djohnnie.SoftwareFactory.Sync` on every push and pull request to `main`. On every **push to `main`** (or a manual run on `main`), the `deploy` job:

1. Logs in to Azure with OpenID Connect (no Azure secret stored in GitHub).
2. Opens a temporary Azure SQL firewall rule for the runner's IP.
3. Applies EF Core migrations with a self-contained migrations bundle (`efbundle`).
4. Removes the firewall rule (always, even on failure).
5. Deploys the published web app to Azure App Service.
6. Deploys the sync function app to the Function App `softwarefactory-sync`.

The deployment is skipped when migrations fail.

## Azure resources (prepare beforehand)

- A resource group containing:
  - An App Service (Web App) with the **.NET 10** runtime stack (Linux or Windows).
  - A Function App named `softwarefactory-sync` on a **Flex Consumption** plan with the **.NET 10 isolated** runtime. Another name or plan needs a matching change to `SYNC_FUNCTION_APP` and the `sku` of the deploy step in the workflow.
  - An Azure SQL logical server and database.
- A SQL login/user that can change the schema (for example `db_owner`) for migrations. The app can use the same or a less-privileged user at runtime.

## Connect GitHub to Azure

Sign in with `az login` and `gh auth login`, then run:

```powershell
./scripts/setup-azure-github.ps1 -SubscriptionId <subscription-id> -ResourceGroup <rg> `
    -WebAppName <web-app> -SqlServerName <sql-server> -SqlDatabaseName <database> -ConfigureWebApp
```

The script creates the Entra app registration `github-DjohnnieGitHubSoftwareFactory` and a federated credential for the `production` environment. It reads the subject from the repository's OIDC settings. This repository uses immutable subjects, so the subject is `repo:Djohnnie@7031043/DjohnnieGitHubSoftwareFactory@1437143137:environment:production`, not `repo:Djohnnie/DjohnnieGitHubSoftwareFactory:...`. Check it with `gh api repos/Djohnnie/DjohnnieGitHubSoftwareFactory/actions/oidc/customization/sub`. The script also grants **Website Contributor** on the Web App and on the Function App (`-FunctionAppName`, default `softwarefactory-sync`) and **SQL Server Contributor** on the SQL server so the workflow can manage firewall rules. It also creates the `production` GitHub environment and prompts for the SQL connection string without echoing it. With `-ConfigureWebApp`, it also stores that connection string on the Web App as `ConnectionStrings:Database`. The script can be re-run safely.

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
| `Speech__Region`, `Speech__ApiKey` | Optional; Azure AI Speech resource that enables dictating titles and descriptions. See [Dictation](#dictation) |
| `Speech__Language`, `Speech__Endpoint` | Optional; locale(s) to recognize (default `en-US`, or up to four comma-separated, e.g. `en-US,nl-BE`, to detect the spoken one) and a custom-domain endpoint to issue tokens from |

Also turn on **Web sockets** (needed for Blazor Interactive Server) and **Session affinity (ARR affinity)** if you scale out to more than one instance. Authentication tickets are kept in memory, so users sign in again after a restart or redeployment.

## Sync function app

`Djohnnie.SoftwareFactory.Sync` is a timer-triggered Azure Function that refreshes every accepted work item of every repository in the database from its GitHub issue: its title, description and status and, for user stories, bugs and tasks, Copilot's progress from the linked pull request (its review state and checks) and Copilot's agent run. The workflow deploys it to `softwarefactory-sync` after the web app. Its database must accept connections from Azure services.

Configure these app settings on the Function App (`FUNCTIONS_WORKER_RUNTIME` is only for `local.settings.json`; a Flex Consumption app doesn't accept it):

| Setting | Notes |
| --- | --- |
| `WorkItemSyncSchedule` | CRON expression with six fields (seconds first), in UTC, e.g. `0 */15 * * * *` for every 15 minutes. The function fails to start without it |
| `ConnectionStrings__Database` | The same database as the Web App. The function reads and updates work items but doesn't migrate |
| `GitHub__SyncToken` | GitHub token that can read every repository in the database: a classic token with `repo`, or a fine-grained token with read access to issues, pull requests, commit statuses, checks and Actions. The function fails to start without it |
| `MediatR__LicenseKey` | Optional; the same MediatR license key as the Web App, if it has one |

The token's permissions aren't checked when the function starts, and missing ones don't fail the run:

- Without access to pull requests, commit statuses or checks, Copilot's progress is never updated: work items keep the progress they had, while their title, description and status are still synced.
- Without access to Actions, the agent run is unknown and its link isn't shown; whether Copilot is still working then follows from the `[WIP]` prefix in the pull request's title, so a failed or cancelled run isn't recognized.

Each run makes, per accepted work item, one REST request for the issue and, for user stories, bugs and tasks, one GraphQL request for the linked pull requests plus one REST request for Copilot's agent run while that pull request is open. REST and GraphQL each have their own limit of 5,000 per hour, so at 15-minute intervals a token handles about 625 items that all have an open pull request, and up to about 1,250 without. A deleted issue or a repository the token can't read is logged as a warning and the run continues. An invalid token or a reached rate limit stops the run, which then shows as failed.

## Image uploads

Images pasted, dropped or picked in the work item and work item template editors are uploaded to an Azure Storage blob container and linked by URL in the Markdown. GitHub (and the Copilot cloud agent) must be able to load them from the issue, so the container must allow anonymous read access to blobs:

```powershell
az storage account create -n <account> -g <resource-group> --sku Standard_LRS --allow-blob-public-access true
az storage container create -n dghsf-uploads --account-name <account> --public-access blob
az storage account show-connection-string -n <account> -g <resource-group> -o tsv
```

Use `--public-access blob`, not `container`, so the container cannot be listed. Blob names are random (`yyyy/MM/<guid>-<name>.png`) and never contain the repository name, but anyone with a link can view the image, also for private repositories. Only PNG, JPEG, GIF and WebP are accepted (detected from the content; SVG is refused). Without configuration, the editors refuse images instead of embedding them as base64.

## Dictation

The microphones in the title fields and at the top right of the description editors (work items and work item templates) use Azure AI Speech speech-to-text. The browser streams the microphone directly to Azure with the Speech SDK for JavaScript (served from `wwwroot/lib`). The server only exchanges the Speech key for a 10-minute authorization token (`sts/v1.0/issueToken`), so the key never reaches the browser. One Speech resource is enough; the free tier (F0) covers 5 audio hours a month:

```powershell
az cognitiveservices account create -n <speech-resource> -g <resource-group> --kind SpeechServices --sku F0 -l westeurope --yes
az cognitiveservices account keys list -n <speech-resource> -g <resource-group> --query key1 -o tsv
az webapp config appsettings set -n <web-app> -g <resource-group> --settings Speech__Region=westeurope Speech__ApiKey=<key>
```

The browser asks for microphone permission the first time (only over HTTPS). Without the `Speech` settings the buttons are hidden.

## Releasing

Push or merge to `main`, or run **Build and release** manually from the Actions tab. Pull requests only build and test. To require approval before production deployments, add required reviewers to the `production` environment.
