using Silk.NET.OpenGL;
using StbImageSharp;
using Fuse.Core;

namespace Fuse.Renderer;

public unsafe class Texture : IDisposable
{
    private readonly GL _gl;
    private readonly uint _id;
    private readonly int _width;
    private readonly int _height;

    public Texture(GL gl, string filepath)
    {
        _gl = gl;

        if (!File.Exists(filepath))
        {
            Logger.Error($"Texture file not found: {filepath}");
            return;
        }

        byte[] fileData = File.ReadAllBytes(filepath);
        ImageResult image;

        try
        {
            image = ImageResult.FromMemory(fileData, ColorComponents.Default);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load texture: {filepath} ({ex.Message})");
            return;
        }

        _width = image.Width;
        _height = image.Height;

        int channels = image.Comp is ColorComponents.RedGreenBlueAlpha ? 4 : 3;
        var format = channels == 4 ? PixelFormat.Rgba : PixelFormat.Rgb;
        var internalFormat = channels == 4 ? InternalFormat.Rgba : InternalFormat.Rgb;

        // Flip vertically (stb_image default is top-left, OpenGL expects bottom-left)
        int rowSize = _width * channels;
        byte[] flipped = new byte[image.Data.Length];
        for (int y = 0; y < _height; y++)
            System.Buffer.BlockCopy(image.Data, y * rowSize, flipped, (_height - 1 - y) * rowSize, rowSize);
        image.Data = flipped;

        _id = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _id);

        fixed (byte* dataPtr = image.Data)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, (int)internalFormat, (uint)_width, (uint)_height, 0,
                format, PixelType.UnsignedByte, dataPtr);
        }

        gl.GenerateMipmap(TextureTarget.Texture2D);

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.NearestMipmapNearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);

        Logger.Info($"Texture loaded: {filepath} ({_width}x{_height})");
    }

    public void Dispose()
    {
        _gl.DeleteTexture(_id);
    }

    public uint ID => _id;
    public int Width => _width;
    public int Height => _height;

    public void Bind(uint slot = 0)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + (int)slot);
        _gl.BindTexture(TextureTarget.Texture2D, _id);
    }
}
