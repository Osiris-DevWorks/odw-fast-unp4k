# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Osiris DevWorks development pipeline (universal)

This section is identical in every Osiris DevWorks repo's CLAUDE.md. Change it in all of them at once. The line just below it says how this repo applies it. Where an older section of this file conflicts with it, this section wins.

The goal is little friction and few GitHub Actions runs: checks and code review run once per release, not once per feature.

1. **Grill the idea.** When Amr brings a new idea, ask questions until it is ready to build. Ask every question as a multiple-choice decision card (`request_decision`, or `AskUserQuestion` in the CLI) with a recommended option, never as questions in prose. A few cards at a time is fine.
2. **Mock up any UI.** When a feature adds or changes UI, show an HTML Artifact mockup during grilling. Build it from the app's real colors, fonts, spacing and layout, read from the source, and show the current screen next to the changed one. Grilling isn't done until Amr approves the mockup.
3. **Grilling sign-off is the approval.** Once Amr says the idea is ready, build it without further check-ins: no step-by-step plan approvals, no code-block reviews, and no commit-message review. Ask again only if the work hits a choice that grilling did not settle.
4. **Push straight to the integration branch, with no feature PR.** Run the repo's tests, lint and type check in your own session. Update this repo's test plan, if it has one, with concrete pass/fail items for the feature. Then push **one squashed commit per feature** to the integration branch, with a message like `#123: short summary` (`Fixes #123` in the body when it closes the issue). A feature branch is fine while you work; only the squashed result lands. Never force-push the integration branch.
5. **Release: one PR, all checks, one review.** When Amr calls a release, open one PR from the integration branch to production. Every pre-release check and the code review run on that PR, once. Fix what they find with more commits on the integration branch. Amr runs the test plan (on preview builds or staging) and merges, or tells you to merge.
6. **Hotfixes.** For an urgent production fix, branch off production, open a PR straight to production (full checks and review run on it), and once it merges, merge production back into the integration branch.
7. **Outside PRs.** Contributor and Dependabot PRs get CI only, with no Claude review bot. Dependabot is grouped and runs monthly.
8. **Workflow triggers.** Keep workflows off feature work: no `push` triggers on integration branches except where a deploy needs one, and the Claude review workflow runs only on PRs into production. Don't add a workflow that runs on every push without Amr's OK.

**How this repo applies it: command-line tools.** The integration branch is the active `release/X.Y.Z` (opened off `main` by `/start_release`, named for the version it will ship) and production is `main`. `VERSION.TXT` is the version: `/start_release` bumps it, and merging the release PR into `main` makes `release.yaml` build every platform, tag `vX.Y.Z` and publish the GitHub release (a push whose version is already tagged builds only). There are no automated tests, so "run the tests" means `dotnet build unp4k.sln -c Release` and running the changed tool against a real `.p4k` or `.dcb`. The test plan is `docs/TEST_PLAN.md`: each feature push adds its own `### #issue: short name` section of pass/fail items. Amr runs it on the build artifacts the release PR's **Release** workflow uploads, before it merges. The code review is a `/review_pr` pass on that PR, run by `/pull_request`.

## Build / Run

This is a .NET solution. All projects multi-target `net6.0;net8.0;net10.0`; the release pipeline (`.github/workflows/release.yaml`) builds against `net10.0`. There are no automated tests in this repo.

```pwsh
dotnet restore
dotnet build unp4k.sln -c Release
# Single-file self-contained publish (matches CI):
dotnet publish src\unp4k\unp4k.csproj          -c Release -r win-x64 -f net10.0
dotnet publish src\unp4k.fs\unp4k.fs.csproj    -c Release -r win-x64 -f net10.0
dotnet publish src\unforge.cli\unforge.cli.csproj -c Release -r win-x64 -f net10.0
# sc.gamedata builds but is not shipped by CI (dev/extraction tool only):
dotnet publish src\sc.gamedata\sc.gamedata.csproj -c Release -r win-x64 -f net10.0
```

Linux builds **must** pass `--no-self-contained -p:UseAppHost=false -p:PublishSingleFile=false` and skip `unp4k.fs` (Dokan is Windows-only) — see the matrix in `.github/workflows/release.yaml`.

`appveyor.yml` is legacy (targets `net47`, `master` branch, AppVeyor). The active CI is GitHub Actions: `release.yaml` builds on PRs into `main` and `release/**` and publishes a release on a push to `main` whose `VERSION.TXT` version has no tag yet.

## Architecture

Seven projects, layered roughly: SharpZipLib (fork) → unforge (library) → unp4k / unp4k.fs / unforge.cli / sc.gamedata (executables).

