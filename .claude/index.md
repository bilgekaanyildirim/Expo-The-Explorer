<!-- stamp: 2026-08-31 systems:22 unmapped:1 unassigned-files:2 -->
# index — system to location, one screen

Step 1 of `procedures/locate.md`: read this before anything else, and only
descend into blueprint/codemap/unitymap for what this table points at.
Regenerate with `python3 .claude/hooks/build_index.py`; it joins existing
maps and never invents a name.

| system | shard(s) | entry files | scenes | prefabs | data | status |
|---|---|---|---|---|---|---|
| BoardDistribution | core | ExpoTheExplorer/Assets/Data/DataScripts/BoardDistributionSettings.cs, ExpoTheExplorer/Assets/Scripts/Core/RequiredItemKey.cs (+4) | - | - | - | OK |
| BoardUI | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/GameConfig.cs, ExpoTheExplorer/Assets/Scripts/Core/BoardGrid.cs (+6) | - | - | ExpoTheExplorer/Assets/Data | OK |
| Bootstrap | core | ExpoTheExplorer/Assets/Data/ExpoTheExplorer.Data.asmdef, ExpoTheExplorer/Assets/Scripts/Bootstrap/GameManager.cs (+10) | - | - | - | OK |
| CelebrationSystem | - | - | - | CelebrationConfetti, Confetti | - | UNMAPPED — blueprint system with no code |
| DataTooling | editor | ExpoTheExplorer/Assets/Editor/DataConfigReserializer.cs | - | - | - | OK |
| DayEditor | core, editor | ExpoTheExplorer/Assets/Editor/DayEditorDayStartPreview.cs, ExpoTheExplorer/Assets/Editor/DayEditorSpriteGUI.cs (+5) | - | - | - | OK |
| DayLifecycle | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/StarScoreConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/DayLifecycle/DayLifecycleManager.cs (+4) | - | - | ExpoTheExplorer/Assets/Data | OK |
| DaySystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayBoardTimelinePlayer.cs, ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/DayCatalogParser.cs (+11) | - | NewItemIntroPopup | - | OK |
| DebugMenu | core | ExpoTheExplorer/Assets/Scripts/Debug/DebugMenuBinder.cs | - | - | - | OK |
| EconomySystem | core | ExpoTheExplorer/Assets/Data/DataScripts/EconomyConfig.cs, ExpoTheExplorer/Assets/Scripts/Systems/EconomySystem/EconomyCalculator.cs (+2) | - | - | ExpoTheExplorer/Assets/Data | OK |
| HapticsSystem | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/HapticConfig.cs, ExpoTheExplorer/Assets/Scripts/Bootstrap/HapticsBinder.cs (+4) | - | - | ExpoTheExplorer/Assets/Data | OK |
| KeySystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/KeySystem/ExpoTheExplorer.Systems.KeySystem.asmdef, ExpoTheExplorer/Assets/Scripts/Systems/KeySystem/KeyManager.cs (+4) | - | NoKeysPopup | ExpoTheExplorer/Assets/Data | OK |
| LivesSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/ExpoTheExplorer.Systems.LivesSystem.asmdef, ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/LivesManager.cs (+4) | - | - | ExpoTheExplorer/Assets/Data | OK |
| MainScreen | ui | ExpoTheExplorer/Assets/Scripts/UI/MainScreenRoot.cs, ExpoTheExplorer/Assets/Scripts/UI/MainScreenView.cs | MainScreen | - | - | OK |
| MetaSystem | core, editor, ui | ExpoTheExplorer/Assets/Scripts/Systems/MetaSystem/MetaLayout.cs, ExpoTheExplorer/Assets/Scripts/Systems/MetaSystem/MetaPurchase.cs (+8) | - | MetaUnlockPopup | ExpoTheExplorer/Assets/Data | OK |
| PlayTesting | editor | ExpoTheExplorer/Assets/Editor/DayJumpWindow.cs | - | - | - | OK |
| PowerupSystem | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/PowerupType.cs, ExpoTheExplorer/Assets/Scripts/Systems/PowerupSystem/ExpoTheExplorer.Systems.PowerupSystem.asmdef (+8) | - | PowerupShop | ExpoTheExplorer/Assets/Data | OK |
| ProgressionSystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/Wallet.cs, ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/DayRewardPurse.cs (+10) | - | HUDCanvas | - | OK |
| Testing | core | ExpoTheExplorer/Assets/Tests/EditMode/BoardGridTests.cs, ExpoTheExplorer/Assets/Tests/EditMode/BoardItemLayerTests.cs (+5) | - | - | - | OK |
| TicketSystem | core, ui | ExpoTheExplorer/Assets/Data/DataScripts/TicketCardVisualsConfig.cs, ExpoTheExplorer/Assets/Data/DataScripts/TicketRuntimeSettings.cs (+10) | - | TicketCard, TicketCard UpDown | ExpoTheExplorer/Assets/Data, ExpoTheExplorer/Assets/Data/FoodData/Burger, ExpoTheExplorer/Assets/Data/FoodData/Hotdog | OK |
| TraySystem | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/ExpoTheExplorer.Systems.TraySystem.asmdef, ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/TraySlot.cs (+2) | - | TrayArea, TrayArea Variant | - | OK |
| Tutorial | core, ui | ExpoTheExplorer/Assets/Scripts/Systems/Tutorial/TutorialDirector.cs, ExpoTheExplorer/Assets/Data/DataScripts/TutorialTextConfig.cs (+9) | - | TutorialPowerupIntro, TutorialPowerupSpotlight, TutorialStepHints, MainScreenTutorialWelcome, MainScreenTutorialStoreHint | ExpoTheExplorer/Assets/Data | OK |

## Gaps
- `sys: ?` on 2 codemap line(s) — first: ExpoTheExplorer/Assets/Data/DataScripts/FoodCatalog.cs, ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- 44 flagged codemap line(s) excluded from this table (ORPHAN, STALE)
