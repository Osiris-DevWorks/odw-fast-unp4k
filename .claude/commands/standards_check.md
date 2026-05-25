---
description: Lint the current diff against this repo's documented standards
---

# /standards_check

Audit the working-tree diff (staged + unstaged) against the project's documented standards. Report findings with file:line references and severity. **Do not auto-fix without confirmation.**

Each check carries a severity:

- **Critical** — breaks correctness, build, or release-mode invariants. Must fix before merge.
- **Major** — wrong but won't immediately break. Should fix before merge.
- **Minor** — stylistic or "consider" notes. Optional.

## Checks

- **AES key sync** *(Critical)*: the Star Citizen AES key is hardcoded in three files (`src/unp4k/Program.cs:22`, `src/unp4k.fs/Program.cs:175`, `src/sc.gamedata/Program.cs:24-28`). If the diff touches any of them, confirm all three remain identical. A mismatch silently breaks decryption for the out-of-sync tool.
- **SharpZipLib fork** *(Critical)*: any new `<PackageReference Include="SharpZipLib"` or `<PackageReference Include="ICSharpCode.SharpZipLib"` referencing a NuGet package instead of the local project reference. This repo uses a local fork (`src/ICSharpCode.SharpZipLib`) with SC-specific AES decryption — the NuGet package lacks it.
- **Zstd.Net assumptions** *(Critical)*: new code that references `Zstd.Net` as if it's wired into the extraction pipeline. It's in the solution but **not currently referenced** by any executable — SC's ZSTD entries are decoded through the SharpZipLib fork.
- **DataForge spec compliance** *(Major)*: changes to `src/unforge/DataForge.cs`, `ComplexTypes/`, or `SimpleTypes/` that diverge from the layout documented in `spec.md` at the repo root without updating the spec. The spec and code must stay in sync.
- **DebugType in Release** *(Major)*: new or modified `.csproj` files that set `DebugType` to anything other than `none` in Release configuration. All projects use `DebugType=none` in Release — the CI also strips any PDBs that slip through.
- **Target framework consistency** *(Major)*: new `.csproj` files that don't multi-target `net6.0;net8.0;net10.0` like all other projects in the solution.
- **Linux build compatibility** *(Major)*: changes to `unp4k.fs` that would affect Linux builds — `unp4k.fs` is Windows-only (Dokan) and is excluded from Linux CI. New executables must consider the Linux publish flags (`--no-self-contained -p:UseAppHost=false -p:PublishSingleFile=false`).
- **Max* recursion guards** *(Major)*: changes to the static `MaxReferenceDepth`, `MaxPointerDepth`, or `MaxNodes` properties on `DataForge`, or new code that bypasses them. These guard against stack overflows when DataForge records reference each other cyclically.
- **Indentation** *(Minor)*: `.editorconfig` mandates **tabs** with `indent_size = 4` for C# and 2 for `.csproj`/XML. Flag space-indented C# or tab-indented XML.

## Code duplication / DRY

Surface candidates:

- **Copy-paste blocks** *(Major)*: 5+ line blocks in the diff that appear verbatim (or near-verbatim) elsewhere in the codebase. Report both locations.
- **Magic literals** *(Minor)*: string or integer literals used in 2+ places without a named constant.
- **Near-duplicate functions** *(Minor)*: a new function whose body is structurally similar to an existing one, differing only in a literal or a single call target — flag as a parameterization candidate.

Calibration:
- 3+ occurrences: recommend extraction.
- 2 occurrences: note as "consider," not a hard finding.
- Single-use helper proposals: do not recommend; premature abstraction is worse than inline duplication.

## Output

Group findings by severity. Within each group, one finding per line: `file:line — short description — fix hint`.

```
**Critical** (blocking):
  src/unp4k/Program.cs:22 — AES key changed but src/sc.gamedata/Program.cs:24 still has old key — sync all three

**Major** (should fix):
  src/NewProject/NewProject.csproj:5 — DebugType=portable in Release — use DebugType=none

**Minor** (consider):
  src/unforge/Foo.cs:42 — spaces instead of tabs — match .editorconfig
```

End with a one-line **verdict**:

- **Clean** — no findings.
- **Minor issues** — only Minor findings.
- **Needs attention** — any Critical or Major findings.

After the verdict, **CHECKPOINT — pause and ask the user whether to draft fixes for the Critical/Major findings, or move on.** Wait for the answer before doing anything else.
