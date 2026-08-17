<!-- stamp: 2026-08-17 systems:11 unmapped:0 unassigned-files:52 -->
# index — system to location, one screen

Step 1 of `procedures/locate.md`: read this before anything else, and only
descend into blueprint/codemap/unitymap for what this table points at.
Regenerate with `python3 .claude/hooks/build_index.py`; it joins existing
maps and never invents a name.

| system | shard(s) | entry files | scenes | prefabs | data | status |
|---|---|---|---|---|---|---|
| BoardDistribution | core | ExpoTheExplorer/Assets/Data/DataScripts/BoardDistributionSettings.cs, ExpoTheExplorer/Assets/Scripts/Systems/BoardDistribution/BoardDistributor.cs (+1) | - | - | - | OK |
| BoardUI | core, ui | ExpoTheExplorer/Assets/Scripts/UI/BoardView.cs, ExpoTheExplorer/Assets/Data/DataScripts/BoardVisualsConfig.cs | - | - | - | OK |
| Bootstrap | core | ExpoTheExplorer/Assets/Scripts/Bootstrap/GameManager.cs, ExpoTheExplorer/Assets/Scripts/Bootstrap/DebugTicketDeliveryController.cs | - | - | - | OK |
| DayEditor | core, editor | ExpoTheExplorer/Assets/Editor/DayEditorDayStartPreview.cs, ExpoTheExplorer/Assets/Editor/DayEditorSettingsPreviews.cs (+5) | - | - | - | OK |
| DayLifecycle | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/DayLifecycle/DayLifecycleManager.cs, ExpoTheExplorer/Assets/Scripts/Systems/DayLifecycle/ExpoTheExplorer.Systems.DayLifecycle.asmdef (+2) | - | - | - | OK |
| DaySystem | core | ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayBoardTimelinePlayer.cs, ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayCatalogParser.cs (+9) | - | - | - | OK |
| EconomySystem | core, editor | ExpoTheExplorer/Assets/Data/DataScripts/EconomyConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/EconomySystem/EconomyCalculator.cs (+3) | - | - | - | OK |
| LivesSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/ExpoTheExplorer.Systems.LivesSystem.asmdef, ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/LivesManager.cs (+3) | - | - | - | OK |
| ProgressionSystem | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/LevelProgressionConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/ExpoTheExplorer.Systems.ProgressionSystem.asmdef (+9) | - | - | - | OK |
| TicketSystem | core, editor | ExpoTheExplorer/Assets/Scripts/Systems/TicketSystem/TicketFactory.cs, ExpoTheExplorer/Assets/Scripts/Systems/TicketSystem/TicketSlotManager.cs (+6) | - | - | - | OK |
| TraySystem | core | ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/TrayManager.cs, ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/ExpoTheExplorer.Systems.TraySystem.asmdef (+2) | - | - | - | OK |

## Gaps
- `sys: ?` on 52 codemap line(s) — first: ExpoTheExplorer/Assets/Data/ExpoTheExplorer.Data.asmdef, ExpoTheExplorer/Assets/Data/DataScripts/BoardAnimationConfig.cs, ExpoTheExplorer/Assets/Data/DataScripts/DragFeelConfig.cs
- 6 flagged codemap line(s) excluded from this table (STALE)
