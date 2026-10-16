using System;
using System.Globalization;
using UnityEngine;

namespace TerraKit.Samples
{
    internal sealed class DemoGenerationPanel
    {
        private readonly RuntimeWorldSettings _settings;
        private readonly TerraKitWorldStreamer _streamer;
        private readonly Action<string> _queueWorldReset;

        private string _seedText;
        private string _cellsText;
        private string _spacingText;
        private string _inputError;

        public DemoGenerationPanel(
            RuntimeWorldSettings settings,
            TerraKitWorldStreamer streamer,
            Action<string> queueWorldReset)
        {
            _settings = settings;
            _streamer = streamer;
            _queueWorldReset = queueWorldReset;

            _seedText = settings.Seed.ToString(CultureInfo.InvariantCulture);
            _cellsText =
                settings.CellsPerRegion.ToString(CultureInfo.InvariantCulture);
            _spacingText =
                settings.SampleSpacing.ToString("R", CultureInfo.InvariantCulture);
        }

        public void Draw(DemoGuiStyles styles)
        {
            GUILayout.Label("World streaming", styles.Subheading);

            DrawSeedSetting();
            DrawCellsSetting();
            DrawSpacingSetting();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Stream radius", GUILayout.Width(132f));

            int newRadius = Mathf.RoundToInt(
                GUILayout.HorizontalSlider(
                    _settings.StreamRadius,
                    0f,
                    10f));

            GUILayout.Label(
                newRadius.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(28f));
            GUILayout.EndHorizontal();

            if (newRadius != _settings.StreamRadius)
            {
                _settings.StreamRadius = newRadius;
                _streamer.MarkDirty();
                _streamer.SetStatus("Streaming radius changed.");
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Regions / batch", GUILayout.Width(132f));

            int newBatch = Mathf.RoundToInt(
                GUILayout.HorizontalSlider(
                    _settings.RegionsPerStreamingBatch,
                    1f,
                    16f));

            GUILayout.Label(
                newBatch.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(28f));
            GUILayout.EndHorizontal();

            _settings.RegionsPerStreamingBatch = newBatch;

            if (!string.IsNullOrEmpty(_inputError))
            {
                GUILayout.Label(_inputError, styles.Error);
            }
        }

        private void DrawSeedSetting()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed", GUILayout.Width(132f));

            string changed = GUILayout.TextField(_seedText ?? string.Empty);
            if (GUILayout.Button("New", GUILayout.Width(48f)))
            {
                _settings.Seed = unchecked((ulong)DateTime.UtcNow.Ticks);
                changed =
                    _settings.Seed.ToString(CultureInfo.InvariantCulture);
            }

            GUILayout.EndHorizontal();

            if (string.Equals(changed, _seedText, StringComparison.Ordinal))
            {
                return;
            }

            _seedText = changed;

            ulong parsed;
            if (ulong.TryParse(
                _seedText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed))
            {
                _settings.Seed = parsed;
                _inputError = null;
                _queueWorldReset("Seed changed");
            }
            else
            {
                _inputError = "Seed must be a non-negative whole number.";
            }
        }

        private void DrawCellsSetting()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Cells / region", GUILayout.Width(132f));
            string changed = GUILayout.TextField(_cellsText ?? string.Empty);
            GUILayout.EndHorizontal();

            if (string.Equals(changed, _cellsText, StringComparison.Ordinal))
            {
                return;
            }

            _cellsText = changed;

            int parsed;
            if (int.TryParse(
                    _cellsText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsed) &&
                parsed >= 8 &&
                parsed <= 256)
            {
                _settings.CellsPerRegion = parsed;
                _inputError = null;
                _queueWorldReset("Region layout changed");
            }
            else
            {
                _inputError =
                    "Cells per region must be between 8 and 256.";
            }
        }

        private void DrawSpacingSetting()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sample spacing", GUILayout.Width(132f));
            string changed = GUILayout.TextField(_spacingText ?? string.Empty);
            GUILayout.EndHorizontal();

            if (string.Equals(
                changed,
                _spacingText,
                StringComparison.Ordinal))
            {
                return;
            }

            _spacingText = changed;

            float parsed;
            if (float.TryParse(
                    _spacingText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out parsed) &&
                !float.IsNaN(parsed) &&
                !float.IsInfinity(parsed) &&
                parsed > 0f)
            {
                _settings.SampleSpacing = parsed;
                _inputError = null;
                _queueWorldReset("Region layout changed");
            }
            else
            {
                _inputError =
                    "Sample spacing must be a positive finite number.";
            }
        }
    }
}
