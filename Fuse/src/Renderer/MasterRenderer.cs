using System.Linq;
using System.Numerics;
using Silk.NET.OpenGL;
using Fuse.Core;

namespace Fuse.Renderer;

public class MasterRenderer
{
    private readonly GL _gl;
    private int _scrWidth, _scrHeight;

    // Shaders
    private Shader _shader = null!;
    private Shader _skyboxShader = null!;
    private Shader _shadowShader = null!;
    private ShadowMap _shadowMap = null!;

    // Textures
    private Texture _crateTexture = null!;
    private Texture _skyboxTexture = null!;
    private Vector3 _skyboxDominantColor = Vector3.One;
    public Texture SkyboxTexture => _skyboxTexture;
    public void SetSkyboxTexture(Texture tex)
    {
        _skyboxTexture = tex;
        if (_skyboxTexture.ID != 0)
        {
            _skyboxDominantColor = _skyboxTexture.GetDominantColor();
            _gl.BindTexture(TextureTarget.Texture2D, _skyboxTexture.ID);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        }
    }

    // Meshes
    private Mesh _skyBoxCubeMesh = null!;

    // Shadow Settings
    public float ShadowBiasFactor = 0.0f;
    public float ShadowBiasBase = 0.0000f;
    public float ShadowNearPlane = 1.0f;
    public float ShadowFarPlane = 300.0f;
    public float ShadowSpread = 2.0f;
    public bool ShadowsEnabled = false;

    public MasterRenderer(GL gl)
    {
        _gl = gl;
    }

    public void Init(AssetManagement.AssetManager assets, int width, int height)
    {
        _scrWidth = width;
        _scrHeight = height;

        _shader = assets.GetShader($"{Fuse.ResPath.Path}/Shaders/default.vert", $"{Fuse.ResPath.Path}/Shaders/default.frag")!;
        _skyboxShader = assets.GetShader($"{Fuse.ResPath.Path}/Shaders/skybox.vert", $"{Fuse.ResPath.Path}/Shaders/skybox.frag")!;
        _shadowShader = assets.GetShader($"{Fuse.ResPath.Path}/Shaders/shadow.vert", $"{Fuse.ResPath.Path}/Shaders/shadow.frag")!;
        
        _shadowMap = new ShadowMap(_gl, 256, 256);
        
        _skyBoxCubeMesh = assets.GetMesh("cube")!;
        
        _crateTexture = assets.GetTexture($"{Fuse.ResPath.Path}/Textures/dev_measurecrate01.bmp");
        _skyboxTexture = assets.GetTexture($"{Fuse.ResPath.Path}/Textures/skybox_1.png");
        
        if (_skyboxTexture.ID != 0)
        {
            _skyboxDominantColor = _skyboxTexture.GetDominantColor();
            _gl.BindTexture(TextureTarget.Texture2D, _skyboxTexture.ID);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        }
    }

    public void Resize(int width, int height)
    {
        _scrWidth = width;
        _scrHeight = height;
    }

    public void RenderFrame(Scene scene, Camera camera, Physics.PhysicsWorld physics)
    {
        float aspect = (float)_scrWidth / _scrHeight;
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix(aspect);

        // --- 1. Shadow Pass ---
        Vector3 lightDir = Vector3.Normalize(new Vector3(1, 2, 1));
        
        float[] cascadeLevels = { ShadowFarPlane * 0.05f, ShadowFarPlane * 0.2f, ShadowFarPlane };
        Matrix4x4[] lightSpaceMatrices = new Matrix4x4[3];

        if (_shadowShader != null && _shadowShader.ID != 0 && ShadowsEnabled)
        {
            _gl.Enable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.CullFace);
            _gl.CullFace(GLEnum.Front); // Fix peter-panning

            _shadowShader.Use();
            
            for (int i = 0; i < 3; i++)
            {
                float near = i == 0 ? camera.NearPlane : cascadeLevels[i - 1];
                float far = cascadeLevels[i];
                lightSpaceMatrices[i] = GetLightSpaceMatrix(camera, aspect, near, far, lightDir);
                
                _shadowShader.SetMat4("uLightSpaceMatrix", lightSpaceMatrices[i]);
                _shadowMap.BindForWriting(i);
                scene.Render(_shadowShader, physics, _crateTexture, lightSpaceMatrices[i]); 
            }
        }

