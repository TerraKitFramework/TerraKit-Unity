using System.Collections.Generic;
using UnityEngine;

namespace TerraKit
{
    // Stores the whole TerraKit graph as a saved asset.
    public sealed class TerraKitGraphAsset : ScriptableObject
    {
        // Stores all nodes in the graph.
        public List<TerraKitNodeData> nodes = new List<TerraKitNodeData>();
        // Stores all connections between the nodes.
        public List<TerraKitEdgeData> edges = new List<TerraKitEdgeData>();

        // Unity presentation settings, separate from native stage parameters.
        public List<TerraKitScatterPrefabMapping> scatterPrefabs =
            new List<TerraKitScatterPrefabMapping>();
    }

    [System.Serializable]
    public sealed class TerraKitScatterPrefabMapping
    {
        // Keep invalid input visible so the editor can explain the valid ID range.
        public long prototypeId;
        public GameObject prefab;
    }
}
