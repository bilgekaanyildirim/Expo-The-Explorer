using System;
using System.Collections.Generic;
using System.Linq;
using ExpoTheExplorer.Core;
using ExpoTheExplorer.Data;
using ExpoTheExplorer.Systems.DaySystem;
using ExpoTheExplorer.Systems.TicketSystem;
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
        private void DrawTicketCardPreview() => DayEditorTicketCardPreview.DrawStrip(TicketSequence, sharedTicketCardVisuals, ref ticketStripScrollPos, ref selectedTicketIndex, ref draggedTicketIndex, ref ticketDragStartMousePos);

        // Not part of the JSON, not serialized -- same "plain private field" convention as
        // sharedCatalog etc. below, just UI state for the preview/editor above.
        private UnityEngine.Vector2 ticketStripScrollPos;
        private int selectedTicketIndex = -1;
        private int draggedTicketIndex = -1;
        private UnityEngine.Vector2 ticketDragStartMousePos;

        [FoldoutGroup("Ticket Sequence"), OnInspectorGUI, PropertyOrder(-0.5f)]
        private void DrawSelectedTicketEditor()
        {
            if (selectedTicketIndex < 0 || selectedTicketIndex >= TicketSequence.Count)
            {
                EditorGUILayout.HelpBox("Select a ticket card above to edit it.", UnityEditor.MessageType.Info);
                return;
            }

            var entry = TicketSequence[selectedTicketIndex];

            EditorGUILayout.LabelField($"Editing Ticket #{selectedTicketIndex}", UnityEditor.EditorStyles.boldLabel);

            if (UnityEngine.GUILayout.Button("Generate Random Ticket"))
            {
                if (sharedCatalog == null || sharedTicketConfig == null)
                {
                    EditorUtility.DisplayDialog("Generate Random Ticket", "Config assets aren't assigned yet -- set them in the toolbar above.", "OK");
                }
                else if (EditorUtility.DisplayDialog("Generate Random Ticket", "This overwrites this ticket's Main/Side/Drink/Modifications/Patience Type. Continue?", "Generate", "Cancel"))
                {
                    // Fresh, unseeded TicketFactory per click -- reuses the exact same generation
                    // logic (weighted Main pick, Side/Drink inclusion chance, modification
                    // count/selection) DayContentGenerator uses for a whole Day, just for this
                    // one ticket, so a re-click always gives a genuinely different result.
                    var factory = new TicketFactory(sharedTicketConfig);
                    var patienceType = factory.PickRandomPatienceType();
                    var ticket = factory.Create(sharedCatalog.Items, factory.PickRandomCustomerName(), patienceType);
                    var regenerated = DayEditorTicketEntry.FromTicket(ticket);
                    entry.MainItem = regenerated.MainItem;
                    entry.SideItem = regenerated.SideItem;
                    entry.DrinkItem = regenerated.DrinkItem;
                    entry.Modifications = regenerated.Modifications;
                    entry.PatienceType = regenerated.PatienceType;
                }
            }

            EditorGUILayout.Space();

            entry.MainItem = DrawCategoryItemField("Main Item", entry.MainItem, FoodCategory.Main, sharedCatalog);
            entry.SideItem = DrawCategoryItemField("Side Item", entry.SideItem, FoodCategory.Side, sharedCatalog);
            entry.DrinkItem = DrawCategoryItemField("Drink Item", entry.DrinkItem, FoodCategory.Drink, sharedCatalog);
            entry.PatienceType = (PatienceType)EditorGUILayout.EnumPopup("Patience Type", entry.PatienceType);

            entry.CustomerNameOverride = EditorGUILayout.TextField("Name Override", entry.CustomerNameOverride);
            entry.TimeLimitSecondsOverride = EditorGUILayout.FloatField("Time Override", entry.TimeLimitSecondsOverride);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Modifications", UnityEditor.EditorStyles.boldLabel);
            for (var i = 0; i < entry.Modifications.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                entry.Modifications[i].Config = (ModificationConfig)EditorGUILayout.ObjectField(entry.Modifications[i].Config, typeof(ModificationConfig), false);
                entry.Modifications[i].IsAddition = EditorGUILayout.ToggleLeft("Addition", entry.Modifications[i].IsAddition, UnityEngine.GUILayout.Width(80));
                var removeClicked = UnityEngine.GUILayout.Button("x", UnityEngine.GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
                if (removeClicked)
                {
                    entry.Modifications.RemoveAt(i);
                    break; // list mutated mid-loop -- next OnGUI pass redraws the rest
                }
            }
            if (UnityEngine.GUILayout.Button("+ Add Modification"))
            {
                entry.Modifications.Add(new DayEditorModification());
            }

            EditorGUILayout.Space();
            if (UnityEngine.GUILayout.Button("Delete This Ticket"))
            {
                TicketSequence.RemoveAt(selectedTicketIndex);
                selectedTicketIndex = -1;
            }
        }

        // Restricts the picker to items of the given category -- ObjectField can't filter by a
        // field value, only by Type, so a plain ObjectField would let e.g. a Side item be
        // assigned into the Main slot. Falls back to the old unfiltered ObjectField when the
        // catalog isn't assigned yet, so editing isn't blocked before the toolbar is configured.
        private static FoodItemConfig DrawCategoryItemField(string label, FoodItemConfig current, FoodCategory category, FoodCatalog catalog)
        {
            if (catalog == null)
            {
                return (FoodItemConfig)EditorGUILayout.ObjectField(label, current, typeof(FoodItemConfig), false);
            }

            var options = catalog.Items.Where(item => item.Category == category).ToList();

            // Keep a mismatched/orphaned current value visible instead of silently dropping it
            // (e.g. data authored before this filter existed, or a category changed since).
            if (current != null && !options.Contains(current))
            {
                options.Insert(0, current);
            }

            var labels = new string[options.Count + 1];
            labels[0] = "None";
            for (var i = 0; i < options.Count; i++)
            {
                labels[i + 1] = options[i].DisplayName;
            }

            var currentIndex = current == null ? 0 : options.IndexOf(current) + 1;
            var selectedIndex = EditorGUILayout.Popup(label, currentIndex, labels);
            return selectedIndex == 0 ? null : options[selectedIndex - 1];
        }

        [UnityEngine.HideInInspector]
        public List<DayEditorTicketEntry> TicketSequence = new();

        [FoldoutGroup("Board Timeline"), OnInspectorGUI, PropertyOrder(-1)]
        private void DrawDayStartPreview() => DayEditorDayStartPreview.DrawGrid(sharedGameConfig, sharedBoardVisuals, BoardTimeline);

        // Only TriggerStepIndex == -1 (Day Start) entries are ever replayed at runtime
        // (BoardDistributor took over everything after Day Start, see decisions.md D-001
        // Phase 2/3) -- this table is Day Start authoring, hand-edited, not simulated or
        // regenerated. AddBoardSpawn() already defaults new rows to -1.
        [InfoBox("Only Trigger Step -1 (Day Start) entries are used by the live game. Other values are ignored at runtime.")]
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

        [Button("Generate")]
        public void Generate()
        {
            if (sharedCatalog == null || sharedTicketConfig == null)
            {
                EditorUtility.DisplayDialog("Generate", "Config assets aren't assigned yet -- set them in the toolbar above.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Generate", "This overwrites the current ticket sequence. Continue?", "Generate", "Cancel"))
            {
                return;
            }

            // No user-facing seed field -- every click gets its own fresh randomness, not
            // reproducible on purpose (nothing else persists a seed for this Day either).
            var seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            var result = DayContentGenerator.Generate(sharedCatalog, sharedTicketConfig, EditorMeta.ToJson(), TicketsRequiredForDay, seed);
            TicketSequence = result.Select(e => DayEditorTicketEntry.FromJson(e, sharedCatalog)).ToList();
        }

        [Button("Add Ticket"), FoldoutGroup("Ticket Sequence")]
        private void AddTicket() => TicketSequence.Add(new DayEditorTicketEntry());

        [Button("Add Board Spawn"), FoldoutGroup("Board Timeline")]
        private void AddBoardSpawn() => BoardTimeline.Add(new DayEditorBoardSpawnEntry());

        // Recomputed on every draw pass -- DayValidator is a cheap ticketSequence.Count check,
        // negligible for live feedback without a timer.
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
        private TicketCardVisualsConfig sharedTicketCardVisuals;
        private BoardVisualsConfig sharedBoardVisuals;
        private Action<DayEditorModel> onSaveRequested;
        private Action<DayEditorModel> onDuplicateRequested;
        private Action<DayEditorModel> onDeleteRequested;

        public void Configure(
            FoodCatalog catalog,
            GameConfig gameConfig,
            TicketGenerationConfig ticketConfig,
            TicketCardVisualsConfig ticketCardVisuals,
            BoardVisualsConfig boardVisuals,
            Action<DayEditorModel> onSave,
            Action<DayEditorModel> onDuplicate,
            Action<DayEditorModel> onDelete)
        {
            sharedCatalog = catalog;
            sharedGameConfig = gameConfig;
            sharedTicketConfig = ticketConfig;
            sharedTicketCardVisuals = ticketCardVisuals;
            sharedBoardVisuals = boardVisuals;
            onSaveRequested = onSave;
            onDuplicateRequested = onDuplicate;
            onDeleteRequested = onDelete;

            RetryVariant?.Configure(catalog, gameConfig, ticketConfig, ticketCardVisuals, boardVisuals, null, null, null);
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
        public FoodItemConfig MainItem;
        public FoodItemConfig SideItem;
        public FoodItemConfig DrinkItem;
        public List<DayEditorModification> Modifications = new();
        public PatienceType PatienceType = PatienceType.Normal;
        public string CustomerNameOverride;
        public float TimeLimitSecondsOverride;

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

        // Maps a freshly-rolled runtime Ticket (TicketFactory.Create) into editor data --
        // used by DrawSelectedTicketEditor's "Generate Random Ticket" button. Deliberately
        // doesn't set CustomerNameOverride/TimeLimitSecondsOverride: those are Day Editor
        // authoring concepts TicketFactory has no notion of, left untouched by design.
        public static DayEditorTicketEntry FromTicket(Ticket ticket) => new()
        {
            MainItem = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Main),
            SideItem = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Side),
            DrinkItem = ticket.RequiredItems.FirstOrDefault(item => item.Category == FoodCategory.Drink),
            Modifications = ticket.Modifications.Select(m => new DayEditorModification { Config = m.Config, IsAddition = m.IsAddition }).ToList(),
            PatienceType = ticket.PatienceType,
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
        [ToggleGroup(BoardDistGroup, "Board Distribution Override")] public GuaranteedTicketCountMode GuaranteedTicketCountModeOverride;
        [ToggleGroup(BoardDistGroup, "Board Distribution Override")] public int GuaranteedTicketCountOverride;
        [ToggleGroup(BoardDistGroup, "Board Distribution Override")] public float GuaranteedTicketCountLambdaOverride;
        [ToggleGroup(BoardDistGroup, "Board Distribution Override")] public float EarlyTicketWeightDecayOverride;
        [ToggleGroup(BoardDistGroup, "Board Distribution Override")] public float UrgentTimeThresholdSecondsOverride;
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
            guaranteedTicketCountModeOverride = GuaranteedTicketCountModeOverride,
            guaranteedTicketCountOverride = GuaranteedTicketCountOverride,
            guaranteedTicketCountLambdaOverride = GuaranteedTicketCountLambdaOverride,
            earlyTicketWeightDecayOverride = EarlyTicketWeightDecayOverride,
            urgentTimeThresholdSecondsOverride = UrgentTimeThresholdSecondsOverride,
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
                GuaranteedTicketCountModeOverride = json.guaranteedTicketCountModeOverride,
                GuaranteedTicketCountOverride = json.guaranteedTicketCountOverride,
                GuaranteedTicketCountLambdaOverride = json.guaranteedTicketCountLambdaOverride,
                EarlyTicketWeightDecayOverride = json.earlyTicketWeightDecayOverride,
                UrgentTimeThresholdSecondsOverride = json.urgentTimeThresholdSecondsOverride,
                LeakDepthOverride = json.leakDepthOverride,
                MaxLeakCountOverride = json.maxLeakCountOverride,
            };
        }
    }
}
