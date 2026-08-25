using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    // One menu item against a problem this project has recorded four times
    // (decisions.md D-004, D-011, D-063, D-074): a ScriptableObject asset does not
    // update itself when its class gains a field. Unity leaves the absent key at the
    // C# initializer and writes nothing, so the .asset drifts into carrying a subset
    // of what the code reads — BoardAnimationConfig.asset was down to 4 keys out of
    // 19 — and the fields missing from the file are the ones an author cannot make
    // stick: an Inspector edit lives in memory until AssetDatabase.SaveAssets runs,
    // so the next script recompile takes it back and the field looks broken.
    //
    // Dirtying every config in the folder and saving once fixes both halves: Unity
    // re-serializes each object WHOLE, so every field the class declares lands in
    // the file, and from then on an Inspector edit has somewhere to land.
    //
    // NOTHING here changes a value. A key present in the file keeps its authored
    // value (it was loaded from there); a key absent from the file is written at the
    // initializer value the game is ALREADY running on. The behaviour is identical
    // afterwards — the file simply stops lying about what it holds. The one real
    // change is subtractive: a key whose field no longer exists (TicketCardVisuals-
    // Config.asset still carries timerWarningRatio/timerCriticalRatio, which moved to
    // EconomyConfig in D-011) is dropped, the same cleanup 50be70c did by hand.
    public static class DataConfigReserializer
    {
        private const string ConfigFolder = "Assets/Data";

        // TOP level of the ExpoTheExplorer menu, beside Day Editor and Meta Editor,
        // deliberately not under a "Data" submenu: the CreateAssetMenu entries for
        // these very configs already occupy "ExpoTheExplorer/Data/..." in the Project
        // window's right-click Create tree, so a tool sharing that path reads as one of
        // them. It cost a round of hunting once; one flat entry is worth more than a
        // tidy grouping of two things that are not the same kind of thing.
        [MenuItem("ExpoTheExplorer/Re-serialize Data Configs")]
        private static void ReserializeAllConfigs()
        {
            var guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { ConfigFolder });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"{nameof(DataConfigReserializer)}: no ScriptableObject assets found under {ConfigFolder}. Nothing to re-serialize.");
                return;
            }

            // Counted BEFORE the save, because the whole point of the report is the
            // difference — after the write every file is complete and there would be
            // nothing left to see.
            var paths = new List<string>(guids.Length);
            var keysBefore = new Dictionary<string, int>(guids.Length);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

                // A sub-asset or a broken import can hand back null; skipping it beats
                // aborting the whole sweep over one file.
                if (asset == null) continue;

                paths.Add(path);
                keysBefore[path] = CountSerializedKeys(path);
                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();

            // Read back off disk rather than trusting the write: this is a tool whose
            // only claim is "the file now holds everything", and the file is the only
            // place that claim can be checked.
            var report = new StringBuilder();
            var grown = 0;

            foreach (var path in paths)
            {
                var before = keysBefore[path];
                var after = CountSerializedKeys(path);
                if (after != before) grown++;

                report.Append('\n')
                    .Append(after == before ? "    " : "  * ")
                    .Append(Path.GetFileName(path))
                    .Append(": ")
                    .Append(before)
                    .Append(" -> ")
                    .Append(after)
                    .Append(" keys");
            }

            Debug.Log($"{nameof(DataConfigReserializer)}: re-serialized {paths.Count} config asset(s) under {ConfigFolder}; {grown} changed shape (marked *). Values are untouched — an absent key is written at the initializer the game was already running on. Check `git diff {ConfigFolder}` to see exactly what landed.{report}");
        }

        // Top-level keys the FILE itself stores, minus Unity's own bookkeeping (every
        // m_ entry: m_Script, m_Name, m_ObjectHideFlags and the rest). Deliberately
        // read off the YAML rather than reflected off the class: the question this
        // answers is "what does this file carry", and a reflection-based count would
        // be a second theory about what Unity serializes — which is precisely the
        // thing that has been wrong here before.
        //
        // Exactly two spaces of indent is what makes a line top-level: nested list
        // entries and sub-objects (MetaCatalog is full of them) sit deeper and are not
        // counted, which is why a nested asset reports a small number. That is the
        // shape of the file, not an error.
        private static int CountSerializedKeys(string path)
        {
            if (!File.Exists(path)) return 0;

            var count = 0;

            foreach (var line in File.ReadLines(path))
            {
                if (line.Length < 4) continue;
                if (line[0] != ' ' || line[1] != ' ') continue;
                if (line[2] == ' ' || line[2] == '-') continue;
                if (line.StartsWith("  m_")) continue;
                if (line.IndexOf(':') < 0) continue;

                count++;
            }

            return count;
        }
    }
}
