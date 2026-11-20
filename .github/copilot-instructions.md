# Copilot instructions

DGHSF is a single-user Blazor app that plans work items per GitHub repository and hands them to the Copilot cloud agent or its own agent worker. This file holds the conventions and invariants that apply everywhere. How each feature behaves is in [`docs/features.md`](../docs/features.md): read the section of the feature you change, and update it when the behavior changes.

## Build and test

- Build the solution: `dotnet build Djohnnie.SoftwareFactory.slnx` (run `dotnet restore Djohnnie.SoftwareFactory.slnx` first; the Functions projects need it).
- Run the test suite: `dotnet test tests\Djohnnie.SoftwareFactory.Web.Tests\Djohnnie.SoftwareFactory.Web.Tests.csproj`
- Run one test: add `--filter "FullyQualifiedName~WorkItemServiceTests.Accept_Hierarchy_CreatesLabelledSubIssuesAndOnlyAssignsTheStory"`.
- CI builds with `-p:TreatWarningsAsErrors=true`, so fix analyzer warnings (also in tests) instead of leaving them.
- EF tools: `dotnet tool restore`, and set `ConnectionStrings__Database` in the shell first (use your local development connection string).
  - Add a migration: `dotnet ef migrations add <Name> --project src\Djohnnie.SoftwareFactory.DataAccess.Sql --output-dir Migrations`
  - Check the snapshot: `dotnet ef migrations has-pending-model-changes --project src\Djohnnie.SoftwareFactory.DataAccess.Sql`
  - Existing migration IDs are preserved. When a migration adds a constraint that existing rows could violate, fix those rows in the migration first.
- The app targets .NET 11 (SDK pinned in `global.json`). Development startup applies EF migrations to whatever database `appsettings.Development.json` points at, so keep that on a local database. That file is gitignored and holds personal settings; `README.md` shows its shape and the shape of the Functions' `local.settings.json`. Keep both in sync when adding settings.
- Tests use xUnit and bUnit. Most use the EF InMemory provider, which doesn't support `ExecuteUpdate`/`ExecuteDelete` or enforce unique indexes, foreign keys, collation or concurrency against a real database. For those, write tests on `SqlServerDatabase` (a new database per test, built by the migrations): LocalDB on Windows, or `DGHSF_TEST_SQL_SERVER`, which CI points at a SQL Server container. They are skipped when neither is available.
- bUnit tests register `BrowserTimeZone` next to `AddMudServices`.
- Deployment: `.github\workflows\release.yml` builds and tests every push and pull request to `main`, and on a push to `main` applies a migrations bundle to Azure SQL and deploys the web app, both Function apps and (when configured) the worker image. See `docs\azure-deployment.md`.

## Architecture

| Project | Responsibility |
| --- | --- |
| `Web` | Blazor UI (Interactive Server, MudBlazor), GitHub sign-in, the selected repository |
| `Application` | MediatR commands and queries per feature, validation, workflows, `GitHubApiClient` |
| `DataAccess.Sql` | EF Core entities, configuration, stores, migrations |
| `Agents` | Microsoft Agent Framework agents, sessions and form tools |
| `Contracts` | Records shared by all projects; no application or infrastructure dependencies |
| `Sync`, `Tasks` | Isolated-worker Azure Function apps; reference only `Application` |
| `Worker` | Container Apps job running the DGHSF agent worker with the GitHub Copilot SDK; the only project referencing `GitHub.Copilot.SDK` |

- References flow from `Web` to `Application`, and from `Application` to `DataAccess.Sql` and `Agents`. Keep `Web` free of EF and Agent Framework types.
- `Application/Features/<Feature>` holds requests, handlers and FluentValidation validators, one type per file in `Commands`, `Queries` and `Validators` subfolders, in the feature namespace. Requests and responses wrap records from `Contracts`; collection responses wrap `CollectionResult<T>`, paged ones `PagedResult<T>`. UI-facing services dispatch through MediatR, whose validation pipeline throws `ValidationFailedException`.
- Every request with a repository name validates it with `ValidRepositoryFullName()` (`Application/Common/RepositoryNameRules`), because the name goes into GitHub API URLs.
- Repository-backed features take `RepositoryContext.Current` and pass the repository full name through every application operation; data is scoped by repository unless it is explicitly shared (models, agent templates, skills, work item templates, internal agents).

### Errors

- A domain rule that refuses an operation (in stores, workflows and services) throws `DomainRuleException` with a message for the user.
- UI catch blocks filter on the expected exceptions (`WorkItemActions.IsExpected`: `DomainRuleException`, `ValidationFailedException`, `PersistenceException`, `GitHubApiException`, `HttpRequestException`) and show them. Everything else, `InvalidOperationException` included, surfaces as a bug. Only fire-and-forget work that nothing else observes catches everything, and it logs what it caught.

### Data access

