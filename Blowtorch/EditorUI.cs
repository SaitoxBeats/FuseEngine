using System;
using System.IO;
using System.Numerics;
using System.Linq;
using ImGuiNET;
using Blowtorch.Model;
using Fuse.Renderer;
using Fuse.Core;

namespace Blowtorch;

public class EditorUI
{
    private bool _showMapWindow = true;
    private bool _showJsonWindow = false;

    // Snapping
    private bool _snapEnabled = false;
    private float _snapGrid = 0.5f;
    private float _snapAngle = 15.0f;

    // Undo/Redo state
    private string _preEditState = "";
    private string _frameBeginState = "";

    // Selection
    public enum GizmoOperation { Translate, Rotate, Scale }
    private GizmoOperation _gizmoOperation = GizmoOperation.Translate;
    private MapObject? _selectedObject;
    private bool _wasUsingGizmo = false;

    public bool ShowMapWindow => _showMapWindow;
    public bool ShowJsonWindow => _showJsonWindow;

    public void Draw(EditorWindow window, EditorViewport viewport, EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        _frameBeginState = sceneService.Document.Serialize();

        HandleKeyboardShortcuts(history);

        DrawMenuBar(window, sceneService, assetService, history);
        DrawViewportWindow(viewport, sceneService, assetService, history);

        if (_showMapWindow)
            DrawMapWindow(sceneService, assetService, history);

        if (_showJsonWindow)
            DrawJsonWindow(sceneService.Document);
    }

