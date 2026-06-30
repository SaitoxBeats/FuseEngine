using Silk.NET.OpenGL;
using System;

namespace Fuse.Renderer;

public unsafe class ShadowMap : IDisposable
{
    private readonly GL _gl;
    public uint FBO { get; private set; }
    public uint DepthTexture { get; private set; }
    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public uint Layers { get; private set; }

    public ShadowMap(GL gl, uint width = 2048, uint height = 2048, uint layers = 3)
    {
        _gl = gl;
        Width = width;
        Height = height;
        Layers = layers;

        // Create FBO
        FBO = _gl.GenFramebuffer();
        
        // Create depth texture array
        DepthTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, DepthTexture);
        
        _gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.DepthComponent32f, Width, Height, Layers, 0, PixelFormat.DepthComponent, PixelType.Float, null);
        
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc, (int)DepthFunction.Lequal);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        
        float[] borderColor = { 1.0f, 1.0f, 1.0f, 1.0f };
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBorderColor, borderColor);

        // Attach depth texture to FBO
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, FBO);
        _gl.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, DepthTexture, 0);
        
        _gl.DrawBuffer(DrawBufferMode.None);
        _gl.ReadBuffer(ReadBufferMode.None);
        
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void BindForWriting(int layer)
    {
        _gl.Viewport(0, 0, Width, Height);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, FBO);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, DepthTexture, 0, layer);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
    }

    public void BindForReading(TextureUnit unit)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2DArray, DepthTexture);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(FBO);
        _gl.DeleteTexture(DepthTexture);
    }
}
