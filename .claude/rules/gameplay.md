---
paths:
  - "Assets/Scripts/Gameplay/**"
---
<!-- Path-scoped rule — loading behavior: see the note in ui.md. -->

# Gameplay domain rules

<!-- Filled in together by the AI + user at bootstrap. Example items: -->
- Gameplay code holds no references to UI (dependency arrow is one-way: UI → gameplay).
- No allocation inside frame-frequency loops.
- <project-specific gameplay conventions>
