---
description: Open the one release PR (release/X.Y.Z -> main), or a hotfix PR into main, then run the docs check, standards check and one code review, and merge on Amr's OK
---

# /pull_request

Features are pushed straight to the active `release/X.Y.Z` with no feature PR (see `/begin_work`). This command opens the only routine PR: the release PR from `release/X.Y.Z` into `main`. Every pre-release check and the code review run here, once. Merging it publishes `vX.Y.Z` (`release.yaml`).

## Arguments

- none — release PR for the active `release/X.Y.Z`.
- `--hotfix` — PR for the current hotfix branch (off `main`) into `main`.

## 1. Check the branch

- **Release:** exactly one `origin/release/*` is open. Its `VERSION.TXT` matches the branch name and is above `main`'s, and `git ls-remote --tags origin v<version>` finds nothing.
- **Hotfix:** the branch is off `main` and bumps the patch version in `VERSION.TXT`. Without a bump the merge builds but publishes nothing.

## 2. Open the PR

```bash
gh pr create --base main --head <branch> --title "Release X.Y.Z" --body "..."
```

Hotfix title: `Hotfix X.Y.Z: short summary`. Body:

```
## Summary
<one bullet per feature commit on the branch: "#NN: summary">

## Test plan
<the release's sections from docs/TEST_PLAN.md, copied as checkboxes, plus the standing checks>

## Checks
- [ ] Release workflow builds every platform on this PR
- [ ] /docs_sync_check
- [ ] /standards_check
- [ ] /review_pr

---
Generated with [Claude Code](https://claude.com/claude-code)
```

## 3. Run the checks, once

1. Wait for the **Release** workflow's build jobs on the PR to pass. Its artifacts are the builds Amr runs the test plan on.
2. Run `/docs_sync_check` and `/standards_check` against the PR's diff (`git diff origin/main...<branch>`).
3. Run `/review_pr <number>`.

Walk Amr through the findings. Fix what Amr wants fixed with more commits on the release branch (or the hotfix branch), never on `main`.

## 4. Merge on Amr's OK

Merge only when Amr says so, with a merge commit (`gh pr merge <number> --merge`). The push to `main` builds, tags `vX.Y.Z` and publishes the GitHub release.

After a release merges: clear the release's sections from `docs/TEST_PLAN.md` on the next `release/X.Y.Z` when `/start_release` opens it.

After a hotfix merges: merge `main` back into the active `release/X.Y.Z` (`git merge --no-edit origin/main`) and push it, so the fix isn't lost.

