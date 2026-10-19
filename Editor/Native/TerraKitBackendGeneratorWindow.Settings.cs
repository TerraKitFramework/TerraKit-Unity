using System;
using UnityEditor;
using UnityEngine;

namespace TerraKit.Editor
{
    internal sealed partial class TerraKitBackendGeneratorWindow
    {
        [SerializeField]
        private TerraKitGenerationSettings _settingsAsset;

        private void DrawSettingsControls()
        {
            DrawParameterGroup("Saved generation settings");

            using (new EditorGUI.DisabledScope(_isGenerating))
            {
                _settingsAsset =
                    (TerraKitGenerationSettings)EditorGUILayout.ObjectField(
                        "Settings asset",
                        _settingsAsset,
                        typeof(TerraKitGenerationSettings),
                        false);

                Rect row = EditorGUILayout.GetControlRect(false, 26f);
                const float gap = 6f;
                const float menuWidth = 80f;
                float buttonWidth = Mathf.Max(0f, (row.width - menuWidth - 2f * gap) / 2f);
                var saveRect = new Rect(row.x, row.y, buttonWidth, row.height);
                var loadRect = new Rect(saveRect.xMax + gap, row.y, buttonWidth, row.height);
                var moreRect = new Rect(loadRect.xMax + gap, row.y, menuWidth, row.height);
                var buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    padding = new RectOffset(12, 12, 4, 4)
                };

                if (GUI.Button(saveRect, "Save Settings", buttonStyle))
                {
                    RunSettingsAction(
                        SaveCurrentSettings,
                        "Generation settings saved.");
                }

                using (new EditorGUI.DisabledScope(_settingsAsset == null))
                {
                    if (GUI.Button(loadRect, "Load Settings", buttonStyle))
                    {
                        RunSettingsAction(
                            LoadSavedSettings,
                            "Settings loaded. Use Auto Preview or Generate Map to update the map.");
                    }
                }

                if (EditorGUI.DropdownButton(moreRect, new GUIContent("More"), FocusType.Keyboard))
                    ShowSettingsMenu(moreRect);
            }

            EditorGUILayout.Space(2f);
            DrawParameterNote(
                "Settings assets and JSON save generation parameters. " +
                "Nodes, connections and node parameters are saved in the Graph asset.");
        }

        private bool SaveCurrentSettings()
        {
            return _settingsAsset == null ? SaveSettingsAs() : UpdateSavedSettings();
        }

        private void ShowSettingsMenu(Rect buttonRect)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Save As..."), false,
                () => RunSettingsAction(SaveSettingsAs, "Generation settings saved."));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Export JSON..."), false,
                () => RunSettingsAction(ExportSettingsJson, "Generation settings exported."));
            menu.AddItem(new GUIContent("Import JSON..."), false,
                () => RunSettingsAction(ImportSettingsJson,
                    "Settings imported. Use Auto Preview or Generate Map to update the map. " +
                    "Use Save Settings to keep an asset."));
            menu.DropDown(buttonRect);
        }

        private TerraKitGenerationSettings CaptureSettings()
        {
            EnsureInputText();

            var settings = CreateInstance<TerraKitGenerationSettings>();

            try
            {
                settings.SetValues(
                    _dimension == 1,
                    _inputText,
                    _regionsX,
                    _regionsY,
                    _regionsZ);

                ValidateSettings(settings);
                return settings;
            }
            catch
            {
                DestroyImmediate(settings);
                throw;
            }
        }

        private bool SaveSettingsAs()
        {
            var settings = CaptureSettings();

            try
            {
                string path = EditorUtility.SaveFilePanelInProject(
                    "Save Generation Settings",
                    "TerraKitGenerationSettings",
                    "asset",
                    "Save generation parameters in this Unity project.");

                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }

                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    throw new ArgumentException(
                        "This asset already exists. Choose a new name, or select it and use Save Settings.");
                }

                AssetDatabase.CreateAsset(settings, path);
                _settingsAsset = settings;
                AssetDatabase.SaveAssetIfDirty(settings);

                return true;
            }
            finally
            {
                if (settings != null && !EditorUtility.IsPersistent(settings))
                {
                    DestroyImmediate(settings);
                }
            }
        }

        private bool UpdateSavedSettings()
        {
            if (_settingsAsset == null)
            {
                throw new ArgumentException(
                    "Select a generation settings asset.");
            }

            string path = AssetDatabase.GetAssetPath(_settingsAsset);

            if (string.IsNullOrEmpty(path) ||
                !path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Select a settings asset under Assets, or use More > Save As.");
            }

            var settings = CaptureSettings();

            try
            {
                Undo.RecordObject(
                    _settingsAsset,
                    "Update TerraKit Generation Settings");

                _settingsAsset.SetValues(
                    settings.is3D,
                    settings.GetInputText(),
                    settings.regionsX,
                    settings.regionsY,
                    settings.regionsZ);

                EditorUtility.SetDirty(_settingsAsset);
                AssetDatabase.SaveAssetIfDirty(_settingsAsset);

                return true;
            }
            finally
            {
                DestroyImmediate(settings);
            }
        }

        private static void ValidateSettings(
            TerraKitGenerationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentException(
                    "Select a generation settings asset.");
            }

            TerraKitBackendGenerationRequest request;

            string errors = InputErrorSummary(
                ValidateInputs(
                    settings.GetInputText(),
                    settings.is3D,
                    out request));

            if (request == null)
            {
                throw new ArgumentException(errors);
            }

            int x, y, z = 1;

            if (!TryRegionCount(settings.regionsX, out x))
            {
                throw new ArgumentException(
                    "Regions X: Enter a positive integer.");
            }

            if (!TryRegionCount(settings.regionsY, out y))
            {
                throw new ArgumentException(
                    "Regions Y: Enter a positive integer.");
            }

            if (settings.is3D &&
                !TryRegionCount(settings.regionsZ, out z))
            {
                throw new ArgumentException(
                    "Regions Z: Enter a positive integer.");
            }

            ValidateRegionLayout(request, x, y, z);
        }

        private bool LoadSavedSettings()
        {
            // Validate before changing current inputs or preview results.
            ValidateSettings(_settingsAsset);

            _dimension = _settingsAsset.is3D ? 1 : 0;
            _inputText = _settingsAsset.GetInputText();

            _regionsX = _settingsAsset.regionsX;
            _regionsY = _settingsAsset.regionsY;
            _regionsZ = _settingsAsset.regionsZ;

            GUI.FocusControl(null);
            ObserveChanges();

            return true;
        }

        private void RunSettingsAction(
            Func<bool> action,
            string successMessage)
        {
            try
            {
                if (!action())
                {
                    return;
                }

                _message = successMessage;
                _messageType = MessageType.Info;
            }
            catch (Exception exception)
            {
                _message =
                    "Settings operation failed: " + exception.Message;
                _messageType = MessageType.Error;

                EditorUtility.DisplayDialog(
                    "Generation settings",
                    exception.Message,
                    "Close");
            }

            Repaint();
        }
    }
}