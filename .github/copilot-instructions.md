# Copilot instructions

## Build and test

- Build the solution: `dotnet build Djohnnie.SoftwareFactory.slnx`
- Run the test suite: `dotnet test tests\Djohnnie.SoftwareFactory.Web.Tests\Djohnnie.SoftwareFactory.Web.Tests.csproj`
- Run one test: `dotnet test tests\Djohnnie.SoftwareFactory.Web.Tests\Djohnnie.SoftwareFactory.Web.Tests.csproj --filter "FullyQualifiedName~EpicServiceTests.Accept_CreatesLabelledIssueAndAssignsCopilotWithAgentAndModel"`
- Restore the local EF tool before using it: `dotnet tool restore`
- Set `ConnectionStrings__Database` in the shell before using EF tools (use the connection string from your local development settings).
- Add a migration: `dotnet ef migrations add <Name> --project src\Djohnnie.SoftwareFactory.DataAccess.Sql --output-dir Migrations`
- Check the migration snapshot: `dotnet ef migrations has-pending-model-changes --project src\Djohnnie.SoftwareFactory.DataAccess.Sql`
- Azure deployment: `.github\workflows\release.yml` builds and tests on pushes and pull requests to `main`. On every push to `main`, it applies an EF migrations bundle to Azure SQL and deploys to App Service. One-time OIDC setup is in `scripts\setup-azure-github.ps1`; see `docs\azure-deployment.md`.

The app targets .NET 10. Development startup applies EF migrations automatically and uses the LocalDB connection string in `appsettings.Development.json`; other environments must supply `ConnectionStrings:Database`. `appsettings.Development.json` is gitignored and holds personal settings such as `OpenAI:ApiKey`; create it by copying the tracked `appsettings.Development.example.json` (keep the template free of secrets and in sync when adding settings). Neither file is published.

## Architecture

- Project references flow from `Web` to `Application`, and from `Application` to `DataAccess.Sql` and `Agents`. All libraries share `Contracts`, which has no application or infrastructure dependencies. Keep Web free of EF and Agent Framework types.
- `Application/Features` contains MediatR commands, queries, handlers and FluentValidation validators grouped by feature, one type per file in `Commands`, `Queries` and `Validators` subfolders (e.g. `Epics/Queries/GetEpicQuery.cs` and `GetEpicQueryHandler.cs`); these keep the feature namespace. Requests and responses wrap records from `Contracts`; collection responses wrap `CollectionResult<T>`. UI-facing application services dispatch through MediatR, whose validation pipeline runs before handlers.
- `DataAccess.Sql` owns EF entities, DTO mapping, stores, `AppDbContext`, migrations and the design-time factory. Stores live in `Stores` behind interfaces (`IEpicStore`, ...) that are registered and injected through DI. Entity configuration lives in `Configuration`, one `ModelBuilder` extension per entity, called from `OnModelCreating`. Stores use `IDbContextFactory<AppDbContext>` and dispose a context per operation. DTOs are separate records, not EF entities. Existing migration IDs and the database schema are preserved.
- `Agents` owns Microsoft Agent Framework agents, sessions, streaming and AI form tools. `Application.Features.Assistant` exposes framework-free conversations and tool factories to Web. Contract records are mutable where form binding requires it; use `Copy()` or `with` for editable copies.
- This is a .NET 10 Blazor app using Interactive Server rendering without prerendering for interactive routes. GitHub OAuth is single-user: `Program.cs` enforces the configured allowed login and keeps the access token in the server-side authentication ticket. GitHub API calls get the token from the `GitHubClaims.AccessToken` claim through `AuthenticationStateProvider`.
- The app operates on one selected repository per Blazor circuit. `RepositoryContext` restores and stores the selection in protected browser local storage; `MainLayout` requires a selection and keys the page body by repository full name so switching repositories recreates the page. Pages marked `[RepositoryIndependent]` (detected through the `RouteData` cascaded from `Routes.razor`) render without a selection, e.g. the agent template pages.
- Repository selection and restored selections update SQL `RepositoryAccesses` through `RepositoryHistoryService`. The picker loads the five most recent names/access times independently of GitHub's full repository list and fetches a recent repository directly when selected before that list finishes.
- Agent templates (`AgentTemplateService`) are stored in SQL Server as full agent file content, are shared across repositories, and are applied to one or more repositories through the application workflow and `GitHubApiClient`. `AgentEditor` and `AgentTemplateEditor` share the `Components/Shared/AgentForm.razor` form.
- Repository-backed features should use `RepositoryContext.Current` and pass the repository full name through application service operations. Epics are scoped by repository; the managed AI model catalog and agent templates are shared across repositories.
- Custom agent profiles live in each repository's `.github/agents` directory and are read or committed through `GitHubApiClient`. `AgentFileSerializer` preserves unedited YAML frontmatter keys and their order; retain that behavior when changing agent-file editing.
- DGHSF Copilot uses the singleton `AssistantAgentFactory` in `Agents` and the `OpenAI` config section (`Endpoint`, `Model`, `ApiKey`; unconfigured means the chat shows a setup hint). Form tools read and update state through an `onUi` callback (`InvokeAsync` + `StateHasChanged`). Web uses `AssistantForms.ForAgent` / `ForEpic` and their instructions with `AssistantChat`. Tools only change the form; the user saves.
- Write extension methods with C# 14 `extension(...)` blocks.
- Use block bodies (`{ ... }`) for methods, constructors, operators and local functions, also in Razor `@code` blocks; expression bodies only where a block is not possible. `.editorconfig` enforces this for C# files.
- UI pages are Razor components using MudBlazor. Tests use xUnit and bUnit in `tests\Djohnnie.SoftwareFactory.Web.Tests`.

## Repository guidance

`README.md` is only a brief project description. `IDEAS.md` includes both implemented features and roadmap items; confirm behavior in the source before treating planned features as available.
