# DGHSF – Gaps and review

> Review of the goal (README, IDEAS.md, copilot-instructions.md) against the implementation, written 2026-11-10. It covers the core flows: auth, the accept workflow, the GitHub MCP scoping, sync, the stores and CI. The test suite wasn't run for this review.

## Short version

The engineering is careful. The duplicate-safe accept with its marker and claim is good, and so is the MCP tool scoping to one repository. But the product has grown sideways instead of forward. The "factory" stops at the moment work leaves it, a lot of effort went into extras, and the docs and code now disagree on basic points.

## What's wrong

1. **US dollar prices are shown as euros.** `ModelPricingScraper` copies GitHub's USD amounts unchanged, and `AiModel.FormatPrice` (`src/Djohnnie.SoftwareFactory.Contracts/AiModels/AiModel.cs:64`) prints them with `€`. The Foundry prices really are in EUR, so one symbol now covers two currencies. Store a currency per price, or label the GitHub prices as $.
2. **The allow-list checks the login, not the account ID.** `Program.cs` compares `ClaimTypes.Name` with `AllowedLogin`. GitHub logins can be renamed and then claimed by someone else. IDEAS.md says "user id/login", so compare the numeric ID (`NameIdentifier`). Also, `appsettings.json` ships `"AllowedLogin": "Djohnnie"`, so the "must be configured" check in `Program.cs` can never fail.
3. **The token is over-privileged.** Sign-in asks for `user` (write access to the profile) only for the personal billing widget, plus full `repo` on every private repo. That token sits in SQL, protected by data-protection keys that are never configured (there's no `AddDataProtection`), so it relies on App Service defaults. A token that broad deserves an explicit, protected key ring.
4. **Images go to a public blob container.** Screenshots from private repos become world-readable URLs that end up in issue bodies and notification emails. Use a private container with SAS links, or let GitHub host the attachments.
5. **Any closed issue counts as done.** `WorkItemStore.ApplyIssueAsync` (`src/Djohnnie.SoftwareFactory.DataAccess.Sql/Stores/WorkItemStore.cs:230`) maps `closed` to `Done` without looking at `state_reason`, so "not planned" and "duplicate" count as finished work in the infographic.
6. **The sync doesn't scale and repeats failures.**
   - It sends one GitHub request per accepted item, forever, including closed items, so the cost only grows.
   - It loads every item of every repo, drafts included, and filters in memory (`WorkItemSyncService`).
   - An issue body longer than `MaxDescriptionLength` fails again every 15 minutes, with only a warning log.
   - Better: one `issues?state=all&since=<last sync>` call per repo, or conditional requests with ETags (304 answers don't count against the rate limit).
7. **The docs contradict the code.**
   - `docs/azure-deployment.md:376` says auth tickets are kept in memory and you sign in again after a restart. `SqlTicketStore` exists to prevent exactly that.
   - IDEAS.md §3 describes an encrypted PAT set on a settings page. The real one is the `GitHub__SyncToken` app setting.
   - IDEAS.md §4.7 says "the app wins". See the next section.

## What's weird

- **The source of truth is backwards.** The vision says the app's database is the source of truth, with "diff issues" when an accepted item changes. In the code, accepted items are read-only and the refresh overwrites title and description from GitHub, so GitHub always wins. That's a legitimate choice, but then accepted work can't be rescoped from the app at all. Pick one model and update IDEAS.md.
- **The layering is heavy for a single-user app.** A call goes Razor → `WorkItemService` (a pure pass-through) → MediatR → handler → `WorkItemWorkflow` → store → EF. On top of that: `Response<T>` wrappers, a Contracts project, Guid plus `SysId` dual keys, and MediatR, which now needs a commercial license key. Each feature is 5–8 files. It's consistent, but it's ceremony paid for on every change.
- **There are a lot of internal agents.** There's a Copilot per page, per work item type and per template type, each with its own prompt, model, mandatory test-before-save and cost tracking. The Chat Suggestions agent makes an LLM call just to render 3 chips when a form opens, and again after every edit. Spend is tracked in detail but there's no budget or cap anywhere.
- **`.github/copilot-instructions.md` reads like a changelog.** It's about 50 very dense lines of implementation trivia ("4 at a time", "3 seconds", "cached 10 minutes") that restate the code. It will go stale and it costs context on every Copilot run. Keep conventions and pointers there; the code already holds the rest.
- **`WorkItemEditor.razor` is 956 lines with 11 injected services.** That's a god component.
- **The test layout is odd.** A single project called `Web.Tests` holds store, workflow, scraper and UI tests. Most use the EF InMemory provider (29 setups), which can't check the concurrency tokens the accept claim depends on. The real-SQL tests exist but only cover part of that.
- **Small ones:** the enum says `Done` but the UI says "Closed". Agents and instructions are committed straight to the default branch, so this breaks as soon as a repo has branch protection.

## What's missing

1. **Anything that happens after the handoff.** This is the big one. After **Accept & assign**, the item sits at In progress until the issue closes. There's no:
   - PR link
   - agent session state
   - "agent finished, needs your review"
   - failed session
   - unassign or re-assign

   IDEAS.md §4.6 admits this. Meanwhile dictation, two kinds of infographics, billing, code-review settings and breadcrumbs got built first. Without this loop it's a GitHub issue writer, not a software factory.
2. **A GitHub App instead of OAuth plus a PAT.** That one change fixes several items above:
   - fine-grained per-repo permissions instead of `user` and `repo`
   - installation tokens for the sync, with no PAT to expire unnoticed
   - webhooks instead of polling
   - higher rate limits
3. **Monitoring.** There's no Application Insights or OpenTelemetry. When the sync token expires, the function fails quietly in logs nobody reads.
4. **Features IDEAS.md promises that don't exist yet:** label management (§4.4), diff issues (§4.5), and chat mode with confirmed destructive actions (§4.8).
5. **A spend cap for the internal agents**, since every request's cost is already tracked.
6. **Backup or export.** Drafts exist only in the SQL database, and that database is the entire planning system.

## Suggested next steps

1. PR and agent session tracking, so the loop closes after the handoff.
2. Move to a GitHub App. Together with step 1, that covers the main missing feature, most of the security points and the sync design.
