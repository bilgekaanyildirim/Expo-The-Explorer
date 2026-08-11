using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using Sirenix.OdinInspector;
using UnityEditor;

namespace ExpoTheExplorer.Editor
{
    [Serializable]
    public class DayEditorModel
    {
        [BoxGroup("Day"), LabelText("Day Index")]
        [InfoBox("$ValidationMessage", InfoMessageType.Error, nameof(HasValidationErrors))]
        [InfoBox("$ValidationMessage", InfoMessageType.Info, nameof(IsValid))]
        public int DayIndex;

        [BoxGroup("Day")]
        public int TicketsRequiredForDay = 10;

        [FoldoutGroup("Ticket Sequence"), OnInspectorGUI, PropertyOrder(-1)]
        private void DrawTicketCardPreview() => DayEditorTicketCardPreview.DrawStrip(TicketSequence, sharedTicketCardVisuals, ref ticketStripScrollPos);

        // Not part of the JSON, not serialized -- same "plain private field" convention as
        // sharedCatalog etc. below, just scroll-position UI state for the preview above.
        private UnityEngine.Vector2 ticketStripScrollPos;

        [TableList(ShowIndexLabels = true), FoldoutGroup("Ticket Sequence")]
        public List<DayEditorTicketEntry> TicketSequence = new();

        [TableList(ShowIndexLabels = true), FoldoutGroup("Board Timeline")]
        public List<DayEditorBoardSpawnEntry> BoardTimeline = new();

        [FoldoutGroup("Editor Overrides (Generate-only)"), HideLabel]
        public DayEditorMetaModel EditorMeta = new();

        // Only one nesting level is supported (matches DayDefinition.RetryVariant's own shape
        // and the current game design -- a retry variant never has its own retry variant), so
        // this foldout/field pair is hidden entirely on a nested (IsTopLevel=false) model.
        [FoldoutGroup("Retry Variant"), ShowIf(nameof(IsTopLevel))]
        public bool HasRetryVariant;

        [FoldoutGroup("Retry Variant"), ShowIf(nameof(IsTopLevel)), HideLabel]
        public DayEditorModel RetryVariant;

        // False for a RetryVariant embedded inside another DayEditorModel -- it has no JSON
        // file of its own, so Save/Duplicate/Delete (file-level actions) don't apply to it.
        // Generate/Add Ticket/Add Board Spawn (content-level actions) still apply at any level.
        [UnityEngine.HideInInspector]
        public bool IsTopLevel = true;

        // Null for a Day that has never been saved under any filename yet (new or duplicated,
        // pre-first-Save) -- set to DayIndex by FromDayJson (loaded from an existing file) and
        // updated by DayEditorWindow after each successful save. Lets the Window detect a
        // DayIndex rename and delete the old file instead of leaving an orphan behind.
        [UnityEngine.HideInInspector]
        public int? LastSavedDayIndex;

        [BoxGroup("Generate")]
        public int GenerateSeed;

        [BoxGroup("Generate"), Button]
        private void RandomizeSeed() => GenerateSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        [Button("Generate")]
        public void Generate()
        {
            if (sharedCatalog == null || sharedGameConfig == null || sharedTicketConfig == null || sharedBoardConfig == null)
            {
                EditorUtility.DisplayDialog("Generate", "Config assets aren't assigned yet -- set them in the toolbar above.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Generate", "This overwrites the current ticket sequence and board timeline. Continue?", "Generate", "Cancel"))
            {
                return;
            }

            var result = DayContentGenerator.Generate(sharedCatalog, sharedGameConfig, sharedTicketConfig, sharedBoardConfig, EditorMeta.ToJson(), TicketsRequiredForDay, GenerateSeed);
            TicketSequence = result.TicketSequence.Select(e => DayEditorTicketEntry.FromJson(e, sharedCatalog)).ToList();
            BoardTimeline = result.BoardTimeline.Select(e => DayEditorBoardSpawnEntry.FromJson(e, sharedCatalog)).ToList();
        }

        [Button("Add Ticket"), FoldoutGroup("Ticket Sequence")]
        private void AddTicket() => TicketSequence.Add(new DayEditorTicketEntry());

        [Button("Add Board Spawn"), FoldoutGroup("Board Timeline")]
        private void AddBoardSpawn() => BoardTimeline.Add(new DayEditorBoardSpawnEntry());

