using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Fuse.Input;
using Fuse.Interaction;
using Fuse.Behaviours;

namespace Fuse.Core;

public unsafe class Application : IDisposable
{
    private readonly Window _window;
    private double _lastTime;
    private bool _paused;
    private int _scrWidth = 1280, _scrHeight = 800;

    // Physics
    private readonly Physics.PhysicsWorld _physics;

    // Player
    private Player.Player _player = null!;
    private Player.PickupController _pickup = null!;

    // Asset manager
    private AssetManagement.AssetManager _assets = null!;

    // Shaders (from AssetManager)
    private Renderer.Shader _shader = null!;
    private Renderer.Shader _skyboxShader = null!;

    // Meshes (from AssetManager)
    private Renderer.Mesh _cubeMesh = null!;
    private Renderer.Mesh _groundMesh = null!;

    // Textures (from AssetManager)
    private Renderer.Texture _crateTexture = null!;
    private Renderer.Texture _skyboxTexture = null!;
    private Renderer.Texture _crosshairTexture = null!;
    private Renderer.Texture _crosshairInteractTexture = null!;

    // Scene
    //public string mapPath = $"{Fuse.ResPath.Path}/Maps/default.json";
    public string mapPath = null!;
    private Renderer.Scene _scene = null!;
    private readonly List<Physics.RigidBody> _bodies = [];

    // UI
    private Renderer.UIRenderer _ui = null!;
    private UI.HUD _hud = null!;
    private UI.HUDText _fpsText = null!;
    private UI.HUDImage _crosshairNode = null!;

    // Debug
    private Debug.DebugDrawer _debugDrawer = null!;

    // ImGui
    private Imgui.ImGuiBackEnd _imgui = null!;
    private Imgui.Console _console = null!;

    // Interaction
    private readonly List<IInteractable> _interactables = [];
    private Interaction.IInteractable? _lookingAt;
    private readonly Dictionary<JoltPhysicsSharp.BodyID, GCHandle> _interactableHandles = [];

    // Behaviours
    private readonly List<IBehaviour> _behaviours = [];

    public Application()
    {
        _window = new Window("Fuse Engine", _scrWidth, _scrHeight);
        _physics = new Physics.PhysicsWorld(new Vector3(0, -9.81f, 0));
    }

    public bool Init()
    {
        if (_window.Handle == null)
        {
            Logger.Error("Window creation failed");
            return false;
        }

        var gl = _window.GL;

        // Asset manager
        _assets = new AssetManagement.AssetManager(gl);

        // Debug drawer
        _debugDrawer = new Debug.DebugDrawer(gl);

        // UI
        _ui = new Renderer.UIRenderer(gl, _scrWidth, _scrHeight);

        // ImGui
        _imgui = new Imgui.ImGuiBackEnd(gl);
        _imgui.Init();

        // Player (must be after UI)
        _player = new Player.Player(_physics, new Vector3(0, 2, 0));

        // Pickup
        var emptyID = new JoltPhysicsSharp.BodyID();
        _pickup = new Player.PickupController(_physics, _player.Camera, emptyID);

        // Console
        _console = new Imgui.Console();
        _console.SetPlayer(_player);
        _console.StartCapture();

        // Assets via AssetManager
        _shader = _assets.GetShader($"{Fuse.ResPath.Path}/Shaders/default.vert", $"{Fuse.ResPath.Path}/Shaders/default.frag")!;
        _skyboxShader = _assets.GetShader($"{Fuse.ResPath.Path}/Shaders/skybox.vert", $"{Fuse.ResPath.Path}/Shaders/skybox.frag")!;
        _cubeMesh = _assets.GetMesh("cube")!;
        _groundMesh = _assets.GetMesh("ground")!;
        _crateTexture = _assets.GetTexture($"{Fuse.ResPath.Path}/Textures/dev_measurecrate01.bmp");
        _skyboxTexture = _assets.GetTexture($"{Fuse.ResPath.Path}/Textures/skybox_2.png");
        _crosshairTexture = _assets.GetTexture($"{Fuse.ResPath.Path}/Textures/UI/crosshair.png");
        _crosshairInteractTexture = _assets.GetTexture($"{Fuse.ResPath.Path}/Textures/UI/crosshair_interact.png");

        // Scene
        InitScene();

        // Wire-up callbacks
        RegisterWindowCallbacks();

        // HUD
        _hud = new UI.HUD();
        _fpsText = _hud.AddText("FPS: 0", UI.HUDAnchor.TopLeft, new Vector2(20, 20), 2.0f, new Vector4(0, 1, 1, 1));
        _crosshairNode = _hud.AddImage(_crosshairTexture, UI.HUDAnchor.Center, Vector2.Zero, new Vector2(8, 8));

        // Register interactables
        RegisterInteractablesAndBehaviours();

        _lastTime = _window.GlfwApi.GetTime();
        Logger.Info(":: Application ready ::");
        return true;
    }

