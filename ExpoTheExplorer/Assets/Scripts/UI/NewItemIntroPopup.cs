using ExpoTheExplorer.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ExpoTheExplorer.UI
{
    // What a Day says about the food it is introducing: a picture of the item, its name, and
    // an optional line about it. Filled from the Day's own `runtime.itemIntro` block by
    // GameManager's day-start sequence, which is already the one place that knows a Day has
    // just opened.
    //
    // IT HOLDS THE DAY STILL. The clock does not run while this is up (GameManager takes a
    // pause hold for the whole sequence), so a player reading about a new item is not
    // spending three customers' patience doing it. That is why the dismiss button is the one
    // part that is NOT optional -- a popup with no way out would freeze the day behind it,
    // which is the worst failure this screen has available.
    //
    // A COPY of MetaUnlockPopup's design rather than a reuse of it (the user's own reference:
    // "tasarım olarak mainscreendeki yeni bi şey açılmayı kullanabilirz"). The two say
    // similar things and are deliberately separate objects: one belongs to the meta grounds
    // on the main screen and the other to the start of a day, and restyling either must not
    // move the other.
    //
    // In a file of its own because it is a MonoBehaviour, which Unity can only bind when the
    // file name matches the class name (D-125, learned by having two prefabs refuse to save).
    // Its prefab's ROOT is its Canvas, because Unity drives an Overlay canvas's rect from the
    // screen and gives a ROOT canvas that treatment in the prefab stage, while a nested one
    // sits at 0x0 and collapses every child into it (D-126, learned twice).
    public class NewItemIntroPopup : MonoBehaviour
    {
        [Tooltip("The header shown when this Day is introducing a FOOD — authored here, e.g. \"NEW ITEM!\". Code only switches it on and off; the words are yours.")]
        [SerializeField] private GameObject itemHeader;

        [Tooltip("The header shown when this Day is introducing a MODIFICATION — authored here, e.g. \"NEW MODIFICATION!\". Its own object rather than a second string, so the longer word can carry its own size and styling.")]
        [SerializeField] private GameObject modificationHeader;

        [Tooltip("The +/- sign, shown only for a modification. Its two sprites come from TicketCardVisualsConfig below — the same asset the ticket cards read, so the badge here and the badge on a card can never disagree.")]
        [SerializeField] private Image directionBadge;

        [Tooltip("Assets/Data/TicketCardVisualsConfig.asset — read for the +/- sprites ONLY. An asset reference, which a prefab can hold, exactly as TicketCardsView holds it.")]
        [SerializeField] private TicketCardVisualsConfig visualsConfig;

        [Tooltip("The thing's name — the Day's Name Override when one is authored, else the config's Display Name.")]
        [SerializeField] private TMP_Text itemNameText;

        [Tooltip("The line under the name, from the Day's Message. The object is hidden when the Day authored none, so the layout closes up rather than leaving a gap.")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("The item's picture, taken from its FoodItemConfig — the same sprite the player will pick up off the board.")]
        [SerializeField] private Image itemImage;

        [Tooltip("REQUIRED. The button that closes this popup and lets the day begin.")]
        [SerializeField] private Button dismissButton;

        // Read by the day-start coroutine, which cannot use a callback: it is a coroutine
        // waiting frame by frame while holding the day paused, not an event handler. Starts
        // true for a popup that could not present itself, so a broken prefab never holds the
        // day hostage.
        public bool IsDismissed { get; private set; }

        // modificationIsAddition is NULL for a food and the direction for a modification --
        // "not a modification" is a real third state, and a plain false would be
        // indistinguishable from a removal, which is the mistake that puts a "-" on a burger.
        public void Bind(string displayName, string message, Sprite picture, bool? modificationIsAddition)
        {
            var isModification = modificationIsAddition.HasValue;

            // Two authored objects, one shown -- the words stay in the prefab where every
            // other player-facing string in this project lives, and the longer of the two
            // ("NEW MODIFICATION") can carry its own font size without code knowing. Both are
            // optional: a prefab wired with only one header still presents, it just says the
            // same thing for both kinds.
            if (itemHeader != null) itemHeader.SetActive(!isModification);
            if (modificationHeader != null) modificationHeader.SetActive(isModification);

            if (directionBadge != null)
            {
                var directionSprite = !isModification || visualsConfig == null
                    ? null
                    : modificationIsAddition.Value ? visualsConfig.AdditionSprite : visualsConfig.RemovalSprite;

                // Off for a food, and also off for a modification whose sprite is missing:
                // an empty badge frame over the icon reads as a third, unnamed symbol.
                directionBadge.sprite = directionSprite;
                directionBadge.gameObject.SetActive(directionSprite != null);

                if (isModification && directionSprite == null)
                {
                    // Loud, because a modification silently losing its +/- is the one failure
                    // that changes what the popup MEANS -- "extra cheese" and "no cheese" look
                    // identical without it.
                    Debug.LogWarning(
                        $"{nameof(NewItemIntroPopup)} on '{name}' is introducing a modification but has no " +
                        $"{(visualsConfig == null ? nameof(TicketCardVisualsConfig) + " assigned" : "addition/removal sprite on its config")}, " +
                        "so the popup cannot say whether it is an addition or a removal.", this);
                }
            }

            if (itemNameText != null) itemNameText.text = displayName;

            if (messageText != null)
            {
                // The OBJECT is switched off for an unauthored line, not just emptied: an
                // empty TMP still occupies its slot in the layout, which on a popup this
                // size reads as a missing sentence rather than as a design with none.
                messageText.text = message;
                messageText.gameObject.SetActive(!string.IsNullOrEmpty(message));
            }

            if (itemImage != null)
            {
                // Hidden rather than left showing whatever the prefab was authored with -- a
                // placeholder standing in for the item being introduced would be actively
                // misleading here, since recognising this picture on the board is the whole
                // job. A null at this point means the FoodItemConfig has no art at all,
                // which DayValidator warns about at authoring time.
                itemImage.sprite = picture;
                itemImage.enabled = picture != null;
            }

            if (dismissButton == null)
            {
                Debug.LogError(
                    $"{nameof(NewItemIntroPopup)} on '{name}' has no dismiss Button, so it could never be closed and " +
                    "the day would stay frozen behind it. Skipping this introduction rather than trapping the player.",
                    this);
                IsDismissed = true;
                return;
            }

            dismissButton.onClick.RemoveAllListeners();
            dismissButton.onClick.AddListener(Dismiss);
        }

        // Public so the day-start sequence can close it on a path the player did not take --
        // the scene going away mid-queue, for instance. Idempotent, so a second press in the
        // same frame cannot advance the queue twice.
        public void Dismiss() => IsDismissed = true;
    }
}
