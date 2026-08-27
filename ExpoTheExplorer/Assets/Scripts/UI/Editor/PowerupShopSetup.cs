using System.Collections.Generic;
using ExpoTheExplorer.Session;
using ExpoTheExplorer.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI.EditorTools
{
    // Builds the main screen's powerup shop -- an open button, a panel, and the three
    // authored rows -- and wires PowerupShopView to all of it (.claude/powerup-plan.md
    // Adım 3b). The runtime half already exists; this step only produces the scene it
    // reads.
    //
    // IT BUILDS THE ROWS, and that is the one real difference from MetaShopSetup, which
    // deliberately does not: over there the row count comes from the catalog at run time
    // and only the catalog knows it. Here the answer is three, it is written in the
    // PowerupType enum, and it cannot change without a design decision -- so the rows are
    // authored objects like any other fixed piece of UI.
    //
    // Same posture as every other setup step in this project: it touches only the scene
    // that is already open, it does NOT save, and it refuses to REBUILD -- if the shop is
    // already there it reports what is missing and leaves the hierarchy alone, so styling
    // done by hand survives.
    //
    // It lives under Scripts/UI/Editor/ rather than Assets/Editor/ for the one hard
    // constraint blueprint.md records for that folder: it references PowerupShopView,
    // which is in the predefined Assembly-CSharp, and an asmdef assembly cannot reference
    // a predefined one at all.
    internal static class PowerupShopSetup
    {
        private const string MenuSceneName = "MainScreen";

        // The DAY scene gets one too since D-105, where an empty powerup opens it. The two
        // builds differ in exactly two ways -- no open button (the empty powerup is the
        // opener) and the bar's Shop field gets filled -- so they share this whole file
        // rather than growing a second near-identical setup step to drift out of sync.
        private const string DaySceneName = "SampleScene";

        // Only ever used when CREATING these objects. Nothing is looked up by name --
        // project rule since D-028, and MetaGroundsSetup is the cautionary tale: after the
        // author renamed its root, its name search found nothing and it would have built a
        // second hierarchy while logging that it had not.
        private const string RootName = "PowerupShop";
        private const string PanelName = "Panel";
        private const string OpenButtonName = "PowerupShopButton";
        private const string CloseButtonName = "CloseButton";
        private const string BackdropName = "Backdrop";
        private const string RowsName = "Rows";

        // A bottom sheet, like the meta shop's, so the grounds stay visible above it. The
        // shorter height is the honest difference: three fixed rows need far less room than
        // a scrolling catalog, and a half-empty panel reads as unfinished.
        private const float PanelHeightFraction = 0.46f;
        private const float PanelSideMargin = 32f;
        private const float PanelPadding = 24f;
        private const float TitleHeight = 64f;

        // STACKED ABOVE the meta shop's market button, which sits bottom-right at roughly
        // (-96, 96) with a 132 side. These two setup steps deliberately do NOT share
        // constants -- coupling them would mean one could not be run without the other --
        // so this is an informed guess, not a guarantee: if the author has moved the market
        // button, the two can overlap. Build() says so in its log rather than pretending to
        // know, and nudging a RectTransform is seconds of Inspector work.
        private static readonly Vector2 ButtonSize = new(132f, 132f);
        private static readonly Vector2 ButtonInset = new(-96f, 252f);

        // Rough on purpose, the same posture MetaShopSetup takes about its own numbers:
        // these are the values most likely to want nudging once real art exists, and
        // nudging them in the Inspector is quicker than arguing about them here.
        private const float RowHeight = 116f;
        private const float RowSpacing = 12f;
        private const float RowInnerPadding = 18f;
        private const float OwnedWidth = 90f;
        private const float PriceWidth = 110f;
        private const float BuyWidth = 160f;
        private const float CloseSize = 64f;

        // Display names for the three rows. THESE ARE LABELS, NOT CONTENT: they are the
        // powerups' fixed identities from GDD 5.2, not authored balancing data, and they
        // are written once into a scene object the author is then free to retype. Routing
        // three constant strings through PowerupConfig would put a second authority on the
        // asset for something that cannot vary per save.
        private const string AutoCollectLabel = "AUTO COLLECT";
        private const string TimeResetLabel = "TIME RESET";
        private const string NoiseClearLabel = "NOISE CLEAR";

        // The column headings, same posture as the three above: fixed identities of the
        // columns, written once into scene objects the author can then retype. Nothing reads
        // them back, and there is nothing per-save or per-Day about what a column contains.
        private const string HeaderRowName = "Header";
        private const string NameHeaderLabel = "NAME";
        private const string CountHeaderLabel = "COUNT";
        private const string PriceHeaderLabel = "PRICE";

        // Shorter than a row and smaller-typed, so the headings read as a legend rather than
        // as a fourth powerup the player cannot buy.
        private const float HeaderHeight = 48f;
        private const float HeaderFontSize = 22f;

        [MenuItem("ExpoTheExplorer/Meta/Build Powerup Shop")]
        private static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var isDayScene = scene.name == DaySceneName;

            if (scene.name != MenuSceneName && !isDayScene)
            {
                EditorUtility.DisplayDialog(
                    "Wrong scene",
                    $"Open '{MenuSceneName}' or '{DaySceneName}' first — this step only touches the scene that is " +
                    "already open, so that saving stays your decision.",
                    "OK");
                return;
            }

            // The DAY scene's powerup bar, and it is the anchor for everything below: it
            // names the Canvas the shop belongs on, and it is the thing that will open it.
            // Found by COMPONENT, never by name (D-028).
            var bar = isDayScene ? Object.FindAnyObjectByType<PowerupBarView>() : null;
            if (isDayScene && bar == null)
            {
                EditorUtility.DisplayDialog(
                    "No PowerupBarView",
                    $"Could not find a {nameof(PowerupBarView)} in '{DaySceneName}'. In the day scene the shop has no " +
                    "open button of its own — an empty powerup opens it — so without the bar there would be no way " +
                    "into what this step builds.",
                    "OK");
                return;
            }

            var canvas = isDayScene ? ResolveDayCanvas(bar) : ResolveMenuCanvas();
            if (canvas == null) return;

            // Found by COMPONENT, never by name. Two powerup shops in one scene is already
            // a bug, so the first one is the one there is.
            var existing = Object.FindAnyObjectByType<PowerupShopView>();
            if (existing != null)
            {
                ReportExisting(existing, isDayScene);

                // Still offered, because the shop existing and the BAR pointing at it are
                // two separate facts: a scene built before D-105 has the first without the
                // second. FillIfEmpty never overwrites, so re-running is safe.
                if (isDayScene) WireBarToShop(bar, existing);

                // The one thing this step will ADD to a shop it did not just build, and it is
                // additive only -- it inserts a first child and touches nothing else, so hand
                // styling still survives. A shop built before D-106 has rows with no legend
                // over them, and rebuilding the panel to get three labels would be the
                // destructive way round.
                if (TryAddHeaderRow(existing, out var headerProblem))
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    Debug.Log(
                        $"Added the NAME / COUNT / PRICE heading row to the existing shop on '{existing.name}'. " +
                        "It uses the same column widths as the rows, so it follows them at any panel width. " +
                        "Scene is dirty — save it yourself.",
                        existing);
                }
                else if (headerProblem != null)
                {
                    Debug.Log(
                        $"No heading row was added to the shop on '{existing.name}': {headerProblem}. Nothing was " +
                        "changed.",
                        existing);
                }

                Selection.activeObject = existing;
                return;
            }

            var view = BuildFresh(canvas, withOpenButton: !isDayScene);
            if (isDayScene) WireBarToShop(bar, view);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeObject = view;

            Debug.Log(
                $"Powerup shop built under '{canvas.name}' as its LAST child, so the panel draws over what is behind " +
                "it. " +
                (isDayScene
                    ? "No open button was built — in the day scene a powerup at 0 charges is the opener, and " +
                      $"{nameof(PowerupBarView)}'s Shop field now points here. Drag each powerup's inactive 'Add' " +
                      "child into the matching block's Empty Badge slot to finish. "
                    : "Its open button sits above the meta shop's market button; the two steps share no constants, " +
                      "so check they do not overlap and nudge the RectTransform if they do. ") +
                "A NAME / COUNT / PRICE heading row sits above the three rows, laid out with the same column " +
                "widths so it follows them at any panel width. " +
                "The price and owned labels are left EMPTY on purpose — PowerupShopView fills them from " +
                "PowerupConfig at run time, so a number typed here would be a second authority. " +
                "Scene is dirty — save it yourself.",
                view);
        }

        // The screen's own Canvas, found through MainScreenView rather than by taking the
        // first Canvas in the scene: the HUD is a Canvas of its own, and building the shop
        // inside it would put the panel on the wrong sorting layer -- and make the Gem
        // counter a child of the thing you spend Gems in. Same reasoning MetaShopSetup
        // writes down.
        private static Canvas ResolveMenuCanvas()
        {
            var screen = Object.FindAnyObjectByType<MainScreenView>();
            if (screen == null)
            {
                EditorUtility.DisplayDialog(
                    "No MainScreenView",
                    "Could not find MainScreenView, so there is no way to tell which Canvas is the screen's own " +
                    "(the HUD has a Canvas of its own). Open the built MainScreen scene.",
                    "OK");
                return null;
            }

            var canvas = screen.GetComponentInParent<Canvas>();
            if (canvas == null) EditorUtility.DisplayDialog("No Canvas", "MainScreenView is not under a Canvas.", "OK");

            return canvas;
        }

        // The BAR's Canvas, for the same reason the menu uses MainScreenView's: the day
        // scene carries several (the HUD, the backdrop, the popups), and the one the shop
        // belongs on is the one already carrying the control that opens it.
        private static Canvas ResolveDayCanvas(PowerupBarView bar)
        {
            var canvas = bar.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog(
                    "No Canvas",
                    $"The scene's {nameof(PowerupBarView)} is not under a Canvas, so there is nowhere to build the " +
                    "shop panel.",
                    "OK");
            }

            return canvas;
        }

        // Fills the bar's Shop field only when it is empty, like every other reference this
        // file writes: an author who has already pointed it somewhere meant to.
        private static void WireBarToShop(PowerupBarView bar, PowerupShopView view)
        {
            var serialized = new SerializedObject(bar);
            FillIfEmpty(serialized, "shop", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Every reference PowerupShopView refuses to open without, spelled the same way that
        // component spells them rather than as a second opinion about what "healthy" means.
        // D-036 is the recorded cost of letting those two lists drift: a setup step called a
        // shop fine that the screen then rejected at Start.
        private static readonly string[] RequiredFields =
        {
            "sessionHost", "panel", "closeButton",
            "autoCollect.buyButton", "timeReset.buyButton", "noiseClear.buyButton",
        };

        // Checked only on the MENU, because the day scene's shop deliberately has none --
        // it left the shared list above for that reason, and reporting it missing there
        // would be this step inventing a fault it created on purpose.
        private const string OpenButtonField = "openButton";

        // Reports rather than repairs, and rather than rebuilding. A half-wired shop is
        // usually a shop the author is midway through styling; blowing it away to produce a
        // correct one is the more destructive of the two mistakes this step can make.
        private static void ReportExisting(PowerupShopView view, bool isDayScene)
        {
            var serialized = new SerializedObject(view);
            var missing = new List<string>();

            if (!isDayScene)
            {
                var opener = serialized.FindProperty(OpenButtonField);
                if (opener == null || opener.objectReferenceValue == null) missing.Add(OpenButtonField);
            }

            foreach (var field in RequiredFields)
            {
                var property = serialized.FindProperty(field);
                if (property == null || property.objectReferenceValue == null) missing.Add(field);
            }

            if (missing.Count == 0)
            {
                Debug.Log(
                    $"A powerup shop already exists on '{view.name}' and every required reference is wired. " +
                    "Nothing was built — this step never rebuilds, so hand styling survives.",
                    view);
                return;
            }

            Debug.LogWarning(
                $"A powerup shop already exists on '{view.name}' but these references are empty: " +
                $"{string.Join(", ", missing)}. Nothing was rebuilt (hand styling survives) — either drag them in, " +
                "or delete the shop object and run this step again for a fresh one.",
                view);
        }

        private static PowerupShopView BuildFresh(Canvas canvas, bool withOpenButton)
        {
            var root = CreateRect(RootName, canvas.transform);
            Stretch(root);

            // LAST child, so the panel draws over the grounds behind it. The HUD is a
            // separate Canvas with its own sorting order and stays on top of both.
            root.SetAsLastSibling();

            // Registered before anything is added underneath, so a single Undo takes the
            // whole hierarchy back out rather than leaving an empty root behind.
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Build Powerup Shop");

            var openButton = withOpenButton ? BuildOpenButton(root) : null;

            // THE DAY SCENE GETS A BACKDROP AND THE MENU DOES NOT, and the difference is
            // what is behind each one. Freezing the day stops the ticket CLOCK; it does not
            // stop a finger, and the board sits in the 54% of the screen the sheet does not
            // cover -- so without something catching those touches the player could keep
            // dragging items into trays while shopping, and batch-deliver an order in a day
            // that is supposed to be held still. A stretched, raycast-catching Image is the
            // whole mechanism. The menu has nothing behind it that a stray tap can break.
            RectTransform backdrop = null;
            var backdropButton = withOpenButton ? null : BuildBackdrop(root, out backdrop);

            // The object PowerupShopView switches on and off. In the day scene that is the
            // backdrop, so the block and the sheet appear and vanish together as one modal;
            // on the menu the sheet is the whole shop.
            var sheet = BuildPanel(backdrop != null ? backdrop : root);
            var panel = backdrop != null ? backdrop : sheet;

            var closeButton = BuildCloseButton(sheet);
            var rows = BuildRowsContainer(sheet);

            BuildHeaderRow(rows);

            var autoCollect = BuildRow(rows, AutoCollectLabel);
            var timeReset = BuildRow(rows, TimeResetLabel);
            var noiseClear = BuildRow(rows, NoiseClearLabel);

            // Switched off here as well as at run time. PowerupShopView hides it in Start
            // for the D-036 reason, but a panel left open in the SCENE is what the author
            // sees every time they open the file, and it covers the screen they are editing.
            panel.gameObject.SetActive(false);

            var view = root.gameObject.AddComponent<PowerupShopView>();
            var serialized = new SerializedObject(view);

            // Found by COMPONENT, like everything else here. On this screen the provider is
            // MainScreenRoot, but asking for the base type keeps this step honest about what
            // it actually needs -- "whatever provides this scene's session".
            var host = Object.FindAnyObjectByType<SessionHost>();
            if (host == null)
            {
                Debug.LogWarning(
                    "No SessionHost was found in the scene, so the shop's Session Host field is empty and it will " +
                    "refuse to open. On the main screen that is MainScreenRoot (run ExpoTheExplorer > Meta > Wire " +
                    "MainScreen Session first, then drag the '--Session--' object in); in the day scene it is the " +
                    "GameManager object.",
                    view);
            }

            FillIfEmpty(serialized, "sessionHost", host);
            FillIfEmpty(serialized, "panel", panel.gameObject);
            if (openButton != null) FillIfEmpty(serialized, "openButton", openButton);
            if (backdropButton != null) FillIfEmpty(serialized, "backdropButton", backdropButton);
            FillIfEmpty(serialized, "closeButton", closeButton);

            WireRow(serialized, "autoCollect", autoCollect);
            WireRow(serialized, "timeReset", timeReset);
            WireRow(serialized, "noiseClear", noiseClear);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        // The nested block's three fields are addressed by a DOTTED path, which is how
        // SerializedObject reaches inside a [Serializable] class field. The strings must
        // track PowerupShopRow's field names; there is no compile-time link between them,
        // which is exactly why ReportExisting checks the same paths back.
        private static void WireRow(SerializedObject serialized, string block, Row row)
        {
            FillIfEmpty(serialized, $"{block}.buyButton", row.Buy);
            FillIfEmpty(serialized, $"{block}.ownedLabel", row.Owned);
            FillIfEmpty(serialized, $"{block}.priceLabel", row.Price);
        }

        private readonly struct Row
        {
            public Row(Button buy, TMP_Text owned, TMP_Text price)
            {
                Buy = buy;
                Owned = owned;
                Price = price;
            }

            public Button Buy { get; }
            public TMP_Text Owned { get; }
            public TMP_Text Price { get; }
        }

        private static RectTransform BuildPanel(RectTransform root)
        {
            var panel = CreateRect(PanelName, root);
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(1f, PanelHeightFraction);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.offsetMin = new Vector2(PanelSideMargin, PanelSideMargin);
            panel.offsetMax = new Vector2(-PanelSideMargin, 0f);

            var background = panel.gameObject.AddComponent<Image>();

            // Placeholder colour, matching the meta shop's so the two panels read as one
            // family until real art exists. Nearly opaque rather than a scrim, because rows
            // must be readable over whatever prop is behind them.
            background.color = new Color(0.08f, 0.09f, 0.12f, 0.96f);

            // Load-bearing rather than decoration: the grounds behind this are a ScrollRect,
            // and an open panel that does not eat raycasts lets a drag on the panel scroll
            // the map instead — which a player reads as the panel being broken.
            background.raycastTarget = true;

            var builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            if (builtin != null)
            {
                background.sprite = builtin;
                background.type = Image.Type.Sliced;
            }

            var title = CreateText("Title", panel, 40f);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(-2f * PanelPadding, TitleHeight);
            title.rectTransform.anchoredPosition = new Vector2(0f, -PanelPadding);
            title.text = "POWERUPS";

            return panel;
        }

        // It swallows the touch AND dismisses on one (D-107, reversing D-105's refusal to let
        // it close). The dismissal needs no hit-test: the sheet is built as this object's
        // CHILD and carries its own raycast-target Image, so UGUI hands a tap on the sheet to
        // the sheet and only an outside tap reaches the Button here.
        private static Button BuildBackdrop(RectTransform root, out RectTransform rectOut)
        {
            var rect = CreateRect(BackdropName, root);
            Stretch(rect);

            var image = rect.gameObject.AddComponent<Image>();

            // Dark and mostly transparent: the board behind must stay READABLE -- a player
            // buying Auto-Collect is looking at the mess they are about to clear. The alpha
            // is the number most likely to want nudging; nudge it in the Inspector.
            image.color = new Color(0f, 0f, 0f, 0.55f);

            // The line that does the actual work. An Image blocks raycasts by default, but
            // saying so here means a later restyle that swaps the sprite cannot quietly
            // turn the block off.
            image.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            // No transition, which matters here more than on an ordinary button: the default
            // ColorTint would flash the whole dim layer lighter on every press, so a tap on
            // the sheet's edge would look like the screen blinking.
            button.transition = Selectable.Transition.None;

            rectOut = rect;
            return button;
        }

        private static Button BuildOpenButton(RectTransform root)
        {
            var rect = CreateRect(OpenButtonName, root);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = ButtonSize;
            rect.anchoredPosition = ButtonInset;

            var background = rect.gameObject.AddComponent<Image>();

            // Placeholder, and a DIFFERENT colour from the market button's green on purpose:
            // two identical squares stacked in a corner is how an author taps the wrong one.
            background.color = new Color(0.24f, 0.28f, 0.52f);

            var builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (builtin != null)
            {
                background.sprite = builtin;
                background.type = Image.Type.Sliced;
            }

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            var label = CreateText("Label", rect, 24f);
            Stretch(label.rectTransform);
            label.text = "POWER\nUPS";

            return button;
        }

        private static Button BuildCloseButton(RectTransform panel)
        {
            var rect = CreateRect(CloseButtonName, panel);
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(CloseSize, CloseSize);
            rect.anchoredPosition = new Vector2(-PanelPadding, -PanelPadding);

            var background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(0.45f, 0.18f, 0.18f);

            var builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (builtin != null)
            {
                background.sprite = builtin;
                background.type = Image.Type.Sliced;
            }

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            var label = CreateText("Label", rect, 34f);
            Stretch(label.rectTransform);
            label.text = "X";

            return button;
        }

        private static RectTransform BuildRowsContainer(RectTransform panel)
        {
            var rows = CreateRect(RowsName, panel);
            rows.anchorMin = new Vector2(0f, 0f);
            rows.anchorMax = new Vector2(1f, 1f);
            rows.pivot = new Vector2(0.5f, 1f);
            rows.offsetMin = new Vector2(PanelPadding, PanelPadding);
            rows.offsetMax = new Vector2(-PanelPadding, -(PanelPadding + TitleHeight));

            // A layout group rather than three hand-placed rects, because the panel's width
            // is a fraction of the screen and therefore unknown here -- a phone and a tablet
            // would need different offsets. The group makes the rows follow whatever width
            // the author ends up with.
            var layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = RowSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;

            return rows;
        }

        // The legend the three rows are read against. It is a ROW, laid out by the same
        // HorizontalLayoutGroup with the same column widths, because that is the only way the
        // headings stay over their columns: the panel's width is a fraction of the screen and
        // unknown here, so any hand-placed heading would drift the moment the shop is opened
        // on a different aspect ratio.
        private static RectTransform BuildHeaderRow(RectTransform parent)
        {
            var row = CreateRect(HeaderRowName, parent);

            // No background Image at all, unlike a powerup row. The rows carry a faint tint
            // to separate them from each other; a heading that also had one would read as a
            // fourth, unbuyable powerup -- which is exactly the mistake a legend must not
            // make. Nothing to raycast either, so a drag across it reaches the panel.
            var height = row.gameObject.AddComponent<LayoutElement>();
            height.preferredHeight = HeaderHeight;
            height.flexibleHeight = 0f;

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = RowInnerPadding;
            layout.padding = new RectOffset((int)RowInnerPadding, (int)RowInnerPadding, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            // Each heading is built with the SAME width rule as the column under it --
            // flexible for the name, Fixed(OwnedWidth) and Fixed(PriceWidth) for the two
            // numbers. Duplicating the three constants here rather than reading a row back is
            // what keeps this method runnable on a shop whose rows do not exist yet.
            var name = CreateHeaderText(NameHeaderLabel, row, TextAlignmentOptions.MidlineLeft);
            Flexible(name.gameObject);

            var count = CreateHeaderText(CountHeaderLabel, row);
            Fixed(count.gameObject, OwnedWidth);

            var price = CreateHeaderText(PriceHeaderLabel, row);
            Fixed(price.gameObject, PriceWidth);

            // An empty rect over the BUY column, and it is load-bearing rather than tidiness:
            // without it the layout group hands the three headings the BUY column's width to
            // share, and every heading slides right of what it labels. It is deliberately
            // unlabelled -- a heading over a column of buttons has nothing to say.
            var buySpacer = CreateRect("BuySpacer", row);
            Fixed(buySpacer.gameObject, BuyWidth);

            return row;
        }

        // Dimmer and smaller than a row's text, which is the whole visual difference between
        // a legend and data. Uppercase comes from the constants, matching the panel's title
        // and the row names.
        private static TMP_Text CreateHeaderText(
            string label, RectTransform parent, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var text = CreateText(label, parent, HeaderFontSize, alignment);
            text.text = label;
            text.color = new Color(1f, 1f, 1f, 0.55f);
            return text;
        }

        // Adds the legend to a shop that was built before it existed. Non-destructive by
        // construction: it only ever inserts a first child, and it decides whether one is
        // already there WITHOUT looking anything up by name (D-028, and MetaGroundsSetup is
        // the cautionary tale -- a renamed object made its name search find nothing and it
        // would have built a second hierarchy while logging that it had not).
        //
        // The test is structural instead: the three wired BUY buttons name the three real
        // rows, so if the rows container's first child is one of them there is no header yet.
        // Rename the header, restyle it, replace it with your own -- the first child stops
        // being a known row and this leaves it alone. Delete it and it comes back.
        private static bool TryAddHeaderRow(PowerupShopView view, out string problem)
        {
            problem = null;

            var serialized = new SerializedObject(view);
            var knownRows = new List<Transform>();
            RectTransform rowsParent = null;

            foreach (var block in new[] { "autoCollect", "timeReset", "noiseClear" })
            {
                var buy = serialized.FindProperty($"{block}.buyButton")?.objectReferenceValue as Button;
                var row = buy != null ? buy.transform.parent : null;
                if (row == null) continue;

                knownRows.Add(row);
                rowsParent ??= row.parent as RectTransform;
            }

            if (rowsParent == null)
            {
                problem =
                    "no BUY button on this shop is wired to a row inside a rows container, so there is no way to " +
                    "tell where the headings would go";
                return false;
            }

            if (rowsParent.childCount > 0 && !knownRows.Contains(rowsParent.GetChild(0)))
            {
                problem = "its first row is not one of the three powerup rows, so something is already sitting there";
                return false;
            }

            var header = BuildHeaderRow(rowsParent);
            header.SetAsFirstSibling();
            Undo.RegisterCreatedObjectUndo(header.gameObject, "Add Powerup Shop Header");
            return true;
        }

        private static Row BuildRow(RectTransform parent, string displayName)
        {
            var row = CreateRect(displayName, parent);

            var background = row.gameObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.06f);
            background.raycastTarget = false;

            var height = row.gameObject.AddComponent<LayoutElement>();
            height.preferredHeight = RowHeight;
            height.flexibleHeight = 0f;

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = RowInnerPadding;
            layout.padding = new RectOffset(
                (int)RowInnerPadding, (int)RowInnerPadding, (int)(RowInnerPadding / 2f), (int)(RowInnerPadding / 2f));
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            // The name takes whatever width the fixed columns leave, so a longer powerup
            // name never pushes the price or the BUY off the row.
            var name = CreateText("Name", row, 30f, TextAlignmentOptions.MidlineLeft);
            name.text = displayName;
            Flexible(name.gameObject);

            // Left EMPTY. PowerupShopView writes the owned count and the price from
            // PowerupManager/PowerupConfig at run time; a number typed here would be a
            // second authority that starts lying the moment the config is retuned. The
            // placeholder dash is so an unwired label is visibly unwired in the Editor
            // rather than looking like a zero-width object.
            var owned = CreateText("Owned", row, 30f);
            owned.text = "–";
            Fixed(owned.gameObject, OwnedWidth);

            var price = CreateText("Price", row, 30f);
            price.text = "–";
            Fixed(price.gameObject, PriceWidth);

            var buy = BuildBuyButton(row);

            return new Row(buy, owned, price);
        }

        private static Button BuildBuyButton(RectTransform row)
        {
            var rect = CreateRect("Buy", row);

            var background = rect.gameObject.AddComponent<Image>();

            // The same green MetaShopView uses for an affordable row, so "you can buy this"
            // looks the same in both shops. Unlike that one, this button is switched OFF
            // when unaffordable rather than dimmed, so Unity's own disabled tint carries the
            // other state and no second colour is authored here.
            background.color = new Color(0.20f, 0.42f, 0.28f);

            var builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (builtin != null)
            {
                background.sprite = builtin;
                background.type = Image.Type.Sliced;
            }

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            var label = CreateText("Label", rect, 26f);
            Stretch(label.rectTransform);
            label.text = "BUY";

            Fixed(rect.gameObject, BuyWidth);
            return button;
        }

        private static void Fixed(GameObject go, float width)
        {
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
        }

        private static void Flexible(GameObject go)
        {
            var element = go.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
        }

        private static void FillIfEmpty(SerializedObject serialized, string name, Object value)
        {
            var property = serialized.FindProperty(name);
            if (property == null || property.objectReferenceValue != null) return;
            property.objectReferenceValue = value;
        }

        // The four helpers below are duplicated from MetaShopSetup rather than extracted
        // into a shared editor utility, and that is a deliberate reading of
        // abstraction-level.md rather than laziness: extracting them means editing a second
        // setup step that this task has no other reason to touch, and a shared base for
        // scene-construction steps is the kind of layer that quietly acquires everyone's
        // special cases. If a third step wants them, that is the moment to extract.
        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TMP_Text CreateText(
            string name, Transform parent, float size, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;

            // Assigned explicitly so a missing TMP Essentials import fails loudly rather
            // than producing an invisible label -- the same call the other setup steps make.
            if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }
            else
            {
                Debug.LogWarning(
                    "No TMP default font asset found while building the powerup shop. Import TMP Essentials " +
                    "(Window > TextMeshPro > Import TMP Essential Resources) and set the font by hand.");
            }

            return text;
        }
    }
}
