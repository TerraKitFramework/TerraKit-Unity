using System;
using System.Linq;
using UnityEngine;

namespace TerraKit.Samples
{
    internal sealed class DemoPipelinePanel
    {
        private readonly RuntimePipelineController _pipeline;
        private Vector2 _availableStageScroll;

        public DemoPipelinePanel(RuntimePipelineController pipeline)
        {
            _pipeline = pipeline;
        }

        public void DrawActivePipeline(DemoGuiStyles styles)
        {
            GUILayout.Label("Active pipeline", styles.Subheading);

            if (_pipeline.Graph == null ||
                _pipeline.Graph.nodes.Count == 0)
            {
                GUILayout.Label(
                    "No stages. Add a stage from the registry below.",
                    styles.Status);
                return;
            }

            for (int index = 0;
                 index < _pipeline.Graph.nodes.Count;
                 index++)
            {
                TerraKitNodeData node = _pipeline.Graph.nodes[index];
                TerraKitBackendStageSchema schema;

                if (!_pipeline.TryGetSchema(node.typeId, out schema))
                {
                    continue;
                }

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();

                GUILayout.Label(
                    (index + 1) + ". " + schema.DisplayName,
                    styles.Subheading);

                GUILayout.FlexibleSpace();

                GUI.enabled = index > 0;
                if (GUILayout.Button("↑", GUILayout.Width(30f)))
                {
                    _pipeline.MoveStage(index, index - 1);
                    EndStageBox();
                    return;
                }

                GUI.enabled = index < _pipeline.Graph.nodes.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(30f)))
                {
                    _pipeline.MoveStage(index, index + 1);
                    EndStageBox();
                    return;
                }

                GUI.enabled = true;
                if (GUILayout.Button("Remove", GUILayout.Width(68f)))
                {
                    _pipeline.RemoveStage(index);
                    EndStageBox();
                    return;
                }

                GUILayout.EndHorizontal();

                GUILayout.Label(
                    schema.TypeId + "  •  " + schema.Category,
                    styles.Small);

                DrawPortSummary(schema);

                foreach (TerraKitBackendParameter definition in schema.Parameters)
                {
                    TerraKitParameterData data =
                        node.parameters.FirstOrDefault(
                            parameter => parameter.key == definition.Id);

                    if (data != null)
                    {
                        DrawParameter(node, definition, data);
                    }
                }

                GUILayout.EndVertical();
            }

            GUILayout.Space(4f);

            GUILayout.Label(
                _pipeline.Status,
                _pipeline.Status != null &&
                _pipeline.Status.StartsWith(
                    "Pipeline invalid",
                    StringComparison.Ordinal)
                    ? styles.Error
                    : styles.Status);