    private void HandleKeyboardShortcuts(CommandHistory history)
    {
        var io = ImGui.GetIO();
        if (io.KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Z)) history.Undo();
            if (ImGui.IsKeyPressed(ImGuiKey.Y)) history.Redo();
        }
    }

    private void DrawMenuBar(EditorWindow window, EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Open...", "Ctrl+O")) { }
                if (ImGui.MenuItem("Save", "Ctrl+S"))
                {
                    sceneService.SaveMap();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Exit")) window.Close();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z")) history.Undo();
                if (ImGui.MenuItem("Redo", "Ctrl+Y")) history.Redo();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("View"))
            {
                ImGui.MenuItem("Map Objects", "", ref _showMapWindow);
                ImGui.MenuItem("Raw JSON", "", ref _showJsonWindow);
                ImGui.EndMenu();
            }
            ImGui.EndMainMenuBar();
        }
    }

    private void DrawViewportWindow(EditorViewport viewport, EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
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
            if (ImGui.RadioButton("Translate (W)", _gizmoOperation == GizmoOperation.Translate)) _gizmoOperation = GizmoOperation.Translate;
            ImGui.SameLine();
            if (ImGui.RadioButton("Rotate (E)", _gizmoOperation == GizmoOperation.Rotate)) _gizmoOperation = GizmoOperation.Rotate;
            ImGui.SameLine();
            if (ImGui.RadioButton("Scale (R)", _gizmoOperation == GizmoOperation.Scale)) _gizmoOperation = GizmoOperation.Scale;

            if (!ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                if (ImGui.IsKeyPressed(ImGuiKey.W)) _gizmoOperation = GizmoOperation.Translate;
                if (ImGui.IsKeyPressed(ImGuiKey.E)) _gizmoOperation = GizmoOperation.Rotate;
                if (ImGui.IsKeyPressed(ImGuiKey.R)) _gizmoOperation = GizmoOperation.Scale;
            }

            var vpPos = ImGui.GetWindowPos();
            var vpSize = ImGui.GetContentRegionAvail();
            
            if (vpSize.X > 0 && vpSize.Y > 0 &&
                ((int)vpSize.X != viewport.Width || (int)vpSize.Y != viewport.Height))
            {
                viewport.CreateFbo((int)vpSize.X, (int)vpSize.Y);
            }

            ImGui.Image((IntPtr)viewport.ColorTexture, vpSize, new Vector2(0, 1), new Vector2(1, 0));

            if (ImGui.IsWindowHovered() && !EditorGizmo.IsUsing())
            {
                // We will handle picking and input after gizmo
            }

            if (_selectedObject != null && _selectedObject.Body != null && sceneService.Document.Objects.Contains(_selectedObject))
            {
                var body = _selectedObject.Body;
                var view = viewport.Camera.ViewMatrix;
                var proj = viewport.Camera.ProjectionMatrix(vpSize.X / vpSize.Y);

                bool isUsing = EditorGizmo.IsUsing();
                if (isUsing && !_wasUsingGizmo) _preEditState = _frameBeginState;

                float snapVal = _snapEnabled ? _snapGrid : 0.0f;
                float angleSnap = _snapEnabled ? _snapAngle : 0.0f;
                bool changed = false;

                if (_gizmoOperation == GizmoOperation.Translate)
                {
                    if (EditorGizmo.ManipulateTranslation(body.Position, view, proj, vpPos, vpSize, out Vector3 newPos, snapVal))
                    {
                        body.Position = newPos;
                        changed = true;
                    }
                }
                else if (_gizmoOperation == GizmoOperation.Rotate)
                {
                    if (EditorGizmo.ManipulateRotation(body.Position, body.Rotation, view, proj, vpPos, vpSize, out Quaternion newRot, angleSnap))
                    {
                        body.Rotation = Quaternion.Normalize(newRot);
                        changed = true;
                    }
                }
                else if (_gizmoOperation == GizmoOperation.Scale)
                {
                    Vector3 currentScale = Vector3.One;
                    if (body.Shape == MapShapeType.Box && body.HalfExtents.HasValue) currentScale = body.HalfExtents.Value * 2.0f;
                    else if (body.Shape == MapShapeType.Sphere && body.Radius.HasValue) currentScale = new Vector3(body.Radius.Value * 2.0f);
                    else currentScale = new Vector3(_selectedObject.ModelScale);

                    if (EditorGizmo.ManipulateScale(body.Position, currentScale, view, proj, vpPos, vpSize, out Vector3 newScale, snapVal))
                    {
                        if (body.Shape == MapShapeType.Box) body.HalfExtents = newScale * 0.5f;
                        else if (body.Shape == MapShapeType.Sphere) body.Radius = newScale.X * 0.5f;
                        else _selectedObject.ModelScale = MathF.Max(newScale.X, MathF.Max(newScale.Y, newScale.Z));
                        changed = true;
                    }
                }

                if (changed)
                {
                    var entity = sceneService.Scene.Entities.FirstOrDefault(e => e.Id == _selectedObject.Id);
                    if (entity != null)
                    {
                        entity.Transform.Position = body.Position;
                        entity.Transform.Rotation = body.Rotation;
                        
                        if (body.Shape == MapShapeType.Box && body.HalfExtents.HasValue)
                            entity.Transform.Scale = body.HalfExtents.Value * 2.0f;
                        else if (body.Shape == MapShapeType.Sphere && body.Radius.HasValue)
                            entity.Transform.Scale = new Vector3(body.Radius.Value * 2.0f);
                        else
                            entity.Transform.Scale = new Vector3(_selectedObject.ModelScale);
                    }
                }

                if (!isUsing && _wasUsingGizmo)
                {
                    var postEditState = sceneService.Document.Serialize();
                    history.PushCommand(new SnapshotCommand(sceneService, assetService, _preEditState, postEditState));
                }
                _wasUsingGizmo = isUsing;
            }

            if (ImGui.IsWindowHovered() && !EditorGizmo.IsUsing() && !EditorGizmo.IsHovered)
            {
                viewport.HandleInput(ImGui.GetIO(), ImGui.GetIO().DeltaTime);

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    EditorGizmo.GetMouseRay(ImGui.GetIO().MousePos, viewport.Camera.ViewMatrix, viewport.Camera.ProjectionMatrix(vpSize.X / vpSize.Y), vpPos, vpSize, out Vector3 rayOrigin, out Vector3 rayDir);
                    
                    MapObject? hitObj = PickObject(rayOrigin, rayDir, sceneService);
                    if (hitObj != null)
                    {
                        _selectedObject = hitObj;
                    }
                }
            }
        }
        ImGui.End();
        ImGui.PopStyleVar(3);
    }

    private MapObject? PickObject(Vector3 rayOrigin, Vector3 rayDir, EditorSceneService sceneService)
    {
        MapObject? closestObj = null;
        float closestDist = float.MaxValue;

        foreach(var obj in sceneService.Document.Objects)
        {
            if (obj.Body == null || !obj.Visible) continue;
            
            float dist = float.MaxValue;
            bool hit = false;
            
            Matrix4x4 modelInv;
            Matrix4x4.Invert(Matrix4x4.CreateFromQuaternion(obj.Body.Rotation) * Matrix4x4.CreateTranslation(obj.Body.Position), out modelInv);
            Vector3 localOrigin = Vector3.Transform(rayOrigin, modelInv);
            Vector3 localDir = Vector3.Normalize(Vector3.TransformNormal(rayDir, modelInv));

            if (obj.Body.Shape == MapShapeType.Sphere && obj.Body.Radius.HasValue)
            {
                hit = RaySphereIntersect(localOrigin, localDir, Vector3.Zero, obj.Body.Radius.Value, out dist);
            }
            else if (obj.Body.Shape == MapShapeType.Box && obj.Body.HalfExtents.HasValue)
            {
                hit = RayAABBIntersect(localOrigin, localDir, -obj.Body.HalfExtents.Value, obj.Body.HalfExtents.Value, out dist);
            }
            else 
            {
                float r = 1.0f;
                if (obj.Body.Shape == MapShapeType.Capsule && obj.Body.Height.HasValue) r = obj.Body.Height.Value;
                if (obj.IsModel) r = obj.ModelScale * 1.5f;
                hit = RaySphereIntersect(localOrigin, localDir, Vector3.Zero, r, out dist);
            }
            
            if (hit && dist < closestDist)
            {
                closestDist = dist;
                closestObj = obj;
            }
        }
        return closestObj;
    }

    private bool RaySphereIntersect(Vector3 ro, Vector3 rd, Vector3 center, float radius, out float t)
    {
        t = 0;
        Vector3 m = ro - center;
        float b = Vector3.Dot(m, rd);
        float c = Vector3.Dot(m, m) - radius * radius;
        if (c > 0.0f && b > 0.0f) return false;
        float discr = b * b - c;
        if (discr < 0.0f) return false;
        t = -b - MathF.Sqrt(discr);
        if (t < 0.0f) t = 0.0f;
        return true;
    }

    private bool RayAABBIntersect(Vector3 ro, Vector3 rd, Vector3 min, Vector3 max, out float t)
    {
        t = 0;
        float t1 = (min.X - ro.X) / rd.X;
        float t2 = (max.X - ro.X) / rd.X;
        float t3 = (min.Y - ro.Y) / rd.Y;
        float t4 = (max.Y - ro.Y) / rd.Y;
        float t5 = (min.Z - ro.Z) / rd.Z;
        float t6 = (max.Z - ro.Z) / rd.Z;

        float tmin = MathF.Max(MathF.Max(MathF.Min(t1, t2), MathF.Min(t3, t4)), MathF.Min(t5, t6));
        float tmax = MathF.Min(MathF.Min(MathF.Max(t1, t2), MathF.Max(t3, t4)), MathF.Max(t5, t6));

        if (tmax < 0 || tmin > tmax) return false;
        t = tmin;
        return true;
    }

    private void HandleUndoStart(EditorSceneService sceneService)
    {
        if (ImGui.IsItemActivated())
        {
            _preEditState = _frameBeginState;
        }
    }

    private void HandleUndoEnd(EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            var postEditState = sceneService.Document.Serialize();
            history.PushCommand(new SnapshotCommand(sceneService, assetService, _preEditState, postEditState));
        }
    }

    private float ApplySnap(float val, float snap)
    {
        if (!_snapEnabled || snap <= 0) return val;
        return MathF.Round(val / snap) * snap;
    }

    private Vector3 ApplySnap(Vector3 val, float snap)
    {
        return new Vector3(ApplySnap(val.X, snap), ApplySnap(val.Y, snap), ApplySnap(val.Z, snap));
    }

    private void DrawMapWindow(EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        var doc = sceneService.Document;
        var scene = sceneService.Scene;

        static Vector3 QuaternionToEuler(Quaternion q)
        {
            float t0 = 2.0f * (q.W * q.X + q.Y * q.Z);
            float t1 = 1.0f - 2.0f * (q.X * q.X + q.Y * q.Y);
            float pitch = MathF.Atan2(t0, t1);

            float t2 = 2.0f * (q.W * q.Y - q.Z * q.X);
            t2 = float.Clamp(t2, -1.0f, 1.0f);
            float yaw = MathF.Asin(t2);

            float t3 = 2.0f * (q.W * q.Z + q.X * q.Y);
            float t4 = 1.0f - 2.0f * (q.Y * q.Y + q.Z * q.Z);
            float roll = MathF.Atan2(t3, t4);

            return new Vector3(
                float.RadiansToDegrees(pitch),
                float.RadiansToDegrees(yaw),
                float.RadiansToDegrees(roll)
            );
        }

        static Quaternion EulerToQuaternion(Vector3 euler)
        {
            float pitch = float.DegreesToRadians(euler.X);
            float yaw = float.DegreesToRadians(euler.Y);
            float roll = float.DegreesToRadians(euler.Z);
            return Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
        }

        ImGui.SetNextWindowSize(new Vector2(400, 600), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Map Objects", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        // --- Snapping Controls ---
        ImGui.Checkbox("Enable Snapping", ref _snapEnabled);
        if (_snapEnabled)
        {
            ImGui.DragFloat("Grid Snap", ref _snapGrid, 0.1f, 0.1f, 10.0f);
            ImGui.DragFloat("Angle Snap", ref _snapAngle, 1.0f, 1.0f, 90.0f);
        }
        ImGui.Separator();

        // --- Creation Controls ---
        if (ImGui.Button("Add Box")) AddNewObject(sceneService, assetService, history, MapShapeType.Box);
        ImGui.SameLine();
        if (ImGui.Button("Add Sphere")) AddNewObject(sceneService, assetService, history, MapShapeType.Sphere);
        ImGui.SameLine();
        if (ImGui.Button("Add Capsule")) AddNewObject(sceneService, assetService, history, MapShapeType.Capsule);

        ImGui.Text($"Objects: {doc.Objects.Count}");
        ImGui.Separator();

        if (doc.PlayerSpawn != null)
        {
            if (ImGui.CollapsingHeader("Player Spawn", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var sp = doc.PlayerSpawn;
                
                Vector3 spPos = sp.Position;
                bool posChanged = ImGui.DragFloat3("Spawn Pos##spPos", ref spPos, 0.05f);
                HandleUndoStart(sceneService);
                if (posChanged)
                {
                    spPos = ApplySnap(spPos, _snapGrid);
                    sp.Position = spPos;
                }
                HandleUndoEnd(sceneService, assetService, history);
                
                float yaw = sp.Yaw;
                bool yawChanged = ImGui.DragFloat("Spawn Yaw##spYaw", ref yaw, 0.5f);
                HandleUndoStart(sceneService);
                if (yawChanged)
                {
                    yaw = ApplySnap(yaw, _snapAngle);
                    sp.Yaw = yaw;
                }
                HandleUndoEnd(sceneService, assetService, history);

                float pitch = sp.Pitch;
                bool pitchChanged = ImGui.DragFloat("Spawn Pitch##spPitch", ref pitch, 0.5f);
                HandleUndoStart(sceneService);
                if (pitchChanged)
                {
                    pitch = ApplySnap(pitch, _snapAngle);
                    sp.Pitch = pitch;
                }
                HandleUndoEnd(sceneService, assetService, history);
            }
            ImGui.Separator();
        }

        MapObject? objectToDelete = null;
        MapObject? objectToDuplicate = null;

        for (int i = 0; i < doc.Objects.Count; i++)
        {
            var obj = doc.Objects[i];
            bool isSelected = _selectedObject == obj;
            var flags = ImGuiTreeNodeFlags.FramePadding | (isSelected ? ImGuiTreeNodeFlags.Selected : 0);
            
            bool open = ImGui.TreeNodeEx($"##obj{i}", flags, $"{i}: {obj.Id}");
            
            if (ImGui.IsItemClicked())
            {
                _selectedObject = obj;
            }

            ImGui.SameLine();
            ImGui.TextColored(obj.Visible ? new Vector4(1, 1, 1, 1) : new Vector4(0.5f, 0.5f, 0.5f, 1),
                obj.IsModel ? obj.Model : obj.Mesh);

            if (!open) continue;

            if (ImGui.Button($"Duplicate##dup{i}")) objectToDuplicate = obj;
            ImGui.SameLine();
            if (ImGui.Button($"Delete##del{i}")) objectToDelete = obj;

            string id = obj.Id;
            bool idChanged = ImGui.InputText($"ID##id{i}", ref id, 64);
            HandleUndoStart(sceneService);
            if (idChanged)
            {
                var entity = scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                if (entity != null) entity.Id = id;
                obj.Id = id;
            }
            HandleUndoEnd(sceneService, assetService, history);

            bool visible = obj.Visible;
            bool visChanged = ImGui.Checkbox($"Visible##vis{i}", ref visible);
            if (visChanged) _preEditState = _frameBeginState;
            if (visChanged)
            {
                obj.Visible = visible;
                var entity = scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                if (entity != null) entity.Visible = visible;
                history.PushCommand(new SnapshotCommand(sceneService, assetService, _preEditState, sceneService.Document.Serialize()));
            }

            string texture = obj.Texture ?? "";
            bool texChanged = ImGui.InputText($"Texture##tex{i}", ref texture, 256);
            HandleUndoStart(sceneService);
            if (texChanged)
            {
                obj.Texture = texture;
                var entity = scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                if (entity != null) entity.TexturePath = texture;
            }
            HandleUndoEnd(sceneService, assetService, history);
            
            if (!obj.IsModel)
            {
                Vector2 uvScale = obj.UvScale;
                bool uvChanged = ImGui.DragFloat2($"UV Scale##uv{i}", ref uvScale, 0.05f);
                HandleUndoStart(sceneService);
                if (uvChanged)
                {
                    obj.UvScale = uvScale;
                    var entity = scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                    if (entity != null) entity.UvScale = uvScale;
                }
                HandleUndoEnd(sceneService, assetService, history);
            }

            string interactable = obj.Interactable ?? "";
            bool interactChanged = ImGui.InputText($"Interactable##interact{i}", ref interactable, 128);
            HandleUndoStart(sceneService);
            if (interactChanged)
            {
                obj.Interactable = interactable;
                var entity = scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                if (entity != null) entity.InteractableType = interactable;
            }
            HandleUndoEnd(sceneService, assetService, history);

            if (obj.Body != null)
            {
                var body = obj.Body;
                if (ImGui.TreeNodeEx("Body", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Text($"Shape: {body.Shape}");

                    Vector3 pos = body.Position;
                    bool posChanged = ImGui.DragFloat3($"Pos##pos{i}", ref pos, 0.05f, 0.0f, 0.0f, "%.3f");
                    HandleUndoStart(sceneService);
                    if (posChanged)
                    {
                        pos = ApplySnap(pos, _snapGrid);
                        body.Position = pos;
                        var entity = scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                        if (entity != null) entity.Transform.Position = pos;
                    }
                    HandleUndoEnd(sceneService, assetService, history);

                    Vector3 euler = QuaternionToEuler(body.Rotation);
                    bool rotChanged = ImGui.DragFloat3($"Rot (Euler)##rot{i}", ref euler, 0.5f, 0.0f, 0.0f, "%.3f");
                    HandleUndoStart(sceneService);
                    if (rotChanged)
                    {
                        euler = ApplySnap(euler, _snapAngle);
                        body.Rotation = Quaternion.Normalize(EulerToQuaternion(euler));
                        var entity = scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                        if (entity != null) entity.Transform.Rotation = body.Rotation;
                    }
                    HandleUndoEnd(sceneService, assetService, history);

                    float mass = body.Mass;
                    bool massChanged = ImGui.DragFloat($"Mass##mass{i}", ref mass, 0.1f, 0.0f, 100000.0f, "%.3f");
                    HandleUndoStart(sceneService);
                    if (massChanged) body.Mass = mass;
                    HandleUndoEnd(sceneService, assetService, history);

                    switch (body.Shape)
                    {
                        case MapShapeType.Box when body.HalfExtents.HasValue:
                            Vector3 he = body.HalfExtents.Value;
                            bool heChanged = ImGui.DragFloat3($"HalfExtents##he{i}", ref he, 0.05f, 0.0f, 1000.0f, "%.3f");
                            HandleUndoStart(sceneService);
                            if (heChanged) 
                            {
                                body.HalfExtents = ApplySnap(he, _snapGrid);
                                var entity = scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                                if (entity != null && body.HalfExtents.HasValue) 
                                    entity.Transform.Scale = body.HalfExtents.Value * 2.0f;
                            }
                            HandleUndoEnd(sceneService, assetService, history);
                            break;
                        case MapShapeType.Sphere when body.Radius.HasValue:
                            float rad = body.Radius.Value;
                            bool radChanged = ImGui.DragFloat($"Radius##rad{i}", ref rad, 0.05f, 0.0f, 1000.0f, "%.3f");
                            HandleUndoStart(sceneService);
                            if (radChanged) 
                            {
                                body.Radius = ApplySnap(rad, _snapGrid);
                                var entity = scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                                if (entity != null && body.Radius.HasValue) 
                                    entity.Transform.Scale = new Vector3(body.Radius.Value * 2.0f);
                            }
                            HandleUndoEnd(sceneService, assetService, history);
                            break;
                        case MapShapeType.Capsule when body.Radius.HasValue && body.Height.HasValue:
                            float capRad = body.Radius.Value;
                            bool capRadChanged = ImGui.DragFloat($"Radius##rad{i}", ref capRad, 0.05f, 0.0f, 1000.0f, "%.3f");
                            HandleUndoStart(sceneService);
                            if (capRadChanged) body.Radius = ApplySnap(capRad, _snapGrid);
                            HandleUndoEnd(sceneService, assetService, history);
                            
                            float capH = body.Height.Value;
                            bool capHChanged = ImGui.DragFloat($"Height##h{i}", ref capH, 0.05f, 0.0f, 1000.0f, "%.3f");
                            HandleUndoStart(sceneService);
                            if (capHChanged) body.Height = ApplySnap(capH, _snapGrid);
                            HandleUndoEnd(sceneService, assetService, history);
                            break;
                    }

                    ImGui.TreePop();
                }
            }

            ImGui.TreePop();
        }

        // Apply Deletion or Duplication
        if (objectToDelete != null)
        {
            var pre = sceneService.Document.Serialize();
            doc.Objects.Remove(objectToDelete);
            if (_selectedObject == objectToDelete) _selectedObject = null;
            var post = sceneService.Document.Serialize();
            history.PushCommand(new SnapshotCommand(sceneService, assetService, pre, post));
            sceneService.PopulateScene(assetService);
        }
        else if (objectToDuplicate != null)
        {
            var pre = sceneService.Document.Serialize();
            var cloneDoc = MapDocument.Parse(sceneService.Document.Serialize());
            int index = doc.Objects.IndexOf(objectToDuplicate);
            if (cloneDoc != null && index >= 0 && index < cloneDoc.Objects.Count)
            {
                var clone = cloneDoc.Objects[index];
                clone.Id += "_copy";
                doc.Objects.Insert(index + 1, clone);
                _selectedObject = doc.Objects[index + 1]; // Auto select duplicate
                var post = sceneService.Document.Serialize();
                history.PushCommand(new SnapshotCommand(sceneService, assetService, pre, post));
                sceneService.PopulateScene(assetService);
            }
        }

        ImGui.End();
    }

    private void AddNewObject(EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history, MapShapeType shape)
    {
        var pre = sceneService.Document.Serialize();
        var doc = sceneService.Document;

        var obj = new MapObject
        {
            Id = $"new_{shape.ToString().ToLower()}",
            Visible = true,
            Mesh = shape == MapShapeType.Sphere ? "sphere" : "cube",
            Body = new MapBody
            {
                Shape = shape,
                Position = new Vector3(0, 1, 0),
                Rotation = Quaternion.Identity,
                Mass = 0,
                Friction = 0.5f,
                Restitution = 0.0f
            }
        };

        if (shape == MapShapeType.Box) obj.Body.HalfExtents = new Vector3(0.5f, 0.5f, 0.5f);
        else if (shape == MapShapeType.Sphere) obj.Body.Radius = 0.5f;
        else if (shape == MapShapeType.Capsule) { obj.Body.Radius = 0.5f; obj.Body.Height = 1.0f; obj.Mesh = "capsule"; }

        doc.Objects.Add(obj);
        _selectedObject = obj; // Auto select new object

        var post = sceneService.Document.Serialize();
        history.PushCommand(new SnapshotCommand(sceneService, assetService, pre, post));
        sceneService.PopulateScene(assetService);
    }

    private void DrawJsonWindow(MapDocument? doc)
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
}