    private void RegisterInteractablesAndBehaviours()
    {
        foreach (var entity in _scene.Entities)
        {
            if (entity.Body != null && entity.Body.IsBuilt && !string.IsNullOrEmpty(entity.InteractableType))
            {
                var interactable = Interaction.InteractionSystem.CreateInteractable(entity.InteractableType);
                if (interactable != null)
                {
                    interactable.Entity = entity;
                    interactable.World = _physics;
                    _interactables.Add(interactable);
                    var gcHandle = GCHandle.Alloc(interactable);
                    _interactableHandles[entity.Body.Native] = gcHandle;
                    _physics.BodyInterface.SetUserData(entity.Body.Native, (ulong)GCHandle.ToIntPtr(gcHandle));
                }
            }
        }

        foreach (var entity in _scene.Entities)
        {
            if (entity.Body != null && entity.Body.IsBuilt && !string.IsNullOrEmpty(entity.BehaviourType))
            {
                var behaviour = BehaviourSystem.Create(entity.BehaviourType);
                if (behaviour != null)
                {
                    behaviour.Entity = entity;
                    behaviour.World = _physics;
                    _behaviours.Add(behaviour);
                }
            }
        }
    }

    private void InitScene()
    {
        _scene = new Renderer.Scene();

        var loaded = Fuse.Scene.MapSerializer.LoadFromFile(mapPath, _scene, _physics, _assets, out var spawn, Fuse.ResPath.Path);
        if (loaded != null)
        {
            _bodies.AddRange(loaded);
            if (spawn.HasValue)
            {
                _player.NativeCharacter.Position = spawn.Value.Position;
                _player.Camera.SetRotation(spawn.Value.Yaw, spawn.Value.Pitch);
            }
            Logger.Important($"CURRENT MAP LOADED: {mapPath}");
        }
        //else
        //{
        //    Logger.Warn("Falling back to procedural scene");
        //    var groundBody = new Physics.RigidBody()
        //        .SetBox(new Vector3(20.0f, 0.5f, 20.0f))
        //        .SetPosition(new Vector3(0, 0, 0))
        //        .SetMass(0)
        //        .SetFriction(0.8f);
        //    groundBody.Build(_physics);
        //    _bodies.Add(groundBody);
        //    _scene.Add(_groundMesh, "ground", groundBody);
        //
        //    var cubeBody = new Physics.RigidBody()
        //        .SetBox(new Vector3(0.5f, 0.5f, 0.5f))
        //        .SetPosition(new Vector3(0, 1, -3))
        //        .SetMass(10.0f)
        //        .SetFriction(0.5f)
        //        .SetRestitution(0.4f);
        //    cubeBody.Build(_physics);
        //    _bodies.Add(cubeBody);
        //
        //    var entity = _scene.Add(_cubeMesh, "cube", cubeBody);
        //    entity.InteractableType = "CubeInteract";
        //}
    }

    private void RegisterWindowCallbacks()
    {
        _window.OnMouseMove += (dx, dy) =>
        {
            _player.Camera.ProcessMouseMovement((float)dx, (float)dy);
        };

        _window.OnScroll += (yoffset) =>
        {
            if (!ImGuiNET.ImGui.GetIO().WantCaptureMouse)
            {
                float fov = _player.Camera.FOV;
                fov -= (float)yoffset * 2.0f;
                _player.Camera.FOV = float.Clamp(fov, 1.0f, 120.0f);
            }
        };

        _window.OnResize += (width, height) =>
        {
            _scrWidth = width;
            _scrHeight = height;
            _window.SetSize(width, height);
            _ui.SetScreenSize(width, height);
        };

        _window.OnKeyPress += (key) =>
        {
            if (key == KeyCodes.Escape)
            {
                _paused = !_paused;
                _window.CursorCaptureEnabled = !_paused;
                if (_paused)
                    Input.Input.ShowCursor();
                else
                    Input.Input.DisableCursor();
            }
        };
    }