- **`src/unp4k`** — Console extractor. Opens a `.p4k` as an encrypted Zip via the SharpZipLib fork and streams entries to disk under the cwd. Filter is a substring match, not a glob (`*.ext` is special-cased to mean `.ext`). Catches per-entry exceptions and prints each one to the console, and deletes the file it had started, so a later run tries it again (a delete that fails is reported). It sends nothing over the network. It lists the matching entries that are not on disk yet, prints `Found N files to extract.`, then extracts on every core through the one `ZipFile` it opened. That is safe because the SharpZipLib fork's `ZipFile` locks the file stream for each read (`TestLocalHeader`, `PartialInputStream`), so keep those locks. Its project turns on server GC with DATAS for 64-bit net8.0 and later builds.
- **`src/unp4k.fs`** — Mounts a `.p4k` or `.dcb` as a read-only virtual drive via Dokan (`DokanNet` 2.3.0.3). Builds an in-memory `VirtualNode` tree once at startup; `CompressedFileSystem` walks the zip entries (and recurses into any `.dcb` it finds), `DataForgeFileSystem` walks DataForge record paths. CryXML and DataForge records are converted to XML lazily on read via `GetContent` lambdas. The interactive console adjusts `DataForge.MaxReferenceDepth` / `MaxPointerDepth` / `MaxNodes` (statics on `unforge.DataForge`) and clears the VFS cache on change. **Windows-only** — Dokan must be installed on the host.
- **`src/unforge`** — Library. Two formats live here:
  - **DataForge** (`DataForge.cs`, `DataForgeTypeReader.cs`, `ComplexTypes/`, `SimpleTypes/`) — bespoke binary DB found inside `.p4k` as `game.dcb`. The on-disk layout (header, definition tables, value arrays, two string tables) is documented in `spec.md` at the repo root; consult it before touching the reader. `FileVersion` and `IsLegacy` gate field widths and which tables exist. Static `Max*` knobs guard against recursion blowups when records reference each other. `Save` reads records on every core, each thread through its own `Fork()` (its own stream position, `RecordStack` and `StructStack`, everything else shared). Every fork shares the definitions, so their read methods take the reader to read through as a parameter, and `Save` fills their lazy fields (`FillLazyFields`) before the threads start, so the threads only read them. Reading the stream or using the stacks through a definition's own `StreamReader` (the DataForge that loaded it) from a fork would race the other threads. Lookups in the preloaded tables and string buffers are safe from any thread. `ReadRecordByPathAsXml` keeps its lock because `unp4k.fs` calls it from Dokan's worker threads on one shared DataForge (`sc.gamedata` calls it from one thread).
  - **CryXML** (`CryXmlB/`) — CryEngine's serialized XML; `CryXmlSerializer.ReadFile` returns a real `XmlDocument`.
- **`src/unforge.cli`** — Wraps `unforge` as a CLI. `Smelter` dispatches by extension: `.dcb` → `DataForge.Save(...xml)`, anything else → `CryXmlSerializer.ReadFile`, saved as `<name>.xml` over any file of that name (`Smelt(path, overwrite: false)` would keep the original as `.raw`, but the CLI always overwrites). After the infile it takes any number of `--exclude <record path>` and `--exclude-file <file>` pairs, in any order, which leave out of a `.dcb` the records at or under those paths (`DataForge.Save(filename, excludePaths)`, whole path segments, ignoring case). The file lists one path a line, skipping blank lines and lines starting with `#`. Any other argument there prints the usage and exits 1, while `-h`, `--help`, `-?` or `/?` as the first argument prints it and exits 0. An infile that does not exist, or a single infile that cannot be converted, prints a line starting `Error converting` and exits 1. In folder mode the run carries on past files it cannot convert. Its project turns on server GC with DATAS for 64-bit net8.0 and later builds, because under the default workstation GC `Save`'s threads spent most of the run waiting on collections. DATAS needs a 64-bit process on .NET 8 or later, so net6.0 and 32-bit builds keep workstation GC.
- **`src/sc.gamedata`** — CLI extraction tool that reads a `.p4k` (streaming `Game2.dcb` into memory via the SharpZipLib fork) or a bare `.dcb`, and emits a `game_data.json` shaped for the sibling `battlestations` project. Unlike `unforge.cli`, it reads DataForge records in-memory via `DataForge.ReadRecordByPathAsXml()` with no disk-XML round-trip. Accepts `--overlay <previous-game_data.json>` to copy physics fields from a prior build (useful for PTU where per-ship physics aren't yet available). **Not a release artifact** — builds with the solution but is not published by CI.
- **`src/ICSharpCode.SharpZipLib`** — Local fork, **not** the NuGet package. The fork adds Star Citizen's AES decryption to the Zip reader; `ZipFile.Key` takes the 16-byte key. Three executables hardcode the same key (`unp4k/Program.cs:14`, `unp4k.fs/Program.cs:175`, `sc.gamedata/Program.cs:24-28`) — keep them in sync if it ever rotates.
- **`src/Zstd.Net`** — Zstd bindings. In the solution but **not currently referenced** by any executable; SC's ZSTD entries are decoded through the SharpZipLib fork. Don't assume it's wired up.

## Conventions

- `.editorconfig` mandates **tabs** with `indent_size = 4` for C# (2 for `.csproj`/XML). Match the existing tab-indented style.
- `DebugType=none` in Release on every project — don't expect PDBs in release output; the CI scripts also explicitly strip any that slip through.

## Versioning and release

`VERSION.TXT` (`X.Y.Z`) is the only version. It starts at 4.0.83, the last tag the old scheme made from the workflow run number, so adopting it published nothing; the next release is the first one cut by `/start_release`. Bump **patch** for fixes, **minor** for new features or a new tool option, **major** for a breaking CLI or output change. `/start_release major|minor|patch` opens `release/X.Y.Z` off `main` with the bump; features land there as one squashed commit each; `/pull_request` opens the one release PR into `main`, and the merge publishes `vX.Y.Z`. A hotfix branches off `main`, bumps the patch version in its own PR into `main` (an unbumped hotfix builds but publishes nothing), and `main` is then merged back into the active release branch.

Smart Citizen bundles `unp4k.exe` and `unforge.exe` built from this repo (`../smart-citizen/src/utils/CLAUDE.md` → `pak_extractor.py` names the commits); a release that changes either tool is worth noting there.
