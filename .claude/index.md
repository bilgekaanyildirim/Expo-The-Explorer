<!-- stamp: 2026-09-04 systems:24 unmapped:0 unassigned-files:243 -->
# index — system to location, one screen

Step 1 of `procedures/locate.md`: read this before anything else, and only
descend into blueprint/codemap/unitymap for what this table points at.
Regenerate with `python3 .claude/hooks/build_index.py`; it joins existing
maps and never invents a name.

| system | shard(s) | entry files | scenes | prefabs | data | status |
|---|---|---|---|---|---|---|
| BoardDistribution | core | ExpoTheExplorer/Assets/Data/DataScripts/BoardDistributionSettings.cs, ExpoTheExplorer/Assets/Scripts/Core/RequiredItemKey.cs (+4) | - | - | - | OK |
| BoardUI | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/GameConfig.cs, ExpoTheExplorer/Assets/Scripts/Core/BoardGrid.cs (+6) | - | - | ExpoTheExplorer/Assets/Data | OK |
| Bootstrap | core, ui | ExpoTheExplorer/Assets/Data/ExpoTheExplorer.Data.asmdef, ExpoTheExplorer/Assets/Scripts/Bootstrap/GameManager.cs (+12) | - | - | - | OK |
| BuildTooling | editor | ExpoTheExplorer/Assets/Editor/CIBuild.cs | - | - | - | OK |
| CelebrationSystem | ui | ExpoTheExplorer/Assets/Scripts/UI/ConfettiView.cs | - | CelebrationConfetti, Confetti | - | OK |
| DataTooling | editor | ExpoTheExplorer/Assets/Editor/DataConfigReserializer.cs | - | - | - | OK |
| DayEditor | core, editor | ExpoTheExplorer/Assets/Editor/DayEditorDayStartPreview.cs, ExpoTheExplorer/Assets/Editor/DayEditorModel.cs (+6) | - | - | - | OK |
| DayLifecycle | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/StarScoreConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/DayLifecycle/DayLifecycleManager.cs (+4) | - | - | ExpoTheExplorer/Assets/Data | OK |
| DaySystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayBoardTimelinePlayer.cs, ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayCatalogParser.cs (+11) | - | NewItemIntroPopup | - | OK |
| DebugMenu | core | ExpoTheExplorer/Assets/Scripts/Debug/DebugMenuBinder.cs, ExpoTheExplorer/Assets/Scripts/Debug/SROptions.Expo.cs | - | - | - | OK |
| EconomySystem | core | ExpoTheExplorer/Assets/Data/DataScripts/EconomyConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/EconomySystem/EconomyCalculator.cs (+2) | - | - | ExpoTheExplorer/Assets/Data | OK |
| HapticsSystem | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/HapticConfig.cs, ExpoTheExplorer/Assets/Scripts/Bootstrap/HapticsBinder.cs (+4) | - | - | ExpoTheExplorer/Assets/Data | OK |
| KeySystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/KeySystem/ExpoTheExplorer.Systems.KeySystem.asmdef, ExpoTheExplorer/Assets/Scripts/Systems/KeySystem/KeyManager.cs (+4) | - | NoKeysPopup | ExpoTheExplorer/Assets/Data | OK |
| LivesSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/ExpoTheExplorer.Systems.LivesSystem.asmdef, ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/LivesManager.cs (+4) | - | - | ExpoTheExplorer/Assets/Data | OK |
| MainScreen | ui | ExpoTheExplorer/Assets/Scripts/UI/MainScreenRoot.cs | MainScreen | - | - | OK |
| MetaSystem | core, editor, ui | ExpoTheExplorer/Assets/Editor/MetaEconomyGUI.cs, ExpoTheExplorer/Assets/Editor/MetaEconomyProjection.cs (+4) | - | MetaUnlockPopup | ExpoTheExplorer/Assets/Data | OK |
| PlayTesting | editor | ExpoTheExplorer/Assets/Editor/DayJumpWindow.cs | - | - | - | OK |
| PowerupSystem | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/PowerupType.cs, ExpoTheExplorer/Assets/Scripts/Systems/PowerupSystem/ExpoTheExplorer.Systems.PowerupSystem.asmdef (+7) | - | PowerupShop | ExpoTheExplorer/Assets/Data | OK |
| ProgressionSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/Wallet.cs, ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/DayRewardPurse.cs (+10) | - | HUDCanvas | - | OK |
| TelemetrySystem | core | ExpoTheExplorer/Assets/Scripts/Systems/TelemetrySystem/ITelemetrySink.cs, ExpoTheExplorer/Assets/Scripts/Systems/TelemetrySystem/RunStatus.cs (+12) | - | - | - | OK |
| Testing | core | ExpoTheExplorer/Assets/Tests/EditMode/ExpoTheExplorer.Tests.EditMode.asmdef, ExpoTheExplorer/Assets/Tests/EditMode/BoardGridTests.cs (+6) | - | - | - | OK |
| TicketSystem | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/TicketCardVisualsConfig.cs, ExpoTheExplorer/Assets/Data/DataScripts/TicketRuntimeSettings.cs (+11) | - | TicketCard, TicketCard UpDown | ExpoTheExplorer/Assets/Data, ExpoTheExplorer/Assets/Data/FoodData/Burger, ExpoTheExplorer/Assets/Data/FoodData/Hotdog | OK |
| TraySystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/TrayManager.cs, ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/ExpoTheExplorer.Systems.TraySystem.asmdef (+2) | - | TrayArea3Item, TrayArea1Item, TrayArea2Item, TrayArea Variant | - | OK |
| Tutorial | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/Tutorial/TutorialDirector.cs, ExpoTheExplorer/Assets/Data/DataScripts/TutorialTextConfig.cs (+9) | - | TutorialPowerupIntro, TutorialPowerupSpotlight, TutorialStepHints, MainScreenTutorialWelcome, MainScreenTutorialStoreHint | ExpoTheExplorer/Assets/Data | OK |

## Gaps
- `sys: ?` on 243 codemap line(s) — first: ExpoTheExplorer/Assets/Data/DataScripts/FoodCatalog.cs, ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs, ExpoTheExplorer/Assets/Plugins/Demigiant/DOTween/Modules/DOTweenModuleAudio.cs
- 47 flagged codemap line(s) excluded from this table (ORPHAN, STALE)
