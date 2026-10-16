# TerraKit Runtime World Demo

This sample demonstrates TerraKit as a live runtime world-generation tool.

## What it demonstrates

- The Unity integration discovers the installed built-in stage schemas from the TerraKit native registry.
- The **Available TerraKit stages** panel is populated from that discovery result; the sample does not contain a hard-coded stage catalogue.
- A pipeline can be created and edited entirely from C# at runtime.
- Stages can be added, removed and reordered while Play Mode is running.
- The sample automatically connects each input to the nearest earlier compatible output resource. Invalid pipelines are reported using the normal TerraKit graph compiler.
- Stage parameter controls are created from the discovered schemas.
- The world is streamed as TerraKit regions around the camera instead of being generated as one fixed map.
- Crossing a region boundary requests new regions and unloads distant regions while retaining already-loaded neighbours.
- Generated TerraKit mesh resources are converted into ordinary Unity `Mesh` objects.

## Camera modes

### Stationary

The camera remains at its current transform. TerraKit keeps the configured region radius loaded around it.

### Orbiting

The camera orbits the region that was beneath it when Orbiting was selected. This is useful for inspecting one area while editing the pipeline.

### Explore

The camera automatically travels across the world on a slowly changing heading. As it crosses TerraKit region boundaries the sample generates terrain ahead and unloads regions left behind. This is the best unattended demonstration mode.

### Free Move

Manual fly camera:

- hold right mouse button to enable free-flight controls
- `WASD`: horizontal movement
- `Q` / `E`: down / up
- `Left Shift`: speed boost
- mouse: look while right mouse is held

Free Move currently uses Unity's legacy `Input` API. If a project is configured for the new Input System only, use Stationary, Orbiting or Explore, or adapt the small input section to the project's input actions.
## Object scattering (Poisson Scatter)

1. Import this sample through Unity Package Manager and open `RuntimeWorldDemo.unity`.
2. Select **TerraKit Runtime World Demo** in the Hierarchy. In the **Object Scattering** section of the Inspector, expand **Scatter Prototypes** and add entries.
3. For each entry, assign a prefab and a unique non-negative **Prototype Id** (for example `0` for trees and `1` for rocks). Adjust **Radius**, **Attempts**, **Min Scale**, **Max Scale**, and **Random Rotation**.
4. Set **Max Scatter Instances Per Region** to cap sample GameObject overhead (default: 200). Enter Play Mode.
5. The default runtime graph automatically adds a `terrakit.scatter.poisson` stage per configured prototype, connects each to the height field, and shows it in the pipeline panel. Use the panel to adjust parameters while playing.

The generated Rust scatter points contain positions, orientations, scales and prototype IDs; Unity maps each ID to the assigned prefab and instantiates it as a child of the corresponding terrain region. When a region unloads, Unity destroys its prefab instances with the region. No prefab assets are included; use your own project prefabs.

Notes: Entries without an assigned prefab are ignored. Prototype IDs must be unique. If you manually add Poisson Scatter stages from the runtime panel, set the stage's `prototype_id` to one of the configured prefab IDs, otherwise its points are ignored. `Reset to Default` restores the Inspector-driven stages. The Poisson stage uses one prototype ID per stage; **this is not weighted multi-prototype scattering**. Individual GameObjects are intended for the demo, not production-scale vegetation rendering. Scatter settings are read when the scene starts; stop and restart Play Mode after editing the Inspector list.
