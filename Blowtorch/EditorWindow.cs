using System;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace Blowtorch;

public unsafe class EditorWindow : IDisposable
{
    private readonly Glfw _glfw;
    private readonly WindowHandle* _handle;
    private readonly GL _gl;

    public EditorWindow(string title, int width, int height)
    {
        _glfw = Glfw.GetApi();
        if (!_glfw.Init())
            throw new Exception("Failed to init GLFW");

        _glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
        _glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
        _glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
        _glfw.WindowHint(WindowHintBool.Maximized, true);

        _handle = _glfw.CreateWindow(width, height, title, null, null);
        if (_handle == null)
        {
            _glfw.Terminate();
            throw new Exception("Failed to create window");
        }

        _glfw.MakeContextCurrent(_handle);
        _glfw.SwapInterval(1);

        //var monitor = _glfw.GetPrimaryMonitor();
        //if (monitor != null)
        //{
        //    var mode = _glfw.GetVideoMode(monitor);
        //    _glfw.GetMonitorPos(monitor, out int monitorX, out int monitorY);
        //    int x = monitorX + (mode->Width - width) / 2;
        //    int y = monitorY + (mode->Height - height) / 2;
        //    _glfw.SetWindowPos(_handle, x, y);
        //}

        _gl = GL.GetApi(_glfw.GetProcAddress);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(GLEnum.Back);
    }

    public Glfw Glfw => _glfw;
    public WindowHandle* Handle => _handle;
    public GL GL => _gl;

    public bool ShouldClose => _glfw.WindowShouldClose(_handle);
    public void SwapBuffers() => _glfw.SwapBuffers(_handle);
    public void PollEvents() => _glfw.PollEvents();
    public void Close() => _glfw.SetWindowShouldClose(_handle, true);

    public void Dispose()
    {
        _glfw.DestroyWindow(_handle);
        _glfw.Terminate();
    }
}
