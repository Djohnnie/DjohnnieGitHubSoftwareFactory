<p align="center">
  <img src="docs/logo.svg" alt="DGHSF logo" width="160" height="160" />
</p>

# DGHSF – Djohnnie's GitHub Software Factory

> Living idea document. It captures what I want to build before any implementation starts.

## 1. Vision

DGHSF is a personal, custom **software factory** web application. It lets me browse my GitHub repositories, manage custom Copilot agents per repository, plan work as epics, features, user stories, bugs and tasks, turn accepted items into GitHub issues, and hand those issues to GitHub's cloud agent. A built-in assistant, **DGHSF Copilot**, helps me write and maintain the planning items.

The application is for me only. My own database is the source of truth; GitHub is where accepted work gets executed.

## 2. Technology and hosting

- **.NET 10** with **Blazor Server-side rendering (SSR)**.
- **Entity Framework** on an **Azure SQL** database.
- Hosted eventually on an **Azure App Service**; local development comes first.
- A separate **Azure Function app** (CRON-triggered) handles synchronisation with GitHub.
- **DGHSF Copilot** is built with the **Microsoft Agent Framework** and connected to my own **Microsoft Foundry** (I pay for it). The model choice is made later.

![Architecture](docs/architecture.svg)

## 3. Authentication and security

- Login is done with my **GitHub account** through a **GitHub OAuth App**, so the same user token can call the GitHub API.
- Only my GitHub account is allowed in: any other account is rejected (allow-list on GitHub user id/login).
- The OAuth token lives only in the **server-side session**. When it is lost or expires, I simply log in again.
- The Azure Function has no user session, so it uses a **fine-grained personal access token (PAT)**. The PAT is configured through a **settings page in the app** and stored in the database (encrypted at rest and never shown in full after saving).

## 4. Features

### 4.1 Repository browser

- Lists **all** my repositories (personal and organisation).
- Whenever I have to pick a repository, the list is sorted by most recent activity, using `pushed_at` from the GitHub repository list.
- Repository and label data are **cached in the database** for speed and to limit API calls.
- The repository page is the entry point for agents and work items.

### 4.2 Custom agent management

For each repository I can create, update and delete custom agents (the `.github/agents/*.agent.md` files). Changes are **committed directly** to the repository, with no pull request and no extra safeguards for now. Only repository-level agents are in scope; organisation-level agents may come later.

**Implemented (agent templates):** a **Templates** page (`/agent-templates`) stores agent templates in our own database, and doesn't need a repository to be selected. A template holds the full agent file (frontmatter + instructions). From a template I can create an agent in the current repository (the editor is prefilled), or **add it to several repositories at once**: I can optionally replace files that differ, and identical files are left untouched. On the Agents page, **Save as template** turns an existing agent into a template.
### 4.3 Work items

I plan work in the app using five item types:

- **Epic**, **Feature** and **User story** form a hierarchy: an epic contains features, and a feature contains user stories.
- **Bug** and **Task** are standalone, or part of an epic, a feature or a user story.
- Epics and features only give structure; only user stories, bugs and tasks are assigned to an agent.

![Hierarchy](docs/hierarchy.svg)

Items live in the app's database as drafts. Nothing reaches GitHub until I **manually accept** an item. On acceptance the item becomes a GitHub issue.

**Implemented (work items):** pages for **Epics** (`/epics`), **Features** (`/features`), **User stories** (`/stories`), **Bugs** (`/bugs`) and **Tasks** (`/tasks`) per selected repository, with full CRUD (title, Markdown description, parent, and assignment settings for stories, bugs and tasks). A feature must be part of an epic and a story part of a feature; bugs and tasks can optionally be part of an epic, feature or story. Each item shows its children with buttons to add new ones. Accepting creates a GitHub issue labelled with its type (`epic`, `feature`, `story`, `bug` or `task`; the label is created with a fixed color when missing, and an existing label is left as it is). The body ends with a hidden `<!-- dghsf:{label}:{id} -->` marker. When the parent already has an issue, the new issue becomes its **sub-issue**; when the parent is accepted later, its accepted children become its sub-issues then. Stories, bugs and tasks are then assigned to the Copilot cloud agent (**Accept & assign**); epics and features are not (**Accept & create issue**). Once accepted, the title, description and parent are read-only. Deleting an item only removes it from the app, never the issue, and is refused while it still has children.

**Implemented (templates):** every type has its own templates page (`/epic-templates`, `/feature-templates`, `/story-templates`, `/bug-templates`, `/task-templates`), shared across repositories. New items start with the first template of their type alphabetically.

### 4.4 Labels and issue mapping

