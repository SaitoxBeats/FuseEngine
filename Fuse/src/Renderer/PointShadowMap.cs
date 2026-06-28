using Silk.NET.OpenGL;
using System;

namespace Fuse.Renderer;

public unsafe class PointShadowMap : IDisposable
{
    private readonly GL _gl;
    public uint FBO { get; private set; }
    public uint TextureID { get; private set; }
    public uint Size { get; private set; }

    public PointShadowMap(GL gl, uint size = 512)
    {
        _gl = gl;
        Size = size;

        FBO = _gl.GenFramebuffer();
        TextureID = _gl.GenTexture();

        _gl.BindTexture(TextureTarget.TextureCubeMap, TextureID);
        for (int i = 0; i < 6; i++)
        {
            _gl.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, InternalFormat.DepthComponent32f, Size, Size, 0, PixelFormat.DepthComponent, PixelType.Float, null);
        }

        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, FBO);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.TextureCubeMapPositiveX, TextureID, 0);
        _gl.DrawBuffer(DrawBufferMode.None);
        _gl.ReadBuffer(ReadBufferMode.None);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void BindForWriting(int face)
    {
        _gl.Viewport(0, 0, Size, Size);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, FBO);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.TextureCubeMapPositiveX + face, TextureID, 0);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
    }

    public void BindForReading(TextureUnit unit)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.TextureCubeMap, TextureID);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(FBO);
        _gl.DeleteTexture(TextureID);
    }
}