- Stores live in `DataAccess.Sql/Stores` behind interfaces registered in DI. They use `IDbContextFactory<AppDbContext>` with a context per operation, and return DTO records, never EF entities.
- Entity configuration lives in `Configuration`, one `ModelBuilder` extension per entity, called from `OnModelCreating`.
- Every entity derives from `Entity`: `Id` (`Guid`, generated by the application) is the nonclustered primary key that foreign keys, contracts and URLs use; `SysId` (`int` identity) is the clustered alternate key and stays inside `DataAccess.Sql`. Sort on `SysId` for insertion order, never on `Id`.
- `AppDbContext.SaveChangesAsync` wraps EF failures in `PersistenceException`; catch that (with the inner exception as a filter) for unique-index or concurrency races.
- Work that more than one process can pick up (background tasks, agent runs, accepting a work item) changes status with a conditional `ExecuteUpdateAsync` on the expected status, or with a concurrency token, so duplicate messages and concurrent callers are harmless. SQL is the source of truth; Service Bus only carries ids, and retries go through the database, never through redelivery. Task handlers must be safe to run again.

### Blazor

- Interactive Server without prerendering. `MainLayout` requires a selected repository and keys the page by its full name, so switching repositories recreates the page; pages marked `[RepositoryIndependent]` render without one.
- Every page starts with `PageBreadcrumbs` (the trail from the overview and a back arrow). Page actions are a row of text buttons between the title and the first box. Detail pages have no Cancel. Editors of stored things have a Reset that confirms and reloads without setting `_loading` (which would tear down the form and its chat), and isn't shown on new items.
- Split large pages into components per section, and keep shared state that outlives a component in a plain class with a `Changed` event or an `onUi` callback (`WorkItemPullRequestState`, `WorkItemModelSuggestions`).
- Show timestamps in the browser's time zone through `BrowserTimeZone` (`Format`/`ToLocal`), never `DateTimeOffset.ToLocalTime()`, which gives the server's zone (UTC on Azure).
- Pages that serve several work item types through one component take the cascaded `RouteData` and check `WorkItemRoutes.IsOutOfStep` before combining the URL's type with their `Id`.
- Layout constants in C# that must match CSS (`WorkItemMapLayout.Box`, `MindmapLayout` sizes) are changed together with the stylesheet.

### Code style

- Use block bodies (`{ ... }`) for methods, constructors, operators and local functions, also in Razor `@code` blocks; expression bodies only where a block is not possible. `.editorconfig` enforces this for C# files.
- Write extension methods with C# 14 `extension(...)` blocks.
- Contract records are mutable where form binding requires it; use `Copy()` or `with` for editable copies.

## Security invariants

- GitHub OAuth is single-user: `Program.cs` only lets in the GitHub account whose numeric id is `GitHub:AllowedUserId` (`GitHubAuthOptions.IsAllowed`); never compare logins, which can be renamed and claimed by someone else. The access token lives in the server-side ticket, encrypted with data protection and stored in SQL (`SqlTicketStore`); the cookie only holds a session key.
- Model output is untrusted (prompt injection). `ChatMarkdown` escapes raw HTML, keeps only http(s)/mailto links and turns images into links; keep it that way.
- The agent worker runs every tool without asking and pushes to the branch. Its shell gets a sanitized environment (`AgentEnvironment.Sanitized`), the git token reaches git only through `GIT_CONFIG_*` variables of the clone and push, and revisions only act on pull request feedback from the repository's owner, members, collaborators and Copilot (`PullRequestDiscussion.IsTrustedAuthor`). Don't widen any of these.
- Knowledge base files, their title, description and path, and uploaded images are public blobs. Keep `KnowledgeBaseRoutes.PublicNotice` where it is shown, and keep confidential details out of anything that becomes public.
- Usage metrics downloads go to signed links **without** the GitHub token.

## Invariants that are easy to break

- `AgentFileSerializer` preserves unedited YAML frontmatter keys and their order when editing agent files.
- Stored enum names and keys keep their old names (e.g. `Copilot` for chat agents). A new internal agent needs its key in `InternalAgentKeys`, its defaults in `InternalAgentDefaults` and a member in `InternalAgentUsageSource`.
- Reasoning tokens are part of the output tokens: store and show them, never charge them separately.
- All prices and costs are displayed in euro with 3 decimals through `AiModel.FormatPrice`; GitHub billing amounts are shown in US dollars.
- The Azure AI Search field names (`file_id`, `chunk`, `text_vector`, `scope`, `repository`) are shared with `AzureAiKnowledgeSearchIndex` and `docs/knowledge-base-search`.
- `MindmapStore.NodeData` property names are the stored JSON shape.
- Don't use GitHub's search API for activity numbers: its secondary rate limit trips after a few quick requests.
- `GitHubApiClient` goes through a resilience pipeline (`GitHubApiResilience`) that retries only GET requests and requests sent with `readOnly: true` (GraphQL queries). Never mark a request that creates or changes something as read-only: a retry would repeat it.

## Repository guidance

`README.md` describes the app, local setup and deployment for people. `docs/features.md` describes each feature's behavior. `IDEAS.md` mixes implemented features and roadmap items; confirm behavior in the source before treating a planned feature as available. `GAPS.md` is a dated review, not a spec.