        // Recomputed on every draw pass -- DayValidator has no Random/BoardDistributor calls, its
        // cost is a small board-sized scan per step, negligible for live feedback without a timer.
        private bool IsValid => Validate().IsValid;
        private bool HasValidationErrors => !IsValid;

        private string ValidationMessage
        {
            get
            {
                var errors = Validate().Errors;
                return errors.Count > 0 ? string.Join("\n", errors) : "Valid.";
            }
        }

        private DayValidationResult Validate()
        {
            if (sharedGameConfig == null || sharedTicketConfig == null)
            {
                return new DayValidationResult(new List<string> { "Not configured yet -- assign the config assets above." });
            }

            return DayValidator.Validate(ToDayDefinition());
        }

        [ShowIf(nameof(IsTopLevel)), Button("Save"), EnableIf(nameof(IsValid)), GUIColor(0.4f, 0.85f, 0.4f, 1f)]
        public void Save() => onSaveRequested?.Invoke(this);

        [ShowIf(nameof(IsTopLevel)), Button("Duplicate")]
        private void Duplicate() => onDuplicateRequested?.Invoke(this);

        [ShowIf(nameof(IsTopLevel)), Button("Delete"), GUIColor(0.85f, 0.35f, 0.35f, 1f)]
        private void Delete() => onDeleteRequested?.Invoke(this);

        // Injected by DayEditorWindow right after construction -- shared config assets used for
        // Generate/Validate, and callbacks back into the Window's file-level operations. Not part
        // of the JSON, not serialized. Stored here (rather than passed as Generate's parameters)
        // specifically so [Button("Generate")] stays parameterless -- a parameterized Odin button
        // would render its own argument fields in the inspector instead of using the shared toolbar.
        private FoodCatalog sharedCatalog;
        private GameConfig sharedGameConfig;
        private TicketGenerationConfig sharedTicketConfig;
        private BoardDistributionConfig sharedBoardConfig;
        private TicketCardVisualsConfig sharedTicketCardVisuals;
        private Action<DayEditorModel> onSaveRequested;
        private Action<DayEditorModel> onDuplicateRequested;
        private Action<DayEditorModel> onDeleteRequested;

        public void Configure(
            FoodCatalog catalog,
            GameConfig gameConfig,
            TicketGenerationConfig ticketConfig,
            BoardDistributionConfig boardConfig,
            TicketCardVisualsConfig ticketCardVisuals,
            Action<DayEditorModel> onSave,
            Action<DayEditorModel> onDuplicate,
            Action<DayEditorModel> onDelete)
        {
            sharedCatalog = catalog;
            sharedGameConfig = gameConfig;
            sharedTicketConfig = ticketConfig;
            sharedBoardConfig = boardConfig;
            sharedTicketCardVisuals = ticketCardVisuals;
            onSaveRequested = onSave;
            onDuplicateRequested = onDuplicate;
            onDeleteRequested = onDelete;

            RetryVariant?.Configure(catalog, gameConfig, ticketConfig, boardConfig, ticketCardVisuals, null, null, null);
        }

        public DayJson ToDayJson()
        {
            return new DayJson
            {
                runtime = new DayRuntimeJson
                {
                    dayIndex = DayIndex,
                    ticketsRequiredForDay = TicketsRequiredForDay,
                    ticketSequence = TicketSequence.Select(e => e.ToJson()).ToArray(),
                    boardTimeline = BoardTimeline.Select(e => e.ToJson()).ToArray(),
                    hasRetryVariant = HasRetryVariant,
                    retryVariant = HasRetryVariant && RetryVariant != null ? RetryVariant.ToDayJson() : null,
                },
                editorMeta = EditorMeta.ToJson(),
            };
        }

        public DayDefinition ToDayDefinition()
        {
            var ticketSequence = TicketSequence.Select(e => e.ToResolved()).ToList();
            var boardTimeline = BoardTimeline.Select(e => e.ToResolved()).ToList();
            var retryVariant = HasRetryVariant && RetryVariant != null ? RetryVariant.ToDayDefinition() : null;
            return new DayDefinition(DayIndex, TicketsRequiredForDay, ticketSequence, boardTimeline, retryVariant);
        }

