---
description: Open the next release/X.Y.Z branch off main and bump VERSION.TXT (major|minor|patch)
---

# /start_release

Open the integration branch for the next release.

## Argument

`$ARGUMENTS` — `major`, `minor` or `patch`. Required. Patch is for fixes only; minor for new features or tool options; major for a breaking CLI or output change.

## Steps

1. `git fetch origin main` and read `VERSION.TXT` on `origin/main` (`X.Y.Z`).
2. Work out the next version from the argument.
3. Check that no `release/*` branch is still open (`git branch -r --list 'origin/release/*'`) and that the tag `v<next>` does not exist (`git ls-remote --tags origin v<next>`). If either is true, stop and tell Amr.
4. Create the branch and bump the version:

   ```bash
   git checkout -b release/<next> origin/main
   echo "<next>" > VERSION.TXT
   git commit -am "Start release <next>"
   git push -u origin release/<next>
   ```

5. Tell Amr the branch is open. Features now land there through `/begin_work`.
