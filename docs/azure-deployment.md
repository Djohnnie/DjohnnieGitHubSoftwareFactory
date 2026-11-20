# Azure deployment

The `Build and release` workflow (`.github/workflows/release.yml`) builds, tests and publishes `Djohnnie.SoftwareFactory.Web`, `Djohnnie.SoftwareFactory.Sync` and `Djohnnie.SoftwareFactory.Tasks` on every push and pull request to `main`. On every **push to `main`** (or a manual run on `main`), the `deploy` job:

1. Logs in to Azure with OpenID Connect (no Azure secret stored in GitHub).
2. Opens a temporary Azure SQL firewall rule for the runner's IP.
3. Applies EF Core migrations with a self-contained migrations bundle (`efbundle`).
4. Removes the firewall rule (always, even on failure).
5. Deploys the published web app to Azure App Service.
6. Deploys the sync function app to the Function App `softwarefactory-sync`.
7. Deploys the tasks function app to the Function App `softwarefactory-tasks`.
8. When the agent worker is set up (the `AZURE_CONTAINER_REGISTRY` variable), builds its Docker image on the runner and pushes it to Azure Container Registry. With the `AZURE_WORKER_JOB_NAME` variable, it also updates the Container Apps job to that image. See [Agent worker](#agent-worker-container-apps-job).

The deployment is skipped when migrations fail.

## Azure resources (prepare beforehand)

- A resource group containing:
  - An App Service (Web App) with the **.NET 10** runtime stack (Linux or Windows).
  - A Function App named `softwarefactory-sync` on a **Flex Consumption** plan with the **.NET 10 isolated** runtime. Another name or plan needs a matching change to `SYNC_FUNCTION_APP` and the `sku` of the deploy step in the workflow.
  - A Function App named `softwarefactory-tasks`, also on **Flex Consumption** with **.NET 10 isolated**. Another name needs a matching change to `TASKS_FUNCTION_APP` in the workflow and `-TasksFunctionAppName` of the setup script.
  - An Azure Service Bus namespace (the **Basic** tier is enough) with a queue, for example `todo-tasks`. Keep the default lock duration and max delivery count; DGHSF schedules retries in the database itself.
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
| `GitHub__AllowedUserId` | Required; the numeric id of the only GitHub account that may sign in (`https://api.github.com/users/<login>` shows it). The id, not the login, because a login can be renamed and claimed by someone else. The app doesn't start without it |
| `OpenAI__Endpoint`, `OpenAI__Model`, `OpenAI__ApiKey` | Optional; enables the DGHSF agents |
| `ImageUploads__ConnectionString` | Optional; storage account connection string that enables image upload in the work item editors. See [Image uploads](#image-uploads) |
| `ImageUploads__ContainerName`, `ImageUploads__PublicBaseUrl`, `ImageUploads__MaxImageSizeInMegabytes` | Optional; default to `dghsf-uploads`, the blob container URL and `10` |
| `KnowledgeBase__ConnectionString` | Optional; storage account connection string that enables uploading files to the knowledge base. See [Knowledge base](#knowledge-base) |
| `KnowledgeBase__ContainerName`, `KnowledgeBase__PublicBaseUrl`, `KnowledgeBase__MaxFileSizeInMegabytes` | Optional; default to `dghsf-knowledge-base`, the blob container URL and `100` |
| `KnowledgeSearch__Endpoint`, `KnowledgeSearch__ApiKey` | Optional; the Azure AI Search service URL and a query key, so the work item chat agents can search the knowledge base. See [Knowledge base search](#knowledge-base-search) |
| `KnowledgeSearch__IndexName`, `KnowledgeSearch__SemanticConfiguration`, `KnowledgeSearch__MaxResults` | Optional; default to `dghsf-knowledge-base`, none (set `default` to use the semantic ranker) and `5` passages per search |
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
| `GitHub__SyncToken` | GitHub token that can read every repository in the database and request a Copilot code review once the Copilot cloud agent finished a revision: a classic token with `repo`, or a fine-grained token with read access to issues, commit statuses, checks and Actions and read and write access to pull requests. Its account needs Copilot code review. The function fails to start without it |
| `MediatR__LicenseKey` | Optional; the same MediatR license key as the Web App, if it has one |

The token's permissions aren't checked when the function starts, and missing ones don't fail the run:

- Without access to pull requests, commit statuses or checks, Copilot's progress is never updated: work items keep the progress they had, while their title, description and status are still synced.
- Without access to Actions, the agent run is unknown and its link isn't shown; whether Copilot is still working then follows from the `[WIP]` prefix in the pull request's title, so a failed or cancelled run isn't recognized.

Each run makes, per accepted work item, one REST request for the issue and, for user stories, bugs and tasks, one GraphQL request for the linked pull requests plus one REST request for Copilot's agent run while that pull request is open. REST and GraphQL each have their own limit of 5,000 per hour, so at 15-minute intervals a token handles about 625 items that all have an open pull request, and up to about 1,250 without. A GitHub server error, network error, timeout or database error is retried twice per work item (after 2 and 10 seconds); retries count towards the request budget. Any other problem with one work item or repository, such as a deleted issue or a repository the token can't read, is logged and the run continues with the others. An invalid token or a reached rate limit stops the run, which then shows as failed.

Everything the sync does is also written to the `SyncLogEvents` table in the database and shown in DGHSF on the **Sync log** page (`/sync-log`), newest first: every work item that changed, every problem, and a summary per run. Runs without changes, and runs without any accepted work item, are collapsed into one line that counts them, so a short interval doesn't flood the log. The events are kept forever.

## Tasks function app

`Djohnnie.SoftwareFactory.Tasks` executes the **background tasks** that the web app stores in the `TodoTasks` table, such as the runs started with **Generate from repository** on the Epics or Background tasks page. The web app never talks to Service Bus. The function app has two functions:

- **TodoTaskDispatch** (timer) takes back stuck tasks, marks the due pending tasks as queued, and sends their ids to the Service Bus queue. A task that stays queued for longer than 15 minutes (a lost message) is queued again. A task whose executor stopped (its 35-minute lease ended) is retried, or fails when it has no attempts left.
- **TodoTaskExecute** (Service Bus trigger) starts the task in the message, runs it, and stores the result and the sub-tasks it created. A message for a task that isn't queued, such as a duplicate or a cancelled run, is ignored. Errors are retried from the database with a growing delay (1, 2, 4 minutes; 3 attempts by default). A problem a retry can't fix, such as a disabled agent or a repository without readable files, fails the task right away. A run ends when all its sub-tasks have ended.

A "Generate from repository" run works like this. The **Repository Analyst** reads the repository's file tree with one request (binaries, dependencies, build output, lock files and generated code are left out) and up to 40 files. It saves epics and features with the status **Generated draft**: a draft that shows it was written by the agents. It can be edited, accepted and deleted like any draft. Then a **Repository Work Item Writer** task per feature reads up to 30 files, writes the feature's description and its user stories (and bugs, for improvements), and saves them as drafts. Both agents get the whole backlog of the repository as an outline (every work item with its type, status and id) so they don't propose what already exists, and can read the description of any work item in it by id (up to 20 per agent). Every draft remembers its run, so **Discard drafts** on the Background tasks page can remove a run's drafts at once. Both agents are internal agents: their prompts and models can be changed on the internal agents page, and their token spend is tracked there.

**Repository snapshots (recommended).** With `RepositorySnapshots__ConnectionString` set, the first task of a run downloads the whole default branch as one archive. That is a single GitHub request. It stores the readable files, plus a manifest, in a private blob container under `{runId}/`, and every task of the run reads from there. A run then uses one GitHub request instead of up to ~30 per feature, and all its tasks see the same commit. A snapshot may hold at most 20,000 files or 100 MB after filtering; for a larger repository, analyze one folder at a time. The `RepositorySnapshotCleanup` function deletes the snapshot of every run that has ended, every 10 minutes. Without the setting, the tasks read the file tree and every file from the GitHub API.

Create the container without anonymous access (don't reuse the image upload container, which is public). As a safety net, add a lifecycle management rule on the storage account that deletes blobs in that container a day after they were modified:

```powershell
az storage container create -n repository-snapshots --account-name <account> --public-access off
```

Configure these app settings on the Function App:

| Setting | Notes |
| --- | --- |
| `TodoTaskDispatchSchedule` | CRON expression with six fields (seconds first), in UTC, e.g. `*/15 * * * * *` for every 15 seconds. The function fails to start without it |
| `TodoTaskQueueName` | The Service Bus queue, e.g. `todo-tasks` |
| `ServiceBus` | The Service Bus connection string. With a managed identity instead, set `ServiceBus__fullyQualifiedNamespace` to `<namespace>.servicebus.windows.net` and give the identity **Azure Service Bus Data Sender** and **Data Receiver** on the queue |
| `ConnectionStrings__Database` | The same database as the Web App. The function reads and writes background tasks and work items but doesn't migrate |
| `GitHub__TasksToken` | GitHub token that can read the contents of every repository that is analyzed: a classic token with `repo`, or a fine-grained token with read access to contents. The function fails to start without it |
| `OpenAI__Endpoint`, `OpenAI__Model`, `OpenAI__ApiKey` | The same model settings as the Web App; without them every run fails because the agents aren't configured |
| `RepositorySnapshots__ConnectionString` | Optional (recommended); the storage account connection string for repository snapshots. Without it, the tasks read from the GitHub API |
| `RepositorySnapshots__ContainerName` | Optional; default `repository-snapshots`. A private container; it is created if it doesn't exist |
| `RepositorySnapshots__MaxFiles`, `RepositorySnapshots__MaxSizeInMegabytes` | Optional; default 20000 and 100 |
| `TodoTasks__MaxAttempts`, `TodoTasks__RetryDelay`, `TodoTasks__Lease`, `TodoTasks__QueuedTimeout` | Optional; default 3, `00:01:00`, `00:35:00` and `00:15:00`. Keep the lease longer than the `functionTimeout` in `host.json` (30 minutes) |
| `MediatR__LicenseKey` | Optional; the same MediatR license key as the Web App, if it has one |

`host.json` runs at most 4 tasks at a time per instance (`maxConcurrentCalls`), which keeps a run within the model's and GitHub's rate limits. Each feature of a run costs a few model calls with the repository outline and the files the agent reads, so a run's token spend grows with the number of features. Limit it with the folder and the maximum number of features when you start the run.

The function app also has **AgentRunDispatch** (timer, on `TodoTaskDispatchSchedule`), which puts the runs of the [agent worker](#agent-worker-container-apps-job) on their own queue. It does nothing until `AgentRuns__QueueName` is set:

| Setting | Notes |
| --- | --- |
| `AgentRuns__QueueName` | Optional; the agent worker's queue, e.g. `agent-runs`. Uses the same `ServiceBus` connection; with a managed identity, also give it **Azure Service Bus Data Sender** on this queue |
| `AgentRuns__QueuedTimeout`, `AgentRuns__Lease`, `AgentRuns__MaxAttempts` | Optional; default `00:15:00`, `00:10:00` and 2. Use the same values on the worker job |

## Agent worker (Container Apps job)

Stories, bugs and tasks can run on the **DGHSF agent worker** instead of the Copilot cloud agent ("Runs on" in the work item's assignment). Then accepting the item doesn't assign Copilot on GitHub. DGHSF stores an *agent run* in the `AgentRuns` table, and a container job runs the agent with the [GitHub Copilot SDK](https://github.com/github/copilot-sdk):

1. The Tasks function app's **AgentRunDispatch** moves pending runs to the `agent-runs` Service Bus queue. Only the run's id travels; SQL is the source of truth.
2. The queue's length scales an **event-driven Azure Container Apps job** (KEDA `azure-servicebus` scaler). Every execution takes one message, starts that run with a lease and completes the message right away: an agent works far longer than a Service Bus lock lasts. It renews the lease every few minutes while the agent works.
3. The execution clones the base branch (shallow), creates the branch `dghsf/<issue>-<title>-<run>`, and runs one Copilot session in the clone. Every tool is approved, because the container is the sandbox.
4. It commits whatever the agent changed, pushes the branch, opens a pull request with the agent's summary and `Closes #<issue>`, and stores the session's **AI credits** and token counts with the run. The work item page lists every run with its cost.

A run that fails before the agent starts (for example the clone) is retried once. After the agent ran, it isn't, because a retry would pay for the agent again: use **Run again** on the work item. When the agent hits the credit limit or the time limit, its work is pushed as a draft pull request. **Cancel run** stops the agent at its next lease renewal and pushes nothing. When an execution dies (its replica timeout, a crash), the dispatcher takes the run back once its lease has ended.

The sync and the Refresh button follow these items through their run (queued, working, stopped) and then through the pull request, like Copilot's.

### Resources

```powershell
# The queue (the Basic tier is enough; keep the default lock duration and max delivery count).
az servicebus queue create -g <rg> --namespace-name <namespace> -n agent-runs

# A registry with the admin account (CI pushes with it, the job pulls with it), and a Container Apps environment.
az acr create -g <rg> -n <registry> --sku Basic --admin-enabled true
az acr credential show -n <registry> --query "{username:username, password:passwords[0].value}"
az containerapp env create -g <rg> -n dghsf-env -l westeurope

# The first image; later the release workflow builds and pushes it. (Or run the workflow once before creating the job,
# and use the :latest tag it pushes.)
az acr build -r <registry> -t dghsf-agent-worker:initial -f src/Djohnnie.SoftwareFactory.Worker/Dockerfile .

az containerapp job create -g <rg> -n dghsf-agent-worker --environment dghsf-env `
    --trigger-type Event --replica-timeout 5400 --replica-retry-limit 0 --replica-completion-count 1 --parallelism 1 `
    --min-executions 0 --max-executions 4 --polling-interval 30 `
    --scale-rule-name agent-runs --scale-rule-type azure-servicebus `
    --scale-rule-metadata "queueName=agent-runs" "namespace=<namespace>" "messageCount=1" `
    --scale-rule-auth "connection=service-bus" `
    --image <registry>.azurecr.io/dghsf-agent-worker:initial `
    --registry-server <registry>.azurecr.io --registry-username <admin username> --registry-password <admin password> `
    --cpu 2 --memory 4Gi `
    --secrets "service-bus=<Service Bus connection string>" "database=<SQL connection string>" `
              "github-token=<repository token>" "copilot-token=<Copilot token>" `
    --env-vars "ServiceBus=secretref:service-bus" "ConnectionStrings__Database=secretref:database" `
               "AgentWorker__GitHubToken=secretref:github-token" "AgentWorker__CopilotToken=secretref:copilot-token" `
               "AgentRuns__QueueName=agent-runs" "AgentWorker__RunTimeout=01:15:00" "AgentWorker__MaxAiCreditsPerRun=1000"
```

- `--registry-username` and `--registry-password` let the job pull with the registry's admin account (stored as a secret of the job). Without the admin account, use `--registry-identity system` instead: the job then pulls with its own identity.
- Keep `--replica-timeout` (seconds) well above `AgentWorker__RunTimeout`. The worker then still has time to push a draft when the agent runs out of time.
- `--max-executions` is how many agents work at the same time.
- To use a managed identity for Service Bus instead of a connection string, set `ServiceBus__fullyQualifiedNamespace` (plus `ServiceBus__clientId` for a user-assigned identity). Give the identity **Azure Service Bus Data Receiver** on the queue, and use `--scale-rule-identity` instead of `--scale-rule-auth`.
- The database must accept connections from the environment's outbound IP addresses.

Then add these to the `production` environment of the repository (**Settings > Environments > production**). The release workflow then builds the image on the runner, pushes it as `dghsf-agent-worker:<commit sha>` and `:latest` with the registry's admin account, and updates the job after every deployment:

| Name | Kind | Value |
| --- | --- | --- |
| `AZURE_CONTAINER_REGISTRY` | variable | The registry's login server, e.g. `myregistry.azurecr.io` (the name alone works too). Without it, no image is built |
| `ACR_USERNAME` | secret | The admin username (**Access keys** of the registry, or `az acr credential show`) |
| `ACR_PASSWORD` | secret | One of the two admin passwords |
| `AZURE_WORKER_JOB_NAME` | variable | Optional; the job, e.g. `dghsf-agent-worker`. Without it, the image is pushed but no job is updated |
| `AZURE_WORKER_RESOURCE_GROUP` | variable | Optional; the job's resource group when it isn't `AZURE_RESOURCE_GROUP` |

Or with the GitHub CLI:

```powershell
gh variable set AZURE_CONTAINER_REGISTRY --env production --body "<registry>.azurecr.io"
gh secret set ACR_USERNAME --env production --body "<admin username>"
gh secret set ACR_PASSWORD --env production   # prompts, so the password stays out of your shell history
gh variable set AZURE_WORKER_JOB_NAME --env production --body "dghsf-agent-worker"
```

Pushing needs no Azure role. Updating the job does: give the workflow's app registration **Contributor** on the job:

```powershell
az role assignment create --assignee <AZURE_CLIENT_ID> --role Contributor --scope $(az containerapp job show -g <rg> -n dghsf-agent-worker --query id -o tsv)
```

When you regenerate an admin password, update `ACR_PASSWORD` and the job's registry password (`az containerapp job registry set -g <rg> -n dghsf-agent-worker --server <registry>.azurecr.io --username <admin username> --password <new password>`).

### Settings

| Setting | Notes |
| --- | --- |
| `ServiceBus` | The Service Bus connection string (or `ServiceBus__fullyQualifiedNamespace`, see above) |
| `AgentRuns__QueueName` | The queue, e.g. `agent-runs` |
| `ConnectionStrings__Database` | The same database as the Web App. The worker reads work items and writes runs but doesn't migrate |
| `AgentWorker__GitHubToken` | The token the worker clones, pushes, opens and comments on pull requests and refreshes work items with, and that asks Copilot code review to review a revision it pushed: a fine-grained token with read and write access to **Contents** and **Pull requests** and read access to **Issues** of the repositories. Without Copilot code review on its account, a revision succeeds with a warning |
| `AgentWorker__CopilotToken` | Optional; the token the Copilot SDK runs with, whose Copilot plan pays the AI credits: a fine-grained token with the account permission **Copilot Requests** (classic `ghp_` tokens aren't accepted). Without it, `AgentWorker__GitHubToken` is used, which then needs that permission too |
| `AgentWorker__MaxAiCreditsPerRun` | Optional; a soft limit per run (checked between model calls). Without it a run has no limit |
| `AgentWorker__RunTimeout` | Optional; default `01:00:00` |
| `AgentWorker__DraftPullRequests` | Optional; `true` to open every pull request as a draft |
| `AgentWorker__CommitAuthorName`, `AgentWorker__CommitAuthorEmail` | Optional; the author of the agent's commit |
| `ImageUploads__ConnectionString` (and the other `ImageUploads__` settings) | Optional; the same image storage as the Web App. With it, the agent adds screenshots of the user interface it changed to its pull requests, for work items with **Include UI screenshots** on (the default). Without it, those pull requests say that there is no image storage. See [Image uploads](#image-uploads) |
| `AgentRuns__Lease`, `AgentRuns__MaxAttempts` | Optional; the same values as the Tasks function app |
| `MediatR__LicenseKey` | Optional; the same MediatR license key as the Web App, if it has one |

### Cost and security

- **Cost.** The SDK reports each session's cost in nano AI units; DGHSF divides by 10<sup>9</sup> to get AI credits, as the SDK's documentation does. That conversion is defined by GitHub's billing, so compare it with the billing usage on the overview page now and then. A run whose cost GitHub only partly reported is shown as "at least". The image runs on the Container Apps consumption plan, so you also pay for the job's CPU and memory while the agent works.
- **The agent can run any command in the container**, and it reads repository content that may try to talk it into things. DGHSF starts the Copilot runtime without the worker's secrets in its environment: no database or Service Bus connection, no repository token and no managed identity endpoint. It passes the token to git only for the clone and the push. The Copilot token is available to the runtime, so give it nothing but **Copilot Requests**. The container can reach the internet; restrict outbound traffic of the environment if your repositories need that.
- The image is the .NET SDK image with git. Add the tool chains your repositories need (Node.js, Python, …) to `src/Djohnnie.SoftwareFactory.Worker/Dockerfile`, so the agent can build and test.
- Custom agents (`.github/agents/<name>.agent.md` on the base branch) give the session their instructions and model; their `tools` list is ignored, because GitHub's tool aliases aren't the runtime's tool names.

## Image uploads

Images pasted, dropped or picked in the work item and work item template editors are uploaded to an Azure Storage blob container and linked by URL in the Markdown. GitHub (and the Copilot cloud agent) must be able to load them from the issue, so the container must allow anonymous read access to blobs:

```powershell
az storage account create -n <account> -g <resource-group> --sku Standard_LRS --allow-blob-public-access true
az storage container create -n dghsf-uploads --account-name <account> --public-access blob
az storage account show-connection-string -n <account> -g <resource-group> -o tsv
```

Use `--public-access blob`, not `container`, so the container cannot be listed. Blob names are random (`yyyy/MM/<guid>-<name>.png`) and never contain the repository name, but anyone with a link can view the image, also for private repositories. Only PNG, JPEG, GIF and WebP are accepted (detected from the content; SVG is refused). Without configuration, the editors refuse images instead of embedding them as base64.

The agent worker uploads the screenshots it adds to pull requests to the same container, so they are just as public: turn **Include UI screenshots** off on work items of private repositories whose user interface must not be visible to anyone with the link.

## Knowledge base

> **Knowledge base files are public.** Anyone with a link can open a file, together with its title, description and folder path (blob metadata, returned as response headers). Don't upload confidential data, such as secrets, credentials, personal data or internal documents that may not be shared. The page and the upload and edit dialog warn about this.

The knowledge base page keeps a library of files: global files for every repository, and per repository its own files in virtual folders. The folders, titles, descriptions and repositories are stored in Azure SQL; each file is a blob named after its id (a GUID) in its own container, separate from the image uploads. Work items link to the files, and GitHub and the Copilot cloud agent must be able to open those links, so the container must allow anonymous read access to blobs. The app creates the container with blob access when it doesn't exist, but that only works when the account allows public access:

```powershell
az storage container create -n dghsf-knowledge-base --account-name <account> --public-access blob
```

Each blob also has `title`, `description` and `path` metadata (the virtual folder, like `/Specs/API`), percent-encoded as UTF-8, so the container is understandable in Storage Explorer without the database. The app keeps it up to date when a file is edited or its folder is renamed or moved. Azure returns metadata as `x-ms-meta-*` response headers when a blob is downloaded, also anonymously, so anyone with a link can read the title, description and folder path, not only the file.

Use `--public-access blob`, not `container`, so the files cannot be listed. The links contain only the file's id, never its title or name, but anyone with a link can download the file, also for private repositories. HTML, SVG, XML and JavaScript files are served as downloads, never shown in the browser. Files are at most `KnowledgeBase__MaxFileSizeInMegabytes` (default 100 MB); they stream through the Blazor circuit, so large files take a while. Without configuration, the page still manages folders and the existing metadata but refuses uploads.

## Knowledge base search

The work item chat agents get a `search_knowledge_base` tool that finds passages in the knowledge base files: the global files and the files of the selected repository. An Azure AI Search indexer reads the knowledge base container, splits every file into overlapping passages of about 2,000 characters, embeds them with an Azure OpenAI embedding model and stores one search document per passage. Each passage carries the file id (the blob name) and the `scope` (`global` or `repository`) and `repository` blob metadata that DGHSF writes, so DGHSF searches with the filter `scope eq 'global' or scope eq null or repository eq 'owner/repo'` (passages without a scope come from files uploaded before files could belong to a repository, which are all global). The search is hybrid: keywords plus vectors, with the query embedded by the index's own vectorizer. DGHSF then checks every passage against the database and takes the file's title, link and folder from there, so a deleted file, or a file that moved to another repository, never reaches an agent, even before the indexer has caught up.

What you need:

- The Azure AI Search service. Basic or higher; the free tier works for trying it out, but limits the index size and has no semantic ranker quota to speak of.
- An Azure OpenAI (or Foundry) resource with an embedding deployment, for example `text-embedding-3-small` (1,536 dimensions). Both the indexer and the query vectorizer call it, so indexing costs embedding tokens for every passage, and every search costs a few.
- Blob soft delete on the storage account, so the indexer notices deleted files and removes their passages:

  ```powershell
  az storage account blob-service-properties update -n <account> -g <resource-group> --enable-delete-retention true --delete-retention-days 7
  ```

Then create the data source, index, skillset and indexer. The script fills in the templates in `docs/knowledge-base-search` and is safe to run again:

```powershell
./scripts/setup-knowledge-search.ps1 -SearchServiceName <search service name> -SearchAdminKey <admin key> `
    -StorageConnectionString "<the same connection string as KnowledgeBase__ConnectionString>" `
    -OpenAIEndpoint https://<resource>.openai.azure.com -OpenAIKey <key> -EmbeddingDeployment <deployment name>
```

Add `-EmbeddingModel text-embedding-3-large -Dimensions 3072` for the large model, `-Schedule PT5M` to index more often (the default is every 15 minutes), and `-ContainerName` or `-IndexName` when you changed them in DGHSF. The vector field can't be changed in place: to switch models or dimensions later, delete the index, run the script again and reset the indexer.

Finally, configure DGHSF:

| Setting | Value |
| --- | --- |
| `KnowledgeSearch__Endpoint` | `https://<search service name>.search.windows.net` |
| `KnowledgeSearch__ApiKey` | A **query** key (Search service > Keys > Manage query keys); DGHSF only reads the index |
| `KnowledgeSearch__SemanticConfiguration` | Optional: `default`, to rerank results with the semantic ranker (enable it on the search service first) |

Check it in the portal: the indexer's execution history shows each run with its documents and warnings, and Search explorer on the index should return passages with `file_id`, `scope` and `repository` filled in. Formats the indexer can't read (images without OCR, archives) are skipped with a warning. Files in the container are recognized by their content type, because the blob names have no extension.

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
