using UnityEngine;

namespace ExpoTheExplorer.Data
{
    // A modification's allowed direction is intrinsic to the modification type
    // itself, not a random per-ticket roll — "No Lettuce" is always a removal
    // regardless of which dish it's attached to.
    public enum ModificationDirection
    {
        AdditionOnly,
        RemovalOnly,
        Both
    }

    [CreateAssetMenu(fileName = "ModificationConfig", menuName = "ExpoTheExplorer/Data/Modification Config")]
    public class ModificationConfig : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField] private ModificationDirection allowedDirection = ModificationDirection.Both;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public ModificationDirection AllowedDirection => allowedDirection;
    }
}
