using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Fuse.Core;

namespace Fuse.Debug;

public unsafe class DebugDrawer : IDisposable
{
    private readonly GL _gl;
    private uint _vao;
    private uint _vbo;
    private uint _shader;
    private int _uView = -1;
    private int _uProj = -1;
    private bool _enabled;

    private readonly List<Line> _lines = [];

    private struct Line
    {
        public Vector3 From;
        public Vector3 To;
        public Vector3 Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DebugVert
    {
        public float PX, PY, PZ;
        public float CR, CG, CB;
    }

    public DebugDrawer(GL gl)
    {
        _gl = gl;

        string vsSrc = """
            #version 330 core
            layout(location = 0) in vec3 aPos;
            layout(location = 1) in vec3 aColor;
            out vec3 vColor;
            uniform mat4 uView;
            uniform mat4 uProj;
            void main() {
                vColor = aColor;
                gl_Position = uProj * uView * vec4(aPos, 1.0);
            }
            """;

        string fsSrc = """
            #version 330 core
            in vec3 vColor;
            out vec4 fragColor;
            void main() {
                fragColor = vec4(vColor, 1.0);
            }
            """;

        uint vs = CompileShader(ShaderType.VertexShader, vsSrc);
        uint fs = CompileShader(ShaderType.FragmentShader, fsSrc);

        _shader = gl.CreateProgram();
        gl.AttachShader(_shader, vs);
        gl.AttachShader(_shader, fs);
        gl.LinkProgram(_shader);

        gl.GetProgram(_shader, GLEnum.LinkStatus, out int success);
        if (success == 0)
        {
            string info = gl.GetProgramInfoLog(_shader);
            Logger.Error($"DebugDrawer shader link: {info}");
        }

        gl.DeleteShader(vs);
        gl.DeleteShader(fs);

        _uView = gl.GetUniformLocation(_shader, "uView");
        _uProj = gl.GetUniformLocation(_shader, "uProj");

        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_shader);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public void Toggle() => _enabled = !_enabled;

    public void PushLine(Vector3 from, Vector3 to, Vector3 color)
    {
        _lines.Add(new Line { From = from, To = to, Color = color });
    }

    public void DrawBox(Vector3 pos, Quaternion rot, Vector3 halfExtents, Vector3 color)
    {
        var r = Matrix4x4.CreateFromQuaternion(rot);
        Vector3[] corners =
        [
            pos + Vector3.Transform(new Vector3(-halfExtents.X, -halfExtents.Y, -halfExtents.Z), r),
            pos + Vector3.Transform(new Vector3( halfExtents.X, -halfExtents.Y, -halfExtents.Z), r),
            pos + Vector3.Transform(new Vector3( halfExtents.X,  halfExtents.Y, -halfExtents.Z), r),
            pos + Vector3.Transform(new Vector3(-halfExtents.X,  halfExtents.Y, -halfExtents.Z), r),
            pos + Vector3.Transform(new Vector3(-halfExtents.X, -halfExtents.Y,  halfExtents.Z), r),
            pos + Vector3.Transform(new Vector3( halfExtents.X, -halfExtents.Y,  halfExtents.Z), r),
            pos + Vector3.Transform(new Vector3( halfExtents.X,  halfExtents.Y,  halfExtents.Z), r),
            pos + Vector3.Transform(new Vector3(-halfExtents.X,  halfExtents.Y,  halfExtents.Z), r),
        ];
        PushBoxWire(corners, color);
    }

