---
description: Review a single GitHub PR with checkpoints between summary, file-by-file walk, and final verdict
---

# /review_pr

Drive a structured review of one PR with human-in-the-loop checkpoints. Files are reviewed in dependency order so issues in foundational layers get caught before reviewing code that depends on them.

Work through every step in order. After each step, stop at the **CHECKPOINT** and wait for the user before continuing.

## Argument

`$ARGUMENTS` — required, one of:

- A PR number (e.g. `5`)
- A PR URL (e.g. `https://github.com/Osiris-DevWorks/odw-fast-unp4k/pull/5`)
- The literal token `current` — resolve the PR for the current branch

If missing or malformed, abort with: */review_pr requires a PR number, URL, or `current`.*

## 1. Gather PR context

Resolve the PR number, then fetch in parallel:

- `gh pr view <N> --json title,body,state,baseRefName,headRefName,author,changedFiles,files,statusCheckRollup,url,isDraft,labels`
- `gh pr diff <N>` — full unified diff (cache this; you'll slice per-file from it later)
- `gh api repos/Osiris-DevWorks/odw-fast-unp4k/pulls/<N>/comments` — code-review comments
- `gh api repos/Osiris-DevWorks/odw-fast-unp4k/issues/<N>/comments` — general PR conversation
- If the title or body references a GitHub issue (`#NN` or `closes #NN`), fetch it: `gh issue view <NN> --json title,body,labels,state` for context.

## 2. Summarize the PR

Present a concise summary:

- **What** — scope in plain language.
- **Why** — linked GitHub issue summary if present; otherwise note "no linked issue."
- **Base / head** — target branch and source branch. **Flag if base is not `main`** — PRs target `main` in this repo.
- **CI status** — pass / fail / pending (from `statusCheckRollup`).
- **Existing comments** — short summary of any prior review comments or conversation.
- **Files changed (N)** — list each file with a one-line description of what it does.

**CHECKPOINT — does this summary look right? Ready to start the file-by-file review?** Wait for the user.

## 3. File-by-file review

Order the changed files by **layer dependency direction** — most upstream layer first, downstream last. Issues in foundational layers get caught before reviewing code that depends on them.

Layer order (innermost -> outermost):

1. `src/ICSharpCode.SharpZipLib/` — forked Zip/AES library
2. `src/Zstd.Net/` — Zstd bindings (currently unused, flag if newly wired up)
3. `src/unforge/` — DataForge + CryXML library
4. `src/unp4k/` — console extractor
5. `src/unp4k.fs/` — Dokan virtual filesystem (Windows-only)
6. `src/unforge.cli/` — unforge CLI wrapper
7. `src/sc.gamedata/` — game data extraction tool
8. Root / config — `CLAUDE.md`, `spec.md`, `README.md`, `.github/`, `.editorconfig`, `.csproj` files

For each file in that order:

1. Slice the diff for this file from the cached `gh pr diff` output.
2. Review for:
   - **Correctness and logic** — does the change do what the PR description claims?
   - **Project standards** (full list in `.claude/commands/standards_check.md`):
     - AES key sync across all three executables
     - SharpZipLib local fork vs NuGet package references
     - Zstd.Net not wired up — flag assumptions otherwise
     - `DebugType=none` in Release
     - Target framework consistency (`net6.0;net8.0;net10.0`)
     - Linux build compatibility (no Dokan in cross-platform code)
     - `Max*` recursion guard integrity on `DataForge`
     - Tab indentation for C#, 2-space for XML/csproj
   - **Spec/doc drift** — if `src/unforge/` types changed, does `spec.md` still match? If CLI args changed, does `README.md` still match?
   - **DRY** — copy-paste blocks >=5 lines, magic literals in 2+ places, near-duplicate functions.
3. Present the review for this file:
   - Header is the file path.
   - For each issue: relevant line(s), a one-line description, a suggested fix, and a **severity tag** (Critical / Major / Minor) per `/standards_check`'s calibration.
   - If no issues: say so briefly.
   - End with a one-line **file verdict**: **Clean / Minor issues / Needs attention**.

**CHECKPOINT — ready to move to the next file?** Wait for the user. If this is the last file, ask instead: *"Ready for the final verdict?"*

## 4. Final verdict

After every file is reviewed:

- **Overall recommendation** — one of:
  - **Approve** (`gh pr review <N> --approve`)
  - **Request Changes** (`gh pr review <N> --request-changes`)
  - **Comment** (`gh pr review <N> --comment`)
- **Issue acceptance check** — for each acceptance criterion or task in the linked GitHub issue, state whether the diff satisfies it. If no issue is linked, note this and skip the AC check.
- **Summary of all issues found**, grouped by severity:
  - **Critical** (blocking merge) — what they are, where, fix hint.
  - **Major** (should fix before merge).
  - **Minor** (consider).
- **Action items** as a checklist the PR author can work through.

Include the full PR URL at the end so the user can click through.

**CHECKPOINT — pause and ask:** *"Post the review now (`gh pr review` with the chosen recommendation), draft inline comments for specific findings before posting, or just print the summary and stop?"* Wait for the user. **Do not post a review or any comments without explicit confirmation.**

## Tone

- Be constructive and educational.
- Assume positive intent.
- Explain *why* behind each suggestion — link to the relevant convention when it documents the rule being violated.
- Offer specific examples, not vague principles.
- Balance criticism with notes on what was done well.
- Focus on the code, not the person.
