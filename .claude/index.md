<!-- stamp: 2026-08-14 systems:2 unmapped:0 unassigned-files:112 -->
# index — system to location, one screen

Step 1 of `procedures/locate.md`: read this before anything else, and only
descend into blueprint/codemap/unitymap for what this table points at.
Regenerate with `python3 .claude/hooks/build_index.py`; it joins existing
maps and never invents a name.

| system | shard(s) | entry files | scenes | prefabs | data | status |
|---|---|---|---|---|---|---|
| BoardUI | core, ui | ExpoTheExplorer/Assets/Scripts/UI/BoardView.cs, ExpoTheExplorer/Assets/Data/DataScripts/BoardVisualsConfig.cs | - | - | - | OK |
| DayEditor | editor | ExpoTheExplorer/Assets/Editor/DayEditorDayStartPreview.cs, ExpoTheExplorer/Assets/Editor/DayEditorModel.cs (+4) | - | - | - | OK |

## Gaps
- `sys: ?` on 112 codemap line(s) — first: ExpoTheExplorer/Assets/Data/ExpoTheExplorer.Data.asmdef, ExpoTheExplorer/Assets/Data/DataScripts/BoardAnimationConfig.cs, ExpoTheExplorer/Assets/Data/DataScripts/BoardDistributionConfig.cs
