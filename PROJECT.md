# FuseEngine & Blowtorch: Project Documentation

This document provides a comprehensive technical overview of the **FuseEngine** codebase and its companion level editor, **Blowtorch**. It describes the overall architecture, individual subsystem components, code structures, data flows, and provides detailed guides on how to modify, extend, and debug any part of the system.

---

## 1. Project Overview

The project is structured as a single C# solution (`FuseEngine.slnx`) targeting **.NET 10.0**. It consists of two primary executable assemblies that share libraries:

1. **[Fuse](file:///e:/DEV/Csharp/FuseEngine/Fuse)**: A lightweight 3D game engine library and playable client. It handles 3D rendering (OpenGL), physics simulation (Jolt Physics), virtual character control, player interaction, HUD display, and game map serialization/deserialization.
2. **[Blowtorch](file:///e:/DEV/Csharp/FuseEngine/Blowtorch)**: A 3D/2D desktop level editor built on top of the Fuse engine codebase. It features a quad-viewport window layout (3D Perspective, Top, Front, Side), translation/rotation/scaling gizmos, level hierarchy inspection, raw JSON document editing, and a snapshot-based undo/redo system.

### Key Technologies & Libraries Used
* **Silk.NET**: Provides high-performance C# bindings for:
  * **GLFW** (`Silk.NET.GLFW`) - Window creation, input polling, and event routing.
  * **OpenGL** (`Silk.NET.OpenGL`) - Modern graphics rendering pipeline (version 3.3 Core Profile).
  * **Assimp** (`Silk.NET.Assimp`) - 3D asset/model import utility.
* **Jolt Physics** (`JoltPhysicsSharp`): High-performance C++ physics engine wrapped for C#. Used for rigid body simulation, collision detection, raycasting, and virtual character controllers.
* **ImGui.NET**: C# wrapper for Dear ImGui. Used for the developer console in the game, and the entire user interface of the editor.
* **StbImageSharp**: Image loading library for reading texture files (BMP, PNG, JPEG, etc.).

---

## 2. Directory Structure

```text
FuseEngine/
├── FuseEngine.slnx               # C# Solution Configuration
├── Fuse/                         # Game Engine Core
│   ├── Fuse.csproj               # Project Configuration (.NET 10.0, Packages)
│   ├── Program.cs                # Client Entry Point
│   ├── res/                      # Assets Directory (Shaders, Textures, Maps, Models)
│   └── src/                      # Source Code
│       ├── Core/                 # Engine loop, window context, logger, ResPath
│       ├── AssetManagement/      # Resource caching & loaders
│       ├── Renderer/             # OpenGL wrappers, camera, scene graph, HUD, debug drawer
│       ├── Input/                # GLFW key/mouse polling and status utilities
│       ├── Physics/              # Jolt Physics wrappers, filters, and rigid body configurations
│       ├── Player/               # CharacterVirtual player controller and pickup mechanics
│       ├── Interaction/          # Entity raycast triggers and IInteractable implementations
│       ├── Scene/                # Map data serialization/deserialization
│       └── UI/                   # HUD components (fonts, text, textures)
└── Blowtorch/                    # Map Editor Application
    ├── Blowtorch.csproj          # Project Configuration (Depends on Fuse)
    ├── Program.cs                # Editor Entry Point
    ├── CommandHistory.cs         # Undo/Redo commands history
    ├── EditorApplication.cs      # Main editor setup, loop, and viewport updates
    ├── EditorAssetService.cs     # Mesh/Texture caches dedicated to the editor view
    ├── EditorGizmo.cs            # Custom translation, rotation, and scaling gizmos
    ├── EditorInputService.cs     # Window inputs routing for ImGui and Viewports
    ├── EditorSceneService.cs     # Document-Scene bridging
    ├── EditorUI.cs               # ImGui windows, toolbar, hierarchy inspector, json editor
    ├── EditorViewport.cs         # FBO viewports drawing, ortho lines, and camera logic
    └── ViewportCamera.cs         # Editor camera controller (orbit, fly, zoom, pan)
```

---

## 3. Engine Architecture (Fuse)

### 3.1. Core Engine Lifecycle (`Fuse.Core`)

The game client lifecycle is managed by [Application.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Core/Application.cs).

* **Initialization (`Init()`)**:
  1. Instantiates a [Window](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Core/Window.cs), initializing GLFW and modern OpenGL context.
  2. Sets up the [AssetManager](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/AssetManagement/AssetManager.cs), [DebugDrawer](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Debug/DebugDrawer.cs), and [UIRenderer](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/UIRenderer.cs).
  3. Initializes [PhysicsWorld](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Physics/PhysicsWorld.cs) (establishing Jolt system parameters and filters).
  4. Spawns the [Player](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Player/Player.cs) character and registers the [PickupController](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Player/PickupController.cs) for object lifting.
  5. Pre-loads shaders, textures, and default primitives (cube, ground).
  6. Loads the active map from file via [MapSerializer](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/MapSerializer.cs).
  7. Registers window resizing, scrolling, and mouse movements event listeners.
* **Game Loop (`Run()`)**:
  * Runs a continuous `while (!_window.ShouldClose)` loop.
  * Calculates delta-time (`dt`) via [Engine.Tick](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Core/Engine.cs).
  * Updates input polling arrays.
  * If the game is not paused:
    * Updates physics-driven object carrying (`_pickup.PhysicsUpdate(dt)`).
    * Steps the physics simulation (`_physics.Step(dt)`).
    * Updates player movements, crouching constraints, and camera matrix updates (`_player.Update(dt)`).
    * Resolves general input commands (e.g. toggles pause, console, debug wires, saves map).
  * Performs the rendering pipeline:
    1. Clears buffers.
    2. Renders skybox cube with sub-grid depth culling configurations.
    3. Renders visible scene geometries using the primary lighting shaders.
    4. Renders physics collider skeletons if the debug drawer (`F9`) is active.
    5. Renders 2D HUD (crosshairs, active frame-rate, paused overlay) with blend configurations.
    6. Renders Dear ImGui elements (developer console, player inspector).
    7. Swaps display buffers and queries OS event updates.

### 3.2. Rendering System (`Fuse.Renderer`)

* **[Mesh.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Mesh.cs)**: Wraps an OpenGL Vertex Array Object (VAO), Vertex Buffer Object (VBO), and Element Buffer Object (EBO). Vertices are mapped using the `Vertex` struct (Position, TexCoord, Normal). It provides utilities to generate basic primitive visual shapes (cubes, tiled ground panels).
* **[Shader.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Shader.cs)**: Compiles GLSL vertex and fragment source files. Includes methods to locate and set shader parameters (e.g. `SetMat4`, `SetVec3`, `SetFloat`).
* **[Texture.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Texture.cs)**: Loads local files into memory using `StbImageSharp`, configures filtering parameters (Linear interpolation, Mipmapping), and uploads pixels to modern GPU textures.
* **[Camera.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Camera.cs)**: Manages camera matrices. Generates perspective projection matrices based on Field of View (FOV) and aspect ratios, and constructs Look-At View matrices based on yaw/pitch rotations.
* **[Scene.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Scene.cs)** & **[Entity.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Renderer/Scene.cs#L18)**: Establishes a scene tree containing active rendering nodes. Entities map visual models (`Mesh`), textures, positions/scales (`Transform`), and physical wrappers (`RigidBody`). During scene rendering, Entity transformations are matched to their corresponding physics body positions.

### 3.3. Input Handling (`Fuse.Input`)

Managed by [Input.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Input/Input.cs). It uses array copying (`s_keysDown` to `s_keysPrev`) at the start of each frame.
* **`KeyPressed(int key)`**: Returns `true` only on the frame the button was pressed.
* **`KeyDown(int key)`**: Returns `true` continuously while the button is held down.
* **Mouse offsets**: Computes frame-to-frame delta movement values (`MouseOffsetX`, `MouseOffsetY`), which are essential for smooth camera mouse look sensitivity.
* Cursor capture states can be toggled using `DisableCursor()` (mouse hidden and centered) or `ShowCursor()` (mouse visible for menu interaction).

### 3.4. Physics Simulation (`Fuse.Physics`)

* **[PhysicsWorld.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Physics/PhysicsWorld.cs)**: Initializes Jolt Foundation settings and thread pools. It sets up collision filters (`BroadPhaseLayerInterfaceTable`, `ObjectLayerPairFilterTable`, `ObjectVsBroadPhaseLayerFilterTable`) using a simplified table setup. It provides entry points to instantiate, step, query, and destroy physics bodies.
* **[RigidBody.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Physics/RigidBody.cs)**: Implements a builder pattern to configure physics bodies before linking them to Jolt. Supports several collider shapes:
  * **Box**: Requires Vector3 half-extents.
  * **Sphere**: Requires radius.
  * **Capsule**: Requires height and radius.
  * **Plane**: Infinite surface configured via normal and offset distance.
  * **Trimesh**: Arbitrary static mesh topology built from raw vertex/index buffers.
  * Body configurations default to `MotionType.Static` if mass is set to `0`, and switch to `MotionType.Dynamic` (with computed inertia tensors) when mass is positive.

### 3.5. Player Controller & Mechanics (`Fuse.Player`)

* **[Player.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Player/Player.cs)**: Implements player mechanics using Jolt's virtual character controller (`CharacterVirtual`).
  * **Movement Physics**: Features a Quake-style movement physics model. Ground movements incorporate friction calculations and linear accelerations. Air movement uses limited acceleration rates to preserve jump momentum.
  * **Jump / Crouch**: Pressing `Space` applies an upward velocity impulse. Crouching (`Left Control`) scales down the controller capsule height using `SetShape()`. If the user releases the crouch key under low ceilings, the controller casts a ray upwards via `NarrowPhaseQuery.CastRay()`; if blocked, the crouch state is maintained to prevent collision clipping.
  * **Noclip (`F1`)**: Toggles a debug flight mode. Bypasses the virtual character collisions, translating the player camera directly through solid objects using keyboard navigation.
  * **Dynamic Interaction**: Calls `PushDynamicBodies()` on collision. Queries virtual contacts and applies proportional impulses to static or dynamic physics bodies based on mass ratio calculations.
* **[PickupController.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Player/PickupController.cs)**: Allows physics-driven object carrying.
  * When pressing `E`, a raycast is emitted from the camera. If it intersects a dynamic rigid body, the object is picked up.
  * During carry, gravity for the object is disabled (`SetGravityFactor(0.0f)`).
  * Every physics update frame, it computes the distance vector between the target position in front of the camera and the object's center of mass. It applies a proportional correction force (`AddForce()`) to move the object towards the target position.
  * When releasing (`E`) or throwing (`Left Click`), the original gravity factor is restored. Throwing applies a forward velocity impulse along the camera's view vector, combined with throw momentum derived from mouse velocity.

### 3.6. Interaction System (`Fuse.Interaction`)

* **[InteractionSystem.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Interaction/InteractionSystem.cs)**: Bridges Jolt Physics bodies and C# class instances. Jolt bodies contain a 64-bit `UserData` pointer. When a rigid body is registered as interactable, the system allocates a C# Garbage Collector handle (`GCHandle.Alloc`) pointing to the [IInteractable](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Interaction/Interactable.cs) interface instance, storing its raw address within the Jolt body.
* **Look-at Raycast**: Every frame, the client casts a short ray (5.0 units) along the camera's center axis. If it hits an interactable body, the crosshair texture changes. Pressing `E` dereferences the GC pointer and calls `OnInteract()` on the target instance (e.g., [ButtonInteract.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Interaction/ButtonInteract.cs), [CubeInteract.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Interaction/CubeInteract.cs)).
* **Memory Management**: When destroying scenes, you must call `Free()` on the allocated `GCHandle` pointers to prevent memory leaks and dangling pointer crashes.

### 3.7. Map Serialization & Brushes (`Fuse.Scene` & `Model`)

The file format for game levels is JSON, processed by [MapSerializer.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/MapSerializer.cs) and parsed into [MapDocument.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/Model/MapDocument.cs).

* **Primitives vs. Brushes**:
  * **Primitives**: Predefined meshes (e.g., `cube`, `ground`) imported from models or procedural assets.
  * **Brushes**: Convex shapes defined using planes (Constructive Solid Geometry/CSG), similar to level formats used in classic games (like Quake).
* **[Brush.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/Model/Brush.cs) & [Face.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/Model/Face.cs)**: A brush consists of a list of `Face` definitions. Each face defines a math plane equation:
  $$\text{Normal} \cdot \vec{P} + D = 0$$
  It also contains UV texture scaling axes (`UAxis`, `VAxis`), scales, offsets, texture paths, and rotation values.
* **[MeshGenerator.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Scene/Model/MeshGenerator.cs)**: Converts a set of planes into a renderable and collidable triangle mesh.
  1. **Plane Intersection**: Intersects all combinations of three planes. For each intersection point, it checks whether it lies behind or on all other planes of the brush. Valid points become the vertices of the brush.
  2. **Polygon Sorting**: For each face, it gathers the valid intersection points lying on its plane, computes their center of mass, and sorts them counter-clockwise using a local coordinate system based on the face normal.
  3. **UV Mapping**: Projects the sorted 3D points onto the face's UV axes (`UAxis`, `VAxis`), applying scaling, offsets, and rotation to generate texture coordinates.
  4. **Triangulation**: Generates indices for rendering using a triangle fan layout originating from the first sorted vertex.

---

## 4. Editor Architecture (Blowtorch)

### 4.1. Application Loop (`EditorApplication.cs`)

The map editor lifecycle is controlled by [EditorApplication.cs](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorApplication.cs).

* **Initialization**:
  1. Instantiates the [EditorWindow](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorWindow.cs).
  2. Starts [EditorInputService](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorInputService.cs), capturing and directing key/mouse inputs to ImGui.
  3. Initializes [ImGuiBackEnd](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Imgui/ImGuiBackEnd.cs) to draw overlays.
  4. Launches [EditorAssetService](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorAssetService.cs) to cache textures and meshes, and [EditorSceneService](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorSceneService.cs) to load the active level document.
  5. Instantiates four separate [EditorViewport](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorViewport.cs) contexts (Perspective 3D, Top Ortho, Front Ortho, Side Ortho) and a [CommandHistory](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/CommandHistory.cs) manager.
* **Loop**:
  * Calculates frame delta time.
  * Captures input states.
  * Directs OpenGL to render into the Framebuffer Objects (FBO) of each of the four viewports.
  * Renders grid lines, wireframe outlines, and solid shapes for each viewport based on its projection mode. Renders debug graphics (e.g. bounding boxes, camera lines). Renders editor gizmos on top.
  * Calls [EditorUI.Draw](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorUI.cs#L89) to build the ImGui editor workspace, outputting the rendering textures of the FBO viewports to window panes.

### 4.2. Editor Viewports (`EditorViewport.cs`)

Viewports handle independent viewing angles using [ViewportCamera.cs](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/ViewportCamera.cs).
* **FBO Setup**: Contains a Framebuffer Object (FBO) linked to a Color texture attachment and a Depth renderbuffer attachment. When rendering a viewport, the editor binds the FBO, renders the scene, and binds the color texture to an ImGui window image widget.
* **Camera Modes**:
  * **Perspective 3D**: Standard camera view. Right-click drag orbits the camera around a pivot point; middle-click drag pans the camera; scroll wheel zooms. Holding right-click enables flying movement controls using `W`/`A`/`S`/`D`/`Q`/`E`.
  * **Orthographic (Top, Front, Side)**: Flat projection views. The camera points down the principal axes (Y, Z, X). Orbiting is disabled. Rendering uses wireframe mode (`GLEnum.Line`) to align with retro level editor styles.

### 4.3. Transform Gizmos (`EditorGizmo.cs`)

The [EditorGizmo.cs](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorGizmo.cs) file implements translation, rotation, and scaling gizmos.

* **World-to-Screen Projection**: Converts 3D coordinates into 2D coordinates on the screen based on view-projection matrices:
  $$P_{clip} = M_{proj} \cdot M_{view} \cdot P_{world}$$
  $$P_{screen} = \left(\frac{P_{clip}.x}{P_{clip}.w}, \frac{P_{clip}.y}{P_{clip}.w}\right)$$
* **Axis Selection**: Draws lines along the local or global X (Red), Y (Green), and Z (Blue) axes. If the cursor is close to an axis line (calculated using the distance from a point to a line segment in screen space), that axis is highlighted.
* **Interaction**: Clicking and dragging projects a ray from the mouse cursor into 3D space (`ScreenToWorldRay`). The intersection of this ray with the active axis plane calculates the movement delta. It supports snapping to a grid offset (`snapAmount`).
* **Scale / Rotate**: The scale gizmo scales the dimensions of the selected object along the active axis. The rotation gizmo projects the mouse ray onto a flat circle plane, calculating the rotation angle using:
  $$\text{Angle} = \text{atan2}(Y, X)$$

### 4.4. Undo/Redo System (`CommandHistory.cs`)

Undo and redo functionality is implemented using the Command pattern in [CommandHistory.cs](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/CommandHistory.cs).
* **`ICommand`**: Interface declaring `Execute()` and `Undo()` methods.
* **`SnapshotCommand`**: When modifying objects, the editor serializes the current state of the map into a JSON string (`stateBefore`). Once the modification completes, it serializes the new state (`stateAfter`). Both states are stored in a `SnapshotCommand` pushed onto the undo stack.
* **Undo / Redo execution**: Undoing pops a command from the undo stack, deserializes `stateBefore`, clears mesh caches, and updates the scene. Redoing applies `stateAfter` and pushes the command back onto the undo stack.

### 4.5. Editor UI Panels (`EditorUI.cs`)

The editor layout is built using ImGui:
* **Menu Bar**: File management (New, Load, Save, Exit), Undo/Redo triggers, and viewport display options.
* **Scene Hierarchy**: Lists all entities in the `MapDocument`. Left-clicking selects an object; right-clicking opens a context menu to add, rename, duplicate, or delete entities.
* **Properties Inspector**: Edit properties of the selected object: name, position, rotation, scale, visibility, mesh keys, texture paths, interactable scripts, and physical parameters (mass, friction, restitution).
* **Brush Builder**: Edit plane offsets for convex brushes. Provides tools to resize brush half-extents and compile brushes into visual/collision meshes.
* **Raw JSON Viewer**: Shows the current JSON representation of the level document in real-time. Helpful for checking structural syntax.

---

## 5. Development & Extension Guides

### 5.1. Adding a New Interactable Object

Follow these steps to create a new interactive object (e.g. a door or a treasure chest):

1. **Create the Script**: Create a C# file in [Fuse/src/Interaction/](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Interaction) (e.g., `DoorInteract.cs`).
2. **Implement `IInteractable`**: Implement the interface and add the `InteractableTypeAttribute`:
   ```csharp
   using Fuse.Core;

   namespace Fuse.Interaction;

   [InteractableType("DoorInteract")]
   public sealed class DoorInteract : IInteractable
   {
       public void OnInteract()
       {
           Logger.Info("Door opened!");
           // Add door logic here (e.g., play sounds or modify physics constraints)
       }
   }
   ```
3. **Configure in the Editor**:
   * Open the map in **Blowtorch**.
   * Select the target entity (e.g. a door model or brush).
   * In the **Properties Inspector**, set **Interactable** to `DoorInteract`.
   * Enable physics for the object by adding a **Body** component so that it can be targeted by raycasts.
   * Save the map.

### 5.2. Adding a New Asset

1. **Import the File**:
   * Copy 3D model files (OBJ, FBX) to `Fuse/res/Models/`.
   * Copy texture files (PNG, BMP, JPG) to `Fuse/res/Textures/`.
2. **Load via code**:
   ```csharp
   // Load texture
   var myTexture = assets.GetTexture($"{ResPath.Path}/Textures/my_texture.png");

   // Load 3D model (Assimp parser automatically builds sub-meshes and vertices)
   var myModel = assets.GetModel($"{ResPath.Path}/Models/my_model.obj", scale: 1.0f);
   ```
3. **Reference in JSON maps**:
   ```json
   {
     "id": "my_statue",
     "visible": true,
     "model": "Models/my_model.obj",
     "model_scale": 1.0,
     "texture": "Textures/my_texture.png",
     "body": {
       "shape": "trimesh",
       "position": [0, 0, 0],
       "rotation": [1, 0, 0, 0],
       "mass": 0
     }
   }
   ```

### 5.3. Modifying Player Movement Code

Player movement logic is located in `ApplyMovement()` within [Player.cs](file:///e:/DEV/Csharp/FuseEngine/Fuse/src/Player/Player.cs#L134).

* **Adjusting Physics Settings**: Update parameters in the constructor or implementation methods:
  * `_maxSpeedGround` (Default: `4.0f`): Base walking speed.
  * `_jumpForce` (Default: `3.8f`): Upward force applied when jumping.
  * `_frictionValue` (Default: `4.0f`): Rate of deceleration on the ground.
  * `_airAccel` (Default: `150.0f`): Speed adjustment capability while airborne.
* **Adding Movement Mechanics (e.g., Double Jump)**:
  1. Add a tracking variable: `private int _jumpCount = 0;`.
  2. Reset it when touching the ground:
     ```csharp
     if (onGround) {
         _jumpCount = 0;
     }
     ```
  3. Allow jumping if airborne and `_jumpCount < 2`:
     ```csharp
     if (Input.Input.KeyPressed(Input.KeyCodes.Space) && (!onGround && _jumpCount < 2)) {
         velocity.Y = _jumpForce;
         _jumpCount++;
     }
     ```

### 5.4. Creating a New Viewport Window in the Editor

Editor panels are created in [EditorUI.cs](file:///e:/DEV/Csharp/FuseEngine/Blowtorch/EditorUI.cs).

1. **Add Panel Layout**: In the `Draw()` method, define the panel:
   ```csharp
   if (ImGui.Begin("My Panel"))
   {
       ImGui.Text("Custom Editor Tool");
       if (ImGui.Button("Click Me"))
       {
           // Add action (e.g. center camera on selection)
       }
       ImGui.End();
   }
   ```
2. **Handle State**: Use services passed to `Draw()` (`EditorSceneService`, `EditorAssetService`) to read and modify active level data. Apply changes using `SnapshotCommand` to ensure they are recorded in the undo/redo history.

---

## 6. Architecture & Physics Tips

### Jolt Physics Lifetime Rules
> [!IMPORTANT]
> Jolt Physics uses unmanaged memory allocations. Always follow these lifetime rules:
> * Calling `BodyInterface.DestroyBody(bodyID)` removes the body from the Jolt simulation.
> * Always call `IDisposable.Dispose()` on Jolt structures (`PhysicsSystem`, `JobSystemThreadPool`, shapes, character controllers) when shutting down to prevent memory leaks.
> * Ensure collision filters (`BroadPhaseLayerInterfaceTable`, `ObjectLayerPairFilterTable`) are kept alive via references in C# class instances. If garbage collected, the Jolt simulation will trigger access violation crashes.

### Collision Filtering
By default, the engine uses simplified collision layer mapping:
* Objects are mapped to layer `0` (`ObjectLayer`).
* In `PhysicsWorld`'s constructor, `_objectLayerFilter.EnableCollision(0, 0)` is called, which enables collisions between all objects in the world.
* If you introduce new collision layers (e.g., separating player controllers from static level geometry), you must expand the layer tables in `PhysicsWorld`'s constructor.

### CSG Brush Mesh Generation
> [!TIP]
> The `MeshGenerator` uses a plane intersection algorithm to generate brush meshes. 
> * Brushes must be **convex**. Concave brushes will not generate correctly and can cause rendering issues.
> * To create concave geometry (like rooms or hallways), assemble multiple convex brushes together.
> * When modifying brush planes via code, verify that the intersection points form a valid convex shape. If planes are parallel or point away from each other, the intersection detector will fail to find valid vertices, resulting in an empty mesh.
