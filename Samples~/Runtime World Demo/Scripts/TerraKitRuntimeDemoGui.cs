using System;
using UnityEngine;

namespace TerraKit.Samples
{
    internal sealed class TerraKitRuntimeDemoGui
    {
        private readonly RuntimePipelineController _pipeline;
        private readonly TerraKitWorldStreamer _streamer;
        private readonly Action<string> _queueWorldReset;

        private readonly DemoGuiStyles _styles = new DemoGuiStyles();
        private readonly DemoGenerationPanel _generationPanel;
        private readonly DemoCameraPanel _cameraPanel;
        private readonly DemoPipelinePanel _pipelinePanel;
        private readonly DemoDiagnosticsPanel _diagnosticsPanel;

        private Vector2 _scroll;

        public TerraKitRuntimeDemoGui(
            RuntimeWorldSettings settings,
            RuntimePipelineController pipeline,
            TerraKitWorldStreamer streamer,
            DemoCameraController camera,
            Action<string> queueWorldReset)
        {
            _pipeline = pipeline;
            _streamer = streamer;
            _queueWorldReset = queueWorldReset;

            _generationPanel =
                new DemoGenerationPanel(
                    settings,
                    streamer,
                    queueWorldReset);

            _cameraPanel =
                new DemoCameraPanel(settings, camera);

            _pipelinePanel =
                new DemoPipelinePanel(pipeline);

            _diagnosticsPanel =
                new DemoDiagnosticsPanel(streamer);
        }

        public void Draw()
        {
            _styles.Ensure();

            float width = Mathf.Min(455f, Screen.width - 24f);
            Rect panel =
                new Rect(12f, 12f, width, Screen.height - 24f);

            GUILayout.BeginArea(panel, _styles.Panel);

            GUILayout.Label(
                "TerraKit Sample World",
                _styles.Heading);

            GUILayout.Label(
                "Build a pipeline from stages discovered from the TerraKit library. Then move through a streamed procedural world.",
                _styles.Status);

            GUILayout.Space(6f);

            _scroll =
                GUILayout.BeginScrollView(
                    _scroll,
                    false,
                    true);

            _generationPanel.Draw(_styles);
            GUILayout.Space(12f);

            _cameraPanel.Draw(_styles);
            GUILayout.Space(12f);

            _pipelinePanel.DrawActivePipeline(_styles);
            GUILayout.Space(12f);

            _pipelinePanel.DrawAvailableStages(_styles);
            GUILayout.Space(12f);

            _diagnosticsPanel.Draw(_styles);

            GUILayout.EndScrollView();

            GUILayout.Space(7f);
            DrawFooter();

            GUILayout.EndArea();
        }

        private void DrawFooter()
        {
            GUILayout.BeginHorizontal();

            GUI.enabled =
                !_streamer.IsGenerating &&
                _pipeline.Graph != null;

            if (GUILayout.Button(
                _streamer.IsGenerating
                    ? "Generating..."
                    : "Rebuild visible world",
                GUILayout.Height(32f)))
            {
                _queueWorldReset("Manual rebuild");
            }

            if (GUILayout.Button(
                "Reset pipeline",
                GUILayout.Width(112f),
                GUILayout.Height(32f)))
            {
                _pipeline.ResetToDefault();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
    }
}
