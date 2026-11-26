---
description: Start work on a GitHub issue — load context, grill it until ready, build it, and push one squashed commit straight to the active release/X.Y.Z
---

# /begin_work

Take a GitHub issue from idea to a squashed commit on the active `release/X.Y.Z`, following the Osiris DevWorks pipeline in `CLAUDE.md`. There is no feature PR: checks and the code review run once, on the release PR (`/pull_request`).

## Arguments

`$ARGUMENTS` — a GitHub issue URL (`https://github.com/Osiris-DevWorks/odw-fast-unp4k/issues/NN`) or short form (`#NN`). Required.

If the argument is missing or malformed, abort with: *"/begin_work requires: <issue-url-or-#NN>"*

## 1. Load the issue

```bash
gh issue view <NN> --json number,title,body,labels,state,url,comments
```

Summarize it in a few lines: what is asked, which tool it touches (`unp4k`, `unp4k.fs`, `unforge` / `unforge.cli`, `sc.gamedata`, the SharpZipLib fork), and anything unclear.

## 2. Grill it until it is ready

Ask every open question as a multiple-choice decision card (`AskUserQuestion` in the CLI, `request_decision` in a project thread) with a recommended option, never as questions in prose. A few cards at a time is fine. Typical questions: CLI flag names and defaults, output format, whether `spec.md` changes, which platforms it must work on.

These tools have no UI, so there is normally no mockup. If the change alters console output a user reads, show the before and after text on a card.

Grilling is done when Amr says it is ready. That sign-off is the approval: from here, build without plan approvals or code check-ins. Ask again only if the work hits a choice grilling did not settle.

## 3. Find the integration branch

```bash
git fetch origin
git branch -r --list 'origin/release/*'
```

Use the single open `release/X.Y.Z` (the one whose version is above `main`'s `VERSION.TXT`). If there is none, stop and tell Amr to run `/start_release` first. Work on a local branch off it (`git checkout -b work/<NN>-<slug> origin/release/X.Y.Z`); only the squashed result is pushed.

## 4. Build it

- Follow `CLAUDE.md` and `.editorconfig` (tabs for C#).
- If `src/unforge/` changes the binary reader, update `spec.md` in the same change.
- If CLI arguments change, update `README.md`.

## 5. Check it

- `dotnet build unp4k.sln -c Release` must succeed.
- Run the changed tool against a real `.p4k` or `.dcb` when one is available; say plainly when it isn't.
- Run `/standards_check` and `/docs_sync_check` on the diff and fix what they find.

## 6. Add the test plan section

Add a `### #NN: short name` section under `## Next release` in `docs/TEST_PLAN.md`, with concrete pass/fail items (the command to run and what should happen).

## 7. Push one squashed commit

```bash
git checkout -B release/X.Y.Z origin/release/X.Y.Z
git merge --squash work/<NN>-<slug>
git commit -m "#NN: short summary" -m "Fixes #NN"
git push origin release/X.Y.Z
```

Use `Fixes #NN` only when the commit closes the issue (GitHub closes it when the release PR merges into `main`). Never force-push the release branch. If the push is refused because the branch moved, rebase the squashed commit onto the new tip and push again.

Report the commit link and the test plan section to Amr.