            DrawConnections(styles);
        }

        public void DrawAvailableStages(DemoGuiStyles styles)
        {
            GUILayout.Label(
                "Available TerraKit stages",
                styles.Subheading);

            GUILayout.Label(
                "Discovered live from the native registry.",
                styles.Small);

            if (_pipeline.AvailableSchemas == null ||
                _pipeline.AvailableSchemas.Count == 0)
            {
                GUILayout.Label(
                    "No backend stages discovered.",
                    styles.Error);
                return;
            }

            _availableStageScroll = GUILayout.BeginScrollView(
                _availableStageScroll,
                GUILayout.Height(
                    Mathf.Min(
                        250f,
                        _pipeline.AvailableSchemas.Count * 78f)));

            string lastCategory = null;

            foreach (TerraKitBackendStageSchema schema
                     in _pipeline.AvailableSchemas)
            {
                if (!string.Equals(
                    lastCategory,
                    schema.Category,
                    StringComparison.Ordinal))
                {
                    if (lastCategory != null)
                    {
                        GUILayout.Space(4f);
                    }

                    GUILayout.Label(
                        string.IsNullOrEmpty(schema.Category)
                            ? "Uncategorised"
                            : schema.Category,
                        styles.Small);

                    lastCategory = schema.Category;
                }

                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.BeginVertical();

                GUILayout.Label(schema.DisplayName, styles.Subheading);
                GUILayout.Label(schema.TypeId, styles.Small);

                if (!string.IsNullOrEmpty(schema.Description))
                {
                    GUILayout.Label(schema.Description, styles.Small);
                }

                GUILayout.EndVertical();

                if (GUILayout.Button(
                    "+ Add",
                    GUILayout.Width(62f),
                    GUILayout.Height(36f)))
                {
                    _pipeline.AddStage(schema.TypeId);
                    GUILayout.EndHorizontal();
                    break;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        private void DrawConnections(DemoGuiStyles styles)
        {
            if (_pipeline.Graph.edges.Count == 0)
            {
                return;
            }

            GUILayout.Label("Automatic connections", styles.Small);

            foreach (TerraKitEdgeData edge in _pipeline.Graph.edges)
            {
                TerraKitNodeData source =
                    _pipeline.Graph.nodes.FirstOrDefault(
                        node => node.id == edge.outputNodeId);

                TerraKitNodeData target =
                    _pipeline.Graph.nodes.FirstOrDefault(
                        node => node.id == edge.inputNodeId);

                if (source != null && target != null)
                {
                    GUILayout.Label(
                        "• " + source.displayName + "." +
                        edge.outputPortId + " → " +
                        target.displayName + "." +
                        edge.inputPortId,
                        styles.Small);
                }
            }
        }

        private void DrawParameter(
            TerraKitNodeData node,
            TerraKitBackendParameter definition,
            TerraKitParameterData data)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                definition.DisplayName,
                GUILayout.Width(132f));

            string previous = data.value ?? string.Empty;
            string next = previous;

            switch (definition.Kind)
            {
                case TerraKitBackendParameterKind.Boolean:
                    bool booleanValue;
                    bool.TryParse(previous, out booleanValue);

                    next = GUILayout.Toggle(
                        booleanValue,
                        GUIContent.none)
                        ? "true"
                        : "false";
                    break;

                case TerraKitBackendParameterKind.Enum:
                    next = DrawEnum(definition, previous);
                    break;

                default:
                    next = GUILayout.TextField(previous);
                    break;
            }

            GUILayout.EndHorizontal();

            if (!string.Equals(
                previous,
                next,
                StringComparison.Ordinal))
            {
                _pipeline.SetParameter(
                    node,
                    definition.Id,
                    next,
                    definition.DisplayName + " changed");
            }
        }

        private static string DrawEnum(
            TerraKitBackendParameter definition,
            string previous)
        {
            int selected = 0;

            for (int i = 0;
                 i < definition.EnumOptions.Count;
                 i++)
            {
                if (definition.EnumOptions[i].Id == previous)
                {
                    selected = i;
                    break;
                }
            }

            string[] labels =
                definition.EnumOptions
                    .Select(option => option.DisplayName)
                    .ToArray();

            if (labels.Length == 0)
            {
                return previous;
            }

            int chosen = GUILayout.SelectionGrid(
                selected,
                labels,
                Mathf.Min(labels.Length, 2));

            return definition.EnumOptions[
                Mathf.Clamp(
                    chosen,
                    0,
                    definition.EnumOptions.Count - 1)].Id;
        }

        private static void DrawPortSummary(
            TerraKitBackendStageSchema schema)
        {
            string inputs = schema.Inputs.Count == 0
                ? "none"
                : string.Join(
                    ", ",
                    schema.Inputs.Select(
                        input => input.Id + ":" + input.ResourceKind));

            string outputs = schema.Outputs.Count == 0
                ? "none"
                : string.Join(
                    ", ",
                    schema.Outputs.Select(
                        output => output.Id + ":" + output.ResourceKind));

            GUILayout.Label("In: " + inputs, GUI.skin.label);
            GUILayout.Label("Out: " + outputs, GUI.skin.label);
        }

        private static void EndStageBox()
        {
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