        // --- 2. Regular Render Pass ---
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)_scrWidth, (uint)_scrHeight);
        _gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Skybox
        if (_skyboxShader.ID != 0 && _skyboxTexture.ID != 0)
        {
            _gl.DepthMask(false);
            _gl.DepthFunc(DepthFunction.Lequal);
            _gl.CullFace(GLEnum.Front);

            _skyboxShader.Use();
            var skyView = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromRotationMatrix(view));
            _skyboxShader.SetMat4("uView", skyView);
            _skyboxShader.SetMat4("uProj", proj);
            _skyboxShader.SetInt("uSkyTexture", 0);
            _skyboxTexture.Bind(0);
            _skyBoxCubeMesh.Draw();

            _gl.CullFace(GLEnum.Back);
            _gl.DepthFunc(DepthFunction.Less);
            _gl.DepthMask(true);
        }

        // World geometry
        if (_shader.ID != 0)
        {
            _gl.Enable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.CullFace);
            _gl.CullFace(GLEnum.Back);
            _gl.DepthFunc(DepthFunction.Less);
            _shader.Use();
            _shader.SetVec3("uLightDir", lightDir);

            float lum = _skyboxDominantColor.X * 0.2126f + _skyboxDominantColor.Y * 0.7152f + _skyboxDominantColor.Z * 0.0722f;
            _shader.SetFloat("uAmbient", 0.02f + 0.28f * lum);
            var tinted = Vector3.Lerp(new Vector3(1, 0.95f, 0.9f), _skyboxDominantColor, 0.5f);
            _shader.SetVec3("uLightColor", tinted * (0.5f + 0.5f * lum));
            
            _shader.SetMat4("uView", view);
            _shader.SetMat4("uProj", proj);
            
            for (int i = 0; i < 3; i++)
            {
                _shader.SetMat4($"uLightSpaceMatrices[{i}]", lightSpaceMatrices[i]);
                _shader.SetFloat($"uCascadePlaneDistances[{i}]", cascadeLevels[i]);
            }
            
            _shader.SetBool("uEnableShadows", ShadowsEnabled);
            
            _shader.SetFloat("uShadowBiasFactor", ShadowBiasFactor);
            _shader.SetFloat("uShadowBiasBase", ShadowBiasBase);
            _shader.SetFloat("uShadowSpread", ShadowSpread);
            
            _shader.SetVec3("uColor", Vector3.One);
            _shader.SetBool("uUseTexture", true);
            
            _shader.SetInt("uTexture", 0);
            _shader.SetInt("uShadowMap", 1);
            _shadowMap.BindForReading(TextureUnit.Texture1);

            _shader.SetVec3("uCameraPos", camera.Position);

            var pointLights = scene.Lights.Where(l => l.Enabled && l.Type == LightType.Point).Take(8).ToList();
            _shader.SetInt("uPointLightCount", pointLights.Count);
            for (int i = 0; i < pointLights.Count; i++)
            {
                var l = pointLights[i];
                _shader.SetVec3($"uPointLights[{i}].position", l.Position);
                _shader.SetVec3($"uPointLights[{i}].color", l.Color * l.Intensity);
                _shader.SetFloat($"uPointLights[{i}].radius", l.Radius);
            }

            var spotLights = scene.Lights.Where(l => l.Enabled && l.Type == LightType.Spot).Take(4).ToList();
            _shader.SetInt("uSpotLightCount", spotLights.Count);
            for (int i = 0; i < spotLights.Count; i++)
            {
                var l = spotLights[i];
                _shader.SetVec3($"uSpotLights[{i}].position", l.Position);
                _shader.SetVec3($"uSpotLights[{i}].direction", Vector3.Normalize(l.Direction));
                _shader.SetVec3($"uSpotLights[{i}].color", l.Color * l.Intensity);
                _shader.SetFloat($"uSpotLights[{i}].radius", l.Radius);
                _shader.SetFloat($"uSpotLights[{i}].innerCos", l.InnerCos);
                _shader.SetFloat($"uSpotLights[{i}].outerCos", l.OuterCos);
            }

            scene.Render(_shader, physics, _crateTexture);
        }
    }

    private Vector4[] GetFrustumCornersWorldSpace(Matrix4x4 proj, Matrix4x4 view)
    {
        Matrix4x4.Invert(view * proj, out Matrix4x4 invVP);
        
        Vector4[] corners = new Vector4[8];
        int i = 0;
        for (int x = 0; x < 2; ++x)
        {
            for (int y = 0; y < 2; ++y)
            {
                for (int z = 0; z < 2; ++z)
                {
                    Vector4 pt = Vector4.Transform(new Vector4(
                        2.0f * x - 1.0f,
                        2.0f * y - 1.0f,
                        2.0f * z - 1.0f,
                        1.0f), invVP);
                    corners[i++] = pt / pt.W;
                }
            }
        }
        return corners;
    }

    private Matrix4x4 GetLightSpaceMatrix(Camera camera, float aspect, float near, float far, Vector3 lightDir)
    {
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(
            float.DegreesToRadians(camera.FOV), aspect, near, far);
        var view = camera.GetViewMatrix();
        
        var corners = GetFrustumCornersWorldSpace(proj, view);
        
        Vector3 center = Vector3.Zero;
        foreach (var v in corners)
        {
            center += new Vector3(v.X, v.Y, v.Z);
        }
        center /= corners.Length;

        var lightView = Matrix4x4.CreateLookAt(center + lightDir, center, Vector3.UnitY);
        
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        
        foreach (var v in corners)
        {
            var trf = Vector4.Transform(v, lightView);
            minX = MathF.Min(minX, trf.X);
            maxX = MathF.Max(maxX, trf.X);
            minY = MathF.Min(minY, trf.Y);
            maxY = MathF.Max(maxY, trf.Y);
            minZ = MathF.Min(minZ, trf.Z);
            maxZ = MathF.Max(maxZ, trf.Z);
        }
        
        float zMult = 10.0f;
        if (minZ < 0) minZ *= zMult; else minZ /= zMult;
        if (maxZ < 0) maxZ /= zMult; else maxZ *= zMult;

        var lightProjection = Matrix4x4.CreateOrthographicOffCenter(minX, maxX, minY, maxY, minZ, maxZ);
        return lightView * lightProjection;
    }
}
