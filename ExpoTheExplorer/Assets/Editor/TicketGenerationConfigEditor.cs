using System.Linq;
using ExpoTheExplorer.Data;
using UnityEditor;

namespace ExpoTheExplorer.Editor
{
    // Read-only preview appended below the normal Inspector — never edits anything itself.
    // Recomputes every OnInspectorGUI call (Unity re-runs this on every repaint), so
    // dragging a MainDishWeight entry's own modificationCountLambda slider updates the
    // numbers below live, with no extra event wiring needed.
    //
    // This asset is a SEED since D-006: per-Day generation settings live in each Day's
    // editorMeta, and the Day Editor draws this same preview for the Day being tuned. The
    // maths sits in DayEditorSettingsPreviews so both views share one implementation.
    [CustomEditor(typeof(TicketGenerationConfig))]
    public class TicketGenerationConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (TicketGenerationConfig)target;
            DayEditorSettingsPreviews.DrawMainDishPreview(
                config.MainDishWeights
                    .Select(w => (w.Food, w.Weight, w.ModificationCountLambda, w.MaxModificationCount)).ToList());
        }
    }
}
