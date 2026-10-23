using UnityEngine;

namespace TerraKit.Editor
{
    /// <summary>
    /// Stores generation settings separately from graph data and prefab mappings.
    /// </summary>
    [CreateAssetMenu(
        menuName = "TerraKit/Generation Settings",
        fileName = "TerraKitGenerationSettings")]
    public sealed class TerraKitGenerationSettings : ScriptableObject
    {
        public bool is3D;

        // Preserve the full Seed range and the original input text.
        public string seed = "12345";
        public string lod = "0";

        public string cellWidth = "16";
        public string cellHeight = "16";
        public string cellDepth = "16";

        public string spacingX = "1";
        public string spacingY = "1";
        public string spacingZ = "1";

        public string regionsX = "3";
        public string regionsY = "3";
        public string regionsZ = "3";

        internal string[] GetInputText()
        {
            return new[]
            {
                seed,
                lod,
                cellWidth,
                cellHeight,
                cellDepth,
                spacingX,
                spacingY,
                spacingZ
            };
        }

        internal void SetValues(
            bool volume,
            string[] inputs,
            string x,
            string y,
            string z)
        {
            is3D = volume;

            seed = inputs[0];
            lod = inputs[1];

            cellWidth = inputs[2];
            cellHeight = inputs[3];
            cellDepth = inputs[4];

            spacingX = inputs[5];
            spacingY = inputs[6];
            spacingZ = inputs[7];

            regionsX = x;
            regionsY = y;
            regionsZ = z;
        }
    }
}