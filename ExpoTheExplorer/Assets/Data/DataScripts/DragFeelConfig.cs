using UnityEngine;

namespace ExpoTheExplorer.Data
{
    [CreateAssetMenu(fileName = "DragFeelConfig", menuName = "ExpoTheExplorer/Data/Drag Feel Config")]
    public class DragFeelConfig : ScriptableObject
    {
        [Header("Hover Offset")]
        [Tooltip("How far above the finger a picked-up item hovers, as a fraction of one cell.")]
        [SerializeField, Range(0f, 1f)] private float offsetFraction = 0.55f;

        [Header("Follow Multipliers")]
        [Tooltip("Item movement = finger's own frame-to-frame movement x this multiplier while pushing up (>1 makes it lead/stretch ahead of the finger).")]
        [SerializeField] private float followMultiplierUp = 1.2f;
        [Tooltip("Item movement = finger's own frame-to-frame movement x this multiplier while pulling down, until the offset floor is reached.")]
        [SerializeField] private float followMultiplierDown = 1.2f;
        [Tooltip("Item movement = finger's own frame-to-frame horizontal movement x this multiplier (symmetric left/right, no offset floor on this axis).")]
        [SerializeField] private float followMultiplierHorizontal = 1.2f;

        [Header("Pickup Scale")]
        [Tooltip("Scale multiplier applied to an item the moment it's picked up.")]
        [SerializeField] private float pickupScaleMultiplier = 1.15f;
        [Tooltip("Duration (seconds) of the pickup scale-up tween.")]
        [SerializeField] private float pickupScaleDuration = 0.15f;

        public float OffsetFraction => offsetFraction;
        public float FollowMultiplierUp => followMultiplierUp;
        public float FollowMultiplierDown => followMultiplierDown;
        public float FollowMultiplierHorizontal => followMultiplierHorizontal;
        public float PickupScaleMultiplier => pickupScaleMultiplier;
        public float PickupScaleDuration => pickupScaleDuration;
    }
}