    public void DrawSphere(Vector3 pos, Quaternion rot, float radius, Vector3 color, int rings = 8, int sectors = 8)
    {
        var r = Matrix4x4.CreateFromQuaternion(rot);
        int prevRing = sectors - 1;
        for (int i = 0; i < sectors; ++i)
        {
            float a0 = (float)i / sectors * MathF.PI * 2.0f;
            float a1 = prevRing / (float)sectors * MathF.PI * 2.0f;
            int prevVert = rings - 1;
            for (int j = 0; j < rings; ++j)
            {
                float b0 = j / (float)(rings - 1) * MathF.PI;
                float b1 = prevVert / (float)(rings - 1) * MathF.PI;

                var v00 = pos + Vector3.Transform(new Vector3(
                    MathF.Cos(a0) * MathF.Sin(b0), MathF.Cos(b0), MathF.Sin(a0) * MathF.Sin(b0)) * radius, r);
                var v01 = pos + Vector3.Transform(new Vector3(
                    MathF.Cos(a1) * MathF.Sin(b0), MathF.Cos(b0), MathF.Sin(a1) * MathF.Sin(b0)) * radius, r);
                var v10 = pos + Vector3.Transform(new Vector3(
                    MathF.Cos(a0) * MathF.Sin(b1), MathF.Cos(b1), MathF.Sin(a0) * MathF.Sin(b1)) * radius, r);
                var v11 = pos + Vector3.Transform(new Vector3(
                    MathF.Cos(a1) * MathF.Sin(b1), MathF.Cos(b1), MathF.Sin(a1) * MathF.Sin(b1)) * radius, r);

                PushLine(v00, v01, color);
                PushLine(v00, v10, color);

                prevVert = j;
            }
            prevRing = i;
        }
    }

    public void DrawCapsule(Vector3 pos, Quaternion rot, float halfHeight, float radius, Vector3 color, int segments = 8)
    {
        var r = Matrix4x4.CreateFromQuaternion(rot);
        float step = MathF.PI * 2.0f / segments;

        var topCenter = pos + Vector3.Transform(new Vector3(0, halfHeight, 0), r);
        var botCenter = pos + Vector3.Transform(new Vector3(0, -halfHeight, 0), r);

        int prev = segments - 1;
        for (int i = 0; i < segments; ++i)
        {
            float a = i * step;
            float ap = prev * step;
            float c = MathF.Cos(a), cp = MathF.Cos(ap);
            float s = MathF.Sin(a), sp = MathF.Sin(ap);

            var t0 = topCenter + Vector3.Transform(new Vector3(c * radius, 0, s * radius), r);
            var t1 = topCenter + Vector3.Transform(new Vector3(cp * radius, 0, sp * radius), r);
            var b0 = botCenter + Vector3.Transform(new Vector3(c * radius, 0, s * radius), r);
            var b1 = botCenter + Vector3.Transform(new Vector3(cp * radius, 0, sp * radius), r);

            PushLine(t0, b0, color);
            PushLine(t0, t1, color);
            PushLine(b0, b1, color);

            int domeSteps = segments / 4;
            for (int j = 1; j <= domeSteps; ++j)
            {
                float b = j / (float)domeSteps * MathF.PI * 0.5f;
                float bp = (j - 1) / (float)domeSteps * MathF.PI * 0.5f;

                var ring0 = topCenter + Vector3.Transform(
                    new Vector3(c * radius * MathF.Sin(b), radius * MathF.Cos(b), s * radius * MathF.Sin(b)), r);
                var ring1 = topCenter + Vector3.Transform(
                    new Vector3(c * radius * MathF.Sin(bp), radius * MathF.Cos(bp), s * radius * MathF.Sin(bp)), r);
                var ringP0 = topCenter + Vector3.Transform(
                    new Vector3(cp * radius * MathF.Sin(b), radius * MathF.Cos(b), sp * radius * MathF.Sin(b)), r);
                var ringP1 = topCenter + Vector3.Transform(
                    new Vector3(cp * radius * MathF.Sin(bp), radius * MathF.Cos(bp), sp * radius * MathF.Sin(bp)), r);

                PushLine(ring0, ringP0, color);
                PushLine(ring0, ring1, color);
            }

            for (int j = 1; j <= domeSteps; ++j)
            {
                float b = j / (float)domeSteps * MathF.PI * 0.5f;
                float bp = (j - 1) / (float)domeSteps * MathF.PI * 0.5f;

                var ring0 = botCenter + Vector3.Transform(
                    new Vector3(c * radius * MathF.Sin(b), -radius * MathF.Cos(b), s * radius * MathF.Sin(b)), r);
                var ring1 = botCenter + Vector3.Transform(
                    new Vector3(c * radius * MathF.Sin(bp), -radius * MathF.Cos(bp), s * radius * MathF.Sin(bp)), r);
                var ringP0 = botCenter + Vector3.Transform(
                    new Vector3(cp * radius * MathF.Sin(b), -radius * MathF.Cos(b), sp * radius * MathF.Sin(b)), r);
                var ringP1 = botCenter + Vector3.Transform(
                    new Vector3(cp * radius * MathF.Sin(bp), -radius * MathF.Cos(bp), sp * radius * MathF.Sin(bp)), r);

                PushLine(ring0, ringP0, color);
                PushLine(ring0, ring1, color);
            }

            prev = i;
        }
    }

