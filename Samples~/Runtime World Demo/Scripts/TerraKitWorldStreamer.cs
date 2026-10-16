using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TerraKit.Samples
{
    internal sealed class TerraKitWorldStreamer
    {
        private readonly RuntimeWorldSettings _settings;
        private readonly RuntimePipelineController _pipeline;
        private readonly TerraKitRegionRenderer _renderer;
        private readonly Dictionary<Vector2Int, LoadedRegion> _loadedRegions =
            new Dictionary<Vector2Int, LoadedRegion>();

        private bool _isGenerating;
        private bool _streamingSuppressed;
        private bool _streamDirty = true;
        private Vector2Int _streamCentre;
        private bool _hasStreamCentre;

        private int _generatedRegionCount;
        private ulong _lastVertexHash;
        private float _lastMinHeight;
        private float _lastMaxHeight;

        public string Status { get; private set; }
        public bool IsGenerating { get { return _isGenerating; } }
        public bool StreamingSuppressed { get { return _streamingSuppressed; } }
        public int LoadedRegionCount { get { return _loadedRegions.Count; } }
        public int GeneratedRegionCount { get { return _generatedRegionCount; } }
        public ulong LastVertexHash { get { return _lastVertexHash; } }
        public float LastMinHeight { get { return _lastMinHeight; } }
        public float LastMaxHeight { get { return _lastMaxHeight; } }

        public TerraKitWorldStreamer(
            Transform parent,
            Material terrainMaterial,
            RuntimePipelineController pipeline,
            RuntimeWorldSettings settings)
        {
            _pipeline = pipeline;
            _settings = settings;
            _renderer = new TerraKitRegionRenderer(parent, terrainMaterial);
            Status = "Waiting to stream terrain...";
        }

        public Vector2Int WorldToRegion(Vector3 worldPosition)
        {
            double size = _settings.RegionWorldSize;
            int x = (int)Math.Floor(worldPosition.x / size);
            int y = (int)Math.Floor(worldPosition.z / size);
            return new Vector2Int(x, y);
        }

        public Vector3 RegionCentre(Vector2Int coordinate)
        {
            float size = (float)_settings.RegionWorldSize;
            return new Vector3(
                (coordinate.x + 0.5f) * size,
                0f,
                (coordinate.y + 0.5f) * size);
        }

        public void SetStatus(string status)
        {
            Status = status;
        }

        public void MarkDirty()
        {
            _streamDirty = true;
        }

        public void Retry()
        {
            _streamingSuppressed = false;
            _streamDirty = true;
            Status = "Retrying streaming...";
        }

        public void Reset(string reason, Camera camera)
        {
            if (_pipeline.Graph == null || _isGenerating)
            {
                return;
            }

            if (!_pipeline.Validate())
            {
                ClearLoadedRegions();
                _streamingSuppressed = true;
                Status = reason + ": pipeline cannot currently produce displayable terrain.";
                return;
            }

            ClearLoadedRegions();
            _streamingSuppressed = false;
            _hasStreamCentre = false;
            _streamDirty = true;
            Status = reason + ". Streaming regions...";

            Update(camera);
        }

        public void Update(Camera camera)
        {
            if (camera == null ||
                _pipeline.Graph == null ||
                _isGenerating ||
                _streamingSuppressed)
            {
                return;
            }

            Vector2Int centre = WorldToRegion(camera.transform.position);
            if (!_hasStreamCentre || centre != _streamCentre)
            {
                _streamCentre = centre;
                _hasStreamCentre = true;
                _streamDirty = true;
            }

            UnloadDistantRegions(centre);

            if (!_streamDirty && HasAllDesiredRegions(centre))
            {
                return;
            }

            GenerateMissingRegions(centre);
            _streamDirty = !HasAllDesiredRegions(centre);
        }

        public void Dispose()
        {
            ClearLoadedRegions();
            _renderer.Dispose();
        }

        private bool HasAllDesiredRegions(Vector2Int centre)
        {
            for (int y = -_settings.StreamRadius; y <= _settings.StreamRadius; y++)
            {
                for (int x = -_settings.StreamRadius; x <= _settings.StreamRadius; x++)
                {
                    if (!_loadedRegions.ContainsKey(
                        new Vector2Int(centre.x + x, centre.y + y)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void GenerateMissingRegions(Vector2Int centre)
        {
            var missing = new List<Vector2Int>();

            for (int y = -_settings.StreamRadius; y <= _settings.StreamRadius; y++)
            {
                for (int x = -_settings.StreamRadius; x <= _settings.StreamRadius; x++)
                {
                    var coordinate = new Vector2Int(centre.x + x, centre.y + y);
                    if (!_loadedRegions.ContainsKey(coordinate))
                    {
                        missing.Add(coordinate);
                    }
                }
            }

            if (missing.Count == 0)
            {
                return;
            }

            missing.Sort((left, right) =>
                SquaredDistance(left, centre).CompareTo(
                    SquaredDistance(right, centre)));

            int count = Mathf.Min(
                Mathf.Max(1, _settings.RegionsPerStreamingBatch),
                missing.Count);

            List<Vector2Int> batchCoordinates = missing.Take(count).ToList();
            var requests = new List<TerraKitBackendGenerationRequest>(count);

            foreach (Vector2Int coordinate in batchCoordinates)
            {
                requests.Add(new TerraKitBackendGenerationRequest(
                    _settings.Seed,
                    coordinate.x,
                    coordinate.y,
                    0,
                    (uint)_settings.CellsPerRegion,
                    (uint)_settings.CellsPerRegion,
                    _settings.SampleSpacing,
                    _settings.SampleSpacing));
            }

            _isGenerating = true;
            Status = "Generating " + count + " region" +
                     (count == 1 ? string.Empty : "s") +
                     " around [" + centre.x + ", " + centre.y + "]...";

            try
            {
                var runner = new TerraKitBackendGraphRunner();
                IReadOnlyList<TerraKitGeneratedRegion> generated =
                    runner.GenerateResources(_pipeline.Graph, requests);

                for (int index = 0; index < generated.Count; index++)
                {
                    TerraKitGeneratedResource meshResource =
                        generated[index].Resources.LastOrDefault(resource =>
                            resource.Kind == TerraKitBackendResourceKind.Mesh &&
                            resource.Mesh != null);

                    if (meshResource == null)
                    {
                        throw new InvalidOperationException(
                            "The current pipeline generated no Mesh resource for region " +
                            batchCoordinates[index] + ".");
                    }

                    AddLoadedRegion(batchCoordinates[index], meshResource.Mesh);
                }

                Status = "Streaming active. Camera region [" +
                         centre.x + ", " + centre.y + "]; " +
                         _loadedRegions.Count + " regions loaded; " +
                         _generatedRegionCount + " generated total.";
            }
            catch (Exception exception)
            {
                _streamingSuppressed = true;
                Status = "Streaming stopped: " + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                _isGenerating = false;
            }
        }

        private void AddLoadedRegion(
            Vector2Int coordinate,
            TerraKitGeneratedMeshData data)
        {
            LoadedRegion old;
            if (_loadedRegions.TryGetValue(coordinate, out old))
            {
                _renderer.Destroy(old);
                _loadedRegions.Remove(coordinate);
            }

            LoadedRegion region = _renderer.Create(coordinate, data);
            _loadedRegions.Add(coordinate, region);
            _generatedRegionCount++;
            AccumulateMeshDiagnostics(data);
        }

        private void AccumulateMeshDiagnostics(TerraKitGeneratedMeshData data)
        {
            if (_generatedRegionCount <= 1)
            {
                _lastVertexHash = 1469598103934665603UL;
                _lastMinHeight = float.PositiveInfinity;
                _lastMaxHeight = float.NegativeInfinity;
            }

            unchecked
            {
                foreach (Vector3 vertex in data.Positions)
                {
                    _lastMinHeight = Mathf.Min(_lastMinHeight, vertex.y);
                    _lastMaxHeight = Mathf.Max(_lastMaxHeight, vertex.y);

                    _lastVertexHash ^= (uint)vertex.x.GetHashCode();
                    _lastVertexHash *= 1099511628211UL;
                    _lastVertexHash ^= (uint)vertex.y.GetHashCode();
                    _lastVertexHash *= 1099511628211UL;
                    _lastVertexHash ^= (uint)vertex.z.GetHashCode();
                    _lastVertexHash *= 1099511628211UL;
                }
            }
        }

        private void UnloadDistantRegions(Vector2Int centre)
        {
            int unloadRadius = _settings.StreamRadius + 1;
            var remove = new List<Vector2Int>();

            foreach (KeyValuePair<Vector2Int, LoadedRegion> pair in _loadedRegions)
            {
                if (Mathf.Abs(pair.Key.x - centre.x) > unloadRadius ||
                    Mathf.Abs(pair.Key.y - centre.y) > unloadRadius)
                {
                    remove.Add(pair.Key);
                }
            }

            foreach (Vector2Int coordinate in remove)
            {
                LoadedRegion region = _loadedRegions[coordinate];
                _renderer.Destroy(region);
                _loadedRegions.Remove(coordinate);
            }
        }

        private void ClearLoadedRegions()
        {
            foreach (LoadedRegion region in _loadedRegions.Values)
            {
                _renderer.Destroy(region);
            }

            _loadedRegions.Clear();
            _hasStreamCentre = false;
            _streamDirty = true;
            _generatedRegionCount = 0;
            _lastVertexHash = 0;
            _lastMinHeight = 0f;
            _lastMaxHeight = 0f;
        }

        private static int SquaredDistance(Vector2Int a, Vector2Int b)
        {
            int dx = a.x - b.x;
            int dy = a.y - b.y;
            return dx * dx + dy * dy;
        }
    }
}
