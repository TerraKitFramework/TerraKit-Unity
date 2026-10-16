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