    public void DrawTrimesh(Vector3 pos, Quaternion rot, Vector3[] vertices, uint[] indices, Vector3 color)
    {
        var r = Matrix4x4.CreateFromQuaternion(rot);
        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = pos + Vector3.Transform(vertices[indices[i]], r);
            var b = pos + Vector3.Transform(vertices[indices[i + 1]], r);
            var c = pos + Vector3.Transform(vertices[indices[i + 2]], r);
            PushLine(a, b, color);
            PushLine(b, c, color);
            PushLine(c, a, color);
        }
    }

    public void Clear() => _lines.Clear();

    public void Render(Matrix4x4 view, Matrix4x4 proj)
    {
        if (_lines.Count == 0) return;

        var verts = new DebugVert[_lines.Count * 2];
        for (int i = 0; i < _lines.Count; i++)
        {
            var l = _lines[i];
            verts[i * 2 + 0] = new DebugVert { PX = l.From.X, PY = l.From.Y, PZ = l.From.Z, CR = l.Color.X, CG = l.Color.Y, CB = l.Color.Z };
            verts[i * 2 + 1] = new DebugVert { PX = l.To.X,   PY = l.To.Y,   PZ = l.To.Z,   CR = l.Color.X, CG = l.Color.Y, CB = l.Color.Z };
        }

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        fixed (DebugVert* vPtr = verts)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(DebugVert)), vPtr, BufferUsageARB.DynamicDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(DebugVert), (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(DebugVert), (void*)(3 * sizeof(float)));

        _gl.UseProgram(_shader);

        float[] viewArr = GetMatrixValues(view);
        float[] projArr = GetMatrixValues(proj);
        fixed (float* vp = viewArr, pp = projArr)
        {
            _gl.UniformMatrix4(_uView, 1, false, vp);
            _gl.UniformMatrix4(_uProj, 1, false, pp);
        }

        _gl.DrawArrays(GLEnum.Lines, 0, (uint)verts.Length);

        _gl.BindVertexArray(0);
    }

    private void PushBoxWire(Vector3[] p, Vector3 color)
    {
        PushLine(p[0], p[1], color);
        PushLine(p[1], p[2], color);
        PushLine(p[2], p[3], color);
        PushLine(p[3], p[0], color);
        PushLine(p[4], p[5], color);
        PushLine(p[5], p[6], color);
        PushLine(p[6], p[7], color);
        PushLine(p[7], p[4], color);
        PushLine(p[0], p[4], color);
        PushLine(p[1], p[5], color);
        PushLine(p[2], p[6], color);
        PushLine(p[3], p[7], color);
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        byte[] srcBytes = System.Text.Encoding.UTF8.GetBytes(source);
        fixed (byte* ptr = srcBytes)
        {
            int len = srcBytes.Length;
            _gl.ShaderSource(shader, 1, (byte**)&ptr, &len);
        }
        _gl.CompileShader(shader);
        _gl.GetShader(shader, GLEnum.CompileStatus, out int success);
        if (success == 0)
        {
            string info = _gl.GetShaderInfoLog(shader);
            Logger.Error($"DebugDrawer shader compile ({type}): {info}");
        }
        return shader;
    }

    private static float[] GetMatrixValues(Matrix4x4 mat) =>
    [
        mat.M11, mat.M12, mat.M13, mat.M14,
        mat.M21, mat.M22, mat.M23, mat.M24,
        mat.M31, mat.M32, mat.M33, mat.M34,
        mat.M41, mat.M42, mat.M43, mat.M44,
    ];
}
