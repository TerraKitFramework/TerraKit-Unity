# TerraKit Unity Frontend

TerraKit provides a Unity Graph Editor and a Generator backed by the native TerraKit C API. Configure a graph, generate a finite map, inspect its results, and save the map as a Prefab with Mesh, material and mapped scatter objects.

## Requirements

- Unity 6000.5.6f1.
- TerraKit Unity package 0.1.6 with its bundled native libraries (ABI 0.2.x, native library 0.0.3). Official release tarballs include platform libraries under `Runtime/Plugins/`. Keep the C# bindings and native libraries from the same compatible release.
- A render pipeline compatible with the materials used for the map.

Run **Tools > TerraKit > Check Backend Connection** to inspect the loaded library, ABI, and available stages. Node availability comes from the backend registry. Supporting a resource type in the frontend does not create a backend stage that generates it.

## Create a graph

Open **Tools > TerraKit > Graph Editor**, then use **New Graph**, select an existing graph, or use **Demo Graph**.

A basic height pipeline is:

```text
Flat Height -> Noise Height -> Heightfield Mesh
```

Right-click the canvas to create nodes. Select a node to edit its parameters in the Inspector. The **Controls** button on the canvas expands or collapses the shortcut reference. Use **Frame All**, **Focus**, or **100%** to adjust the view.

**Validate** checks graph structure, parameter values, and execution order. Errors can identify the affected nodes. Graph edits clear the previous validation report; run Validate again when needed. Generation also validates the graph before native execution.

A graph can return intermediate HeightField outputs and multiple Mesh outputs. There is no requirement to have exactly one Mesh output. DensityField and VoxelVolume generation requires compatible stages in the loaded backend.

## Generate a map

Click **Generator** in the Graph Editor toolbar. Start with:

| Setting | Value |
| --- | --- |
| Region mode | 2D |
| Seed | 12345 |
| LOD | 0 |
| Cell Width / Height | 16 / 16 |
| Base Spacing X / Y | 1 / 1 |
| Regions X / Y | 3 / 3 |

Click **Generate Map**. Region counts determine how many neighboring regions are requested through the same pipeline. Coordinates are assigned automatically around zero; users do not enter a world location. Even counts have one more region on the negative side. In 2D mode, the second region axis corresponds to Unity world Z.

3D mode adds depth and a third region axis. The graph must contain stages compatible with 3D generation.

Changing the graph's generation content or request settings clears Results and the temporary Scene preview. Moving a graph node does not clear them. A new generation attempt also clears previous results; cancellation or failure leaves no generated results. Saved assets and their Scene instances remain intact.

## Save and restore generation settings

The Generator's **Saved generation settings** section manages request parameters separately from the Graph asset.

- **Save As...** creates a new generation settings asset under the Unity project's Assets folder.
- **Update Saved** updates the selected settings asset from the current valid inputs.
- **Load Settings** validates and applies the selected settings asset.
- **Export Settings JSON** exports the current request parameters.
- **Import Settings JSON** validates and applies a settings file. The selected settings asset becomes None; existing assets are not overwritten.
- **Reset Request Settings** restores the default request parameters.

Settings include 2D/3D mode, Seed, LOD, cell dimensions, spacing and region counts. Numeric inputs are stored as strings to preserve the full unsigned 64-bit Seed range. The format is `TerraKit.GenerationSettings`, version 1.

Nodes, links and node parameters remain in the Graph asset. Generation settings JSON and generated resource JSON are different exports. Changed request parameters clear current Results and the temporary preview.

## Amplify Height and Temperature Map

Amplify Height processes a connected height field. Its threshold range selects the input heights to affect. Choose Additive or Multiplicative mode, set the amplification value, and use the amplification limit to bound the affected result. Inspect the original and processed HeightField outputs to compare exact sample values.

For example, a Flat Height of 2 produces 7 with Additive value 5, or 6 with Multiplicative value 3, when the input falls inside the thresholds and the limit does not clamp the result.

Temperature Map creates an independent scalar field. Scale controls the spatial sampling scale; Seed Domain separates its deterministic noise domain from other stages. The Generator Seed is part of the generation request. A Temperature Map does not by itself assign biomes or materials.

Grayscale previews are normalized to each selected output's own range. Multiplying a height field can leave its grayscale pattern unchanged while changing its numeric values and Mesh heights. Compare **Range** and **Raw sample value** to check the actual effect.

## Scatter data and prefab mappings

Connect a height output to **Poisson Scatter**. For a map with both terrain and scatter objects, branch the same processed height output into Heightfield Mesh and Poisson Scatter:

```text
Flat Height -> Noise Height -> Amplify Height -> Heightfield Mesh
                                            -> Poisson Scatter
```

In **Scatter prefab mappings**, assign one Project prefab asset to each Prototype ID produced by the graph. Scene objects are not accepted. Empty, invalid or duplicate mappings show diagnostics; duplicate IDs are excluded from the scene preview.

