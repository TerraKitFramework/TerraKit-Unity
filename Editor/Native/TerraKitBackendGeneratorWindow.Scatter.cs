using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TerraKit.Editor
{
    internal sealed partial class TerraKitBackendGeneratorWindow
    {
        private int _scatterPointIndex;

        private void DrawScatterDataPreview(TerraKitGeneratedResource resource)
        {
            DrawParameterGroup("Scatter Data Preview");

            var scatter = resource.Scatter;
            var points = scatter.Points;
            if (points == null || points.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "This output contains no scatter points.",
                    MessageType.Info);
                return;
            }

            _scatterPointIndex = Mathf.Clamp(
                _scatterPointIndex, 0, points.Length - 1);
            _scatterPointIndex = EditorGUILayout.IntSlider(
                "Point index", _scatterPointIndex, 0, points.Length - 1);

            var point = points[_scatterPointIndex];
            DrawIdentifier(
                "Prototype ID",
                point.PrototypeId.ToString(CultureInfo.InvariantCulture));

            using (new EditorGUI.DisabledScope(true))
            {
                DrawResultVector(
                    "Local Position", point.LocalPosition);
                DrawResultVector(
                    "Rotation (degrees)", point.Rotation.eulerAngles);
                DrawResultVector(
                    "Scale", point.Scale);
            }

            DrawIdentifier(
                "World Position",
                FormatResultVector(
                    scatter.Origin.X + point.LocalPosition.x,
                    scatter.Origin.Y + point.LocalPosition.y,
                    scatter.Origin.Z + point.LocalPosition.z));
            DrawParameterNote(
                "Point indices start at 0. World Position = World Origin + Local Position.");
        }

        private void DrawScatterPrefabControls()
        {
            string backendError;
            if (_graph == null || _graph.nodes == null ||
                !TerraKitNodeRegistry.TryLoadBackend(out backendError))
                return;

            bool hasScatter = TerraKitNodeRegistry.BackendSchemas.Any(schema =>
                schema.Outputs.Any(port =>
                    port.ResourceKind == TerraKitBackendResourceKind.ScatterPoints) &&
                _graph.nodes.Any(node => node != null && node.typeId == schema.TypeId));
            if (!hasScatter) return;

            DrawParameterGroup("Scatter prefab mappings");
            DrawParameterNote(
                "Saved with this Graph. Match each Prototype ID to a prefab asset. " +
                "Generation settings JSON does not contain these mappings.");

            // Capture the graph before scheduling edits; changing the selected graph
            // must never redirect an outstanding edit to a different asset.
            TerraKitGraphAsset graph = _graph;
            var mappings = graph.scatterPrefabs;
            using (new EditorGUI.DisabledScope(_isGenerating))
            {
                if (mappings != null)
                {
                    for (int index = 0; index < mappings.Count; index++)
                    {
                        int removeIndex = index;
                        var mapping = mappings[index];
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField("Mapping " + (index + 1), EditorStyles.boldLabel);
                                if (GUILayout.Button("Remove", GUILayout.Width(72)))
                                    QueueScatterMappingEdit(graph, () =>
                                        graph.scatterPrefabs.RemoveAt(removeIndex));
                            }

                            if (mapping == null)
                            {
                                EditorGUILayout.HelpBox("Remove this empty mapping and add a new one.", MessageType.Warning);
                                continue;
                            }

                            EditorGUI.BeginChangeCheck();
                            long prototypeId = EditorGUILayout.LongField("Prototype ID", mapping.prototypeId);
                            var prefab = (GameObject)EditorGUILayout.ObjectField(
                                "Prefab", mapping.prefab, typeof(GameObject), false);
                            if (EditorGUI.EndChangeCheck())
                                QueueScatterMappingEdit(graph, () =>
                                {
                                    mapping.prototypeId = prototypeId;
                                    mapping.prefab = prefab;
                                });
                        }
                    }
                }

                if (ActionButton("Add Prefab Mapping", inline: true))
                    QueueScatterMappingEdit(graph, () =>
                    {
                        var used = new HashSet<long>(graph.scatterPrefabs
                            .Where(item => item != null).Select(item => item.prototypeId));
                        long prototypeId = 0;
                        while (used.Contains(prototypeId)) prototypeId++;
                        graph.scatterPrefabs.Add(new TerraKitScatterPrefabMapping
                        {
                            prototypeId = prototypeId
                        });
                    });
            }

            DrawScatterMappingWarnings(mappings);
        }

        private void QueueScatterMappingEdit(TerraKitGraphAsset graph, Action edit)
        {
            // Defer asset writes and list changes until IMGUI finishes drawing.
            // Field edits do not open dialogs or invalidate native generation data.
            EditorApplication.delayCall += () =>
            {
                if (this == null || graph == null) return;
                try
                {
                    Undo.RecordObject(graph, "Edit TerraKit Scatter Prefabs");
                    if (graph.scatterPrefabs == null)
                        graph.scatterPrefabs = new List<TerraKitScatterPrefabMapping>();
                    edit();
                    EditorUtility.SetDirty(graph);
                    AssetDatabase.SaveAssetIfDirty(graph);
                    if (_graph == graph)
                    {
                        // Keep valid data, but remove instances made with the previous mappings.
                        _scene.Dispose();
                        _message = "Scatter prefab mappings saved with " + graph.name +
                            ". Generate Map to update the Scene preview.";
                        _messageType = MessageType.Info;
                        SceneView.RepaintAll();
                    }
                    Repaint();
                }
                catch (Exception exception)
                {
                    ShowError(exception, "Could not save scatter prefab mappings.");
                }
            };
        }

        private void DrawScatterMappingWarnings(List<TerraKitScatterPrefabMapping> mappings)
        {
            var ids = new HashSet<long>();
            var assignedIds = new HashSet<uint>();
            if (mappings != null)
            {
                foreach (var mapping in mappings.Where(item => item != null))
                {
                    string id = mapping.prototypeId.ToString(CultureInfo.InvariantCulture);
                    if (mapping.prototypeId < 0 || mapping.prototypeId > uint.MaxValue)
                    {
                        EditorGUILayout.HelpBox(
                            "Prototype ID must be an integer from 0 to 4294967295.", MessageType.Error);
                        continue;
                    }
                    if (!ids.Add(mapping.prototypeId))
                        EditorGUILayout.HelpBox(
                            "Duplicate Prototype ID: " + id + ". Use one mapping per ID.", MessageType.Error);

                    if (mapping.prefab == null)
                        EditorGUILayout.HelpBox(
                            "Assign a prefab asset for Prototype ID " + id + ".", MessageType.Warning);
                    else if (!EditorUtility.IsPersistent(mapping.prefab) ||
                        !PrefabUtility.IsPartOfPrefabAsset(mapping.prefab))
                        EditorGUILayout.HelpBox(
                            "Prototype ID " + id + " requires a prefab asset from the Project window.", MessageType.Error);
                    else
                        assignedIds.Add((uint)mapping.prototypeId);
                }
            }

            if (_results == null) return;
            var missing = _results.SelectMany(region => region.Resources)
                .Where(resource => resource.Scatter != null && resource.Scatter.Points != null)
                .SelectMany(resource => resource.Scatter.Points)
                .Select(point => point.PrototypeId).Distinct()
                .Where(id => !assignedIds.Contains(id)).OrderBy(id => id).ToArray();
            if (missing.Length != 0)
                EditorGUILayout.HelpBox(
                    "Generated points have no assigned prefab for Prototype IDs: " +
                    string.Join(", ", missing.Select(id => id.ToString(CultureInfo.InvariantCulture))) +
                    ". Scatter data can still be generated and exported.", MessageType.Warning);
        }
    }
}