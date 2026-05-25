---
description: Verify README.md and spec.md match the current code
---

# /docs_sync_check

Cross-check the repo's documentation against the current code. Catches drift introduced when CLI arguments change but `README.md` still describes the old usage, or when the DataForge binary reader is modified but `spec.md` still documents the old layout.

## Severities

- **Critical** — `spec.md` documents a field layout, offset, or version gate that no longer matches `src/unforge/DataForge.cs` or the types under `ComplexTypes/`/`SimpleTypes/`. Anyone reading the spec to understand or modify the reader will be misled.
- **Major** — `README.md` CLI usage or feature description no longer matches the actual argument parsing in `src/unp4k/Program.cs`, `src/unp4k.fs/Program.cs`, `src/unforge.cli/Program.cs`, or `src/sc.gamedata/Program.cs`.
- **Minor** — `CLAUDE.md` architecture section describes something that has changed (project count, file paths, line numbers for the AES key, etc.), or `README.md` mentions a feature that has been removed/renamed but isn't user-facing-critical.

## Checks

### 1. spec.md vs DataForge reader *(Critical)*

Compare the field layouts, offsets, type widths, and version gates documented in `spec.md` against the actual code in:
- `src/unforge/DataForge.cs` — header parsing, offset calculations, string table loading
- `src/unforge/ComplexTypes/DataForgeStructDefinition.cs` — struct definition fields
- `src/unforge/ComplexTypes/DataForgePropertyDefinition.cs` — property definition fields
- `src/unforge/ComplexTypes/DataForgeEnumDefinition.cs` — enum definition fields
- `src/unforge/ComplexTypes/DataForgeDataMapping.cs` — data mapping fields (version-dependent widths)
- `src/unforge/ComplexTypes/DataForgeRecord.cs` — record definition fields (legacy vs non-legacy)
- `src/unforge/Enums.cs` — `EDataType` and `EConversionType` values

Flag any discrepancy: missing fields, wrong types, wrong offsets, missing version gates, undocumented new tables.

### 2. README.md CLI usage vs code *(Major)*

For each executable described in `README.md`:
- **unp4k** — verify argument positions, default values, filter behavior description against `src/unp4k/Program.cs`
- **unp4k.fs** — verify argument positions, mount-point defaults, interactive key options against `src/unp4k.fs/Program.cs`
- **unforge.cli** — verify described behavior (`.dcb` → XML, CryXML → `.raw` rename) against `src/unforge.cli/Program.cs`

Flag usage descriptions that no longer match, missing new arguments, or described behavior that has changed.

### 3. CLAUDE.md currency *(Minor, escalate to Major if factually wrong)*

Spot-check the `CLAUDE.md` Architecture section:
- Project count matches the solution file
- AES key line-number references are still accurate
- Project descriptions match current behavior
- Build commands still work

## Output

Group findings by severity. Within each group: `doc-file:line -> code-file:line — short description`.

```
**Critical** (spec drift):
  spec.md:82 -> src/unforge/ComplexTypes/DataForgeDataMapping.cs:15 — spec says StructCount is always UInt32; code uses UInt16 for FileVersion < 5

**Major** (should fix):
  README.md:19 -> src/unp4k/Program.cs:29 — README says filter supports wildcards; code only supports substring match with *.ext special-case

**Minor** (consider):
  CLAUDE.md:32 -> src/unp4k/Program.cs:22 — AES key line reference says :13, actual is :22
```

End with a one-line **verdict**:

- **Clean** — no findings.
- **Minor issues** — only Minor findings.
- **Needs attention** — any Critical or Major findings.

After the verdict, **CHECKPOINT — pause and ask the user whether to draft doc updates for the Critical/Major findings, or move on.** Do not edit docs without confirmation — surface, don't auto-correct.
