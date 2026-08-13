# <GAME NAME>

<!-- 200-LINE LIMIT: This file is fully loaded every session and survives
     compaction. If it grows, both context space and adherence drop. Move
     detail into .claude/rules/ or .claude/ files. This comment block does
     not enter context. -->

## Phase

- **Phase:** production
<!-- production | shipping — release-grade in both; the phase only sets the
     proof standard for optimization (cost threshold vs measurement).
     Transitions happen only via the phase-transition audit (skill router). -->

## Invariants

<!-- Every item must be BINARY: violated / not violated at a glance.
     No "would be nice" items. No two rules may contradict each other.
     Day-zero items are STRUCTURAL, not optimizations. Candidate list: -->
- Content data (numbers, curves, tables, text) is never embedded in code; it is read from data.
- Save data carries a version number; unversioned saves are never written.
- Every piece of data has a single writer; if a second writer appears, code halts.
- Dependency arrows between systems are one-directional (plan: .claude/decisions.md).
- C# changes are made only with the Edit/Write tools; writing files via Bash
  is forbidden.
- <GAME-SPECIFIC INVARIANTS — the AI proposes at bootstrap, the user prunes>

## Fingerprint summary

<!-- Full version in .claude/fingerprint.md — this block is a few-line copy. -->
- Space: <2D/3D, grid/free, unit>
- Determinism: <required?>
- Authorities: <data → owner, the 2-3 most critical>
- Scale: <the largest n values>

## Pointers

- **Start here every task:** .claude/index.md (system → where it lives)
- Scope: .claude/scope.md · Fingerprint: .claude/fingerprint.md
- Blueprint (systems/scenes/prefabs/folders): .claude/blueprint.md
- Decisions: .claude/decisions.md · Code map: .claude/codemap-*.md
- Scene map: .claude/unitymap.md · Asset map: .claude/assetmap.md
- Shard definition: .claude/shards.json · Domain rules: .claude/rules/
