using System.Globalization;
using UnityEngine;

namespace TerraKit.Samples
{
    internal sealed class DemoDiagnosticsPanel
    {
        private readonly TerraKitWorldStreamer _streamer;

        public DemoDiagnosticsPanel(TerraKitWorldStreamer streamer)
        {
            _streamer = streamer;
        }

        public void Draw(DemoGuiStyles styles)
        {
            GUILayout.Label("Runtime status", styles.Subheading);
            GUILayout.Label(_streamer.Status, styles.Status);

            Camera unityCamera = Camera.main;
            if (unityCamera != null)
            {
                Vector2Int coordinate =
                    _streamer.WorldToRegion(
                        unityCamera.transform.position);

                GUILayout.Label(
                    "Camera region: [" +
                    coordinate.x + ", " +
                    coordinate.y + "]");
            }

            GUILayout.Label(
                "Loaded regions: " +
                _streamer.LoadedRegionCount);

            GUILayout.Label(
                "Generated since rebuild: " +
                _streamer.GeneratedRegionCount);

            if (_streamer.GeneratedRegionCount > 0)
            {
                GUILayout.Label(
                    "Observed height range: " +
                    _streamer.LastMinHeight.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture) +
                    " .. " +
                    _streamer.LastMaxHeight.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture));

                GUILayout.Label(
                    "Rolling mesh hash: 0x" +
                    _streamer.LastVertexHash.ToString("X16"));
            }

            if (_streamer.StreamingSuppressed &&
                GUILayout.Button("Retry streaming"))
            {
                _streamer.Retry();
            }
        }
    }
}
