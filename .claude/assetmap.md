<!-- stamp: 2026-08-30T19:25Z source-sig:97759d792dba assets:49 prefabs:15 scenes:2 asmdefs:19 -->
# assetmap — asset inventory (data, prefabs, load surface, assemblies)

Regenerate with `python3 .claude/hooks/build_assetmap.py`. `data-source.md`
reads the ScriptableObject section before deciding where a value lives;
the cost model reads the load-surface section before pricing a load.

## Assemblies (.asmdef) — the real compile boundary

- ExpoTheExplorer/Assets/Data/ExpoTheExplorer.Data.asmdef | name: ExpoTheExplorer.Data | refs: - | platforms: all | covers: ExpoTheExplorer/Assets/Data/**
- ExpoTheExplorer/Assets/Editor/ExpoTheExplorer.Editor.asmdef | name: ExpoTheExplorer.Editor | refs: ExpoTheExplorer.Data,ExpoTheExplorer.Core,ExpoTheExplorer.Systems.DaySystem,ExpoTheExplorer.Systems.TicketSystem,ExpoTheExplorer.Systems.ProgressionSystem,ExpoTheExplorer.Systems.EconomySystem | platforms: Editor | covers: ExpoTheExplorer/Assets/Editor/**
- ExpoTheExplorer/Assets/Scripts/Core/ExpoTheExplorer.Core.asmdef | name: ExpoTheExplorer.Core | refs: ExpoTheExplorer.Data | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Core/**
- ExpoTheExplorer/Assets/Scripts/Session/ExpoTheExplorer.Session.asmdef | name: ExpoTheExplorer.Session | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data,ExpoTheExplorer.Systems.ProgressionSystem,ExpoTheExplorer.Systems.LivesSystem,ExpoTheExplorer.Systems.KeySystem,ExpoTheExplorer.Systems.PowerupSystem | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Session/**
- ExpoTheExplorer/Assets/Scripts/Simulation/ExpoTheExplorer.Simulation.asmdef | name: ExpoTheExplorer.Simulation | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data,ExpoTheExplorer.Systems.DaySystem,ExpoTheExplorer.Systems.TicketSystem,ExpoTheExplorer.Systems.BoardDistribution,ExpoTheExplorer.Systems.TraySystem | platforms: Editor | covers: ExpoTheExplorer/Assets/Scripts/Simulation/**
- ExpoTheExplorer/Assets/Scripts/Systems/BoardDistribution/ExpoTheExplorer.Systems.BoardDistribution.asmdef | name: ExpoTheExplorer.Systems.BoardDistribution | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/BoardDistribution/**
- ExpoTheExplorer/Assets/Scripts/Systems/DayLifecycle/ExpoTheExplorer.Systems.DayLifecycle.asmdef | name: ExpoTheExplorer.Systems.DayLifecycle | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data,ExpoTheExplorer.Systems.EconomySystem | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/DayLifecycle/**
- ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/ExpoTheExplorer.Systems.DaySystem.asmdef | name: ExpoTheExplorer.Systems.DaySystem | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data,ExpoTheExplorer.Systems.TicketSystem,ExpoTheExplorer.Systems.BoardDistribution | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/DaySystem/**
- ExpoTheExplorer/Assets/Scripts/Systems/EconomySystem/ExpoTheExplorer.Systems.EconomySystem.asmdef | name: ExpoTheExplorer.Systems.EconomySystem | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/EconomySystem/**
- ExpoTheExplorer/Assets/Scripts/Systems/HapticsSystem/ExpoTheExplorer.Systems.HapticsSystem.asmdef | name: ExpoTheExplorer.Systems.HapticsSystem | refs: ExpoTheExplorer.Data | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/HapticsSystem/**
- ExpoTheExplorer/Assets/Scripts/Systems/KeySystem/ExpoTheExplorer.Systems.KeySystem.asmdef | name: ExpoTheExplorer.Systems.KeySystem | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data,ExpoTheExplorer.Systems.ProgressionSystem | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/KeySystem/**
- ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/ExpoTheExplorer.Systems.LivesSystem.asmdef | name: ExpoTheExplorer.Systems.LivesSystem | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data,ExpoTheExplorer.Systems.ProgressionSystem | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/LivesSystem/**
- ExpoTheExplorer/Assets/Scripts/Systems/MetaSystem/ExpoTheExplorer.Systems.MetaSystem.asmdef | name: ExpoTheExplorer.Systems.MetaSystem | refs: ExpoTheExplorer.Data | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/MetaSystem/**
- ExpoTheExplorer/Assets/Scripts/Systems/PowerupSystem/ExpoTheExplorer.Systems.PowerupSystem.asmdef | name: ExpoTheExplorer.Systems.PowerupSystem | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data,ExpoTheExplorer.Systems.ProgressionSystem | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/PowerupSystem/**
- ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/ExpoTheExplorer.Systems.ProgressionSystem.asmdef | name: ExpoTheExplorer.Systems.ProgressionSystem | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/ProgressionSystem/**
- ExpoTheExplorer/Assets/Scripts/Systems/TicketSystem/ExpoTheExplorer.Systems.TicketSystem.asmdef | name: ExpoTheExplorer.Systems.TicketSystem | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/TicketSystem/**
- ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/ExpoTheExplorer.Systems.TraySystem.asmdef | name: ExpoTheExplorer.Systems.TraySystem | refs: ExpoTheExplorer.Core,ExpoTheExplorer.Data | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/TraySystem/**
- ExpoTheExplorer/Assets/Scripts/Systems/Tutorial/ExpoTheExplorer.Systems.Tutorial.asmdef | name: ExpoTheExplorer.Systems.Tutorial | refs: - | platforms: all | covers: ExpoTheExplorer/Assets/Scripts/Systems/Tutorial/**
- ExpoTheExplorer/Assets/Tests/EditMode/ExpoTheExplorer.Tests.EditMode.asmdef | name: ExpoTheExplorer.Tests.EditMode | refs: ExpoTheExplorer.Session,ExpoTheExplorer.Core,ExpoTheExplorer.Data,ExpoTheExplorer.Systems.TicketSystem,ExpoTheExplorer.Systems.BoardDistribution,ExpoTheExplorer.Systems.TraySystem | platforms: Editor | covers: ExpoTheExplorer/Assets/Tests/EditMode/**

## ScriptableObject assets

- ExpoTheExplorer/Assets/DefaultVolumeProfile.asset | type: guid:d7fd9488 | script: ?
- ExpoTheExplorer/Assets/UniversalRenderPipelineGlobalSettings.asset | type: guid:2ec995e5 | script: ?
- ExpoTheExplorer/Assets/Data/BoardAnimationConfig.asset | type: BoardAnimationConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/BoardAnimationConfig.cs
- ExpoTheExplorer/Assets/Data/BoardVisualsConfig.asset | type: BoardVisualsConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/BoardVisualsConfig.cs
- ExpoTheExplorer/Assets/Data/DragFeelConfig.asset | type: DragFeelConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/DragFeelConfig.cs
- ExpoTheExplorer/Assets/Data/EconomyConfig.asset | type: EconomyConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/EconomyConfig.cs
- ExpoTheExplorer/Assets/Data/GameConfig.asset | type: GameConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/GameConfig.cs
- ExpoTheExplorer/Assets/Data/HapticConfig.asset | type: HapticConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/HapticConfig.cs
- ExpoTheExplorer/Assets/Data/KeyConfig.asset | type: KeyConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/KeyConfig.cs
- ExpoTheExplorer/Assets/Data/LivesConfig.asset | type: LivesConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/LivesConfig.cs
- ExpoTheExplorer/Assets/Data/MetaCatalog.asset | type: MetaCatalog | script: ExpoTheExplorer/Assets/Data/DataScripts/MetaCatalog.cs
- ExpoTheExplorer/Assets/Data/PowerupConfig.asset | type: PowerupConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/PowerupConfig.cs
- ExpoTheExplorer/Assets/Data/StarScoreConfig.asset | type: StarScoreConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/StarScoreConfig.cs
- ExpoTheExplorer/Assets/Data/TicketCardVisualsConfig.asset | type: TicketCardVisualsConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/TicketCardVisualsConfig.cs
- ExpoTheExplorer/Assets/Data/TicketGenerationConfig.asset | type: TicketGenerationConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/TicketGenerationConfig.cs
- ExpoTheExplorer/Assets/Data/TutorialTextConfig.asset | type: TutorialTextConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/TutorialTextConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/FoodCatalog.asset | type: FoodCatalog | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodCatalog.cs
- ExpoTheExplorer/Assets/Data/FoodData/Beverages/Food_Beer.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Beverages/Food_BluberryMilkshake.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Beverages/Food_Cola.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Beverages/Food_Fanta.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Beverages/Food_Sprite.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Beverages/Food_StawberryMilkshake.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Beverages/Food_StrawberryMatcha.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Beverages/Food_VanillaMilkshake.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Burger/Food_Burger.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Burger/Mod_ExtraCheese.asset | type: ModificationConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/ModificationConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Burger/Mod_ExtraPatty.asset | type: ModificationConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/ModificationConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Burger/Mod_NoLettuce.asset | type: ModificationConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/ModificationConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Burger/Mod_NoTomato.asset | type: ModificationConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/ModificationConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Desserts/Food_ChocolateCookie.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Desserts/Food_ChocolateIceCream.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Desserts/Food_DoubleChocolateCookie.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Desserts/Food_MatchaCake.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Desserts/Food_MatchaDonut.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Desserts/Food_MatchaIceCream.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Desserts/Food_Sufle.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Desserts/Food_VanillaIceCream.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Hotdog/Food_Hotdog.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Hotdog/Mod_ExtraKetchup.asset | type: ModificationConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/ModificationConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Hotdog/Mod_ExtraMayonnaise.asset | type: ModificationConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/ModificationConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Hotdog/Mod_ExtraMustard.asset | type: ModificationConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/ModificationConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Sides/Food_CrispyOnion.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Sides/Food_Fries.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Data/FoodData/Sides/Food_MozerellaSticks.asset | type: FoodItemConfig | script: ExpoTheExplorer/Assets/Data/DataScripts/FoodItemConfig.cs
- ExpoTheExplorer/Assets/Resources/DOTweenSettings.asset | type: guid:a811bde7 | script: ?
- ExpoTheExplorer/Assets/Settings/Renderer2D.asset | type: guid:11145981 | script: ?
- ExpoTheExplorer/Assets/Settings/UniversalRP.asset | type: guid:bf2edee5 | script: ?
- ExpoTheExplorer/Assets/Settings/Build Profiles/iOS.asset | type: guid:00000000 | script: ?

## Prefabs

- ExpoTheExplorer/Assets/Prefabs/Confetti.prefab | variant-of: - | scripts: -
- ExpoTheExplorer/Assets/Prefabs/TrayArea Variant.prefab | variant-of: ExpoTheExplorer/Assets/Prefabs/TrayArea.prefab | scripts: -
- ExpoTheExplorer/Assets/Prefabs/TrayArea.prefab | variant-of: - | scripts: WorldTrayView
- ExpoTheExplorer/Assets/Prefabs/UI/CelebrationConfetti.prefab | variant-of: ExpoTheExplorer/Assets/Prefabs/Confetti.prefab | scripts: ConfettiView
- ExpoTheExplorer/Assets/Prefabs/UI/HUDCanvas.prefab | variant-of: - | scripts: PowerupShopView, DayNumberView, HapticButton, KeysView, SoftMoneyView, GemsView, PowerupBarView, AutoCollectRunner
- ExpoTheExplorer/Assets/Prefabs/UI/MainScreenTutorialStoreHint.prefab | variant-of: - | scripts: MainScreenStoreHintPopup
- ExpoTheExplorer/Assets/Prefabs/UI/MainScreenTutorialWelcome.prefab | variant-of: - | scripts: MainScreenWelcomePopup
- ExpoTheExplorer/Assets/Prefabs/UI/MetaUnlockPopup.prefab | variant-of: - | scripts: MetaUnlockPopup
- ExpoTheExplorer/Assets/Prefabs/UI/NoKeysPopup.prefab | variant-of: - | scripts: HapticButton
- ExpoTheExplorer/Assets/Prefabs/UI/PowerupShop.prefab | variant-of: - | scripts: PowerupShopView
- ExpoTheExplorer/Assets/Prefabs/UI/TutorialPowerupIntro.prefab | variant-of: - | scripts: TutorialPowerupIntroView
- ExpoTheExplorer/Assets/Prefabs/UI/TutorialPowerupSpotlight.prefab | variant-of: - | scripts: TutorialPowerupSpotlightView
- ExpoTheExplorer/Assets/Prefabs/UI/TutorialStepHints.prefab | variant-of: - | scripts: TutorialStepHints
- ExpoTheExplorer/Assets/Prefabs/UI/Tickets/TicketCard UpDown.prefab | variant-of: ExpoTheExplorer/Assets/Prefabs/UI/Tickets/TicketCard.prefab | scripts: -
- ExpoTheExplorer/Assets/Prefabs/UI/Tickets/TicketCard.prefab | variant-of: - | scripts: ModificationSlotView, TicketCardView, TrayFillCounterView

## Scenes

- ExpoTheExplorer/Assets/Scenes/MainScreen.unity
- ExpoTheExplorer/Assets/Scenes/SampleScene.unity

## Runtime load surface

- ExpoTheExplorer/Assets/Resources | 47 file(s) | loaded by string at runtime; ships in every build
- ExpoTheExplorer/Assets/StompyRobot/SRDebugger/Resources | 41 file(s) | loaded by string at runtime; ships in every build
- ExpoTheExplorer/Assets/StompyRobot/SRDebugger/usr/Resources | 3 file(s) | loaded by string at runtime; ships in every build
- ExpoTheExplorer/Assets/TextMesh Pro/Resources | 21 file(s) | loaded by string at runtime; ships in every build
- ExpoTheExplorer/Assets/ThirdParty/Feel/NiceVibrations/Scripts/Components/Resources | 6 file(s) | loaded by string at runtime; ships in every build
