using UnityEngine;
using UnityEngine.Rendering;

namespace TerraKit.Samples
{
    internal sealed class DemoSceneResources
    {
        private readonly Transform _parent;
        private bool _ownsCamera;
        private bool _ownsSun;

        public Camera Camera { get; private set; }
        public Light Sun { get; private set; }
        public Material TerrainMaterial { get; private set; }

        public DemoSceneResources(Transform parent)
        {
            _parent = parent;
        }

        public void Initialise()
        {
            Camera = Camera.main;
            if (Camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(_parent, false);
                Camera = cameraObject.AddComponent<Camera>();
                _ownsCamera = true;
            }

            Camera.nearClipPlane = 0.3f;
            Camera.farClipPlane = 10000f;

            Sun = Object.FindFirstObjectByType<Light>();
            if (Sun == null)
            {
                var lightObject = new GameObject("Directional Light");
                lightObject.transform.SetParent(_parent, false);
                lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                Sun = lightObject.AddComponent<Light>();
                Sun.type = LightType.Directional;
                Sun.intensity = 1.25f;
                _ownsSun = true;
            }

            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultMaterial != null)
            {
                TerrainMaterial = new Material(pipeline.defaultMaterial);
            }
            else
            {
                Shader shader = Shader.Find("Standard") ??
                                Shader.Find("Universal Render Pipeline/Lit") ??
                                Shader.Find("HDRP/Lit");

                if (shader != null)
                {
                    TerrainMaterial = new Material(shader);
                }
            }

            if (TerrainMaterial != null)
            {
                TerrainMaterial.name = "TerraKit Runtime Demo Material";
                Color terrainColour = new Color(0.34f, 0.56f, 0.31f, 1f);

                if (TerrainMaterial.HasProperty("_BaseColor"))
                {
                    TerrainMaterial.SetColor("_BaseColor", terrainColour);
                }
                else if (TerrainMaterial.HasProperty("_Color"))
                {
                    TerrainMaterial.SetColor("_Color", terrainColour);
                }
            }
        }

        public void Dispose()
        {
            if (TerrainMaterial != null)
            {
                Object.Destroy(TerrainMaterial);
                TerrainMaterial = null;
            }

            if (_ownsCamera && Camera != null)
            {
                Object.Destroy(Camera.gameObject);
                Camera = null;
            }

            if (_ownsSun && Sun != null)
            {
                Object.Destroy(Sun.gameObject);
                Sun = null;
            }
        }
    }
}
