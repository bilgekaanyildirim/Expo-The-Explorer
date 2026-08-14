<!-- stamp: 2026-08-14 systems:6 unmapped:0 unassigned-files:95 -->
# index — system to location, one screen

Step 1 of `procedures/locate.md`: read this before anything else, and only
descend into blueprint/codemap/unitymap for what this table points at.
Regenerate with `python3 .claude/hooks/build_index.py`; it joins existing
maps and never invents a name.

| system | shard(s) | entry files | scenes | prefabs | data | status |
|---|---|---|---|---|---|---|
| BoardDistribution | core | ExpoTheExplorer/Assets/Data/DataScripts/BoardDistributionConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/BoardDistribution/BoardDistributor.cs | - | - | - | OK |
| BoardUI | core, ui | ExpoTheExplorer/Assets/Scripts/UI/BoardView.cs, ExpoTheExplorer/Assets/Data/DataScripts/BoardVisualsConfig.cs | - | - | - | OK |
| Bootstrap | core | ExpoTheExplorer/Assets/Scripts/Bootstrap/GameManager.cs, ExpoTheExplorer/Assets/Scripts/Bootstrap/DebugTicketDeliveryController.cs | - | - | - | OK |
| DayEditor | editor | ExpoTheExplorer/Assets/Editor/DayEditorDayStartPreview.cs, ExpoTheExplorer/Assets/Editor/DayEditorModel.cs (+4) | - | - | - | OK |
| DaySystem | core | ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayBoardTimelinePlayer.cs, ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayCatalogNavigator.cs (+8) | - | - | - | OK |
| TicketSystem | core | ExpoTheExplorer/Assets/Scripts/Systems/TicketSystem/TicketFactory.cs, ExpoTheExplorer/Assets/Scripts/Systems/TicketSystem/TicketSlotManager.cs (+1) | - | - | - | OK |

## Gaps
- `sys: ?` on 95 codemap line(s) — first: ExpoTheExplorer/Assets/Data/ExpoTheExplorer.Data.asmdef, ExpoTheExplorer/Assets/Data/DataScripts/BoardAnimationConfig.cs, ExpoTheExplorer/Assets/Data/DataScripts/DragFeelConfig.cs
