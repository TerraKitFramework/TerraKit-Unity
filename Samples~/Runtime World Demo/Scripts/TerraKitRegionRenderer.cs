using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TerraKit.Samples
{
    internal sealed class LoadedRegion
    {
        public readonly Vector2Int Coordinate;
        public readonly GameObject GameObject;
        public readonly Mesh Mesh;

        public LoadedRegion(Vector2Int coordinate, GameObject gameObject, Mesh mesh)
        {
            Coordinate = coordinate;
            GameObject = gameObject;
            Mesh = mesh;
        }
    }

    internal sealed class TerraKitRegionRenderer
    {
        private readonly Transform _worldRoot;
        private readonly Material _material;
        private readonly Dictionary<uint, GameObject> _prefabs = new Dictionary<uint, GameObject>();
        private readonly int _maxScatterInstancesPerRegion;

        public TerraKitRegionRenderer(
            Transform parent, Material material,
            IReadOnlyList<TerraKitScatterPrototype> prototypes,
            int maxScatterInstancesPerRegion)
        {
            var root = new GameObject("Generated TerraKit World");
            root.transform.SetParent(parent, false);
            _worldRoot = root.transform;
            _material = material;
            _maxScatterInstancesPerRegion = Mathf.Max(0, maxScatterInstancesPerRegion);

            if (prototypes != null)
            {
                foreach (TerraKitScatterPrototype prototype in prototypes)
                {
                    if (prototype != null && prototype.Prefab != null)
                        _prefabs.Add(checked((uint)prototype.PrototypeId), prototype.Prefab);
                }
            }
        }

        public LoadedRegion Create(
            Vector2Int coordinate, TerraKitGeneratedMeshData data,
            IReadOnlyList<TerraKitScatterData> scatters)
        {
            Mesh mesh = CreateUnityMesh(data, coordinate);

            var regionObject = new GameObject(
                "TerraKit Region " + coordinate.x + "," + coordinate.y);

            regionObject.transform.SetParent(_worldRoot, false);
            regionObject.transform.position = data.Origin;

            var filter = regionObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = regionObject.AddComponent<MeshRenderer>();
            if (_material != null)
            {
                renderer.sharedMaterial = _material;
            }

            SpawnScatter(regionObject.transform, data.WorldOrigin, scatters);
            return new LoadedRegion(coordinate, regionObject, mesh);
        }

        private void SpawnScatter(
            Transform regionRoot, TerraKitDouble3 meshOrigin,
            IReadOnlyList<TerraKitScatterData> scatters)
        {
            if (scatters == null || _prefabs.Count == 0 || _maxScatterInstancesPerRegion == 0)
                return;

            // Interleave stage outputs so one dense tree prototype cannot
            // consume the entire budget before rocks or shrubs are considered.
            int spawned = 0;
            var indices = new int[scatters.Count];
            bool remaining;
            do
            {
                remaining = false;
                for (int i = 0; i < scatters.Count; i++)
                {
                    TerraKitScatterData scatter = scatters[i];
                    if (scatter == null || scatter.Points == null ||
                        indices[i] >= scatter.Points.Length)
                        continue;

                    remaining = true;
                    TerraKitScatterPoint point = scatter.Points[indices[i]++];
                    GameObject prefab;
                    if (!_prefabs.TryGetValue(point.PrototypeId, out prefab))
                        continue;

                    // Subtract origins in double precision before casting.
                    var instance = UnityEngine.Object.Instantiate(prefab, regionRoot);
                    instance.transform.localPosition = new Vector3(
                        (float)(scatter.Origin.X - meshOrigin.X + point.LocalPosition.x),
                        (float)(scatter.Origin.Y - meshOrigin.Y + point.LocalPosition.y),
                        (float)(scatter.Origin.Z - meshOrigin.Z + point.LocalPosition.z));
                    instance.transform.localRotation = point.Rotation * prefab.transform.localRotation;
                    instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, point.Scale);

                    if (++spawned >= _maxScatterInstancesPerRegion)
                        return;
                }
            } while (remaining);
        }

        public void Destroy(LoadedRegion region)
        {
            if (region == null)
            {
                return;
            }

            if (region.Mesh != null)
            {
                Object.Destroy(region.Mesh);
            }

            if (region.GameObject != null)
            {
                Object.Destroy(region.GameObject);
            }
        }

        public void Dispose()
        {
            if (_worldRoot != null)
            {
                Object.Destroy(_worldRoot.gameObject);
            }
        }

        private static Mesh CreateUnityMesh(
            TerraKitGeneratedMeshData data,
            Vector2Int coordinate)
        {
            var mesh = new Mesh
            {
                name = "TerraKit Region " + coordinate.x + "," + coordinate.y
            };

            if (data.Positions.Length > ushort.MaxValue)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.vertices = data.Positions;
            mesh.triangles = data.Indices;

            if (data.Normals != null && data.Normals.Length == data.Positions.Length)
            {
                mesh.normals = data.Normals;
            }
            else
            {
                mesh.RecalculateNormals();
            }

            if (data.Texcoords != null && data.Texcoords.Length == data.Positions.Length)
            {
                mesh.uv = data.Texcoords;
            }

            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
