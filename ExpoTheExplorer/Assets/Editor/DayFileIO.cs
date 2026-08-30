using System.Collections.Generic;
using System.IO;
using ExpoTheExplorer.Systems.DaySystem;
using UnityEngine;

namespace ExpoTheExplorer.Editor
{
    public readonly struct DayFile
    {
        public string FileName { get; }
        public DayJson Json { get; }

        public DayFile(string fileName, DayJson json)
        {
            FileName = fileName;
            Json = json;
        }
    }

    // Plain file I/O, no UnityEditor dependency -- takes an arbitrary folder path so tests
    // can point it at a temp directory instead of the real Assets/Resources/Days. AssetDatabase
    // refresh/import is the caller's (DayEditorWindow's) job, not this class's.
    public static class DayFileIO
    {
        public static string GetFilePath(string daysFolderPath, int dayIndex) =>
            Path.Combine(daysFolderPath, $"day_{dayIndex:D2}.json");

        public static void Save(string daysFolderPath, DayJson dayJson)
        {
            Directory.CreateDirectory(daysFolderPath);
            File.WriteAllText(GetFilePath(daysFolderPath, dayJson.runtime.dayIndex), JsonUtility.ToJson(dayJson, true));
        }

        public static void Delete(string daysFolderPath, int dayIndex)
        {
            var path = GetFilePath(daysFolderPath, dayIndex);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        // Closes the holes in the numbering: the Days are kept in the order of the dayIndex they
        // carry and rewritten as 0..n-1 by position, so a deleted Day does not leave "Day 7"
        // missing between 6 and 8.
        //
        // File-side rather than model-side on purpose: going through DayEditorModel.ToDayJson()
        // would have made this a valid-Days-only operation, and a Day with validation errors has
        // to be renumbered along with the rest or the numbering is wrong for everyone.
        public static List<(int OldIndex, int NewIndex)> Renumber(string daysFolderPath) =>
            WriteInOrder(daysFolderPath, LoadAll(daysFolderPath));

        // Moves one Day to another place in the running order and renumbers everything to match:
        // dragging Day 7 above Day 2 makes it Day 2 and pushes 2..6 up by one. `toPosition` is the
        // place the Day ends up in the FINAL list, already counted with the Day removed from its
        // old place -- the caller works that out, because only the caller knows which gap in the
        // list the user actually pointed at.
        //
        // Same write as Renumber, and deliberately so: a reorder and the compaction after a delete
        // are the same operation given a different order, and two implementations of "rewrite the
        // Days as 0..n-1" would eventually disagree about one of them.
        public static List<(int OldIndex, int NewIndex)> Move(string daysFolderPath, int fromDayIndex, int toPosition)
        {
            var files = LoadAll(daysFolderPath);
            var from = files.FindIndex(f => f.Json.runtime.dayIndex == fromDayIndex);
            if (from < 0)
            {
                return new List<(int OldIndex, int NewIndex)>();
            }

            var moving = files[from];
            files.RemoveAt(from);
            files.Insert(Mathf.Clamp(toPosition, 0, files.Count), moving);
            return WriteInOrder(daysFolderPath, files);
        }

        // Writes the Days out as 0..n-1 in the order given, moving each one's file NAME and the
        // dayIndex INSIDE it together -- everything downstream reads one or the other and they must
        // not disagree. Returns the Days that actually moved, oldIndex -> newIndex, in that order.
        //
        // Nothing is deleted that is not written back immediately: the Days are already in memory,
        // then only the files whose path has to change are removed, then all are written. That also
        // makes it safe for an arbitrary reorder and not just a compaction -- a file that keeps its
        // path is the only file that can want that path, since a wanted path is one per position,
        // so every file standing on somebody else's destination has been deleted before the first
        // write lands.
        private static List<(int OldIndex, int NewIndex)> WriteInOrder(string daysFolderPath, List<DayFile> ordered)
        {
            var moved = new List<(int OldIndex, int NewIndex)>();
            var stale = new List<string>();

            for (var i = 0; i < ordered.Count; i++)
            {
                var currentPath = Path.Combine(daysFolderPath, ordered[i].FileName + ".json");
                var wantedPath = GetFilePath(daysFolderPath, i);
                if (ordered[i].Json.runtime.dayIndex == i && currentPath == wantedPath)
                {
                    continue;
                }

                moved.Add((ordered[i].Json.runtime.dayIndex, i));
                if (currentPath != wantedPath)
                {
                    stale.Add(currentPath);
                }
            }

            if (moved.Count == 0)
            {
                return moved;
            }

            foreach (var path in stale)
            {
                File.Delete(path);
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Json.runtime.dayIndex = i;
                Save(daysFolderPath, ordered[i].Json);
            }

            return moved;
        }

        public static List<DayFile> LoadAll(string daysFolderPath)
        {
            var results = new List<DayFile>();
            if (!Directory.Exists(daysFolderPath))
            {
                return results;
            }

            foreach (var path in Directory.GetFiles(daysFolderPath, "*.json"))
            {
                var json = JsonUtility.FromJson<DayJson>(File.ReadAllText(path));
                results.Add(new DayFile(Path.GetFileNameWithoutExtension(path), json));
            }

            results.Sort((a, b) => a.Json.runtime.dayIndex.CompareTo(b.Json.runtime.dayIndex));
            return results;
        }
    }
}
