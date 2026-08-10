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
