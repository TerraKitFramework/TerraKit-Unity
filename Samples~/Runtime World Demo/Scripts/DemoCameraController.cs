using System;
using UnityEngine;

namespace TerraKit.Samples
{
    /// <summary>
    /// Contains all camera-mode behaviour for the runtime sample.
    /// </summary>
    internal sealed class DemoCameraController
    {
        private readonly Camera _camera;
        private readonly RuntimeWorldSettings _settings;
        private readonly TerraKitWorldStreamer _streamer;

        private float _orbitAngle = 35f;
        private Vector3 _orbitCentre = Vector3.zero;
        private float _exploreYaw = 35f;
        private float _freeYaw = 35f;
        private float _freePitch = 24f;
        private bool _freeLookInitialised;

        public DemoCameraMode Mode { get { return _settings.CameraMode; } }

        public DemoCameraController(
            Camera camera,
            RuntimeWorldSettings settings,
            TerraKitWorldStreamer streamer)
        {
            _camera = camera;
            _settings = settings;
            _streamer = streamer;
        }

        public void PlaceInitialCamera()
        {
            if (_camera == null)
            {
                return;
            }

            float size = (float)_settings.RegionWorldSize;
            _orbitCentre = new Vector3(size * 0.5f, 0f, size * 0.5f);

            Vector3 offset = Quaternion.Euler(0f, _orbitAngle, 0f) *
                             new Vector3(
                                 0f,
                                 0f,
                                 -Mathf.Max(size, 20f) *
                                 _settings.OrbitRadiusRegions);

            _camera.transform.position =
                _orbitCentre + offset + Vector3.up * _settings.CameraHeight;

            LookDownward(_camera.transform, _orbitCentre);
            CaptureFreeLookAngles();
        }

        public void Update()
        {
            if (_camera == null)
            {
                return;
            }

            switch (_settings.CameraMode)
            {
                case DemoCameraMode.Stationary:
                    break;
                case DemoCameraMode.Orbiting:
                    UpdateOrbitCamera();
                    break;
                case DemoCameraMode.Explore:
                    UpdateExploreCamera();
                    break;
                case DemoCameraMode.FreeMove:
                    UpdateFreeMoveCamera();
                    break;
            }
        }

        public void SetMode(DemoCameraMode mode)
        {
            if (_settings.CameraMode == mode)
            {
                return;
            }

            _settings.CameraMode = mode;

            if (mode == DemoCameraMode.Orbiting)
            {
                _orbitCentre = _streamer.RegionCentre(
                    _streamer.WorldToRegion(_camera.transform.position));
            }
            else if (mode == DemoCameraMode.Explore)
            {
                Vector3 forward =
                    Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up);

                if (forward.sqrMagnitude > 0.001f)
                {
                    _exploreYaw =
                        Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                }
            }
            else if (mode == DemoCameraMode.FreeMove)
            {
                CaptureFreeLookAngles();
            }

            _streamer.MarkDirty();
        }

        private void UpdateOrbitCamera()
        {
            float size = (float)_settings.RegionWorldSize;
            float radius =
                Mathf.Max(size, 20f) *
                Mathf.Max(0.25f, _settings.OrbitRadiusRegions);

            _orbitAngle = Mathf.Repeat(
                _orbitAngle +
                _settings.OrbitSpeedDegrees * Time.deltaTime,
                360f);

            Vector3 offset =
                Quaternion.Euler(0f, _orbitAngle, 0f) *
                new Vector3(0f, 0f, -radius);

            _camera.transform.position =
                _orbitCentre + offset + Vector3.up * _settings.CameraHeight;

            LookDownward(_camera.transform, _orbitCentre);
        }

        private void UpdateExploreCamera()
        {
            float turn =
                Mathf.Sin(Time.time * 0.17f) * _settings.ExploreTurnSpeed;

            _exploreYaw += turn * Time.deltaTime;

            Quaternion heading = Quaternion.Euler(0f, _exploreYaw, 0f);
            Vector3 forward = heading * Vector3.forward;

            Vector3 position =
                _camera.transform.position +
                forward * _settings.ExploreSpeed * Time.deltaTime;

            position.y = _settings.CameraHeight;
            _camera.transform.position = position;
            _camera.transform.rotation =
                Quaternion.Euler(22f, _exploreYaw, 0f);
        }

        private void UpdateFreeMoveCamera()
        {
            if (!_freeLookInitialised)
            {
                CaptureFreeLookAngles();
            }

            try
            {
                if (!Input.GetMouseButton(1))
                {
                    return;
                }

                float speed =
                    _settings.FreeMoveSpeed *
                    (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f);

                float horizontal = 0f;
                float forward = 0f;
                float vertical = 0f;

                if (Input.GetKey(KeyCode.A)) horizontal -= 1f;
                if (Input.GetKey(KeyCode.D)) horizontal += 1f;
                if (Input.GetKey(KeyCode.S)) forward -= 1f;
                if (Input.GetKey(KeyCode.W)) forward += 1f;
                if (Input.GetKey(KeyCode.Q)) vertical -= 1f;
                if (Input.GetKey(KeyCode.E)) vertical += 1f;

                _freeYaw +=
                    Input.GetAxis("Mouse X") * _settings.FreeLookSensitivity;

                _freePitch -=
                    Input.GetAxis("Mouse Y") * _settings.FreeLookSensitivity;

                _freePitch = Mathf.Clamp(_freePitch, -85f, 85f);

                _camera.transform.rotation =
                    Quaternion.Euler(_freePitch, _freeYaw, 0f);

                Vector3 move =
                    _camera.transform.right * horizontal +
                    _camera.transform.forward * forward +
                    Vector3.up * vertical;

                if (move.sqrMagnitude > 1f)
                {
                    move.Normalize();
                }

                _camera.transform.position += move * speed * Time.deltaTime;
            }
            catch (InvalidOperationException)
            {
                // The sample remains usable when the project disables the
                // legacy Input API; the other camera modes require no input.
            }
        }

        private void CaptureFreeLookAngles()
        {
            if (_camera == null)
            {
                return;
            }

            Vector3 euler = _camera.transform.eulerAngles;
            _freeYaw = euler.y;
            _freePitch = euler.x > 180f ? euler.x - 360f : euler.x;
            _freeLookInitialised = true;
        }

        private static void LookDownward(
            Transform cameraTransform,
            Vector3 target)
        {
            Vector3 direction = target - cameraTransform.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                cameraTransform.rotation =
                    Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }
    }
}