    public void Run()
    {
        Logger.Info("Entering game loop");

        try
        {
            while (!_window.ShouldClose)
            {
                double now = _window.GlfwApi.GetTime();
                float dt = (float)(now - _lastTime);
                _lastTime = now;
                Engine.Tick(dt);

                Input.Input.Update();

                var gl = _window.GL;

                // Update
                if (!_paused)
                {
                    _pickup.PhysicsUpdate(dt);
                    _physics.Step(float.Min(dt, 0.0333f));
                    _player.Update(dt);
                    _pickup.Update(dt);
                    foreach (var interactable in _interactables)
                        interactable.Update(dt);
                    foreach (var behaviour in _behaviours)
                        behaviour.Update(dt);
                }

                HandleInput();

                // Render
                Render(gl);

                // Debug physics shapes
                DrawDebug();

                // UI
                DrawUI(gl);

                // ImGui
                _imgui.NewFrame(dt, _scrWidth, _scrHeight);
                _imgui.DrawWindows(_player);
                _console.Draw();
                _imgui.Render();

                _window.SwapBuffers();
                _window.PollEvents();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Game loop exception: {ex.Message}\n{ex.StackTrace}");
        }

        Logger.Info("Exited game loop");
    }

    private void HandleInput()
    {
        //if (Input.Input.KeyPressed(KeyCodes.F5))
        //{
        //    foreach (var entity in _scene.Entities)
        //    {
        //        if (entity.Id == "cube" && entity.Body != null && entity.Body.IsBuilt)
        //        {
        //            _physics.SetBodyPosition(entity.Body.Native, new Vector3(0, 1, -3));
        //            _physics.BodyInterface.SetLinearVelocity(entity.Body.Native, Vector3.Zero);
        //            _physics.BodyInterface.SetAngularVelocity(entity.Body.Native, Vector3.Zero);
        //        }
        //    }
        //
        //    _player.NativeCharacter.Position = new Vector3(0,2,0);
        //    _player.NativeCharacter.LinearVelocity = Vector3.Zero;
        //}

        if (Input.Input.KeyPressed(KeyCodes.F6))
        {
            string savePath = mapPath;
            var spawn = new Fuse.Scene.PlayerSpawn(
                _player.NativeCharacter.Position,
                _player.Camera.Yaw,
                _player.Camera.Pitch);
            Fuse.Scene.MapSerializer.SaveToFile(_scene, _physics, savePath, spawn);
        }

        if (Input.Input.KeyPressed(KeyCodes.F5))
        {
            _interactables.Clear();
            _behaviours.Clear();
            string loadPath = mapPath;
            foreach (var b in _bodies)
            {
                if (b.IsBuilt)
                    _physics.DestroyBody(b.Native);
            }
            _bodies.Clear();
            foreach (var handle in _interactableHandles.Values)
                handle.Free();
            _interactableHandles.Clear();

            var loaded = Fuse.Scene.MapSerializer.LoadFromFile(loadPath, _scene, _physics, _assets, out var spawn, Fuse.ResPath.Path);
            if (loaded != null)
            {
                _bodies.AddRange(loaded);
                if (spawn.HasValue)
                {
                    _player.NativeCharacter.Position = spawn.Value.Position;
                    _player.NativeCharacter.LinearVelocity = Vector3.Zero;
                    _player.Camera.SetRotation(spawn.Value.Yaw, spawn.Value.Pitch);
                }
            }

            RegisterInteractablesAndBehaviours();
        }

        if (Input.Input.KeyPressed(KeyCodes.F9))
            _debugDrawer.Toggle();

        if (Input.Input.KeyPressed(KeyCodes.GraveAccent))
        {
            _console.Toggle();
            if (_console.IsOpen)
                Input.Input.ShowCursor();
            else
                Input.Input.DisableCursor();
        }
    }

    private void DrawDebug()
    {
        if (!_debugDrawer.Enabled) return;

        _debugDrawer.Clear();

        foreach (var b in _bodies)
        {
            if (!b.IsBuilt) continue;

            var pos = b.Position(_physics);
            var rot = b.Rotation(_physics);
            var color = b.Mass > 0 ? new Vector3(1, 1, 0) : new Vector3(1, 0, 0);

            switch (b.Type)
            {
                case Physics.RigidBody.ShapeType.Box:
                    _debugDrawer.DrawBox(pos, rot, b.BoxHalfExtents, color);
                    break;
                case Physics.RigidBody.ShapeType.Sphere:
                    _debugDrawer.DrawSphere(pos, rot, b.SphereRadius, color);
                    break;
                case Physics.RigidBody.ShapeType.Capsule:
                    _debugDrawer.DrawCapsule(pos, rot, b.CapsuleHeight * 0.5f, b.CapsuleRadius, color);
                    break;
                case Physics.RigidBody.ShapeType.Trimesh:
                    if (b.TrimeshVertices != null && b.TrimeshIndices != null)
                        _debugDrawer.DrawTrimesh(pos, rot, b.TrimeshVertices, b.TrimeshIndices, color);
                    break;
            }
        }

        // Draw player character capsule
        float capsuleHalfH = _player.IsCrouching ? 0.4f : 0.9f;
        _debugDrawer.DrawCapsule(_player.Position, Quaternion.Identity, capsuleHalfH, 0.5f, new Vector3(0, 1, 0));
        _debugDrawer.DrawBox(_player.FeetPosition, Quaternion.Identity, new Vector3(0.1f), new Vector3(0, 1, 1));

        float aspect = (float)_scrWidth / _scrHeight;
        var view = _player.Camera.GetViewMatrix();
        var proj = _player.Camera.GetProjectionMatrix(aspect);
        _debugDrawer.Render(view, proj);
    }

    private void Render(GL gl)
    {
        float aspect = (float)_scrWidth / _scrHeight;
        var view = _player.Camera.GetViewMatrix();
        var proj = _player.Camera.GetProjectionMatrix(aspect);

        gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Skybox
        if (_skyboxShader.ID != 0 && _skyboxTexture.ID != 0)
        {
            gl.DepthMask(false);
            gl.DepthFunc(DepthFunction.Lequal);
            gl.CullFace(GLEnum.Front);

            _skyboxShader.Use();
            var skyView = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromRotationMatrix(view));
            _skyboxShader.SetMat4("uView", skyView);
            _skyboxShader.SetMat4("uProj", proj);
            _skyboxShader.SetInt("uSkyTexture", 0);
            _skyboxTexture.Bind(0);
            _cubeMesh.Draw();

            gl.CullFace(GLEnum.Back);
            gl.DepthFunc(DepthFunction.Less);
            gl.DepthMask(true);
        }

        // World geometry
        if (_shader.ID != 0)
        {
            gl.Enable(EnableCap.DepthTest);
            gl.Enable(EnableCap.CullFace);
            gl.CullFace(GLEnum.Back);
            gl.DepthFunc(DepthFunction.Less);
            _shader.Use();
            _shader.SetVec3("uLightDir", Vector3.Normalize(new Vector3(1, 2, 1)));
            _shader.SetVec3("uLightColor", new Vector3(1, 0.95f, 0.9f));
            _shader.SetFloat("uAmbient", 0.15f);
            _shader.SetMat4("uView", view);
            _shader.SetMat4("uProj", proj);
            _shader.SetVec3("uColor", Vector3.One);
            _shader.SetBool("uUseTexture", true);
            _shader.SetInt("uTexture", 0);

            _scene.Render(_shader, _physics, _crateTexture);
        }
    }

