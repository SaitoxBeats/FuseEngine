using System;
using Fuse.Imgui;

namespace Blowtorch;

public unsafe class EditorApplication : IDisposable
{
    private EditorWindow _window = null!;
    private EditorInputService _inputService = null!;
    private ImGuiBackEnd _imgui = null!;
    private EditorAssetService _assetService = null!;
    private EditorSceneService _sceneService = null!;
    private EditorViewport _viewport = null!;
    private EditorUI _ui = null!;
    private CommandHistory _history = null!;

    public bool Init()
    {
        try
        {
            _window = new EditorWindow("Blowtorch Map Editor", 1280, 800);
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine(ex.Message);
            return false;
        }

        var gl = _window.GL;
        var handle = _window.Handle;
        var glfw = _window.Glfw;

        // Initialize Services
        _inputService = new EditorInputService();
        _inputService.Initialize(glfw, handle);

        _imgui = new ImGuiBackEnd(gl);
        _imgui.Init();

        _assetService = new EditorAssetService(gl);
        _assetService.Initialize(AppContext.BaseDirectory);

        _sceneService = new EditorSceneService();
        _sceneService.LoadMap(_assetService.FuseResPath);
        _sceneService.PopulateScene(_assetService);

        _viewport = new EditorViewport(gl);
        _ui = new EditorUI();
        _history = new CommandHistory();

        return true;
    }

    public void Run()
    {
        double lastTime = _window.Glfw.GetTime();

        while (!_window.ShouldClose)
        {
            double now = _window.Glfw.GetTime();
            float dt = (float)(now - lastTime);
            lastTime = now;

            _window.Glfw.GetFramebufferSize(_window.Handle, out int fbWidth, out int fbHeight);
            var gl = _window.GL;
            gl.Viewport(0, 0, (uint)fbWidth, (uint)fbHeight);
            gl.ClearColor(0.12f, 0.12f, 0.14f, 1.0f);
            gl.Clear(Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit | Silk.NET.OpenGL.ClearBufferMask.DepthBufferBit);

            _inputService.Update();
            _imgui.NewFrame(dt, fbWidth, fbHeight);

            // Render Viewport
            _viewport.BeginRender();
            _viewport.RenderScene(_assetService, _sceneService);
            _viewport.RenderDebug(_assetService, _sceneService);
            _viewport.EndRender(fbWidth, fbHeight);

            // Render UI
            _ui.Draw(_window, _viewport, _sceneService, _assetService, _history);

            _imgui.Render();
            _window.SwapBuffers();
            _window.PollEvents();
        }
    }

    public void Dispose()
    {
        _viewport.Dispose();
        _assetService.Dispose();
        _imgui.Dispose();
        _window.Dispose();
    }
}
