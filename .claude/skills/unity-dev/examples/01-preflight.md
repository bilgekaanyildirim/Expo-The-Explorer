# Example: full preflight declaration

Game: top-down farming game (an example — the genre is not binding).

```markdown
# Preflight: Watering system

- Task: Add watering state to planted tiles; a watered tile grows the next day.
- Phase: production
- Enforcement: yes — map-health report present at session start
- Located: step 1 — index.md row `Farming` → gameplay shard, entry FieldGrid.cs
- Attachment point: FieldGrid (owner of tile data) + DayCycle event
- Map repairs: FieldGrid.cs codemap line is STALE (api changed last task) — repaired here
- Won't change: FieldGrid's tile schema, the save format, the UI layer
- Procedures: data-source → watering state lives in tile data (authority FieldGrid);
  recompute-timing → growth computation on the day-changed event, not per frame;
  ownership → single writer FieldGrid, WateringTool sends commands
- Data source: tile state in FieldGrid; watering range from the tool SO
- Editor tasks: WateringCan.asset written by Claude; one manual step — drag
  the asset onto WateringTool's Tool slot in the Player prefab
- Assumptions: Assumed the day change is published from a single event; if it
  is published from two sources, growth triggers twice → ownership violation
- Risks: A field will be added to the save schema; a default is needed for old saves

## Manifest
- Assets/Scripts/Gameplay/FieldGrid.cs
- Assets/Scripts/Gameplay/WateringTool.cs
- Assets/Data/Tools/WateringCan.asset
```

Note: the manifest lists only the files **to be written**; DayCycle.cs will be
read but is not on the list because it will not change.
