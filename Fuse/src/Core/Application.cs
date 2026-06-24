using System.Numerics;
using Fuse.Input;
using Silk.NET.OpenGL;

namespace Fuse.Core;

public unsafe class Application : IDisposable
{
    private readonly Window _window;
    private double _lastTime;
    private bool _paused;
    private int _scrWidth = 1280, _scrHeight = 800;

    // Core Systems
    private readonly Physics.PhysicsWorld _physics;
    private AssetManagement.AssetManager _assets = null!;
    private Renderer.MasterRenderer _renderer = null!;
    private Scene.SceneManager _sceneManager = null!;
    private Interaction.PlayerInteraction _interaction = null!;

    // Player
    private Player.Player _player = null!;
    private Player.PickupController _pickup = null!;

    // UI & Debug
    private Renderer.UIRenderer _ui = null!;
    private UI.HUD _hud = null!;
    private UI.HUDText _fpsText = null!;
    private UI.HUDImage _crosshairNode = null!;
    private Debug.DebugDrawer _debugDrawer = null!;
    private Imgui.ImGuiBackEnd _imgui = null!;
    private Imgui.Console _console = null!;

    public Application()
    {
        _window = new Window("Fuse", _scrWidth, _scrHeight);
        _physics = new Physics.PhysicsWorld(new Vector3(0, -9.81f, 0));
    }

    public bool Init(string initialMap)
    {
        if (_window.Handle == null)
        {
            Logger.Error("Window creation failed");
            return false;
        }

        var gl = _window.GL;

        // Managers & Core
        _assets = new AssetManagement.AssetManager(gl);
        _renderer = new Renderer.MasterRenderer(gl);
        _sceneManager = new Scene.SceneManager(_physics, _assets);
        _debugDrawer = new Debug.DebugDrawer(gl);
        
        // UI & ImGui
        _ui = new Renderer.UIRenderer(gl, _scrWidth, _scrHeight);
        _imgui = new Imgui.ImGuiBackEnd(gl);
        _imgui.Init();

        // Player setup
        _player = new Player.Player(_physics, new Vector3(0, 2, 0));
        var emptyID = new JoltPhysicsSharp.BodyID();
        _pickup = new Player.PickupController(_physics, _player.Camera, emptyID);

        // Console
        _console = new Imgui.Console();
        _console.SetPlayer(_player);
        _console.StartCapture();
        _console.OnLoadMap = (map) => LoadMap(map);
        _console.OnLoadSky = (fileName) =>
        {
            var tex = _assets.GetTexture($"{Fuse.ResPath.Path}/Textures/{fileName}");
            if (tex.ID == 0)
                Logger.Error($"Failed to load skybox: {fileName}");
            else
                _renderer.SetSkyboxTexture(tex);
        };

        // HUD
        _hud = new UI.HUD();
        _fpsText = _hud.AddText("FPS: 0", UI.HUDAnchor.TopLeft, new Vector2(20, 20), 2.0f, new Vector4(0, 1, 1, 1));
        var crosshairTexture = _assets.GetTexture($"{Fuse.ResPath.Path}/Textures/UI/crosshair.png");
        var crosshairInteractTexture = _assets.GetTexture($"{Fuse.ResPath.Path}/Textures/UI/crosshair_interact.png");
        _crosshairNode = _hud.AddImage(crosshairTexture, UI.HUDAnchor.Center, Vector2.Zero, new Vector2(8, 8));

        // Initialization
        _renderer.Init(_assets, _scrWidth, _scrHeight);
        _interaction = new Interaction.PlayerInteraction(_physics, _player, _crosshairNode, crosshairTexture, crosshairInteractTexture);

        // Default Map Loading
        LoadMap(initialMap);

        RegisterWindowCallbacks();

        _lastTime = _window.GlfwApi.GetTime();
        Logger.Info(":: Application ready ::");
        return true;
    }

    private void LoadMap(string mapName)
    {
        var spawn = _sceneManager.LoadMap(mapName);
        if (spawn.HasValue)
        {
            _player.NativeCharacter.Position = spawn.Value.Position;
            _player.NativeCharacter.LinearVelocity = Vector3.Zero;
            _player.Camera.SetRotation(spawn.Value.Yaw, spawn.Value.Pitch);
        }
        _sceneManager.InitTriggerSystem(_player);
    }
    
