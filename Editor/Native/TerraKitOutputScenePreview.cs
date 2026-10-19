using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TerraKit.Editor
{
    internal sealed class TerraKitOutputScenePreview : IDisposable
    {
        private readonly Dictionary<GameObject, int> _objectRegions = new Dictionary<GameObject, int>();
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<GameObject> _scatterObjects = new List<GameObject>();
        private const int MaxScatterPreviewInstances = 10000;
        private Dictionary<uint, GameObject> _scatterPrefabLookup = new Dictionary<uint, GameObject>();

        internal int ScatterInstanceCount => _scatterObjects.Count;
        internal bool HasSceneObjects => _objects.Count != 0 || _scatterObjects.Count != 0;
        internal string PreviewNotice { get; private set; }
        private GameObject _root;
        private Scene _exportScene;
        private IReadOnlyList<TerraKitGeneratedRegion> _results;

        internal static string Label(TerraKitGeneratedResource resource)
        {
            var output = resource.Output;
            string id = output.NodeId ?? "";
            if (id.Length > 8) id = id.Substring(0, 8);
            return "#" + output.ResourceKey + " " + output.NodeName + " [" + id + "] / " +
                   output.PortName + " (" + output.Kind + ")";
        }

        internal static string RegionLabel(TerraKitBackendGenerationRequest request)
        {
            return "(" + request.RegionX + ", " + request.RegionY +
                   (request.Is3D ? ", " + request.RegionZ : "") + ")";
        }

        internal bool HasVisibleMeshes
        {
            get { return _objects.Any(item => item != null && item.activeInHierarchy); }
        }

        internal void Show(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName,
            IReadOnlyList<TerraKitScatterPrefabMapping> scatterPrefabs = null,
            bool frameSceneView = true)
        {
            Build(results, graphName, scatterPrefabs);
            if (!HasSceneObjects)
            {
                Dispose();
                return;
            }

            ShowAll();
            // Automatic previews update the map without moving the Scene camera.
            if (!frameSceneView)
            {
                SceneView.RepaintAll();
                return;
            }

            Bounds bounds = default;
            bool hasBounds = false;
            foreach (var item in _objects.Concat(_scatterObjects))
            {
                if (item == null) continue;
                foreach (var renderer in item.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                if (!hasBounds)
                {
                    bounds = new Bounds(item.transform.position, Vector3.one);
                    hasBounds = true;
                }
            }
            if (hasBounds && SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(bounds, false);
            SceneView.RepaintAll();
        }

        private void ShowAll()
        {
            _root.SetActive(true);
            foreach (var item in _objects)
            {
                item.transform.parent.gameObject.SetActive(true);
                item.SetActive(true);
            }
            foreach (var item in _scatterObjects)
            {
                if (item != null && item.transform.parent != null)
                    item.transform.parent.gameObject.SetActive(true);
            }
        }

        private void Build(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName,
            IReadOnlyList<TerraKitScatterPrefabMapping> scatterPrefabs = null)
        {
            Dispose();
            try
            {
                _root = NewObject("Preview-" + graphName, null);
                var prefabLookup = BuildScatterPrefabLookup(scatterPrefabs);
                _scatterPrefabLookup = prefabLookup;
                var pipeline = GraphicsSettings.currentRenderPipeline;
                if (pipeline == null) pipeline = GraphicsSettings.defaultRenderPipeline;
                Material material = pipeline != null ? pipeline.defaultMaterial : null;
                if (material == null) material = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                for (int r = 0; r < results.Count; r++)
                {
                    GameObject region = null;
                    foreach (var resource in results[r].Resources)
                    {
                        if (resource.Mesh == null)
                        {
                            if (resource.Scatter != null && prefabLookup.Count != 0)
                            {
                                if (region == null)
                                    region = NewObject("Region " + RegionLabel(results[r].Request), _root.transform);
                                BuildScatterObjects(resource, region.transform, prefabLookup);
                            }
                            continue;
                        }
                        if (region == null) region = NewObject("Region " + RegionLabel(results[r].Request), _root.transform);
                        var child = NewObject(Label(resource), region.transform);
                        var mesh = TerraKitResourceExport.CreateMesh(resource.Mesh);
                        _meshes.Add(mesh);
                        mesh.name = Label(resource);
                        mesh.hideFlags = HideFlags.HideAndDontSave;
                        child.AddComponent<MeshFilter>().sharedMesh = mesh;
                        child.AddComponent<MeshRenderer>().sharedMaterial = material;
                        // The Generator arranges regions around zero, so no origin rebasing is needed.
                        child.transform.localPosition = resource.Mesh.Origin;
                        _objects.Add(child);
                        _objectRegions.Add(child, r);
                    }
                }
                _results = results;
            }
            catch { Dispose(); throw; }
        }

        private static Dictionary<uint, GameObject> BuildScatterPrefabLookup(
            IReadOnlyList<TerraKitScatterPrefabMapping> mappings)
        {
            var lookup = new Dictionary<uint, GameObject>();
            var seen = new HashSet<uint>();
            if (mappings == null) return lookup;

            foreach (var mapping in mappings)
            {
                if (mapping == null || mapping.prototypeId < 0 || mapping.prototypeId > uint.MaxValue)
                    continue;
                uint id = (uint)mapping.prototypeId;
                if (!seen.Add(id))
                {
                    // Ambiguous IDs must never choose a prefab based on list order.
                    lookup.Remove(id);
                    continue;
                }
                if (mapping.prefab != null && EditorUtility.IsPersistent(mapping.prefab) &&
                    PrefabUtility.IsPartOfPrefabAsset(mapping.prefab))
                    lookup.Add(id, mapping.prefab);
            }
            return lookup;
        }

        private void BuildScatterObjects(TerraKitGeneratedResource resource, Transform region,
            Dictionary<uint, GameObject> prefabs)
        {
            var scatter = resource.Scatter;
            if (scatter.Points == null) return;
            GameObject group = null;
            for (int index = 0; index < scatter.Points.Length; index++)
            {
                var point = scatter.Points[index];
                if (!prefabs.TryGetValue(point.PrototypeId, out var prefab)) continue;
                if (_scatterObjects.Count >= MaxScatterPreviewInstances)
                {
                    PreviewNotice = "Scene preview is limited to " + MaxScatterPreviewInstances +
                        " scatter instances. Generated data and JSON export still contain all points.";
                    return;
                }

                // Add the precise origin before converting to Unity's float coordinates.
                var position = new Vector3(
                    (float)(scatter.Origin.X + point.LocalPosition.x),
                    (float)(scatter.Origin.Y + point.LocalPosition.y),
                    (float)(scatter.Origin.Z + point.LocalPosition.z));
                if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
                    throw new InvalidOperationException("A scatter position exceeds Unity's coordinate range.");

                if (group == null) group = NewObject(Label(resource), region);
                var instance = PrefabUtility.InstantiatePrefab(prefab, group.transform) as GameObject;
                if (instance == null)
                    throw new InvalidOperationException("Could not instantiate scatter prefab " + prefab.name + ".");

                _scatterObjects.Add(instance);
                instance.name = prefab.name + " [Point " + index + ", Prototype " + point.PrototypeId + "]";
                instance.transform.localPosition = position;
                instance.transform.localRotation = point.Rotation * prefab.transform.localRotation;
                instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, point.Scale);

                // Hide only temporary instances from scene saving; shared assets remain untouched.
                foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                    child.gameObject.hideFlags |= HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static GameObject NewObject(string name, Transform parent)
        {
            var result = new GameObject(name);
            result.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            result.transform.SetParent(parent, false);
            return result;
        }

        // Export from a private snapshot without changing the live preview.
        private TerraKitOutputScenePreview PrepareExport(IReadOnlyList<TerraKitGeneratedRegion> results,
            string graphName, IReadOnlyList<TerraKitScatterPrefabMapping> scatterPrefabs = null)
        {
            var snapshot = new TerraKitOutputScenePreview();
            try
            {
                var requestedPrefabs = BuildScatterPrefabLookup(scatterPrefabs);
                bool mappingsMatch = scatterPrefabs == null ||
                    (requestedPrefabs.Count == _scatterPrefabLookup.Count &&
                     requestedPrefabs.All(pair => _scatterPrefabLookup.TryGetValue(pair.Key, out var prefab) &&
                         prefab == pair.Value));
                if (_root != null && ReferenceEquals(_results, results) && mappingsMatch &&
                    _objects.Concat(_scatterObjects).All(item =>
                        item != null && item.transform.IsChildOf(_root.transform)))
                {
                    snapshot._root = Object.Instantiate(_root);
                    snapshot._root.name = _root.name;
                    snapshot._results = results;
                    snapshot.PreviewNotice = PreviewNotice;
                    snapshot._scatterPrefabLookup = new Dictionary<uint, GameObject>(_scatterPrefabLookup);
                    foreach (var item in _objects)
                    {
                        var copy = FindSnapshotCopy(item.transform, _root.transform, snapshot._root.transform);
                        snapshot._objects.Add(copy.gameObject);
                        snapshot._objectRegions.Add(copy.gameObject, _objectRegions[item]);
                    }
                    foreach (var item in _scatterObjects)
                    {
                        var copy = FindSnapshotCopy(item.transform, _root.transform, snapshot._root.transform);
                        snapshot._scatterObjects.Add(copy.gameObject);
                    }
                    // Snapshot geometry and materials are borrowed; cleanup owns only its objects.
                }
                else snapshot.Build(results, graphName, scatterPrefabs);

                snapshot._exportScene = EditorSceneManager.NewPreviewScene();
                SceneManager.MoveGameObjectToScene(snapshot._root, snapshot._exportScene);
                snapshot._root.hideFlags = HideFlags.HideAndDontSave;
                snapshot.ShowAll();
                return snapshot;
            }
            catch { snapshot.Dispose(); throw; }
        }

        private static Transform FindSnapshotCopy(Transform source, Transform originalRoot, Transform copyRoot)
        {
            var indices = new Stack<int>();
            while (source != originalRoot)
            {
                indices.Push(source.GetSiblingIndex());
                source = source.parent;
            }
            while (indices.Count > 0) copyRoot = copyRoot.GetChild(indices.Pop());
            return copyRoot;
        }

        internal string SaveImage(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName)
        {
            using (var snapshot = PrepareExport(results, graphName))
            {
                var renderers = snapshot._objects.Select(item => item.GetComponent<MeshRenderer>()).ToArray();
                return TerraKitMeshImageExport.Save(renderers, graphName);
            }
        }

        internal bool SavePrefab(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName,
            IReadOnlyList<TerraKitScatterPrefabMapping> scatterPrefabs = null)
        {
            var lookup = BuildScatterPrefabLookup(scatterPrefabs);
            var missing = results.SelectMany(region => region.Resources)
                .Where(resource => resource.Scatter != null && resource.Scatter.Points != null)
                .SelectMany(resource => resource.Scatter.Points)
                .Select(point => point.PrototypeId).Distinct()
                .Where(id => !lookup.ContainsKey(id)).OrderBy(id => id).ToArray();
            if (missing.Length != 0)
                throw new InvalidOperationException(
                    "Assign one valid prefab per generated Prototype ID before saving the complete map. Missing or ambiguous IDs: " +
                    string.Join(", ", missing.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))));

            var destination = _root != null ? _root.scene : SceneManager.GetActiveScene();
            using (var snapshot = PrepareExport(results, graphName, scatterPrefabs))
            {
                if (!snapshot.HasSceneObjects)
                    throw new InvalidOperationException("The map has no Mesh or mapped scatter objects to save.");
                if (!string.IsNullOrEmpty(snapshot.PreviewNotice))
                    throw new InvalidOperationException(
                        "The scatter preview is truncated. Reduce the scatter count or region count before saving the complete map.");
                return snapshot.SaveEntries(snapshot._objects.ToArray(), graphName + "_Map", destination,
                    "Save Map Prefab", "Save all Regions with their Meshes, materials and scatter objects as one map Prefab.",
                    true, snapshot._scatterObjects.ToArray());
            }
        }

        internal bool SaveRegionMeshes(IReadOnlyList<TerraKitGeneratedRegion> results, string graphName,
            int regionIndex)
        {
            if (results == null || regionIndex < 0 || regionIndex >= results.Count)
                throw new ArgumentOutOfRangeException(nameof(regionIndex), "Select a generated Region first.");
            if (!results[regionIndex].Resources.Any(resource => resource.Mesh != null))
                throw new InvalidOperationException("The selected Region has no Mesh outputs to save.");

            using (var snapshot = PrepareExport(results, graphName))
            {
                var objects = snapshot.ObjectsForRegion(regionIndex);
                var request = results[regionIndex].Request;
                string name = graphName + "_Region_" + request.RegionX + "_" + request.RegionY +
                    (request.Is3D ? "_" + request.RegionZ : "");
                // A companion Prefab keeps material slots bound to the saved Mesh assets.
                return snapshot.SaveEntries(objects, name, default,
                    "Save Region Meshes and Materials",
                    "Save all Mesh outputs in the selected Region with their materials. A Region Prefab preserves material assignments.",
                    false);
            }
        }

        private GameObject[] ObjectsForRegion(int regionIndex)
        {
            return _objects.Where(item => _objectRegions[item] == regionIndex).ToArray();
        }

        private bool SaveEntries(
            GameObject[] objects,
            string suggestedName,
            Scene destination,
            string dialogTitle,
            string description,
            bool placeInScene,
            GameObject[] scatterObjects = null)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                dialogTitle, suggestedName, "prefab", description);

            if (string.IsNullOrEmpty(path))
                return false;

            // Import folders created in the save dialog before using AssetDatabase.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            path = path.Replace('\\', '/');
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');

            if (string.IsNullOrEmpty(parent) ||
                !AssetDatabase.IsValidFolder(parent))
            {
                throw new IOException(
                    "The save folder is not available in Unity: " + parent);
            }

            return SaveEntriesToPath(objects, path, destination, placeInScene, scatterObjects);
        }

        private bool SaveEntriesToPath(GameObject[] objects, string path, Scene destination, bool placeInScene,
            GameObject[] scatterObjects = null)
        {
            // Each save owns new assets; existing exports and their instances remain unchanged.
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            string prefabPath = path;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/" +
                Path.GetFileNameWithoutExtension(path) + "_Resources");
            var savedPaths = new List<string>();
            var meshes = new Dictionary<Mesh, Mesh>();
            var materials = new Dictionary<Material, Material>();
            var textures = new Dictionary<Texture, Texture>();
            var ancestors = new Dictionary<Transform, Transform>();
            GameObject export = null;
            GameObject instance = null;
            try
            {
                string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
                if (string.IsNullOrEmpty(guid)) throw new IOException("Could not create the asset folder.");
                savedPaths.Add(folder);
                export = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
                export.hideFlags = HideFlags.HideAndDontSave;
                foreach (var source in objects)
                {
                    var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
                    if (sourceMesh == null) throw new InvalidOperationException("A preview Mesh is missing.");
                    if (!meshes.TryGetValue(sourceMesh, out var mesh))
                    {
                        mesh = Object.Instantiate(sourceMesh);
                        mesh.hideFlags = HideFlags.None;
                        string meshPath = folder + "/Mesh_" + (meshes.Count + 1) + ".asset";
                        SaveNewAsset(mesh, meshPath, savedPaths);
                        meshes.Add(sourceMesh, mesh);
                    }

                    var child = new GameObject(source.name);
                    child.transform.SetParent(CopyAncestors(source.transform.parent,
                        export.transform, ancestors), false);
                    CopyLocalTransform(source.transform, child.transform);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var sourceRenderer = source.GetComponent<MeshRenderer>();
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = sourceRenderer.sharedMaterials
                        .Select(material => SaveMaterial(material, folder, materials, textures, savedPaths)).ToArray();
                    renderer.enabled = sourceRenderer.enabled;
                    renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                    renderer.receiveShadows = sourceRenderer.receiveShadows;
                }

                foreach (var source in scatterObjects ?? Array.Empty<GameObject>())
                {
                    if (source == null) throw new InvalidOperationException("A scatter preview instance is missing.");
                    var parentTransform = CopyAncestors(source.transform.parent, export.transform, ancestors);
                    var child = Object.Instantiate(source, parentTransform, false);
                    child.name = source.name;
                    CopyLocalTransform(source.transform, child.transform);
                    ClearTemporaryFlags(child);
                    SaveScatterTransientAssets(child, folder, meshes, materials, textures, savedPaths);
                }

                export.hideFlags = HideFlags.None;
                savedPaths.Add(prefabPath);
                var prefab = PrefabUtility.SaveAsPrefabAsset(export, prefabPath);
                if (prefab == null) throw new IOException("Could not save the prefab.");
                AssetDatabase.SaveAssets();

                if (placeInScene)
                {
                    // Only Save Prefab places a complete map instance at the Unity origin.
                    instance = PrefabUtility.InstantiatePrefab(prefab, destination) as GameObject;
                    if (instance == null) throw new IOException("Could not place the saved prefab in the scene.");
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    instance.transform.localScale = Vector3.one;
                    Undo.RegisterCreatedObjectUndo(instance, "Save TerraKit Prefab");
                    EditorSceneManager.MarkSceneDirty(instance.scene);
                    Selection.activeGameObject = instance;
                }
                else Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                SceneView.RepaintAll();
            }
            catch
            {
                if (instance != null) Object.DestroyImmediate(instance);
                for (int i = savedPaths.Count - 1; i >= 0; i--) AssetDatabase.DeleteAsset(savedPaths[i]);
                throw;
            }
            finally { if (export != null) Object.DestroyImmediate(export); }
            return true;
        }

        private static void ClearTemporaryFlags(GameObject root)
        {
            const HideFlags temporary = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild |
                HideFlags.DontUnloadUnusedAsset;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.hideFlags &= ~temporary;
                foreach (var component in transform.GetComponents<Component>())
                    if (component != null) component.hideFlags &= ~temporary;
            }
        }

        private static void SaveScatterTransientAssets(GameObject root, string folder,
            Dictionary<Mesh, Mesh> meshes, Dictionary<Material, Material> materials,
            Dictionary<Texture, Texture> textures, List<string> savedPaths)
        {
            // Persistent prototype assets remain shared; temporary geometry must be saved.
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                filter.sharedMesh = SaveTransientMesh(filter.sharedMesh, folder, meshes, savedPaths);
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                renderer.sharedMesh = SaveTransientMesh(renderer.sharedMesh, folder, meshes, savedPaths);
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
                collider.sharedMesh = SaveTransientMesh(collider.sharedMesh, folder, meshes, savedPaths);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(material =>
                    material == null || EditorUtility.IsPersistent(material)
                        ? material : SaveMaterial(material, folder, materials, textures, savedPaths)).ToArray();
        }

        private static Mesh SaveTransientMesh(Mesh source, string folder,
            Dictionary<Mesh, Mesh> meshes, List<string> savedPaths)
        {
            if (source == null || EditorUtility.IsPersistent(source)) return source;
            if (meshes.TryGetValue(source, out var saved)) return saved;
            saved = Object.Instantiate(source);
            saved.hideFlags = HideFlags.None;
            SaveNewAsset(saved, folder + "/Mesh_" + (meshes.Count + 1) + ".asset", savedPaths);
            meshes.Add(source, saved);
            return saved;
        }

        private Transform CopyAncestors(Transform source, Transform root,
            Dictionary<Transform, Transform> copies)
        {
            if (source == null || source == _root.transform) return root;
            if (copies.TryGetValue(source, out var copy)) return copy;
            var parent = CopyAncestors(source.parent, root, copies);
            // Copy generated region groups below the permanent map root.
            copy = new GameObject(source.name).transform;
            copy.SetParent(parent, false);
            CopyLocalTransform(source, copy);
            copies.Add(source, copy);
            return copy;
        }

        private static void CopyLocalTransform(Transform source, Transform target)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }

        private static Material SaveMaterial(Material source, string folder,
            Dictionary<Material, Material> materials, Dictionary<Texture, Texture> textures,
            List<string> savedPaths)
        {
            if (source == null) return null;
            if (materials.TryGetValue(source, out var saved)) return saved;
            saved = new Material(source) { name = source.name, hideFlags = HideFlags.None };
            SaveNewAsset(saved, folder + "/Material_" + (materials.Count + 1) + ".mat", savedPaths);
            materials.Add(source, saved);
            foreach (string property in saved.GetTexturePropertyNames())
            {
                var texture = saved.GetTexture(property);
                if (texture == null || EditorUtility.IsPersistent(texture)) continue;
                if (!textures.TryGetValue(texture, out var savedTexture))
                {
                    // Temporary render targets cannot be referenced by a persistent material.
                    if (texture is RenderTexture)
                        throw new InvalidOperationException("Save the material's RenderTexture as a texture asset first.");
                    savedTexture = Object.Instantiate(texture);
                    savedTexture.hideFlags = HideFlags.None;
                    SaveNewAsset(savedTexture, folder + "/Texture_" + (textures.Count + 1) + ".asset", savedPaths);
                    textures.Add(texture, savedTexture);
                }
                saved.SetTexture(property, savedTexture);
            }
            EditorUtility.SetDirty(saved);
            return saved;
        }

        private static void SaveNewAsset(Object asset, string path, List<string> savedPaths)
        {
            savedPaths.Add(path);
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                if (AssetDatabase.GetAssetPath(asset) != path)
                    throw new IOException("Could not save asset: " + path);
            }
            catch
            {
                if (!EditorUtility.IsPersistent(asset)) Object.DestroyImmediate(asset);
                throw;
            }
        }

        public void Dispose()
        {
            // Release selected preview objects before destroying them.
            var previousSelection = Selection.objects;

            var remainingSelection = previousSelection.Where(selected =>
            {
                if (selected == null)
                    return false;

                if (selected is Mesh mesh && _meshes.Contains(mesh))
                    return false;

                GameObject gameObject = selected as GameObject;

                if (selected is Component component)
                    gameObject = component.gameObject;

                if (gameObject == null || _root == null)
                    return true;

                return gameObject != _root &&
                    !gameObject.transform.IsChildOf(_root.transform);
            }).ToArray();

            if (remainingSelection.Length != previousSelection.Length)
            {
                Selection.objects = remainingSelection;
                // Release cached Inspector editors while their preview targets still exist.
                ActiveEditorTracker.sharedTracker.ForceRebuild();
            }

            if (_root != null)
                Object.DestroyImmediate(_root);

            foreach (var mesh in _meshes)
            {
                if (mesh != null)
                    Object.DestroyImmediate(mesh);
            }

            _objects.Clear();
            _scatterObjects.Clear();
            _scatterPrefabLookup.Clear();
            PreviewNotice = null;
            _objectRegions.Clear();
            _meshes.Clear();
            _root = null;
            _results = null;

            if (_exportScene.IsValid())
                EditorSceneManager.ClosePreviewScene(_exportScene);

            _exportScene = default;
        }
    }
}