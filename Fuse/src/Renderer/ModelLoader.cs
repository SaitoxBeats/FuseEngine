using System.Numerics;
using Silk.NET.OpenGL;
using Silk.NET.Assimp;
using Fuse.Core;

namespace Fuse.Renderer;

public class LoadedModel : IDisposable
{
    public Mesh? Mesh { get; set; }
    public Vector3[] CollVertices { get; set; } = [];
    public uint[] CollIndices { get; set; } = [];

    public void Dispose()
    {
        Mesh?.Dispose();
    }
}

public static unsafe class ModelLoader
{
    private static Assimp? s_assimp;

    private static Assimp Api => s_assimp ??= Assimp.GetApi();

    public static LoadedModel? Load(GL gl, string path, float scale = 1.0f)
    {
        if (!System.IO.File.Exists(path))
        {
            Logger.Error($"Model file not found: {path}");
            return null;
        }

        var scene = Api.ImportFile(path,
            (uint)(PostProcessSteps.Triangulate | PostProcessSteps.GenerateSmoothNormals));

        if (scene == null || scene->MRootNode == null || scene->MNumMeshes == 0)
        {
            Logger.Error($"Failed to load model: {path}");
            return null;
        }

        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        var collVerts = new List<Vector3>();

        for (int m = 0; m < scene->MNumMeshes; m++)
        {
            var mesh = scene->MMeshes[m];
            uint baseIndex = (uint)vertices.Count;

            for (int i = 0; i < mesh->MNumVertices; i++)
            {
                var pos = new Vector3(
                    mesh->MVertices[i].X,
                    mesh->MVertices[i].Y,
                    mesh->MVertices[i].Z);

                var uv = Vector2.Zero;
                if (mesh->MTextureCoords[0] != null)
                    uv = new Vector2(mesh->MTextureCoords[0][i].X, mesh->MTextureCoords[0][i].Y);

                var normal = new Vector3(0, 1, 0);
                if (mesh->MNormals != null)
                    normal = new Vector3(mesh->MNormals[i].X, mesh->MNormals[i].Y, mesh->MNormals[i].Z);

                vertices.Add(new Vertex { Position = pos, TexCoord = uv, Normal = normal });
                collVerts.Add(pos);
            }

            for (int i = 0; i < mesh->MNumFaces; i++)
            {
                var face = mesh->MFaces[i];
                for (int j = 0; j < face.MNumIndices; j++)
                    indices.Add(face.MIndices[j] + baseIndex);
            }
        }

        Api.ReleaseImport(scene);

        var resultMesh = new Mesh(gl, [.. vertices], [.. indices]);
        Logger.Asset($"Model loaded: {path} ({vertices.Count} verts, {indices.Count} indices)");

        return new LoadedModel
        {
            Mesh = resultMesh,
            CollVertices = [.. collVerts],
            CollIndices = [.. indices],
        };
    }
}
