using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TerraKit.Editor
{
    internal sealed partial class TerraKitBackendGeneratorWindow
    {
        private const string SettingsJsonFormat = "TerraKit.GenerationSettings";
        private const int SettingsJsonVersion = 1;

        [Serializable]
        private sealed class GenerationSettingsJson
        {
            public string format;
            public int version;
            public string regionMode;
            public string seed;
            public string lod;
            public string cellWidth;
            public string cellHeight;
            public string cellDepth;
            public string spacingX;
            public string spacingY;
            public string spacingZ;
            public string regionsX;
            public string regionsY;
            public string regionsZ;
        }

        private bool ExportSettingsJson()
        {
            var settings = CaptureSettings();

            try
            {
                var document = new GenerationSettingsJson
                {
                    format = SettingsJsonFormat,
                    version = SettingsJsonVersion,
                    regionMode = settings.is3D ? "3D" : "2D",
                    seed = settings.seed,
                    lod = settings.lod,
                    cellWidth = settings.cellWidth,
                    cellHeight = settings.cellHeight,
                    cellDepth = settings.cellDepth,
                    spacingX = settings.spacingX,
                    spacingY = settings.spacingY,
                    spacingZ = settings.spacingZ,
                    regionsX = settings.regionsX,
                    regionsY = settings.regionsY,
                    regionsZ = settings.regionsZ
                };

                string path = EditorUtility.SaveFilePanel(
                    "Export Generation Settings",
                    "",
                    "TerraKitGenerationSettings",
                    "json");

                if (string.IsNullOrEmpty(path)) return false;

                File.WriteAllText(
                    path,
                    JsonUtility.ToJson(document, true),
                    new UTF8Encoding(false));

                return true;
            }
            finally
            {
                DestroyImmediate(settings);
            }
        }

        private bool ImportSettingsJson()
        {
            string path = EditorUtility.OpenFilePanel(
                "Import Generation Settings", "", "json");

            if (string.IsNullOrEmpty(path)) return false;

            if (new FileInfo(path).Length > 1024 * 1024)
            {
                throw new ArgumentException(
                    "This file is too large for generation settings (maximum 1 MB).");
            }

            var document = JsonUtility.FromJson<GenerationSettingsJson>(
                File.ReadAllText(path));

            if (document == null || document.format != SettingsJsonFormat)
            {
                throw new ArgumentException(
                    "Select a generation settings JSON exported by this tool.");
            }

            if (document.version != SettingsJsonVersion)
            {
                throw new ArgumentException(
                    "Unsupported generation settings JSON version.");
            }

            if (document.regionMode != "2D" && document.regionMode != "3D")
            {
                throw new ArgumentException("Region mode must be 2D or 3D.");
            }

            string[] inputs =
            {
                document.seed, document.lod,
                document.cellWidth, document.cellHeight, document.cellDepth,
                document.spacingX, document.spacingY, document.spacingZ
            };

            foreach (string input in inputs)
            {
                if (input == null)
                {
                    throw new ArgumentException(
                        "The settings JSON is missing required parameters.");
                }
            }

            if (document.regionsX == null || document.regionsY == null ||
                document.regionsZ == null)
            {
                throw new ArgumentException(
                    "The settings JSON is missing region counts.");
            }

            var settings = CreateInstance<TerraKitGenerationSettings>();

            try
            {
                settings.SetValues(
                    document.regionMode == "3D", inputs,
                    document.regionsX, document.regionsY, document.regionsZ);

                // Reject invalid files before changing the window or its preview.
                ValidateSettings(settings);

                _dimension = settings.is3D ? 1 : 0;
                _inputText = settings.GetInputText();
                _regionsX = settings.regionsX;
                _regionsY = settings.regionsY;
                _regionsZ = settings.regionsZ;
                _settingsAsset = null;

                GUI.FocusControl(null);
                ObserveChanges();

                return true;
            }
            finally
            {
                DestroyImmediate(settings);
            }
        }
    }
}