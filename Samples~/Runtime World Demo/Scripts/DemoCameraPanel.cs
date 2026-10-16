using System;
using System.Globalization;
using UnityEngine;

namespace TerraKit.Samples
{
    internal sealed class DemoCameraPanel
    {
        private readonly RuntimeWorldSettings _settings;
        private readonly DemoCameraController _camera;

        public DemoCameraPanel(
            RuntimeWorldSettings settings,
            DemoCameraController camera)
        {
            _settings = settings;
            _camera = camera;
        }

        public void Draw(DemoGuiStyles styles)
        {
            GUILayout.Label("Camera", styles.Subheading);

            DemoCameraMode previous = _camera.Mode;
            int selected = GUILayout.SelectionGrid(
                (int)_camera.Mode,
                new[] { "Stationary", "Orbiting", "Explore", "Free move" },
                2);

            selected = Mathf.Clamp(selected, 0, 3);
            if ((DemoCameraMode)selected != previous)
            {
                _camera.SetMode((DemoCameraMode)selected);
            }

            switch (_camera.Mode)
            {
                case DemoCameraMode.Stationary:
                    GUILayout.Label(
                        "Camera remains where it is; regions around its current position stay streamed.",
                        styles.Small);
                    break;

                case DemoCameraMode.Orbiting:
                    DrawFloatSlider(
                        "Orbit speed",
                        value => _settings.OrbitSpeedDegrees = value,
                        _settings.OrbitSpeedDegrees,
                        0f,
                        30f,
                        "0.0");

                    DrawFloatSlider(
                        "Orbit radius",
                        value => _settings.OrbitRadiusRegions = value,
                        _settings.OrbitRadiusRegions,
                        0.25f,
                        3f,
                        "0.00");

                    DrawFloatSlider(
                        "Camera height",
                        value => _settings.CameraHeight = value,
                        _settings.CameraHeight,
                        10f,
                        300f,
                        "0");

                    GUILayout.Label(
                        "Orbits around the region that was under the camera when the mode was selected.",
                        styles.Small);
                    break;

                case DemoCameraMode.Explore:
                    DrawFloatSlider(
                        "Travel speed",
                        value => _settings.ExploreSpeed = value,
                        _settings.ExploreSpeed,
                        0f,
                        180f,
                        "0");

                    DrawFloatSlider(
                        "Turn amount",
                        value => _settings.ExploreTurnSpeed = value,
                        _settings.ExploreTurnSpeed,
                        0f,
                        30f,
                        "0.0");

                    DrawFloatSlider(
                        "Camera height",
                        value => _settings.CameraHeight = value,
                        _settings.CameraHeight,
                        10f,
                        300f,
                        "0");

                    GUILayout.Label(
                        "Automatically travels across region boundaries. New terrain is generated ahead and old regions are unloaded.",
                        styles.Small);
                    break;

                case DemoCameraMode.FreeMove:
                    DrawFloatSlider(
                        "Move speed",
                        value => _settings.FreeMoveSpeed = value,
                        _settings.FreeMoveSpeed,
                        5f,
                        200f,
                        "0");

                    DrawFloatSlider(
                        "Look sensitivity",
                        value => _settings.FreeLookSensitivity = value,
                        _settings.FreeLookSensitivity,
                        0.2f,
                        8f,
                        "0.0");

                    GUILayout.Label(
                        "Hold right mouse to move/look: WASD move, Q/E down/up, Shift boost. Requires Unity's legacy Input API.",
                        styles.Small);
                    break;
            }
        }

        private static void DrawFloatSlider(
            string label,
            Action<float> setter,
            float value,
            float minimum,
            float maximum,
            string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(132f));

            float next =
                GUILayout.HorizontalSlider(value, minimum, maximum);

            setter(next);

            GUILayout.Label(
                next.ToString(format, CultureInfo.InvariantCulture),
                GUILayout.Width(48f));

            GUILayout.EndHorizontal();
        }
    }
}