        public static DayEditorModel FromDayJson(DayJson json, FoodCatalog catalog, bool isTopLevel = true)
        {
            var runtime = json?.runtime;
            var model = new DayEditorModel
            {
                IsTopLevel = isTopLevel,
                DayIndex = runtime?.dayIndex ?? 0,
                TicketsRequiredForDay = runtime?.ticketsRequiredForDay ?? 10,
                TicketSequence = (runtime?.ticketSequence ?? Array.Empty<TicketEntryJson>())
                    .Select(e => DayEditorTicketEntry.FromJson(e, catalog)).ToList(),
                BoardTimeline = (runtime?.boardTimeline ?? Array.Empty<BoardSpawnEntryJson>())
                    .Select(e => DayEditorBoardSpawnEntry.FromJson(e, catalog)).ToList(),
                EditorMeta = DayEditorMetaModel.FromJson(json?.editorMeta),
                HasRetryVariant = runtime?.hasRetryVariant ?? false,
            };

            if (model.HasRetryVariant && runtime?.retryVariant != null)
            {
                model.RetryVariant = FromDayJson(runtime.retryVariant, catalog, isTopLevel: false);
            }

            return model;
        }

        // Round-trips through the same JSON conversion used for Save/Load rather than a second,
        // hand-written deep-copy -- the two paths can't drift out of sync with each other.
        public DayEditorModel Clone(FoodCatalog catalog) => FromDayJson(ToDayJson(), catalog, IsTopLevel);
    }

    [Serializable]
    public class DayEditorModification
    {
        public ModificationConfig Config;
        public bool IsAddition;

        public static DayEditorModification FromJson(ModificationEntryJson json, FoodCatalog catalog) => new()
        {
            Config = catalog.GetModificationById(json.modificationId),
            IsAddition = json.isAddition,
        };

        public ModificationEntryJson ToJson() => new()
        {
            modificationId = Config != null ? Config.Id : string.Empty,
            isAddition = IsAddition,
        };

        public Modification ToResolved() => new(Config, IsAddition);
    }

    [Serializable]
    public class DayEditorTicketEntry
    {
        [TableColumnWidth(120, false)] public FoodItemConfig MainItem;
        [TableColumnWidth(120, false)] public FoodItemConfig SideItem;
        [TableColumnWidth(120, false)] public FoodItemConfig DrinkItem;
        public List<DayEditorModification> Modifications = new();
        public PatienceType PatienceType = PatienceType.Normal;
        [LabelText("Name Override")] public string CustomerNameOverride;
        [LabelText("Time Override")] public float TimeLimitSecondsOverride;

        public static DayEditorTicketEntry FromJson(TicketEntryJson json, FoodCatalog catalog) => new()
        {
            MainItem = catalog.GetById(json.mainItemId),
            SideItem = string.IsNullOrEmpty(json.sideItemId) ? null : catalog.GetById(json.sideItemId),
            DrinkItem = string.IsNullOrEmpty(json.drinkItemId) ? null : catalog.GetById(json.drinkItemId),
            Modifications = (json.modifications ?? Array.Empty<ModificationEntryJson>())
                .Select(m => DayEditorModification.FromJson(m, catalog)).ToList(),
            PatienceType = Enum.TryParse<PatienceType>(json.patienceType, out var patienceType) ? patienceType : PatienceType.Normal,
            CustomerNameOverride = json.customerNameOverride,
            TimeLimitSecondsOverride = json.timeLimitSecondsOverride,
        };

        public TicketEntryJson ToJson() => new()
        {
            mainItemId = MainItem != null ? MainItem.Id : string.Empty,
            sideItemId = SideItem != null ? SideItem.Id : string.Empty,
            drinkItemId = DrinkItem != null ? DrinkItem.Id : string.Empty,
            modifications = Modifications.Select(m => m.ToJson()).ToArray(),
            patienceType = PatienceType.ToString(),
            customerNameOverride = CustomerNameOverride,
            timeLimitSecondsOverride = TimeLimitSecondsOverride,
        };

        public ResolvedTicketEntry ToResolved()
        {
            var mods = Modifications.Select(m => m.ToResolved()).ToList();
            return new ResolvedTicketEntry(MainItem, SideItem, DrinkItem, mods, PatienceType, CustomerNameOverride, TimeLimitSecondsOverride);
        }
    }

    [Serializable]
    public class DayEditorBoardSpawnEntry
    {
        [LabelText("Trigger Step (-1 = Day Start)")]
        public int TriggerStepIndex = -1;

