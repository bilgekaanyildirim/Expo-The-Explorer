# Example: codemap lines (three criticality levels)

The schema is defined in SKILL.md. One line each from three different games:

```
Assets/Scripts/Core/GameState.cs | game flow state machine | sys: Core | api: void Transition(State s); State Active { get; } | dep: - | used: SceneLoader,UIRouter,SaveSystem | crit: K1 | note: single writer; state never changes outside Transition | h:9c1af23e
Assets/Scripts/Gameplay/BulletPool.cs | bullet lifecycle pool | sys: Combat | api: Bullet Get(BulletType t); void Release(Bullet b) | dep: BulletType | used: WeaponSystem | crit: K2 | note: pooled per cost model (D-021); size from fingerprint n | h:44b0d7c1
Assets/Scripts/UI/GoldCounter.cs | gold amount display | sys: Economy | api: - | dep: Wallet | used: - | crit: K3 | note: subscribes to Wallet.Changed event; never reads per frame | h:1e77a05b
```

Every field is filled or `-`; the role is 3-6 words; `sys:` names the system
exactly as `blueprint.md` spells it; the `note` field carries a decision, not
chatter.

## What a line looks like before it is finished

`build_codemap.py` writes the stub; the AI turns it into the lines above:

```
Assets/Scripts/Gameplay/BulletPool.cs | MISSING-role | sys: ? | api: Get(BulletType t); Release(Bullet b) | dep?: BulletType,WeaponSystem | used: ? | crit: ? | note: auto-added; AI must complete | h:44b0d7c1
```

`dep?:` is a mechanical draft — `WeaponSystem` appears in the file only as a
comment, so confirming the field means dropping it, not copying it.

## Markers

The leading marker and `h:` are written by the script and never by hand:

```
STALE  Assets/Scripts/Gameplay/BulletPool.cs | bullet lifecycle pool | sys: Combat | ...
ORPHAN Assets/Scripts/Gameplay/OldPool.cs    | bullet lifecycle pool | sys: Combat | ...
MOVED  Assets/Scripts/UI/Spawner.cs          | spawns bullets        | sys: Combat | ...
```

- `STALE` — the file changed after the line was written. Re-read the file,
  correct `api:`/`dep:`/`used:`/`note:`, then delete the word `STALE`.
- `ORPHAN` — the file is gone. If it was renamed, the new line carries
  `possible rename/move of <old path>`: move the semantic fields across and
  delete the orphan line.
- `MOVED` — the file now matches another shard's pattern in `shards.json`.
  The correct shard already has a fresh stub; carry the content over.

Deleting a marker without repairing the line is the one thing that breaks the
map quietly, because the next run has no way to tell the difference.
