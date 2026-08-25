<!-- stamp: 2026-08-25 systems:13 unmapped:0 unassigned-files:37 -->
# index — system to location, one screen

Step 1 of `procedures/locate.md`: read this before anything else, and only
descend into blueprint/codemap/unitymap for what this table points at.
Regenerate with `python3 .claude/hooks/build_index.py`; it joins existing
maps and never invents a name.

| system | shard(s) | entry files | scenes | prefabs | data | status |
|---|---|---|---|---|---|---|
| BoardDistribution | core | ExpoTheExplorer/Assets/Data/DataScripts/BoardDistributionSettings.cs, ExpoTheExplorer/Assets/Scripts/Core/RequiredItemKey.cs (+3) | - | - | - | OK |
| BoardUI | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/GameConfig.cs, ExpoTheExplorer/Assets/Scripts/Core/BoardGrid.cs (+4) | - | - | - | OK |
| Bootstrap | core | ExpoTheExplorer/Assets/Scripts/Bootstrap/GameManager.cs, ExpoTheExplorer/Assets/Scripts/Core/GameState.cs (+7) | - | - | - | OK |
| DayEditor | editor | ExpoTheExplorer/Assets/Editor/DayEditorDayStartPreview.cs, ExpoTheExplorer/Assets/Editor/DayEditorSpriteGUI.cs (+3) | - | - | - | OK |
| DayLifecycle | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/StarScoreConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/DayLifecycle/DayLifecycleManager.cs (+5) | - | - | - | OK |
| DaySystem | core | ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayBoardTimelinePlayer.cs, ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayCatalogParser.cs (+8) | - | - | - | OK |
| EconomySystem | core | ExpoTheExplorer/Assets/Data/DataScripts/EconomyConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/EconomySystem/EconomyCalculator.cs (+2) | - | - | - | OK |
| LivesSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/ExpoTheExplorer.Systems.LivesSystem.asmdef, ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/LivesManager.cs (+3) | - | - | - | OK |
| MainScreen | editor, ui | ExpoTheExplorer/Assets/Scripts/UI/MainScreenRoot.cs, ExpoTheExplorer/Assets/Scripts/UI/MainScreenView.cs (+4) | MainScreen | - | - | OK |
| MetaSystem | core, editor, ui | ExpoTheExplorer/Assets/Data/DataScripts/MetaCatalog.cs, ExpoTheExplorer/Assets/Scripts/Systems/MetaSystem/MetaLayout.cs (+15) | - | - | - | OK |
| ProgressionSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/Wallet.cs, ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/DayRewardPurse.cs (+9) | - | HudCanvas | - | OK |
| TicketSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Core/Ticket.cs, ExpoTheExplorer/Assets/Scripts/Systems/TicketSystem/TicketSlotManager.cs (+6) | - | TicketCard, TicketCard Into, TicketCard UpDown | - | OK |
| TraySystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/TrayManager.cs, ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/ExpoTheExplorer.Systems.TraySystem.asmdef (+3) | - | - | - | OK |

## Gaps
- `sys: ?` on 37 codemap line(s) — first: ExpoTheExplorer/Assets/Data/ExpoTheExplorer.Data.asmdef, ExpoTheExplorer/Assets/Data/DataScripts/BoardAnimationConfig.cs, ExpoTheExplorer/Assets/Data/DataScripts/DragFeelConfig.cs
- 15 flagged codemap line(s) excluded from this table (STALE)
