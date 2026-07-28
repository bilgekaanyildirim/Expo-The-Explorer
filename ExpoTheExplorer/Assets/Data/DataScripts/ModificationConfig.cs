using UnityEngine;

namespace ExpoTheExplorer.Data
{
    [CreateAssetMenu(fileName = "ModificationConfig", menuName = "ExpoTheExplorer/Data/Modification Config")]
    public class ModificationConfig : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
    }
}
