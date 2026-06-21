using Blowtorch;
using Blowtorch.Model;
using System.Numerics;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;
using ImGuiNET;
using Fuse.Imgui;
using Fuse.Input;
using Fuse.AssetManagement;
using Fuse.Renderer;
using Fuse.Core;

unsafe
{
    // --- Init GLFW ---
    var glfw = Glfw.GetApi();
    if (!glfw.Init())
    {
        System.Console.Error.WriteLine("Failed to init GLFW");
        return;
    }

    glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
    glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
    glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);

    var handle = glfw.CreateWindow(1280, 800, "Blowtorch Map Editor", null, null);
    if (handle == null)
    {
        System.Console.Error.WriteLine("Failed to create window");
        glfw.Terminate();
        return;
    }

    glfw.MakeContextCurrent(handle);
    glfw.SwapInterval(1);

    var monitor = glfw.GetPrimaryMonitor();
    if (monitor != null)
    {
        var mode = glfw.GetVideoMode(monitor);
        glfw.GetMonitorPos(monitor, out int monitorX, out int monitorY);
        int x = monitorX + (mode->Width - 1280) / 2;
        int y = monitorY + (mode->Height - 800) / 2;
        glfw.SetWindowPos(handle, x, y);
    }

    var gl = GL.GetApi(glfw.GetProcAddress);
    gl.Enable(EnableCap.DepthTest);
    gl.Enable(EnableCap.CullFace);
    gl.CullFace(GLEnum.Back);

    // Init Fuse Input (feeds ImGui mouse/key state)
    Input.Init(glfw, handle);

    // Init ImGui
    var imgui = new ImGuiBackEnd(gl);
    imgui.Init();

    glfw.SetKeyCallback(handle, (w, key, scanCode, action, mods) =>
    {
        var imguiKey = GlfwKeyToImGuiKey(key);
        if (imguiKey != ImGuiKey.None)
            ImGui.GetIO().AddKeyEvent(imguiKey, action != InputAction.Release);
    });

    glfw.SetCharCallback(handle, (w, codepoint) =>
    {
        ImGui.GetIO().AddInputCharacter(codepoint);
    });

    glfw.SetScrollCallback(handle, (w, offsetX, offsetY) =>
    {
        ImGui.GetIO().AddMouseWheelEvent(0, (float)offsetY);
    });

    // --- Resolve Fuse resource path ---
    string fuseResPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        @"..\..\..\..\Fuse\res"));

    // --- Load map document ---
    string mapPath = Path.Combine(fuseResPath, "Maps", "default.json");
    var doc = MapDocument.Load(mapPath) ?? new MapDocument();

    // --- Init rendering assets ---
    var assets = new AssetManager(gl);
    var shader = assets.GetShader(
        Path.Combine(fuseResPath, "Shaders", "default.vert"),
        Path.Combine(fuseResPath, "Shaders", "default.frag"));

    // Build scene entities from MapDocument
    var scene = new Scene();
    var meshCache = new Dictionary<string, Mesh?>();
    var texCache = new Dictionary<string, uint>();

    foreach (var mapObj in doc.Objects)
    {
        Mesh? mesh = null;

        if (mapObj.IsModel && mapObj.Model != null)
        {
            string modelPath = Path.GetFullPath(Path.Combine(fuseResPath, mapObj.Model));
            if (!meshCache.TryGetValue(modelPath, out mesh))
            {
                var model = assets.GetModel(modelPath, mapObj.ModelScale);
                mesh = model?.Mesh;
                meshCache[modelPath] = mesh;
            }
        }
        else if (mapObj.Mesh != null)
        {
            if (!meshCache.TryGetValue(mapObj.Mesh, out mesh))
            {
                mesh = assets.GetMesh(mapObj.Mesh);
                meshCache[mapObj.Mesh] = mesh;
            }
        }

        if (mesh == null) continue;

        var entity = scene.Add(mesh, mapObj.Id);
        entity.MeshKey = mapObj.Mesh ?? mapObj.Model ?? "";
        entity.TexturePath = mapObj.Texture ?? "";
        entity.Visible = mapObj.Visible;
        entity.ModelScale = mapObj.ModelScale;

        if (mapObj.Body != null)
        {
            entity.Transform.Position = mapObj.Body.Position;
            entity.Transform.Rotation = mapObj.Body.Rotation;
        }

        // Pre-load texture if specified
        if (!string.IsNullOrEmpty(mapObj.Texture) && !texCache.ContainsKey(mapObj.Texture))
        {
            string rel = mapObj.Texture;
            if (rel.StartsWith("res/") || rel.StartsWith("res\\"))
                rel = rel[4..];
            string texPath = Path.GetFullPath(Path.Combine(fuseResPath, rel));
            if (File.Exists(texPath))
            {
                var texture = new Fuse.Renderer.Texture(gl, texPath);
                texCache[mapObj.Texture] = texture.ID;
            }
            else
            {
                texCache[mapObj.Texture] = 0;
                Logger.Warn($"Texture not found: {texPath}");
            }
        }
    }

    // Load a default texture for entities without one
    uint defaultTex = 0;
    string crateTexPath = Path.Combine(fuseResPath, "Textures", "dev_measurecrate01.bmp");
    if (File.Exists(crateTexPath))
    {
        var crateTex = new Fuse.Renderer.Texture(gl, crateTexPath);
        defaultTex = crateTex.ID;
    }

    // --- Viewport FBO ---
    uint vpFbo = gl.GenFramebuffer();
    uint vpColorTex = gl.GenTexture();
    uint vpDepthRbo = gl.GenRenderbuffer();
    int vpWidth = 800, vpHeight = 600;

    void CreateFbo(int w, int h)
    {
        if (vpColorTex != 0) gl.DeleteTexture(vpColorTex);
        if (vpDepthRbo != 0) gl.DeleteRenderbuffer(vpDepthRbo);

        vpColorTex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, vpColorTex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba, (uint)w, (uint)h, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        vpDepthRbo = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, vpDepthRbo);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)w, (uint)h);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, vpFbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, vpColorTex, 0);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, vpDepthRbo);

        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            Logger.Error("Viewport FBO incomplete");

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        vpWidth = w;
        vpHeight = h;
    }

    CreateFbo(800, 600);

    // Build grid mesh
    Mesh gridMesh = CreateGridMesh(gl, 20, 1.0f);

    // Viewport camera
    var vpCam = new ViewportCamera();
    Vector2 lastMouse = Vector2.Zero;
    bool isOrbiting = false;
    bool isPanning = false;

    // --- Main loop ---
    double lastTime = glfw.GetTime();
    bool showMapWindow = true;
    bool showJsonWindow = true;

    while (!glfw.WindowShouldClose(handle))
    {
        double now = glfw.GetTime();
        float dt = (float)(now - lastTime);
        lastTime = now;

        glfw.GetFramebufferSize(handle, out int fbWidth, out int fbHeight);
        gl.Viewport(0, 0, (uint)fbWidth, (uint)fbHeight);
        gl.ClearColor(0.12f, 0.12f, 0.14f, 1.0f);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        Input.Update();
        imgui.NewFrame(dt, fbWidth, fbHeight);

        // --- Render 3D viewport to FBO ---
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, vpFbo);
        gl.Viewport(0, 0, (uint)vpWidth, (uint)vpHeight);
        gl.ClearColor(0.25f, 0.25f, 0.3f, 1.0f);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        gl.Enable(EnableCap.DepthTest);
        gl.Enable(EnableCap.CullFace);

        float aspect = (float)vpWidth / vpHeight;
        var view = vpCam.ViewMatrix;
        var proj = vpCam.ProjectionMatrix(aspect);

        // Grid
        gl.Disable(EnableCap.CullFace);
        shader.Use();
        shader.SetMat4("uView", view);
        shader.SetMat4("uProj", proj);
        shader.SetBool("uUseTexture", false);
        shader.SetVec3("uColor", new Vector3(0.3f, 0.3f, 0.35f));
        shader.SetMat4("uModel", Matrix4x4.Identity);
        gridMesh.Draw();
        gl.Enable(EnableCap.CullFace);

        // Scene entities
        if (shader.ID != 0)
        {
            shader.Use();
            shader.SetVec3("uLightDir", Vector3.Normalize(new Vector3(1, 2, 1)));
            shader.SetVec3("uLightColor", new Vector3(1, 0.95f, 0.9f));
            shader.SetFloat("uAmbient", 0.2f);
            shader.SetMat4("uView", view);
            shader.SetMat4("uProj", proj);

            foreach (var entity in scene.Entities)
            {
                if (!entity.Visible || entity.Mesh == null) continue;

                shader.SetMat4("uModel", entity.Transform.Matrix);
                shader.SetVec3("uColor", Vector3.One);

                // Bind texture
                uint texId = 0;
                if (!string.IsNullOrEmpty(entity.TexturePath) && texCache.TryGetValue(entity.TexturePath, out var cachedTex))
                    texId = cachedTex;

                if (texId == 0 && defaultTex != 0)
                    texId = defaultTex;

                if (texId != 0)
                {
                    shader.SetBool("uUseTexture", true);
                    shader.SetInt("uTexture", 0);
                    gl.ActiveTexture(TextureUnit.Texture0);
                    gl.BindTexture(TextureTarget.Texture2D, texId);
                }
                else
                {
                    shader.SetBool("uUseTexture", false);
                }

                entity.Mesh.Draw();
            }
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, (uint)fbWidth, (uint)fbHeight);

        // --- ImGui windows ---
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Open...", "Ctrl+O")) { }
                if (ImGui.MenuItem("Save", "Ctrl+S")) { }
                ImGui.Separator();
                if (ImGui.MenuItem("Exit")) break;
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("View"))
            {
                ImGui.MenuItem("Map Objects", "", ref showMapWindow);
                ImGui.MenuItem("Raw JSON", "", ref showJsonWindow);
                ImGui.EndMenu();
            }
            ImGui.EndMainMenuBar();
        }

        // Viewport window - fills entire area below menu bar
        {
            var mainViewport = ImGui.GetMainViewport();
            var workPos = mainViewport.WorkPos;
            var workSize = mainViewport.WorkSize;
            ImGui.SetNextWindowPos(workPos);
            ImGui.SetNextWindowSize(workSize);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);

            if (ImGui.Begin("Scene Viewport", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBringToFrontOnFocus |
                ImGuiWindowFlags.NoNavFocus))
            {
                var vpSize = ImGui.GetContentRegionAvail();
                if (vpSize.X > 0 && vpSize.Y > 0 &&
                    ((int)vpSize.X != vpWidth || (int)vpSize.Y != vpHeight))
                {
                    CreateFbo((int)vpSize.X, (int)vpSize.Y);
                }

                ImGui.Image((IntPtr)vpColorTex, vpSize, new Vector2(0, 1), new Vector2(1, 0));

                // Viewport input
                if (ImGui.IsWindowHovered())
                {
                    // Scroll zoom
                    float scroll = ImGui.GetIO().MouseWheel;
                    if (scroll != 0)
                        vpCam.Zoom(scroll);

                    // Right-click orbit
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
                    {
                        var mouse = ImGui.GetIO().MousePos;
                        if (!isOrbiting)
                        {
                            isOrbiting = true;
                            lastMouse = mouse;
                        }
                        else
                        {
                            float dx = mouse.X - lastMouse.X;
                            float dy = mouse.Y - lastMouse.Y;
                            vpCam.Orbit(dx, dy);
                            lastMouse = mouse;
                        }
                    }
                    else
                    {
                        isOrbiting = false;
                    }

                    // Middle-click pan
                    if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
                    {
                        var mouse = ImGui.GetIO().MousePos;
                        if (!isPanning)
                        {
                            isPanning = true;
                            lastMouse = mouse;
                        }
                        else
                        {
                            float dx = mouse.X - lastMouse.X;
                            float dy = mouse.Y - lastMouse.Y;
                            vpCam.Pan(dx, dy);
                            lastMouse = mouse;
                        }
                    }
                    else
                    {
                        isPanning = false;
                    }
                }
                else
                {
                    isOrbiting = false;
                    isPanning = false;
                }
            }
            ImGui.End();
            ImGui.PopStyleVar(3);
        }

        if (showMapWindow)
            DrawMapWindow(doc);

        if (showJsonWindow)
            DrawJsonWindow(doc);

        imgui.Render();
        glfw.SwapBuffers(handle);
        glfw.PollEvents();
    }

    // --- Cleanup ---
    gl.DeleteFramebuffer(vpFbo);
    gl.DeleteTexture(vpColorTex);
    gl.DeleteRenderbuffer(vpDepthRbo);
    gridMesh.Dispose();
    foreach (var texId in texCache.Values)
    {
        if (texId != 0) gl.DeleteTexture(texId);
    }
    if (defaultTex != 0) gl.DeleteTexture(defaultTex);
    assets.Clear();
    imgui.Dispose();
    glfw.DestroyWindow(handle);
    glfw.Terminate();

    // --- Helper functions ---
    Mesh CreateGridMesh(GL gl, int size, float spacing)
    {
        int half = size / 2;
        var verts = new List<Vertex>();
        var idxs = new List<uint>();

        for (int i = -half; i <= half; i++)
        {
            float p = i * spacing;
            verts.Add(new Vertex { Position = new Vector3(p, 0, -half * spacing), Normal = Vector3.UnitY });
            verts.Add(new Vertex { Position = new Vector3(p, 0, half * spacing), Normal = Vector3.UnitY });
            verts.Add(new Vertex { Position = new Vector3(-half * spacing, 0, p), Normal = Vector3.UnitY });
            verts.Add(new Vertex { Position = new Vector3(half * spacing, 0, p), Normal = Vector3.UnitY });
        }

        for (uint i = 0; i < verts.Count; i++)
            idxs.Add(i);

        return new Mesh(gl, verts.ToArray(), idxs.ToArray());
    }

    void DrawMapWindow(MapDocument? doc)
    {
        ImGui.SetNextWindowSize(new Vector2(350, 500), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Map Objects", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        if (doc == null)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Failed to load map");
            ImGui.End();
            return;
        }

        ImGui.Text($"Version: {doc.Version}");
        ImGui.Text($"Objects: {doc.Objects.Count}");
        ImGui.Separator();

        if (doc.PlayerSpawn != null)
        {
            if (ImGui.CollapsingHeader("Player Spawn", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var sp = doc.PlayerSpawn;
                ImGui.Text($"Position: {sp.Position.X:F3}, {sp.Position.Y:F3}, {sp.Position.Z:F3}");
                ImGui.Text($"Yaw: {sp.Yaw:F3}   Pitch: {sp.Pitch:F3}");
            }
            ImGui.Separator();
        }

        ImGui.Text("Objects:");
        ImGui.Separator();

        for (int i = 0; i < doc.Objects.Count; i++)
        {
            var obj = doc.Objects[i];
            bool open = ImGui.TreeNodeEx($"##obj{i}", ImGuiTreeNodeFlags.FramePadding, $"{i}: {obj.Id}");

            ImGui.SameLine();
            ImGui.TextColored(obj.Visible ? new Vector4(1, 1, 1, 1) : new Vector4(0.5f, 0.5f, 0.5f, 1),
                obj.IsModel ? obj.Model : obj.Mesh);

            if (!open) continue;

            ImGui.Text($"Visible: {(obj.Visible ? "yes" : "no")}");
            if (obj.IsModel)
            {
                ImGui.Text($"Model: {obj.Model}");
                if (obj.ModelScale != 1.0f)
                    ImGui.Text($"Scale: {obj.ModelScale}");
            }
            else
            {
                ImGui.Text($"Mesh: {obj.Mesh}");
            }
            if (!string.IsNullOrEmpty(obj.Texture))
                ImGui.Text($"Texture: {obj.Texture}");
            if (!string.IsNullOrEmpty(obj.Interactable))
                ImGui.Text($"Interactable: {obj.Interactable}");

            if (obj.Body != null)
            {
                var body = obj.Body;
                if (ImGui.TreeNodeEx("Body", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Text($"Shape: {body.Shape}");
                    ImGui.Text($"Pos: {body.Position.X:F3}, {body.Position.Y:F3}, {body.Position.Z:F3}");
                    ImGui.Text($"Rot: w={body.Rotation.W:F3} x={body.Rotation.X:F3} y={body.Rotation.Y:F3} z={body.Rotation.Z:F3}");
                    ImGui.Text($"Mass: {body.Mass}  Friction: {body.Friction}  Restitution: {body.Restitution}");

                    switch (body.Shape)
                    {
                        case MapShapeType.Box when body.HalfExtents.HasValue:
                            var he = body.HalfExtents.Value;
                            ImGui.Text($"HalfExtents: {he.X}, {he.Y}, {he.Z}");
                            break;
                        case MapShapeType.Sphere when body.Radius.HasValue:
                            ImGui.Text($"Radius: {body.Radius.Value}");
                            break;
                        case MapShapeType.Capsule when body.Radius.HasValue && body.Height.HasValue:
                            ImGui.Text($"Radius: {body.Radius.Value}  Height: {body.Height.Value}");
                            break;
                        case MapShapeType.Plane when body.Normal.HasValue && body.Distance.HasValue:
                            var n = body.Normal.Value;
                            ImGui.Text($"Normal: {n.X}, {n.Y}, {n.Z}  Dist: {body.Distance.Value}");
                            break;
                    }

                    ImGui.TreePop();
                }
            }

            ImGui.TreePop();
        }

        ImGui.End();
    }

    void DrawJsonWindow(MapDocument? doc)
    {
        ImGui.SetNextWindowSize(new Vector2(450, 500), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Raw JSON", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        if (doc != null)
        {
            string json = doc.Serialize();
            ImGui.InputTextMultiline("##json", ref json, (uint)json.Length,
                new Vector2(-1, -1), ImGuiInputTextFlags.ReadOnly);
        }
        else
        {
            ImGui.Text("No map loaded");
        }

        ImGui.End();
    }

    static ImGuiKey GlfwKeyToImGuiKey(Keys key)
    {
        int k = (int)key;
        if (k >= KeyCodes.A && k <= KeyCodes.Z)
            return ImGuiKey.A + (k - KeyCodes.A);
        if (k >= KeyCodes.D0 && k <= KeyCodes.D9)
            return ImGuiKey._0 + (k - KeyCodes.D0);

        return k switch
        {
            KeyCodes.Enter => ImGuiKey.Enter,
            KeyCodes.Escape => ImGuiKey.Escape,
            KeyCodes.Backspace => ImGuiKey.Backspace,
            KeyCodes.Tab => ImGuiKey.Tab,
            KeyCodes.Space => ImGuiKey.Space,
            KeyCodes.Delete => ImGuiKey.Delete,
            KeyCodes.Insert => ImGuiKey.Insert,
            KeyCodes.Up => ImGuiKey.UpArrow,
            KeyCodes.Down => ImGuiKey.DownArrow,
            KeyCodes.Left => ImGuiKey.LeftArrow,
            KeyCodes.Right => ImGuiKey.RightArrow,
            KeyCodes.Home => ImGuiKey.Home,
            KeyCodes.End => ImGuiKey.End,
            KeyCodes.PageUp => ImGuiKey.PageUp,
            KeyCodes.PageDown => ImGuiKey.PageDown,
            KeyCodes.LeftShift => ImGuiKey.LeftShift,
            KeyCodes.LeftControl => ImGuiKey.LeftCtrl,
            KeyCodes.LeftAlt => ImGuiKey.LeftAlt,
            KeyCodes.LeftSuper => ImGuiKey.LeftSuper,
            KeyCodes.RightShift => ImGuiKey.RightShift,
            KeyCodes.RightControl => ImGuiKey.RightCtrl,
            KeyCodes.RightAlt => ImGuiKey.RightAlt,
            KeyCodes.RightSuper => ImGuiKey.RightSuper,
            _ => ImGuiKey.None
        };
    }
}