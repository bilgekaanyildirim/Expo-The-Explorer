using System.Collections.Generic;
using UnityEngine;

namespace ExpoTheExplorer.Systems.DaySystem
{
    public readonly struct DayJsonFile
    {
        public string FileName { get; }
        public string Json { get; }

        public DayJsonFile(string fileName, string json)
        {
            FileName = fileName;
            Json = json;
        }
    }

    // Thin Unity-dependent boundary -- keeps DayCatalogParser plain C# and testable
    // without touching Resources/TextAsset.
    public class DayJsonSource
    {
        private readonly string resourceFolder;

        public DayJsonSource(string resourceFolder = "Days")
        {
            this.resourceFolder = resourceFolder;
        }

        public IReadOnlyList<DayJsonFile> LoadAll()
        {
            var textAssets = Resources.LoadAll<TextAsset>(resourceFolder);
            var files = new List<DayJsonFile>(textAssets.Length);
            foreach (var asset in textAssets)
            {
                files.Add(new DayJsonFile(asset.name, asset.text));
            }
            return files;
        }
    }
}