    private void ReloadMap()
    {
        var spawn = _sceneManager.ReloadMap();
        if (spawn.HasValue)
        {
            _player.NativeCharacter.Position = spawn.Value.Position;
            _player.NativeCharacter.LinearVelocity = Vector3.Zero;
            _player.Camera.SetRotation(spawn.Value.Yaw, spawn.Value.Pitch);
        }
        _sceneManager.InitTriggerSystem(_player);
    }

    private void RegisterWindowCallbacks()
    {
        _window.OnMouseMove += (dx, dy) => { _player.Camera.ProcessMouseMovement((float)dx, (float)dy); };
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
            _renderer.Resize(width, height);
        };

        _window.OnKeyPress += (key) =>
        {
            if (key == KeyCodes.F12) _renderer.ShadowsEnabled = !_renderer.ShadowsEnabled;
            if (key == KeyCodes.Escape)
            {
                _paused = !_paused;
                _window.CursorCaptureEnabled = !_paused;
                if (_paused) Input.Input.ShowCursor();
                else Input.Input.DisableCursor();
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
                    
                    _sceneManager.Update(dt);
                    if (_sceneManager.CheckPendingResets())
                    {
                        ReloadMap();
                    }
                }

                HandleInput();

                // Render
                _renderer.RenderFrame(_sceneManager.ActiveScene, _player.Camera, _physics);

                // Debug
                if (_debugDrawer.Enabled)
                {
                    _debugDrawer.Clear();
                    _sceneManager.DrawDebug(_debugDrawer);
                    _debugDrawer.DrawPlayerDebug(_player); // We'll move player debug to DebugDrawer
                    float aspect = (float)_scrWidth / _scrHeight;
                    _debugDrawer.Render(_player.Camera.GetViewMatrix(), _player.Camera.GetProjectionMatrix(aspect));
                }

                // UI
                DrawUI(gl);

                // ImGui
                _imgui.NewFrame(dt, _scrWidth, _scrHeight);
                _imgui.DrawWindows(_player);

                //if (_paused)
                //{
                //    ImGuiNET.ImGui.Begin("Shadow Settings");
                //    ImGuiNET.ImGui.DragFloat("Bias Factor", ref _renderer.ShadowBiasFactor, 0.0001f, 0.0f, 0.1f, "%.5f");
                //    ImGuiNET.ImGui.DragFloat("Bias Base", ref _renderer.ShadowBiasBase, 0.00001f, 0.0f, 0.01f, "%.6f");
                //    ImGuiNET.ImGui.DragFloat("Near Plane", ref _renderer.ShadowNearPlane, 1.0f, -200.0f, 200.0f, "%.1f");
                //    ImGuiNET.ImGui.DragFloat("Far Plane", ref _renderer.ShadowFarPlane, 1.0f, 10.0f, 1000.0f, "%.1f");
                //    ImGuiNET.ImGui.DragFloat("Spread (Softness)", ref _renderer.ShadowSpread, 0.1f, 0.0f, 20.0f, "%.1f");
                //    ImGuiNET.ImGui.End();
                //}

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
        if (Input.Input.KeyPressed(KeyCodes.F6))
        {
            string savePath = _sceneManager.CurrentMapPath;
            var spawn = new Fuse.Scene.PlayerSpawn(
                _player.NativeCharacter.Position,
                _player.Camera.Yaw,
                _player.Camera.Pitch);
            Fuse.Scene.MapSerializer.SaveToFile(_sceneManager.ActiveScene, _physics, savePath, spawn);
        }

        if (Input.Input.KeyPressed(KeyCodes.F5)) ReloadMap();
        if (Input.Input.KeyPressed(KeyCodes.F9)) _debugDrawer.Toggle();
        if (Input.Input.KeyPressed(KeyCodes.GraveAccent))
        {
            _console.Toggle();
            if (_console.IsOpen) Input.Input.ShowCursor();
            else Input.Input.DisableCursor();
        }
    }

    private void DrawUI(GL gl)
    {
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        if (_fpsText != null) _fpsText.Text = $"FPS: {Engine.FPS}";
        
        if (!_paused) _interaction.Update();

        _hud.Update(_scrWidth, _scrHeight);
        _hud.Draw(_ui, _scrWidth, _scrHeight);

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
        _sceneManager.Dispose();
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
