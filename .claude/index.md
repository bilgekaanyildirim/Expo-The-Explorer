<!-- stamp: 2026-08-26 systems:21 unmapped:0 unassigned-files:24 -->
# index — system to location, one screen

Step 1 of `procedures/locate.md`: read this before anything else, and only
descend into blueprint/codemap/unitymap for what this table points at.
Regenerate with `python3 .claude/hooks/build_index.py`; it joins existing
maps and never invents a name.

| system | shard(s) | entry files | scenes | prefabs | data | status |
|---|---|---|---|---|---|---|
| BoardDistribution | core | ExpoTheExplorer/Assets/Data/DataScripts/BoardDistributionSettings.cs, ExpoTheExplorer/Assets/Scripts/Core/RequiredItemKey.cs (+3) | - | - | - | OK |
| BoardUI | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/GameConfig.cs, ExpoTheExplorer/Assets/Scripts/Core/BoardGrid.cs (+5) | - | - | ExpoTheExplorer/Assets/Data | OK |
| Bootstrap | core | ExpoTheExplorer/Assets/Scripts/Bootstrap/GameManager.cs, ExpoTheExplorer/Assets/Scripts/Core/GameState.cs (+6) | - | - | - | OK |
| DataTooling | editor | ExpoTheExplorer/Assets/Editor/DataConfigReserializer.cs | - | - | - | OK |
| DayEditor | editor | ExpoTheExplorer/Assets/Editor/DayEditorDayStartPreview.cs, ExpoTheExplorer/Assets/Editor/DayEditorModel.cs (+4) | - | - | - | OK |
| DayLifecycle | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/StarScoreConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/DayLifecycle/DayLifecycleManager.cs (+5) | - | - | ExpoTheExplorer/Assets/Data | OK |
| DaySystem | core | ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayBoardTimelinePlayer.cs, ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayCatalogParser.cs (+9) | - | - | - | OK |
| DebugMenu | core | ExpoTheExplorer/Assets/Scripts/Debug/DebugMenuBinder.cs, ExpoTheExplorer/Assets/Scripts/Debug/SROptions.Expo.cs | - | - | - | OK |
| EconomySystem | core | ExpoTheExplorer/Assets/Data/DataScripts/EconomyConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/EconomySystem/EconomyCalculator.cs (+2) | - | - | ExpoTheExplorer/Assets/Data | OK |
| HapticsSystem | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/HapticConfig.cs, ExpoTheExplorer/Assets/Scripts/Bootstrap/HapticsBinder.cs (+4) | - | - | ExpoTheExplorer/Assets/Data | OK |
| KeySystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/KeySystem/ExpoTheExplorer.Systems.KeySystem.asmdef, ExpoTheExplorer/Assets/Scripts/Systems/KeySystem/KeyManager.cs (+4) | - | NoKeysPopup | ExpoTheExplorer/Assets/Data | OK |
| LivesSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/ExpoTheExplorer.Systems.LivesSystem.asmdef, ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/LivesManager.cs (+4) | - | - | ExpoTheExplorer/Assets/Data | OK |
| MainScreen | editor, ui | ExpoTheExplorer/Assets/Scripts/UI/MainScreenRoot.cs, ExpoTheExplorer/Assets/Scripts/UI/MainScreenView.cs (+4) | MainScreen | - | - | OK |
| MetaSystem | core, editor, ui | ExpoTheExplorer/Assets/Data/DataScripts/MetaCatalog.cs, ExpoTheExplorer/Assets/Scripts/Systems/MetaSystem/MetaLayout.cs (+15) | - | - | ExpoTheExplorer/Assets/Data | OK |
| PlayTesting | editor | ExpoTheExplorer/Assets/Editor/DayJumpWindow.cs | - | - | - | OK |
| PowerupSystem | core, editor, ui | ExpoTheExplorer/Assets/Data/DataScripts/PowerupType.cs, ExpoTheExplorer/Assets/Scripts/Systems/PowerupSystem/ExpoTheExplorer.Systems.PowerupSystem.asmdef (+8) | - | - | ExpoTheExplorer/Assets/Data | OK |
| ProgressionSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/Wallet.cs, ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/DayRewardPurse.cs (+9) | - | HUDCanvas | - | OK |
| Testing | core | ExpoTheExplorer/Assets/Tests/EditMode/ExpoTheExplorer.Tests.EditMode.asmdef | - | - | - | OK |
| TicketSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Core/Ticket.cs, ExpoTheExplorer/Assets/Scripts/Systems/TicketSystem/TicketSlotManager.cs (+9) | - | TicketCard, TicketCard Into, TicketCard UpDown | ExpoTheExplorer/Assets/Data | OK |
| TraySystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/TrayManager.cs, ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/ExpoTheExplorer.Systems.TraySystem.asmdef (+3) | - | - | - | OK |
| Tutorial | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/Tutorial/TutorialDirector.cs, ExpoTheExplorer/Assets/Data/DataScripts/TutorialTextConfig.cs (+5) | - | - | ExpoTheExplorer/Assets/Data | OK |

## Gaps
- `sys: ?` on 24 codemap line(s) — first: ExpoTheExplorer/Assets/Data/ExpoTheExplorer.Data.asmdef, ExpoTheExplorer/Assets/Data/DataScripts/DragFeelConfig.cs, ExpoTheExplorer/Assets/Data/DataScripts/FoodCatalog.cs
- 14 flagged codemap line(s) excluded from this table (STALE)
