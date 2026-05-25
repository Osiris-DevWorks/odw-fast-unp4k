---
description: Start work on a GitHub issue — load context, open a feature branch off main, then plan and implement step-by-step with check-ins
---

# /begin_work

Bootstrap a work session for a GitHub issue. Creates a feature branch off `main`, then drives the work in chunks with checkpoints between each stage so the user stays in the loop.

Work through every step in order. After each step, stop at the **CHECKPOINT** and wait for the user before proceeding.

## Arguments

`$ARGUMENTS` — a GitHub issue URL (`https://github.com/Osiris-DevWorks/odw-fast-unp4k/issues/NN`) or short form (`#NN`). Required.

If the argument is missing or malformed, abort with: *"/begin_work requires: <issue-url-or-#NN>"*

## 1. Fetch and summarize the issue

```bash
gh issue view <NN> --json number,title,body,labels,state,url
```

- If `state` is `closed`, warn the user.
- Present a short summary: number, title, state, labels, and a one-paragraph summary of the body.

**CHECKPOINT — confirm the issue summary captures the work, or correct it.** Wait for the user.

## 2. Ensure clean main

- Confirm the current branch is `main` or that there are no uncommitted changes. If the working tree is dirty, abort and list the offending files.
- If not on `main`: `git checkout main`.
- Best-effort fetch: `git fetch origin main`. If the fetch succeeds, check `git rev-list --count main..origin/main`. If > 0, warn: *"Local main is behind origin/main."* If the fetch fails (offline), warn but proceed.

**CHECKPOINT — confirm main is ready.** Wait for the user.

## 3. Create the feature branch

- Slug the issue title: lowercase, replace non-alphanumerics with `-`, collapse repeats, trim to ~40 chars.
- Branch name: `feature/<NN>-<slug>` (example: `feature/5-parallel-dataforge-reads`).
- Create it: `git checkout -b feature/<NN>-<slug>` from `main`.

Report:
- `Issue: #<NN> — <title>` (with URL)
- `Feature branch: feature/<NN>-<slug>`

**CHECKPOINT — ready to plan the work?** Wait for the user.

## 4. Plan the work

Before writing any code:

1. Identify which projects this issue touches. Read the `CLAUDE.md` Architecture section to understand the layer dependencies: `ICSharpCode.SharpZipLib` -> `unforge` -> executables (`unp4k`, `unp4k.fs`, `unforge.cli`, `sc.gamedata`).
2. Propose the implementation in **manageable chunks**. One concept at a time. For each chunk, present:
   - What you'll change and why.
   - The file(s) involved (with paths).
   - Any trade-offs the user should decide on.
   - Whether `spec.md` or `README.md` needs a corresponding update.
   - **CHECKPOINT — wait for the user to confirm this chunk before moving to the next.**
3. After every chunk is confirmed individually, ask the user to approve the full plan as a whole.

**CHECKPOINT — full plan approved?** Wait for the user.

## 5. Implement step-by-step

Once the plan is approved:

- Implement one chunk at a time, in the order from the plan.
- After each chunk, show the diff for that chunk and pause.
- **CHECKPOINT — wait for the user to confirm this chunk before moving to the next.**
- After any chunk that touches `src/unforge/` types, verify `spec.md` consistency. After any chunk that touches CLI argument parsing, verify `README.md` consistency.

## 6. Final walkthrough

When all chunks are implemented:

1. Run `/standards_check` and `/docs_sync_check` in sequence. Walk the user through findings.
2. Give a step-by-step walkthrough of everything that changed:
   - Files modified, grouped by project (`SharpZipLib` / `unforge` / `unp4k` / `unp4k.fs` / `unforge.cli` / `sc.gamedata`).
   - Why each change was made (link back to the plan chunk).
   - Anything left intentionally untouched and why.
3. Verify the solution builds: `dotnet build unp4k.sln -c Release`.

**CHECKPOINT — ready to run `/pull_request`?** Wait for the user.

## Notes

- The feature branch is local-only. The user pushes when ready.
- This command does not open a PR. `/pull_request` does.
- If the issue requires an exploratory spike that will not ship, work on a personal branch instead.
