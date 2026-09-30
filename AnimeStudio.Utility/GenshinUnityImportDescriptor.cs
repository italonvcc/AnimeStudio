using System;
using System.IO;
using Newtonsoft.Json;

namespace AnimeStudio
{
    /// <summary>
    /// Identifies an exported model's existing metadata for the Unity package importer.
    /// This file contains data only; Unity Editor code belongs to the installed package.
    /// </summary>
    public static class GenshinUnityImportDescriptor
    {
        public const string FileName = "anime-studio-import.json";

        public static string Write(string directory, string kind, string recipe, string model)
        {
            if (kind is not ("genshin-model" or "genshin-character" or "genshin-weapon"))
                throw new ArgumentException("Unsupported Genshin import kind.", nameof(kind));
            if (Path.GetFileName(recipe) != recipe || Path.GetFileName(model) != model ||
                string.IsNullOrWhiteSpace(recipe) || string.IsNullOrWhiteSpace(model))
                throw new InvalidDataException("The import recipe and model must be filenames in the descriptor folder.");
            string recipePath = Path.Combine(directory, recipe);
            string modelPath = Path.Combine(directory, model);
            if (!File.Exists(recipePath) || !File.Exists(modelPath))
                throw new FileNotFoundException("The import descriptor requires its exported recipe and model in the same folder.");
            string path = Path.Combine(directory, FileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(new { schemaVersion = 1, kind, recipe, model }, Formatting.Indented));
            return path;
        }
    }
}
