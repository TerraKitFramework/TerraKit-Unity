using System;
using UnityEngine;

namespace TerraKit.Samples
{
    public sealed class TerraKitRuntimeWorldDemo : MonoBehaviour
    {
        [Header("Generation")]
        [SerializeField] private ulong seed = 12345;
        [SerializeField, Range(8, 256)] private int cellsPerRegion = 64;
        [SerializeField] private float sampleSpacing = 2f;
        [SerializeField, Range(0, 10)] private int streamRadius = 2;
        [SerializeField, Range(1, 16)] private int regionsPerStreamingBatch = 4;

        [Header("Camera")]
        [SerializeField] private DemoCameraMode cameraMode = DemoCameraMode.Explore;
        [SerializeField] private float cameraHeight = 90f;
        [SerializeField] private float orbitSpeedDegrees = 7f;
        [SerializeField] private float orbitRadiusRegions = 1.15f;
        [SerializeField] private float exploreSpeed = 42f;
        [SerializeField] private float exploreTurnSpeed = 7f;
        [SerializeField] private float freeMoveSpeed = 55f;
        [SerializeField] private float freeLookSensitivity = 2.2f;

        [Header("Regeneration")]
        [SerializeField] private float changeDebounceSeconds = 0.18f;

        private RuntimeWorldSettings _settings;
        private DemoSceneResources _scene;
        private RuntimePipelineController _pipeline;
        private TerraKitWorldStreamer _streamer;
        private DemoCameraController _cameraController;
        private TerraKitRuntimeDemoGui _gui;

        private bool _pendingWorldReset;
        private float _resetAt;
        private string _pendingResetReason;
        private string _fatalError;

        private void Awake()
        {
            _settings = new RuntimeWorldSettings
            {
                Seed = seed,
                CellsPerRegion = cellsPerRegion,
                SampleSpacing = sampleSpacing,
                StreamRadius = streamRadius,
                RegionsPerStreamingBatch = regionsPerStreamingBatch,
                CameraMode = cameraMode,
                CameraHeight = cameraHeight,
                OrbitSpeedDegrees = orbitSpeedDegrees,
                OrbitRadiusRegions = orbitRadiusRegions,
                ExploreSpeed = exploreSpeed,
                ExploreTurnSpeed = exploreTurnSpeed,
                FreeMoveSpeed = freeMoveSpeed,
                FreeLookSensitivity = freeLookSensitivity
            };

            try
            {
                _scene = new DemoSceneResources(transform);
                _scene.Initialise();

                _pipeline = new RuntimePipelineController();
                _pipeline.Initialise();

                _streamer = new TerraKitWorldStreamer(
                    transform,
                    _scene.TerrainMaterial,
                    _pipeline,
                    _settings);

                _cameraController = new DemoCameraController(
                    _scene.Camera,
                    _settings,
                    _streamer);

                _gui = new TerraKitRuntimeDemoGui(
                    _settings,
                    _pipeline,
                    _streamer,
                    _cameraController,
                    QueueWorldReset);

                _pipeline.Changed += QueueWorldReset;

                _streamer.SetStatus(
                    "TerraKit ready. " +
                    _pipeline.AvailableSchemas.Count +
                    " stages discovered from the native registry.");
            }
            catch (Exception exception)
            {
                _fatalError =
                    "TerraKit could not initialise: " +
                    exception.Message;

                Debug.LogException(exception, this);
            }
        }

        private void Start()
        {
            if (_cameraController == null || _streamer == null)
            {
                return;
            }

            _cameraController.PlaceInitialCamera();
            _streamer.Reset("Initial world", _scene.Camera);
        }

        private void Update()
        {
            if (_streamer == null || _cameraController == null)
            {
                return;
            }

            if (_pendingWorldReset &&
                !_streamer.IsGenerating &&
                Time.unscaledTime >= _resetAt)
            {
                _pendingWorldReset = false;
                _streamer.Reset(
                    string.IsNullOrEmpty(_pendingResetReason)
                        ? "Pipeline/settings changed"
                        : _pendingResetReason,
                    _scene.Camera);
            }

            _cameraController.Update();

            if (!_pendingWorldReset)
            {
                _streamer.Update(_scene.Camera);
            }
        }

        private void OnGUI()
        {
            if (_gui != null)
            {
                _gui.Draw();
                return;
            }

            if (!string.IsNullOrEmpty(_fatalError))
            {
                GUILayout.BeginArea(
                    new Rect(12f, 12f, Mathf.Min(520f, Screen.width - 24f), 150f),
                    GUI.skin.window);

                GUILayout.Label("TerraKit Runtime World");
                GUILayout.Label(_fatalError);
                GUILayout.EndArea();
            }
        }

        private void OnDestroy()
        {
            if (_pipeline != null)
            {
                _pipeline.Changed -= QueueWorldReset;
            }

            if (_streamer != null)
            {
                _streamer.Dispose();
            }

            if (_pipeline != null)
            {
                _pipeline.Dispose();
            }

            if (_scene != null)
            {
                _scene.Dispose();
            }
        }

        private void QueueWorldReset(string reason)
        {
            if (_streamer == null)
            {
                return;
            }

            _pendingWorldReset = true;
            _pendingResetReason = reason;
            _resetAt =
                Time.unscaledTime +
                Mathf.Max(0.02f, changeDebounceSeconds);

            _streamer.SetStatus(
                reason + ". World regeneration queued...");
        }
    }
}
