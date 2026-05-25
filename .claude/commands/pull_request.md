---
description: Open a draft PR for the current branch, run the self-review with checkpoints, then offer to mark it ready
---

# /pull_request

Open or update a **draft** PR for the current branch, run the self-review with checkpoints between each section, then offer to mark it ready for review.

Work through every section in order. After each section, stop at the **CHECKPOINT** and wait for the user to confirm before continuing. Never auto-fix a standards violation without confirming first.

## Arguments

`$ARGUMENTS` may contain:

- `--testing "description"` — manual-test notes the user has already performed. These flow into the *Testing performed* block of the PR body.
- `--skip-draft` — open the PR as ready-for-review immediately and skip the self-review walkthrough. Use only when the user has run the checks elsewhere or is intentionally bypassing the gate.

If no flags are passed, proceed with the default draft-then-walkthrough flow.

## 1. Branch validation

- Confirm the current branch is **not** `main`. PRs target `main`.
- Confirm the branch has commits ahead of `main`: `git rev-list --count main..HEAD`. If zero, abort — nothing to PR.

**CHECKPOINT — confirm the branch is ready for a PR.** Wait for the user.

## 2. PR scope

A PR should cover **one concern**: a single feature, a single bug fix, a batch of related bug fixes (same subsystem), a self-contained refactor, or a self-contained doc update. Mixed-concern PRs create review burden — surface candidate splits and let the user decide.

Run these against `git diff --name-only $(git merge-base HEAD origin/main)...HEAD` plus `git diff --shortstat`:

- **File-count signal**: more than 8 files touched outside a deliberate cross-cutting change (project-wide rename, dep bump) -> flag for scope review.
- **Net-line signal**: more than ~400 net lines changed, excluding generated files -> flag for scope review.
- **Concern-mixing signal**: scan paths and patches. If any of these are mixed in one diff, list each cluster with its file list and ask the user to confirm or split:
  - feature work in one project + unrelated bug fix in another
  - refactor of existing code + a new feature built on top of it
  - doc rewrite unrelated to the code change
  - "drive-by" formatting edits in files not otherwise needed for the work
- **Title sanity**: if the working title needs "and" or a semicolon to describe both concerns, that's a split signal.

Surface clusters; do not auto-split. If the user confirms the PR is correctly scoped despite a signal trip (e.g. intentional cross-cutting refactor), call out the rationale in the PR body so reviewers know what to focus on.

**CHECKPOINT — confirm scope (or explain why a flagged signal is intentional) before opening the draft PR.** Wait for the user.

## 3. Open the draft PR

- Check if a PR already exists for this branch: `gh pr view --json url,body,title,isDraft,number`.
  - **No PR exists**: build title and body per *PR title and body* below, then `gh pr create --base main --draft --title "..." --body "..."`.
  - **Draft PR exists**: refresh body (preserve any `- [x]` ticks the user has already applied) and title if needed via `gh pr edit <number>`.
  - **Non-draft PR exists**: pause and ask the user whether to operate on it as-is (no draft revert) or convert to draft (`gh pr ready --undo <number>`). Default: operate as-is.
- Print the PR URL.

If `--skip-draft` was passed, open with `gh pr create` (no `--draft`) and skip directly to step 5; the user takes responsibility for the self-review.

**CHECKPOINT — ready to start the self-review walkthrough?** Wait for the user.

## 4. Self-review walkthrough

Run the two focused checks one at a time. After each check, present findings grouped by severity and stop at the section's CHECKPOINT.

### 4a. Docs sync check
Follow the procedure in `.claude/commands/docs_sync_check.md`. Present findings grouped by severity and end with the verdict line.

**CHECKPOINT — ready for the next check?** Wait for the user.

### 4b. Standards spot-check
Follow the procedure in `.claude/commands/standards_check.md`. Present findings grouped by severity and end with the verdict line.

**CHECKPOINT — self-review complete.** Summarize the combined verdict (worst-case of the two). Ask the user one of:

- *"Draft fixes for the Critical/Major findings before marking ready?"*
- *"Mark ready as-is (accepting open findings as known)?"*
- *"Pause — leave the PR as draft, I'll come back to it?"*

Wait for the answer.

## 5. Mark ready (if requested)

When the user is satisfied and explicitly asks to mark ready:

- `gh pr ready <number>`
- Confirm the PR is no longer marked draft.
- Print the final URL.

If the user paused or chose to draft fixes, leave the PR as draft and stop — the user will come back later.

## PR title and body

### Title
- Under 70 chars, imperative, no trailing period.
- Match the repo prefix style when appropriate: `unp4k:`, `unforge:`, `unp4k.fs:`, `sc.gamedata:`, `SharpZipLib:`, `ci:`, `docs:`, etc.
- If the work was launched via `/begin_work` and the GitHub issue number is known, prefix with `[#NN]`.

### Body

Use a HEREDOC to preserve formatting. When updating an existing PR, fetch the current body, **preserve any `- [x]` ticks** the human has already applied, and refresh items whose surrounding context has changed.

```
## Summary
<1-3 bullets describing what changed and why. If linked to an issue, cite it: "Closes #42.">

## Testing Checklist
- [ ] PR is scoped to one concern
- [ ] `dotnet build unp4k.sln -c Release` succeeds
- [ ] Affected executable exercised against a real `.p4k` or `.dcb` file (note which: unp4k / unp4k.fs / unforge.cli / sc.gamedata)
- [ ] `spec.md` reviewed for drift if `src/unforge/` was touched
- [ ] `README.md` CLI usage reviewed for drift if argument parsing was touched
- [ ] AES key still in sync across all three files if any key reference was touched
- [ ] No NuGet SharpZipLib references (local fork only)
- [ ] New `.csproj` files multi-target `net6.0;net8.0;net10.0` and use `DebugType=none` in Release
- [ ] Tab indentation for C#, 2-space for `.csproj`/XML per `.editorconfig`
- [ ] Linux build not broken (no Dokan deps in cross-platform code, publish flags respected)

## Testing performed
<!-- If --testing "..." was passed, paste that description here verbatim. Otherwise: "Per the Testing Checklist above." -->

---
Generated with [Claude Code](https://claude.com/claude-code)
```

Fetch `<github_username>` via `gh api user --jq '.login'`. If the call fails, omit the attribution footer.
