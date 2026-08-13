---
paths:
  - "Assets/Scripts/UI/**"
---
<!-- Path-scoped rule: loads only when a matching file is read, not on every tool
     use. It IS re-loaded after a compaction (InstructionsLoaded fires with
     load_reason: compact), so the risk is not compaction — it is that a
     decision taken before any matching file is opened never sees this file.
     Such a rule belongs in the preflight too. Verify loading with /memory. -->

# UI domain rules

<!-- Filled in together by the AI + user at bootstrap. Example items: -->
- UI READS game state; it never writes. Write requests go to the owner as commands.
- UI updates happen via event subscription; state is never polled per frame.
- <project-specific UI conventions>