    private void UpdateInteractionRay()
    {
        if (_fpsText == null) return;
        Vector3 origin = _player.EyePosition;
        Vector3 dir = _player.Camera.Front;
        float range = 5.0f;

        var hit = InteractionSystem.RaycastInteractable(_physics, origin, dir, range);

        if (hit != _lookingAt)
        {
            _lookingAt = hit;
            if (hit != null)
                _crosshairNode.Texture = _crosshairInteractTexture;
            else
                _crosshairNode.Texture = _crosshairTexture;
        }

        if (Input.Input.KeyPressed(KeyCodes.E) && _lookingAt != null)
            _lookingAt.OnInteract();
    }

    private void DrawUI(GL gl)
    {
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Update FPS text
        if (_fpsText != null)
            _fpsText.Text = $"FPS: {Engine.FPS}";

        // Paused state
        if (_paused)
        {
            _crosshairNode.Texture = _crosshairTexture;
        }

        UpdateInteractionRay();
        _hud.Update(_scrWidth, _scrHeight);
        _hud.Draw(_ui, _scrWidth, _scrHeight);

        // Paused overlay
        if (_paused)
        {
            Vector2 center = _ui.Center;
            _ui.DrawText(center.X - 60.0f, center.Y - 20.0f, "PAUSED".AsSpan(),
                new Vector4(1, 1, 0, 1), 3.0f);
        }

        gl.Disable(EnableCap.Blend);
        gl.Enable(EnableCap.CullFace);
        gl.Enable(EnableCap.DepthTest);
    }

    public void Dispose()
    {
        foreach (var handle in _interactableHandles.Values)
            handle.Free();

        foreach (var b in _bodies)
        {
            if (b.IsBuilt)
                _physics.DestroyBody(b.Native);
        }
        _scene.Clear();
        _console.StopCapture();
        _player.Dispose();
        _imgui.Shutdown();
        _ui.Dispose();
        _debugDrawer.Dispose();
        _assets.Clear();
        _physics.Dispose();
        _window.Dispose();
        Logger.Info("Application shutdown");
    }
}