        public FoodItemConfig Item;
        public List<DayEditorModification> Modifications = new();
        public bool UseExactCell;
        [ShowIf(nameof(UseExactCell))] public int X;
        [ShowIf(nameof(UseExactCell))] public int Y;

        public static DayEditorBoardSpawnEntry FromJson(BoardSpawnEntryJson json, FoodCatalog catalog) => new()
        {
            TriggerStepIndex = json.triggerStepIndex,
            Item = catalog.GetById(json.itemId),
            Modifications = (json.modifications ?? Array.Empty<ModificationEntryJson>())
                .Select(m => DayEditorModification.FromJson(m, catalog)).ToList(),
            UseExactCell = json.useExactCell,
            X = json.x,
            Y = json.y,
        };

        public BoardSpawnEntryJson ToJson() => new()
        {
            triggerStepIndex = TriggerStepIndex,
            itemId = Item != null ? Item.Id : string.Empty,
            modifications = Modifications.Select(m => m.ToJson()).ToArray(),
            useExactCell = UseExactCell,
            x = X,
            y = Y,
        };

        public ResolvedBoardSpawnEntry ToResolved()
        {
            var mods = Modifications.Select(m => m.ToResolved()).ToList();
            return new ResolvedBoardSpawnEntry(TriggerStepIndex, Item, mods, UseExactCell, X, Y);
        }
    }

    [Serializable]
    public class DayEditorMetaModel
    {
        private const string TicketGenGroup = nameof(HasTicketGenerationOverride);
        private const string BoardDistGroup = nameof(HasBoardDistributionOverride);

        [ToggleGroup(TicketGenGroup, "Ticket Generation Override")] public bool HasTicketGenerationOverride;
        [ToggleGroup(TicketGenGroup, "Ticket Generation Override")] public float SideInclusionChanceOverride;
        [ToggleGroup(TicketGenGroup, "Ticket Generation Override")] public float DrinkInclusionChanceOverride;
        [ToggleGroup(TicketGenGroup, "Ticket Generation Override")] public float ModificationCountLambdaOverride;

        [ToggleGroup(BoardDistGroup, "Board Distribution Override")] public bool HasBoardDistributionOverride;
        [ToggleGroup(BoardDistGroup, "Board Distribution Override")] public float NoiseLeakCountLambdaOverride;
        [ToggleGroup(BoardDistGroup, "Board Distribution Override")] public int GuaranteedTicketCountOverride;
        [ToggleGroup(BoardDistGroup, "Board Distribution Override")] public int LeakDepthOverride;
        [ToggleGroup(BoardDistGroup, "Board Distribution Override")] public int MaxLeakCountOverride;

        public DayEditorMetaJson ToJson() => new()
        {
            hasTicketGenerationOverride = HasTicketGenerationOverride,
            sideInclusionChanceOverride = SideInclusionChanceOverride,
            drinkInclusionChanceOverride = DrinkInclusionChanceOverride,
            modificationCountLambdaOverride = ModificationCountLambdaOverride,
            hasBoardDistributionOverride = HasBoardDistributionOverride,
            noiseLeakCountLambdaOverride = NoiseLeakCountLambdaOverride,
            guaranteedTicketCountOverride = GuaranteedTicketCountOverride,
            leakDepthOverride = LeakDepthOverride,
            maxLeakCountOverride = MaxLeakCountOverride,
        };

        public static DayEditorMetaModel FromJson(DayEditorMetaJson json)
        {
            if (json == null) return new DayEditorMetaModel();

            return new DayEditorMetaModel
            {
                HasTicketGenerationOverride = json.hasTicketGenerationOverride,
                SideInclusionChanceOverride = json.sideInclusionChanceOverride,
                DrinkInclusionChanceOverride = json.drinkInclusionChanceOverride,
                ModificationCountLambdaOverride = json.modificationCountLambdaOverride,
                HasBoardDistributionOverride = json.hasBoardDistributionOverride,
                NoiseLeakCountLambdaOverride = json.noiseLeakCountLambdaOverride,
                GuaranteedTicketCountOverride = json.guaranteedTicketCountOverride,
                LeakDepthOverride = json.leakDepthOverride,
                MaxLeakCountOverride = json.maxLeakCountOverride,
            };
        }
    }
}
