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

        public TerraKitRegionRenderer(Transform parent, Material material)
        {
            var root = new GameObject("Generated TerraKit World");
            root.transform.SetParent(parent, false);
            _worldRoot = root.transform;
            _material = material;
        }

        public LoadedRegion Create(Vector2Int coordinate, TerraKitGeneratedMeshData data)
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

            return new LoadedRegion(coordinate, regionObject, mesh);
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
