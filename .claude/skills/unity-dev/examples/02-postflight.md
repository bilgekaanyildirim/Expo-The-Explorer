# Example: full postflight output

Closing the same task (the watering system):

```markdown
# Postflight: Watering system

- [Y] No procedure violations
- [Y] Located, not scanned (locate step 1: index.md row `Farming` → gameplay shard)
- [Y] Invariant scan clean
- [Y] Proof standard met
- [Y] Codemap updated (2 lines, sys: Farming, dep? confirmed, no markers left)
- [Y] decisions.md updated (D-014: watering state lives in tile data — affects: Farming, Assets/Scripts/Gameplay/FieldGrid.cs)
- [N] Assumptions closed
- [Y] Editor side synced (unitymap regenerated; WateringTool wired on the Player prefab, no new NULL slots)
- [Y] Blueprint consistent — `check_blueprint.py` → `0 error(s), 1 warning(s), 3 info.`
      (the warning is the pre-existing `Audio` system with no code, untouched here)
- [Y] Shipping signals checked (release scope still clearly far)

NO: Could not verify that the day change is published from a single event
(a second Invoke path appears in DayCycle). Marked OPEN in the fingerprint;
question for the user: is DayCycle.EndDay the only place that publishes the
day change?
```

Two things this example is showing:

- The NO item does not make the task "done" — the question was escalated to
  the user.
- The blueprint line quotes the script's own output. `[Y] Blueprint
  consistent` with no counts behind it is the failure mode this item exists
  to prevent.
