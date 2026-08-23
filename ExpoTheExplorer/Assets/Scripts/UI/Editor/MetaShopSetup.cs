using System.Collections.Generic;
using ExpoTheExplorer.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI.EditorTools
{
    // Builds the shop's container -- market button and an empty panel -- and wires
    // MetaShopView to it (meta-shop-plan.md Ş1). The rows inside the panel are NOT built
    // here: they come from the catalog at run time in Ş2, because how many there are
    // depends on the location and on what the player already owns, and the catalog is their
    // single authority (D-015).
    //
    // Same posture as MetaGroundsSetup and the other meta steps: it touches only the scene
    // that is already open, it does NOT save, and it refuses to REBUILD -- if the shop is
    // already there it leaves the hierarchy alone, so styling done by hand survives.
    internal static class MetaShopSetup
    {
        private const string SceneName = "MainScreen";

        // Only used when CREATING these objects. Nothing is ever looked up by name --
        // project rule since 2026-08-21 (D-028), and MetaGroundsSetup is the cautionary
        // tale: after the author renamed its root, its name search found nothing and it
        // would have built a second hierarchy while logging that it had not.
        private const string RootName = "MetaShop";
        private const string PanelName = "Panel";
        private const string ButtonName = "MarketButton";

        // The panel is a bottom sheet rather than a full-screen page: the grounds stay
        // visible above it, which is the whole reason a decoration shop sits on this screen
        // at all -- you buy a prop while looking at where it will go.
        private const float PanelHeightFraction = 0.68f;
        private const float PanelSideMargin = 32f;

        // Bottom-right, and large: this is the screen's second action after Play, and a
        // corner button on a phone has to clear a thumb.
        private static readonly Vector2 ButtonSize = new(132f, 132f);
        private static readonly Vector2 ButtonInset = new(-96f, 96f);

        // Rough on purpose. Row height and spacing are the two numbers most likely to want
        // nudging once real art is in, and nudging them in the Inspector is quicker than
        // arguing about them here -- this step never touches an existing shop again.
        private const float RowHeight = 132f;
        private const float RowSpacing = 12f;
        private const float ListPadding = 20f;
        private const float IconSize = 96f;
        private const float BuyWidth = 150f;
        private const float PriceWidth = 130f;

        // The confirm popup. Same posture as the row numbers: rough, and quicker to nudge
        // in the Inspector than to argue about here.
        private static readonly Vector2 PopupSize = new(620f, 430f);
        private const float PopupPadding = 28f;
        private const float PopupButtonHeight = 96f;

        [MenuItem("ExpoTheExplorer/Meta/Build Meta Shop")]
        private static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                EditorUtility.DisplayDialog(
                    "Wrong scene",
                    $"Open the '{SceneName}' scene first — this step only touches the scene that is already open, " +
                    "so that saving stays your decision.",
                    "OK");
                return;
            }

            // The MainScreen canvas, found via MainScreenView rather than by taking the
            // first Canvas in the scene: the HUD is a Canvas of its own, and building the
            // shop inside it would put the panel on the wrong sorting layer -- and worse,
            // would make the wallet a child of the thing you spend it in.
            var screen = Object.FindAnyObjectByType<MainScreenView>();
            if (screen == null)
            {
                EditorUtility.DisplayDialog(
                    "No MainScreenView",
                    "Could not find MainScreenView, so there is no way to tell which Canvas is the screen's own " +
                    "(the HUD has a Canvas of its own). Open the built MainScreen scene.",
                    "OK");
                return;
            }

            var canvas = screen.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("No Canvas", "MainScreenView is not under a Canvas.", "OK");
                return;
            }

            // Found by COMPONENT, not by name. Two shops in one scene is already a bug, so
            // the first one is the one there is.
            var existing = Object.FindAnyObjectByType<MetaShopView>();
            if (existing != null && !TryReplaceIncomplete(existing))
            {
                ReportExisting(existing);
                EditorSceneManager.MarkSceneDirty(scene);
                Selection.activeObject = existing;
                return;
            }

            var view = BuildFresh(canvas);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeObject = view;

            Debug.Log(
                $"Meta shop built under '{canvas.name}' as its LAST child, so the panel draws over the grounds. " +
                "The HUD stays on top of both — its Canvas carries a sorting order of its own. " +
                "Scene is dirty — save it yourself.",
                view);
        }

        // Every reference MetaShopView refuses to run without. Deliberately the same list as
        // that component's own check rather than a second opinion about what "healthy" means:
        // if the two ever drifted, this step would call a shop fine that the screen then
        // rejected at Start, which is precisely the confusion that produced D-036.
        private static readonly string[] RequiredFields =
        {
            "marketButton", "panel", "sessionHost", "grounds", "rowsParent", "rowTemplate",
            "confirmPopup", "confirmBox", "confirmBuyButton", "confirmCancelButton",
        };

        // An OLDER shop -- built before a later step added its objects and its references --
        // cannot be repaired in place: the list and the popup are nested hierarchies
        // (viewport → content → template; the popup root and its box) and threading new
        // pieces into a half-built one in the right order would mean maintaining two
        // construction paths forever. So the only repair is a rebuild, and this asks first.
        //
        // The step's promise was never "never rebuild"; it was "never rebuild BEHIND YOUR
        // BACK". Telling the author what is missing and letting them decide keeps that,
        // where silently reporting and doing nothing left them stuck for three steps.
        private static bool TryReplaceIncomplete(MetaShopView existing)
        {
            var missing = MissingFields(existing);
            if (missing.Count == 0) return false;

            var root = existing.gameObject;
            var confirmed = EditorUtility.DisplayDialog(
                "Meta shop is out of date",
                $"'{root.name}' is missing {missing.Count} reference(s) that this step now wires:\n\n" +
                $"{string.Join(", ", missing)}\n\n" +
                "These come with objects it does not have, so they cannot be filled in by dragging — the " +
                "shop has to be rebuilt.\n\n" +
                "Rebuild it? Anything you styled on the old shop is lost, and the scene is NOT saved either " +
                "way, so you can still leave without saving.",
                "Rebuild",
                "Leave it alone");

            if (!confirmed) return false;

            Undo.DestroyObjectImmediate(root);

            Debug.Log(
                $"Replaced the out-of-date meta shop; it was missing: {string.Join(", ", missing)}.");
            return true;
        }

        private static List<string> MissingFields(MetaShopView view)
        {
            var serialized = new SerializedObject(view);
            var missing = new List<string>();

            foreach (var field in RequiredFields)
            {
                var property = serialized.FindProperty(field);

                // A property that does not exist counts as missing too: it means this step
                // and the component are out of step, which the author needs to see rather
                // than have quietly skipped.
                if (property == null || property.objectReferenceValue == null) missing.Add(field);
            }

            return missing;
        }

        // There is deliberately nothing to "refresh" here. This step's references point at
        // objects it CREATED, so once the shop exists it cannot re-point them without
        // searching -- which is the thing the project rule forbids. So it reports instead:
        // an empty field is named, and the fix is the author's drag or a delete-and-re-run.
        // Reached only when the shop is COMPLETE, or when the author declined the rebuild
        // TryReplaceIncomplete offered. Either way there is nothing to do but say so: this
        // step's references point at objects it created, so it cannot re-point them without
        // searching, which the project rule forbids (D-028).
        private static void ReportExisting(MetaShopView view)
        {
            var missing = MissingFields(view);

            if (missing.Count == 0)
            {
                Debug.Log(
                    $"Meta shop already present (found '{view.name}') and fully wired; nothing was rebuilt and your " +
                    "layout was left alone.",
                    view);
                return;
            }

            Debug.LogWarning(
                $"Meta shop left as it is (found '{view.name}'), still missing: {string.Join(", ", missing)}. " +
                "The screen will keep itself hidden rather than sit over the map, and the market button will not " +
                "respond. Run this step again and accept the rebuild when you want it working.",
                view);
        }

        private static MetaShopView BuildFresh(Canvas canvas)
        {
            var root = CreateRect(RootName, canvas.transform);
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Build Meta Shop");
            Stretch(root);

            // Last sibling = drawn last = on top of everything else on this canvas. The
            // grounds are the first sibling (a backdrop), so this ordering is what puts the
            // shop over them. Sibling order is the only thing that decides depth inside one
            // Canvas -- and one Canvas is the point: the bug that ate the HUD earlier came
            // from two Canvases both sitting at sorting order 0.
            root.SetAsLastSibling();

            var view = Undo.AddComponent<MetaShopView>(root.gameObject);

            var panel = BuildPanel(root);
            var list = BuildList(panel);

            // Between the panel and the market button, and that position is the whole
            // design: it draws over the list, while the market button still draws over IT.
            // The machine can go from ÖNİZLEME straight to KAPALI, so the market button has
            // to stay reachable -- anything covering it would lock the player in a preview
            // with a ghost on the map and no way out. That mattered more when a full-screen
            // backdrop lived here (removed in D-038); the ordering is kept because the
            // reason survives whatever the popup is made of.
            var confirm = BuildConfirmPopup(root);

            // Created AFTER the panel, so it is the later sibling and stays on top of it.
            // That is not cosmetic: the panel covers the bottom-right corner, so a button
            // drawn underneath it could not be tapped to close the panel again, and the
            // shop would be a one-way door.
            var button = BuildMarketButton(root);

            var serialized = new SerializedObject(view);
            Set(serialized, "marketButton", button);
            Set(serialized, "panel", panel.gameObject);
            Set(serialized, "rowsParent", list.Content);
            Set(serialized, "rowTemplate", list.RowTemplate);
            Set(serialized, "confirmPopup", confirm.Root);
            Set(serialized, "confirmBox", confirm.Box);
            Set(serialized, "confirmNameLabel", confirm.NameLabel);
            Set(serialized, "confirmPriceLabel", confirm.PriceLabel);
            Set(serialized, "confirmBuyButton", confirm.BuyButton);
            Set(serialized, "confirmCancelButton", confirm.CancelButton);

            // The two data references this step did not build. Type lookups, which D-028
            // leaves to author-time tools, and fill-only-if-empty so a deliberate choice is
            // never overwritten -- the same pair MetaGroundsSetup.AssignData makes.
            FillIfEmpty(serialized, "sessionHost", Object.FindAnyObjectByType<MainScreenRoot>());
            FillIfEmpty(serialized, "grounds", Object.FindAnyObjectByType<MetaGroundsView>());

            serialized.ApplyModifiedProperties();

            return view;
        }

        private struct ShopList
        {
            public Transform Content;
            public MetaShopRowView RowTemplate;
        }

        private struct ConfirmPopup
        {
            public GameObject Root;
            public RectTransform Box;
            public TMP_Text NameLabel;
            public TMP_Text PriceLabel;
            public Button BuyButton;
            public Button CancelButton;
        }

        // Name, price, BUY, cancel. Built inside a root that MetaShopView toggles rather
        // than toggling the box itself -- the root is what the view's state machine owns, and
        // keeping that indirection means the popup can grow a second part later without the
        // view learning to switch two things (two switches is one that gets left on).
        //
        // There is no backdrop any more: the user removed it and D-038 took it out of the
        // code, so CANCEL is the only dismissal and the map is not dimmed during a preview.
        private static ConfirmPopup BuildConfirmPopup(RectTransform shopRoot)
        {
            var root = CreateRect("ConfirmPopup", shopRoot);
            Stretch(root);

            // A full-screen backdrop button used to sit here as the first child, dimming the
            // map and cancelling on a tap outside the box (MS6). The user removed it from
            // the scene and it is gone from here too (D-038), so CANCEL is the only way out
            // of a preview, and the map is no longer dimmed while one is up -- which shows
            // the ghost more clearly, not less.

            var box = CreateRect("Box", root);
            box.anchorMin = new Vector2(0.5f, 0.5f);
            box.anchorMax = new Vector2(0.5f, 0.5f);
            // Bottom-centre pivot, so MetaShopView can say "your lower edge sits just over
            // the ghost" in one assignment instead of doing half-height arithmetic. The
            // pivot is the only reason this box is not centred on screen any more; its
            // children anchor to the box's rect, which did not change, so nothing inside
            // moves.
            box.pivot = new Vector2(0.5f, 0f);
            box.sizeDelta = PopupSize;
            box.anchoredPosition = Vector2.zero;

            var boxImage = box.gameObject.AddComponent<Image>();
            boxImage.color = new Color(0.10f, 0.11f, 0.15f, 0.98f);
            var boxSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            if (boxSprite != null)
            {
                boxImage.sprite = boxSprite;
                boxImage.type = Image.Type.Sliced;
            }
            // Kept true so the box swallows the taps that land on it rather than letting
            // them reach the map behind. Nothing behind acts on them today -- the ScrollRect
            // is disabled during a preview (D-032) and the backdrop that used to cancel on
            // such a tap is gone (D-038) -- so this is no longer load-bearing; it is left as
            // the honest behaviour for a panel, not as something the popup relies on.
            boxImage.raycastTarget = true;

            var nameLabel = CreateText("Name", box, 44f);
            nameLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
            nameLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            nameLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
            nameLabel.rectTransform.sizeDelta = new Vector2(-PopupPadding * 2f, 90f);
            nameLabel.rectTransform.anchoredPosition = new Vector2(0f, -PopupPadding);
            nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;

            var priceLabel = CreateText("Price", box, 56f);
            priceLabel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            priceLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            priceLabel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            priceLabel.rectTransform.sizeDelta = new Vector2(PopupSize.x - PopupPadding * 2f, 90f);
            priceLabel.rectTransform.anchoredPosition = new Vector2(0f, 20f);

            var buyButton = CreatePopupButton(box, "Buy", "BUY", new Color(0.20f, 0.42f, 0.28f),
                new Vector2(0f, PopupPadding + PopupButtonHeight + RowSpacing));

            // Visibly not the confirming button, and below it: the finger travels to the
            // green one to spend money, not away from it.
            var cancelButton = CreatePopupButton(box, "Cancel", "CANCEL", new Color(1f, 1f, 1f, 0.12f),
                new Vector2(0f, PopupPadding));

            return new ConfirmPopup
            {
                Root = root.gameObject,
                Box = box,
                NameLabel = nameLabel,
                PriceLabel = priceLabel,
                BuyButton = buyButton,
                CancelButton = cancelButton,
            };
        }

        private static Button CreatePopupButton(
            RectTransform box, string name, string caption, Color color, Vector2 offsetFromBottom)
        {
            var rect = CreateRect(name, box);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(-PopupPadding * 2f, PopupButtonHeight);
            rect.anchoredPosition = offsetFromBottom;

            var background = rect.gameObject.AddComponent<Image>();
            background.color = color;
            var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (sprite != null)
            {
                background.sprite = sprite;
                background.type = Image.Type.Sliced;
            }

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            var label = CreateText("Label", rect, 34f);
            Stretch(label.rectTransform);
            label.text = caption;

            return button;
        }

        // A ScrollRect whose content sizes itself from the rows: sixteen offers overflow a
        // bottom sheet and the number changes with every purchase, so the height cannot be
        // authored. VerticalLayoutGroup + ContentSizeFitter is what makes scrolling fall
        // out of the row count instead of being a number to maintain.
        private static ShopList BuildList(RectTransform panel)
        {
            var viewport = CreateRect("ListViewport", panel);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(ListPadding, ListPadding);
            viewport.offsetMax = new Vector2(-ListPadding, -ListPadding);
            // RectMask2D rather than Mask, the same choice MetaGroundsSetup makes: no extra
            // material and no stencil buffer for a plain rectangular clip.
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = CreateRect("Content", viewport);
            // Top-stretched with a top pivot, so the height the fitter computes grows
            // DOWNWARD and the list starts at its first row rather than its last.
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = RowSpacing;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            // Clamped, not Elastic: a list that already fits should sit still rather than
            // bounce, which reads as a bug. Same call the grounds' scroll makes.
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var template = BuildRowTemplate(content);

            // Left INACTIVE. It is a template, not the first row: MetaShopView clones it
            // and switches each clone on. Note the container above stays active -- hiding
            // the container to get this sample row out of sight is exactly what would
            // silently hide every real row, which TicketCardView records the same way.
            template.gameObject.SetActive(false);

            return new ShopList { Content = content, RowTemplate = template };
        }

        // Icon | name | price | BUY, left to right. Authored here once and cloned at run
        // time, so restyling this one object restyles the whole list -- and there is no
        // second prefab asset to keep in sync with the scene.
        private static MetaShopRowView BuildRowTemplate(RectTransform content)
        {
            var row = CreateRect("RowTemplate", content);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, RowHeight);

            // Fixed height, and the layout group is told not to control it: a row is a
            // constant, while the LIST's height is what has to be computed.
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.minHeight = RowHeight;
            element.preferredHeight = RowHeight;

            var rowBackground = row.gameObject.AddComponent<Image>();
            rowBackground.color = new Color(1f, 1f, 1f, 0.06f);

            // One knob for the whole row's fade, used when the player cannot afford it
            // (D-035). Only its alpha is ever touched -- `interactable` is deliberately left
            // alone, because the row's BUY must still open the preview.
            var rowGroup = row.gameObject.AddComponent<CanvasGroup>();

            var icon = CreateRect("Icon", row);
            icon.anchorMin = new Vector2(0f, 0.5f);
            icon.anchorMax = new Vector2(0f, 0.5f);
            icon.pivot = new Vector2(0f, 0.5f);
            icon.sizeDelta = new Vector2(IconSize, IconSize);
            icon.anchoredPosition = new Vector2(RowSpacing, 0f);
            var iconImage = icon.gameObject.AddComponent<Image>();
            // Preserved so a tall prop is not squashed into the square slot. There is no
            // per-item scale in the catalog by design (D-015), so the sprite's own aspect
            // is the only thing that can decide this.
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            // Stretched between the icon and the price so the name takes whatever is left
            // -- the row must survive a long display name without pushing BUY off the edge.
            var nameLabel = CreateText("Name", row, 34f, TextAlignmentOptions.Left);
            Stretch(nameLabel.rectTransform);
            nameLabel.rectTransform.offsetMin = new Vector2(IconSize + RowSpacing * 2f, 0f);
            nameLabel.rectTransform.offsetMax = new Vector2(-(BuyWidth + PriceWidth + RowSpacing * 2f), 0f);
            // No wrap plus ellipsis: a long display name must lose its tail rather than
            // grow a second line, because the row's height is a fixed LayoutElement and a
            // wrapped name would be clipped mid-letter instead of ending in "…".
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;
            nameLabel.textWrappingMode = TextWrappingModes.NoWrap;

            var priceLabel = CreateText("Price", row, 32f, TextAlignmentOptions.Right);
            priceLabel.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            priceLabel.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            priceLabel.rectTransform.pivot = new Vector2(1f, 0.5f);
            priceLabel.rectTransform.sizeDelta = new Vector2(PriceWidth, RowHeight);
            priceLabel.rectTransform.anchoredPosition = new Vector2(-(BuyWidth + RowSpacing * 2f), 0f);

            var buy = CreateRect("Buy", row);
            buy.anchorMin = new Vector2(1f, 0.5f);
            buy.anchorMax = new Vector2(1f, 0.5f);
            buy.pivot = new Vector2(1f, 0.5f);
            buy.sizeDelta = new Vector2(BuyWidth, RowHeight - RowSpacing * 2f);
            buy.anchoredPosition = new Vector2(-RowSpacing, 0f);

            var buyBackground = buy.gameObject.AddComponent<Image>();
            buyBackground.color = new Color(0.20f, 0.42f, 0.28f);
            var builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (builtin != null)
            {
                buyBackground.sprite = builtin;
                buyBackground.type = Image.Type.Sliced;
            }

            var buyButton = buy.gameObject.AddComponent<Button>();
            buyButton.targetGraphic = buyBackground;

            var buyLabel = CreateText("Label", buy, 30f, TextAlignmentOptions.Center);
            Stretch(buyLabel.rectTransform);
            buyLabel.text = "BUY";

            var view = row.gameObject.AddComponent<MetaShopRowView>();
            var serialized = new SerializedObject(view);
            Set(serialized, "iconImage", iconImage);
            Set(serialized, "nameLabel", nameLabel);
            Set(serialized, "priceLabel", priceLabel);
            Set(serialized, "buyButton", buyButton);
            Set(serialized, "group", rowGroup);
            serialized.ApplyModifiedProperties();

            return view;
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
            // Placeholder colour -- the real frame comes in Ş6, where Art/UI/Popup already
            // has the Day Complete and Fail art to match. Nearly opaque rather than a light
            // scrim, because rows have to be readable over whatever prop is behind them.
            background.color = new Color(0.08f, 0.09f, 0.12f, 0.96f);

            // Load-bearing, not decoration: the grounds behind this are a ScrollRect, and
            // an open panel that does not eat raycasts lets a drag on the panel scroll the
            // map instead. The player would read that as the panel being broken.
            background.raycastTarget = true;

            var builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            if (builtin != null)
            {
                background.sprite = builtin;
                background.type = Image.Type.Sliced;
            }

            return panel;
        }

        private static Button BuildMarketButton(RectTransform root)
        {
            var rect = CreateRect(ButtonName, root);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = ButtonSize;
            rect.anchoredPosition = ButtonInset;

            var background = rect.gameObject.AddComponent<Image>();
            // Placeholder: the project has no market/shop art at all (meta-shop-plan MS5).
            // A coloured square that plainly says SHOP is honest about being unfinished,
            // where a repurposed icon from another system would read as final.
            background.color = new Color(0.20f, 0.42f, 0.28f);

            var builtin = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (builtin != null)
            {
                background.sprite = builtin;
                background.type = Image.Type.Sliced;
            }

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            var label = CreateText("Label", rect, 28f);
            Stretch(label.rectTransform);
            label.text = "SHOP";

            return button;
        }

        private static void Set(SerializedObject serialized, string name, Object value)
        {
            var property = serialized.FindProperty(name);
            if (property != null) property.objectReferenceValue = value;
        }

        // Fills a reference only when it is empty, so re-running the step never overwrites
        // a choice the author made by hand. Same helper, same reason, as MetaGroundsSetup's.
        private static void FillIfEmpty(SerializedObject serialized, string name, Object value)
        {
            var property = serialized.FindProperty(name);
            if (property == null || property.objectReferenceValue != null) return;
            property.objectReferenceValue = value;
        }

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
                    "No TMP default font asset found while building the meta shop. Import TMP Essentials " +
                    "(Window > TextMeshPro > Import TMP Essential Resources) and set the font by hand.");
            }

            return text;
        }
    }
}
