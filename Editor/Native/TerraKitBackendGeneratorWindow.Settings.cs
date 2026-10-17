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

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (ActionButton("Save As...", inline: true))
                    {
                        RunSettingsAction(
                            SaveSettingsAs,
                            "Generation settings saved.");
                    }

                    using (new EditorGUI.DisabledScope(_settingsAsset == null))
                    {
                        if (ActionButton("Update Saved", inline: true))
                        {
                            RunSettingsAction(
                                UpdateSavedSettings,
                                "Saved settings updated.");
                        }

                        if (ActionButton("Load Settings", inline: true))
                        {
                            RunSettingsAction(
                                LoadSavedSettings,
                                "Settings loaded. Generate Map to use them.");
                        }
                    }
                }
            }

            DrawSettingsJsonControls();
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
                        "This asset already exists. Choose a new name, or use Update Saved.");
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
                    "Select a settings asset under Assets, or use Save As.");
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