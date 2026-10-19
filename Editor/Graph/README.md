# TerraKit Unity Frontend

Build terrain graphs, preview finite maps, and save them as Unity assets.

## Requirements

Unity 6000.5.6f1 and matching TerraKit native libraries.

## Quick start

1. Run **Tools > TerraKit > Check Backend Connection**.
2. Open **Tools > TerraKit > Graph Editor**. Create or select a Graph.
3. Connect nodes, edit parameters, and click **Validate**.
4. Open **Generator**, set Seed and region settings, then click **Generate Map**.
5. Select a **Region** and **Output** to inspect, save or export.

Example: `Flat Height -> Noise Height -> Heightfield Mesh`.
2D maps are arranged around the Unity origin on the X/Z plane.

## Auto Preview

- Enable **Auto Preview** to regenerate about 0.6 seconds after edits stop.
- Moving nodes does not regenerate. Automatic updates keep your Scene view unchanged.
- Select the Preview root and press **F** over the Scene view to see the whole map.
- Auto Preview defaults to off and resets on window close, script reload or Play mode.
- Invalid inputs clear temporary results. Correct them to resume automatic updates.

## Settings and scatter

Generation settings can be saved as assets or JSON. Nodes and links stay in the Graph.
For scatter, map **Prototype IDs** to Prefabs under **Scatter prefab mappings**,
then click **Generate Map** after changing mappings.

## Save and export

| Action | Contents |
| --- | --- |
| Save Mesh | Selected Region's Meshes and materials; no scatter |
| Save Prefab | Whole map with Meshes, materials and mapped scatter |
| Export JSON | Selected Region and Output data |
| Export PNG | Whole map's Meshes; no scatter |
| Export Heightmap / Slice Image | Selected grid output or Z slice |

Saved maps are independent snapshots. Closing the Generator clears temporary results
and previews without changing saved maps. Scatter previews allow up to 10,000 instances;
truncated maps cannot be saved as a complete Prefab.