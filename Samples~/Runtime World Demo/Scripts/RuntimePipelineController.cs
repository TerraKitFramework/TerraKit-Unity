using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TerraKit.Samples
{
    internal sealed class RuntimePipelineController
    {
        private const string FlatHeightType = "terrakit.height.flat";
        private const string NoiseHeightType = "terrakit.height.noise";
        private const string HeightMeshType = "terrakit.mesh.height_field";

        private readonly Dictionary<string, TerraKitBackendStageSchema> _schemas =
            new Dictionary<string, TerraKitBackendStageSchema>();

        private int _nextNodeOrdinal = 1;

        public TerraKitGraphAsset Graph { get; private set; }
        public IReadOnlyList<TerraKitBackendStageSchema> AvailableSchemas { get; private set; }
        public string Status { get; private set; }

        public event Action<string> Changed;

        public void Initialise()
        {
            string error;
            if (!TerraKitNodeRegistry.RefreshBackend(out error))
            {
                throw new InvalidOperationException(error);
            }

            AvailableSchemas = TerraKitNodeRegistry.BackendSchemas
                .OrderBy(schema => schema.Category)
                .ThenBy(schema => schema.DisplayName)
                .ToList()
                .AsReadOnly();

            _schemas.Clear();
            foreach (TerraKitBackendStageSchema schema in AvailableSchemas)
            {
                _schemas.Add(schema.TypeId, schema);
            }

            Graph = ScriptableObject.CreateInstance<TerraKitGraphAsset>();
            Graph.hideFlags = HideFlags.HideAndDontSave;

            ResetToDefault(false);
        }

        public void Dispose()
        {
            if (Graph != null)
            {
                UnityEngine.Object.Destroy(Graph);
                Graph = null;
            }
        }

        public bool TryGetSchema(string typeId, out TerraKitBackendStageSchema schema)
        {
            return _schemas.TryGetValue(typeId, out schema);
        }

        public void ResetToDefault()
        {
            ResetToDefault(true);
        }

        private void ResetToDefault(bool notify)
        {
            Graph.nodes.Clear();
            Graph.edges.Clear();
            _nextNodeOrdinal = 1;

            if (_schemas.ContainsKey(FlatHeightType))
            {
                Graph.nodes.Add(CreateNode(FlatHeightType));
            }

            if (_schemas.ContainsKey(NoiseHeightType))
            {
                TerraKitNodeData noise = CreateNode(NoiseHeightType);
                SetParameterDirect(noise, "algorithm", "perlin");
                SetParameterDirect(noise, "frequency", "0.012");
                SetParameterDirect(noise, "octaves", "5");
                SetParameterDirect(noise, "amplitude", "28");
                SetParameterDirect(noise, "persistence", "0.5");
                SetParameterDirect(noise, "lacunarity", "2");
                SetParameterDirect(noise, "mode", "add");
                Graph.nodes.Add(noise);
            }

            if (_schemas.ContainsKey(HeightMeshType))
            {
                Graph.nodes.Add(CreateNode(HeightMeshType));
            }

            RebuildAutoConnections();
            Validate();

            if (notify)
            {
                RaiseChanged("Default pipeline restored");
            }
        }

        public TerraKitNodeData CreateNode(string typeId)
        {
            TerraKitBackendStageSchema schema;
            if (!_schemas.TryGetValue(typeId, out schema))
            {
                throw new InvalidOperationException("Unknown TerraKit stage: " + typeId);
            }

            var node = new TerraKitNodeData
            {
                id = "runtime-stage-" + _nextNodeOrdinal++,
                typeId = typeId,
                schemaVersion = schema.SchemaVersion,
                displayName = schema.DisplayName
            };

            foreach (TerraKitBackendParameter parameter in schema.Parameters)
            {
                node.parameters.Add(new TerraKitParameterData
                {
                    key = parameter.Id,
                    displayName = parameter.DisplayName,
                    type = MapParameterType(parameter.Kind),
                    value = parameter.DefaultValue != null
                        ? parameter.DefaultValue.ToInvariantString()
                        : DefaultParameterText(parameter.Kind)
                });
            }

            return node;
        }

        public void AddStage(string typeId)
        {
            TerraKitNodeData node = CreateNode(typeId);

            int insertIndex = Graph.nodes.Count;
            for (int index = Graph.nodes.Count - 1; index >= 0; index--)
            {
                TerraKitBackendStageSchema schema;
                if (_schemas.TryGetValue(Graph.nodes[index].typeId, out schema) &&
                    schema.Outputs.Any(output => output.ResourceKind == TerraKitBackendResourceKind.Mesh))
                {
                    insertIndex = index;
                    break;
                }
            }

            Graph.nodes.Insert(insertIndex, node);
            OnStructureChanged("Added " + node.displayName);
        }

        public void RemoveStage(int index)
        {
            if (index < 0 || index >= Graph.nodes.Count)
            {
                return;
            }

            string name = Graph.nodes[index].displayName;
            Graph.nodes.RemoveAt(index);
            OnStructureChanged("Removed " + name);
        }

        public void MoveStage(int from, int to)
        {
            if (from < 0 || from >= Graph.nodes.Count ||
                to < 0 || to >= Graph.nodes.Count ||
                from == to)
            {
                return;
            }

            TerraKitNodeData node = Graph.nodes[from];
            Graph.nodes.RemoveAt(from);
            Graph.nodes.Insert(to, node);
            OnStructureChanged("Reordered pipeline");
        }

        public void SetParameter(
            TerraKitNodeData node,
            string key,
            string value,
            string reason)
        {
            TerraKitParameterData parameter =
                node.parameters.FirstOrDefault(candidate => candidate.key == key);

            if (parameter == null || string.Equals(parameter.value, value, StringComparison.Ordinal))
            {
                return;
            }

            parameter.value = value;
            Validate();
            RaiseChanged(reason);
        }

        public bool Validate()
        {
            if (Graph == null)
            {
                Status = "No runtime graph exists.";
                return false;
            }

            TerraKitGraphCompileResult result = new TerraKitGraphCompiler().Compile(Graph);
            if (!result.Success)
            {
                Status = "Pipeline invalid:\n• " + string.Join("\n• ", result.Errors);
                return false;
            }

            int meshOutputs = 0;
            foreach (TerraKitNodeData node in Graph.nodes)
            {
                TerraKitBackendStageSchema schema;
                if (_schemas.TryGetValue(node.typeId, out schema))
                {
                    meshOutputs += schema.Outputs.Count(
                        output => output.ResourceKind == TerraKitBackendResourceKind.Mesh);
                }
            }

            if (meshOutputs == 0)
            {
                Status = "Pipeline is valid, but it has no Mesh output to display in this sample.";
                return false;
            }

            Status = "Pipeline valid. " + Graph.nodes.Count + " stages, " +
                     Graph.edges.Count + " automatic connections, " +
                     meshOutputs + " Mesh output" +
                     (meshOutputs == 1 ? "." : "s. The final Mesh output is displayed.");

            return true;
        }

        public void RebuildAutoConnections()
        {
            Graph.edges.Clear();
            int edgeOrdinal = 1;

            for (int inputNodeIndex = 0; inputNodeIndex < Graph.nodes.Count; inputNodeIndex++)
            {
                TerraKitNodeData inputNode = Graph.nodes[inputNodeIndex];
                TerraKitBackendStageSchema inputSchema;
                if (!_schemas.TryGetValue(inputNode.typeId, out inputSchema))
                {
                    continue;
                }

                foreach (TerraKitBackendInputPort inputPort in inputSchema.Inputs)
                {
                    TerraKitNodeData producerNode = null;
                    TerraKitBackendOutputPort producerPort = null;

                    for (int producerIndex = inputNodeIndex - 1; producerIndex >= 0; producerIndex--)
                    {
                        TerraKitNodeData candidateNode = Graph.nodes[producerIndex];
                        TerraKitBackendStageSchema candidateSchema;
                        if (!_schemas.TryGetValue(candidateNode.typeId, out candidateSchema))
                        {
                            continue;
                        }

                        TerraKitBackendOutputPort candidatePort = candidateSchema.Outputs
                            .FirstOrDefault(output => output.ResourceKind == inputPort.ResourceKind);

                        if (candidatePort != null)
                        {
                            producerNode = candidateNode;
                            producerPort = candidatePort;
                            break;
                        }
                    }

                    if (producerNode != null && producerPort != null)
                    {
                        Graph.edges.Add(new TerraKitEdgeData
                        {
                            id = "runtime-edge-" + edgeOrdinal++,
                            outputNodeId = producerNode.id,
                            outputPortId = producerPort.Id,
                            inputNodeId = inputNode.id,
                            inputPortId = inputPort.Id
                        });
                    }
                }
            }
        }

        private void OnStructureChanged(string reason)
        {
            RebuildAutoConnections();
            Validate();
            RaiseChanged(reason);
        }

        private void RaiseChanged(string reason)
        {
            Action<string> handler = Changed;
            if (handler != null)
            {
                handler(reason);
            }
        }

        private static void SetParameterDirect(TerraKitNodeData node, string key, string value)
        {
            TerraKitParameterData parameter =
                node.parameters.FirstOrDefault(candidate => candidate.key == key);

            if (parameter != null)
            {
                parameter.value = value;
            }
        }

        private static TerraKitParameterType MapParameterType(TerraKitBackendParameterKind kind)
        {
            switch (kind)
            {
                case TerraKitBackendParameterKind.Boolean:
                    return TerraKitParameterType.Boolean;
                case TerraKitBackendParameterKind.SignedInteger64:
                case TerraKitBackendParameterKind.UnsignedInteger64:
                    return TerraKitParameterType.Integer;
                case TerraKitBackendParameterKind.Float32:
                case TerraKitBackendParameterKind.Float64:
                    return TerraKitParameterType.Float;
                default:
                    return TerraKitParameterType.String;
            }
        }

        private static string DefaultParameterText(TerraKitBackendParameterKind kind)
        {
            switch (kind)
            {
                case TerraKitBackendParameterKind.Boolean:
                    return "false";
                case TerraKitBackendParameterKind.SignedInteger64:
                case TerraKitBackendParameterKind.UnsignedInteger64:
                case TerraKitBackendParameterKind.Float32:
                case TerraKitBackendParameterKind.Float64:
                    return "0";
                case TerraKitBackendParameterKind.Vector2Float64:
                    return "(0, 0)";
                case TerraKitBackendParameterKind.Vector3Float64:
                    return "(0, 1, 0)";
                default:
                    return string.Empty;
            }
        }
    }
}