- The type of an item is expressed with a **label** (`epic`, `feature`, `story`, `bug`, `task`). These labels are managed automatically: DGHSF creates a missing label when it creates the first issue of that type. I do not use GitHub issue types or a Projects board.
- The hierarchy is kept in the app and mirrored on GitHub with **sub-issues** (`POST /repos/{owner}/{repo}/issues/{number}/sub_issues` with the child's issue id).
- I can fetch the labels of a repository and **create, update and delete** them from the app.
- When creating an issue I can choose labels manually, or have them assigned automatically. I want to explore a mix of **rules** (item type to label) and **AI suggestions** from DGHSF Copilot.

### 4.5 Lifecycle and changes after acceptance

![Lifecycle](docs/lifecycle.svg)

An item moves from **Draft** to **Accepted** (issue created), then **In progress** (agent assigned), and finally **Done** (issue closed and/or pull request merged).

Accepted items are not edited in place on GitHub. If I change an accepted item, the app automatically creates a **linked "diff" issue** that summarises what changed and links back to the original issue. The original issue stays as it was.

### 4.6 Agent assignment

I can assign GitHub's **Copilot cloud agent** to an issue, choosing:

- a **custom agent** from the repository's `.github/agents`, and
- the **model** for that specific assignment.

The app shows the agent's session status and links to the resulting pull request, and lets me **re-assign or unassign** the agent.

**Verified and implemented:** the REST API supports this (public preview): `POST /repos/{owner}/{repo}/issues/{number}/assignees` with assignee `copilot-swe-agent[bot]` and an `agent_assignment` object (`target_repo`, `base_branch`, `custom_instructions`, `custom_agent`, `model`). The custom agent id is the agent file name without `.agent.md`. A user story, bug or task stores the agent, model (from the managed models list; empty means auto), base branch and custom instructions. On accept, the status goes to **In progress** when GitHub confirms Copilot is assigned. If the assignment fails (for example when the cloud agent isn't enabled for the repository), the item stays **Accepted** with its issue, and **Assign agent** retries. Session status, PR links and unassign are still to do.

### 4.7 Two-way synchronisation

![Sync](docs/sync.svg)

An Azure Function polls GitHub on a CRON schedule (no webhooks), and only for repositories that have actual work in the app. Everything flows back into the app: issue state, labels, comments, linked pull requests and agent sessions. When both sides changed, **the app wins**.

### 4.8 DGHSF Copilot

My own assistant, backed by Microsoft Foundry and the Microsoft Agent Framework, with tools to interact with the application. It works in two modes:

- **In-page assist:** on a CRUD page (for example creating an epic) it fills in or changes the form in the UI. I always decide whether to save.
- **Chat mode:** outside a specific page, it can act directly through tools without UI, such as updating epics, features and stories. **Destructive actions** (deleting, accepting or creating issues) need my confirmation.

Conversations are **persisted** as chat history.

**Implemented (in-page assist):** a generic agent (`AssistantAgentFactory`, Microsoft Agent Framework `ChatClientAgent`) uses an OpenAI-compatible endpoint configured with `OpenAI:Endpoint` (empty = public OpenAI; Foundry/Azure OpenAI resource-root URLs automatically use `/openai/v1/`, while explicit API paths are preserved), `OpenAI:Model` (the deployment name on Azure) and `OpenAI:ApiKey`. It uses the Responses API to support reasoning with function tools; set `OpenAI:UseResponsesApi` to `false` for services that only support Chat Completions. The agent editor and the draft work item editors have a chat panel, with instructions per type (assignment tools only for stories, bugs and tasks). The assistant asks follow-up questions and prefills the form through tools; I still save manually. Chat history isn't persisted yet, and chat mode is still to do.

## 5. Models and cost

I want to list the available models and their cost. What I found:

- The **GitHub Models** catalog is **retired** (fully since July 30, 2026), so it cannot be used.
- I haven't found a stable public API that lists Copilot models with their cost multipliers.
- Still to investigate: whether the Copilot SDK/CLI exposes a model list. (The cloud agent assignment does accept a model; see 4.6.)

**Implemented:** a **Models** page (`/models`) keeps a managed list of models in our own database (EF Core, SQL Server / LocalDB in development). Models are shared across repositories.

- Full CRUD: name, model id (the value written to an agent's `model` key), provider, release status, category, tier/threshold, and USD prices per 1M tokens (input, cached input, cache write, output). Models can be enabled or disabled.
- **Import from GitHub docs**: paste the link to the Copilot "Models and pricing" page (only `https://docs.github.com/...` is allowed). The pricing tables are scraped and shown as a preview (new / price change / unchanged), and the selected rows are imported. Existing models are matched on name + tier; their prices are refreshed, but a changed model id or enabled state is kept. The model id is derived from the name (e.g. "Claude Sonnet 4.6" → `claude-sonnet-4.6`) and can be edited.
- The **agent editor** offers the enabled models in a dropdown that shows the name, provider and input/output cost, plus the long-context pricing. A `model` value that isn't in the list stays visible and is kept.

## 6. Roadmap (MVP order)

1. Login (GitHub OAuth, single-user allow-list) and repository list.
2. Settings page (PAT) and custom agent management per repository.
3. Work items (epics, features, user stories, bugs, tasks) and label management.
4. Accept → GitHub issue creation, including diff-issues.
5. Agent assignment (custom agent and model) and run tracking.
6. Azure Function polling and two-way sync.
7. DGHSF Copilot: in-page assist first, then chat mode with tools.

## 7. Open points

- Exact **PAT permissions** (contents, issues, pull requests, metadata) and a rotation plan.
- ~~Verify that the **assignment API** supports a custom agent and model per assignment.~~ Verified (public preview); see 4.6.
- The model list is scraped from the docs page; check that the derived **model ids** match what Copilot accepts.
- Decide which **Foundry model** to use.
- Decide how **auto-labelling** combines rules and AI suggestions.