Mappings are saved with the Graph asset. They are not part of generation settings assets or generation settings JSON. Mapping edits keep the generated data but clear the temporary Scene preview; click **Generate Map** again to rebuild it with the new mappings. Missing mappings do not prevent scatter data generation or JSON export, but every generated Prototype ID needs one valid mapping before saving a complete map Prefab.

Select a ScatterPoints output to inspect **Placements**, **World Origin**, and **Scatter Data Preview**. Use **Point index** to inspect the Prototype ID, local position, rotation, scale and world position. World position includes the precise resource origin.

Scatter preview instances use each point's position and rotation. Point rotation is composed with the prefab root's rotation, and point scale multiplies the prefab root's scale. A prefab whose pivot is at its center can appear partly inside the terrain; place its visual geometry above the root pivot if its base should sit on the sampled surface.

Scene previews are limited to 10,000 scatter instances. The generated data and JSON export still contain all points. Saving a complete map is blocked while its scatter preview is truncated.

## Inspect, preview, and save

The **Region** and **Output** selectors choose the result shown in the data panel. They also select the result exported as JSON or a data image. They do not hide other regions in the Scene preview.

- **Generate Map** displays all generated Mesh outputs and all scatter objects with valid prefab mappings, across all requested regions.
- **Save Mesh** saves every Mesh output in the selected Region, including its materials, as a Region Prefab and associated resource assets. The Output selector does not limit this save. Scatter objects are excluded, and this action does not place an instance in the Scene.
- **Save Prefab** saves all generated regions, Mesh outputs, materials and mapped scatter objects as one map Prefab, then places an instance at the Unity origin. Region and Output selection do not limit this save. Scatter-only graphs can also be saved when their generated points have valid mappings.

The Save section is available when generated results contain Meshes or nonempty scatter output. Save Mesh still requires Mesh output in the selected Region. Height-only, density-only and voxel-only graphs use the data export controls instead.

Saved terrain Meshes preserve their material assignments. Temporary textures used by those materials are saved as assets; existing texture assets remain referenced. Materials using a RenderTexture must be changed to use a persistent texture before saving. Persistent Mesh and material assets used by scatter prefabs remain shared. Scatter objects are copied into the saved map as a generated snapshot; later changes to the source Graph do not regenerate that snapshot.

The generated map layout is shared by Preview and the map Prefab. The Preview root and the automatically placed map Prefab instance start at Unity position `(0, 0, 0)`. Child objects retain their relative region positions. Manually dragging a saved Prefab into a Scene can place that new instance at the drop location.

Closing the Generator clears its temporary preview and generated Results. Script compilation and assembly reload also clear temporary results and release selected preview objects before cleanup. Open the Generator and click **Generate Map** again after a reload. Saved map assets and their Scene instances remain independent of the temporary preview.

## Export

| Selected output | JSON | Image |
| --- | --- | --- |
| HeightField | Original height samples and metadata | **Export Heightmap**: grayscale heightmap PNG |
| DensityField | Complete 3D density samples and metadata | **Export Slice Image**: current Z slice PNG |
| VoxelVolume | Complete voxel data and metadata | **Export Slice Image**: current Z slice PNG |
| Mesh | Selected Mesh result and metadata | **Export PNG**: rendered image of all map Mesh outputs |
| ScatterPoints | Placements, origin, rotation, scale and prototype IDs | Inspect points in Scatter Data Preview and mapped objects in the Scene; no scatter image export |

Images are visualizations. Use JSON when exact original values or complete volume data are needed. Mesh PNG export includes all map Mesh outputs and excludes scatter objects. It does not require an existing scene preview.

## Troubleshooting

### Backend connection fails

Check the bundled native library, platform import settings, architecture, and ABI compatibility. Loading ABI 0.2.x requires the matching updated bindings. Run **Check Backend Connection** again after fixing the library.

### Generation settings are invalid

Read the Generator's field messages. Seed must fit an unsigned 64-bit integer, LOD must be between 0 and 30, cell dimensions must be between 1 and 4096, and spacing must be finite and greater than zero. Cell dimensions must be divisible by `2^LOD`. Region counts must be positive integers. Large valid requests can still exceed available resources.

### Validation passes but generation fails

Native stages may impose additional constraints. Read the backend diagnostic, particularly when using 2D stages with a 3D request.

### A node type is missing

The Create Node menu lists stages discovered from the loaded backend. A DensityField or VoxelVolume reader in the frontend does not imply that the backend contains a corresponding producer or meshing stage.

## Editing and runtime API

Copying multiple nodes includes connections whose two endpoints are in the selection. Pasted nodes and edges receive new IDs. Use Unity Undo and Redo to restore graph edits.

Runtime callers use `TerraKitBackendGraphRunner.GenerateResources(...)` with one request or a list of compatible region requests, then inspect the returned resources. The legacy `Generate(...)` shortcut has been removed.

Compilation issues are stored in `TerraKitGraphCompileResult.ErrorIssues`. Each issue contains a message and an optional Node ID. `Success` is derived from that list; separate `Errors` and `Warnings` lists are no longer exposed.