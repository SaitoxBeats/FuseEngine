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

    public static LoadedModel? Load(GL gl, string path)
    {
        string cleanPath = path;
        int meshIndex = -1;
        int hashIdx = path.IndexOf('#');
        if (hashIdx != -1)
        {
            cleanPath = path.Substring(0, hashIdx);
            int.TryParse(path.Substring(hashIdx + 1), out meshIndex);
        }

        if (!System.IO.File.Exists(cleanPath))
        {
            Logger.Error($"Model file not found: {cleanPath}");
            return null;
        }

        var scene = Api.ImportFile(cleanPath,
            (uint)(PostProcessSteps.Triangulate | PostProcessSteps.GenerateSmoothNormals | PostProcessSteps.TransformUVCoords));

        if (scene == null || scene->MRootNode == null || scene->MNumMeshes == 0)
        {
            Logger.Error($"Failed to load model: {cleanPath}");
            return null;
        }

        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        var collVerts = new List<Vector3>();

        int startMesh = 0;
        int endMesh = (int)scene->MNumMeshes;
        if (meshIndex >= 0 && meshIndex < (int)scene->MNumMeshes)
        {
            startMesh = meshIndex;
            endMesh = meshIndex + 1;
        }

        for (int m = startMesh; m < endMesh; m++)
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

    public static LoadedModel?[] LoadAllSubmeshes(GL gl, string cleanPath)
    {
        if (!System.IO.File.Exists(cleanPath))
        {
            Logger.Error($"Model file not found: {cleanPath}");
            return [];
        }

        var scene = Api.ImportFile(cleanPath,
            (uint)(PostProcessSteps.Triangulate | PostProcessSteps.GenerateSmoothNormals | PostProcessSteps.TransformUVCoords));

        if (scene == null || scene->MRootNode == null || scene->MNumMeshes == 0)
        {
            Logger.Error($"Failed to load model submeshes: {cleanPath}");
            return [];
        }

        int count = (int)scene->MNumMeshes;
        var results = new LoadedModel?[count];

        for (int m = 0; m < count; m++)
        {
            var mesh = scene->MMeshes[m];
            var vertices = new List<Vertex>();
            var indices = new List<uint>();
            var collVerts = new List<Vector3>();

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
                    indices.Add(face.MIndices[j]);
            }

            var resultMesh = new Mesh(gl, [.. vertices], [.. indices]);
            results[m] = new LoadedModel
            {
                Mesh = resultMesh,
                CollVertices = [.. collVerts],
                CollIndices = [.. indices],
            };
        }

        Api.ReleaseImport(scene);
        Logger.Asset($"Model loaded all submeshes: {cleanPath} ({count} meshes)");

        return results;
    }
}
