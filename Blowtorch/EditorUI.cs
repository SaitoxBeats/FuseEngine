using System;
using System.IO;
using System.Numerics;
using System.Linq;
using ImGuiNET;
using Fuse.Scene.Model;
using Fuse.Renderer;
using Fuse.Core;

namespace Blowtorch;

public unsafe class EditorUI
{
    private bool _showMapWindow = true;
    private bool _showJsonWindow = false;

    // Snapping
    private bool _snapEnabled = true;
    private float _snapGrid = 1.0f;
    private float _snapAngle = 15.0f;

    // Undo/Redo state
    private string _preEditState = "";
    private string _frameBeginState = "";

    // Selection & Modes
    public enum EditorMode { Select, DrawBrush }
    public enum GizmoOperation { Translate, Rotate, Scale }
    
    private EditorMode _currentMode = EditorMode.Select;
    private GizmoOperation _gizmoOperation = GizmoOperation.Translate;
    private MapObject? _selectedObject;
    private HashSet<MapObject> _selectedObjects = new();
    private HashSet<string> _lastSelectedObjectIds = new();
    private double _lastSelectionTime = 0.0;
    private bool _showModelImportDialog = false;
    private List<string> _modelFiles = new();
    private int _selectedModelIndex = -1;
    private string? _detectedTexturePath = null;
    private bool _wasUsingGizmo = false;
    private EditorViewport? _activeDraggingViewport;

    // Brush Tool State
    private bool _isDrawingBrush = false;
    private bool _hasPreviewBrush = false;
    private Vector3 _previewBrushMin;
    private Vector3 _previewBrushMax;
    private EditorViewport? _drawingViewport;

    // Handle Dragging State
    public enum HandleType
    {
        None,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
    private bool _isDraggingHandle = false;
    private HandleType _activeHandle = HandleType.None;
    private EditorViewport? _draggingHandleViewport;
    private bool _draggingHandleIsPreview = false;
    private void SyncSelection(MapDocument doc)
    {
        if (_selectedObject != null && !doc.Objects.Contains(_selectedObject))
        {
            _selectedObject = doc.Objects.FirstOrDefault(o => o.Id == _selectedObject.Id);
        }
        var newSelectedObjects = new HashSet<MapObject>();
        foreach (var obj in _selectedObjects)
        {
            if (doc.Objects.Contains(obj))
            {
                newSelectedObjects.Add(obj);
            }
            else
            {
                var matched = doc.Objects.FirstOrDefault(o => o.Id == obj.Id);
                if (matched != null)
                {
                    newSelectedObjects.Add(matched);
                }
            }
        }
        _selectedObjects = newSelectedObjects;
    }

    public bool ShowMapWindow => _showMapWindow;
    public bool ShowJsonWindow => _showJsonWindow;

    public void Draw(EditorWindow window, EditorViewport viewport3D, EditorViewport viewportTop, EditorViewport viewportFront, EditorViewport viewportSide, EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        SyncSelection(sceneService.Document);
        var currentIds = new HashSet<string>(_selectedObjects.Select(o => o.Id));
        if (!_lastSelectedObjectIds.SetEquals(currentIds))
        {
            _lastSelectionTime = ImGui.GetTime();
            _lastSelectedObjectIds = currentIds;
        }
        _frameBeginState = sceneService.Document.Serialize();

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _activeDraggingViewport = null;
        }

        HandleKeyboardShortcuts(sceneService, assetService, history);

        // --- Dockspace Fullscreen ---
        var mainViewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(mainViewport.WorkPos);
        ImGui.SetNextWindowSize(mainViewport.WorkSize);
        ImGui.SetNextWindowViewport(mainViewport.ID);
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        ImGuiWindowFlags dockWindowFlags = ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoDocking;
        dockWindowFlags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        dockWindowFlags |= ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;

        ImGui.Begin("MainDockSpaceWindow", dockWindowFlags);
        ImGui.PopStyleVar(3);

        uint dockspaceId = ImGui.GetID("MainDockSpace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.None);



        DrawMenuBar(window, sceneService, assetService, history);
        ImGui.End();

        DrawViewportWindow(window, viewport3D, viewportTop, viewportFront, viewportSide, sceneService, assetService, history);

        if (_showMapWindow)
            DrawMapWindow(sceneService, assetService, history);

        if (_showJsonWindow)
            DrawJsonWindow(sceneService.Document);
    }

    private void DuplicateObject(MapObject obj, EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        if (obj == null) return;
        var doc = sceneService.Document;
        var pre = doc.Serialize();
        var cloneDoc = MapDocument.Parse(doc.Serialize());
        int index = doc.Objects.IndexOf(obj);
        if (cloneDoc != null && index >= 0 && index < cloneDoc.Objects.Count)
        {
            var clone = cloneDoc.Objects[index];
            clone.Id += "_copy";
            doc.Objects.Insert(index + 1, clone);
            SceneNameManager.EnsureAllUnique(doc);
            _selectedObject = doc.Objects[index + 1]; // Auto select duplicate
            _selectedObjects.Clear();
            _selectedObjects.Add(_selectedObject);
            var post = doc.Serialize();
            history.PushCommand(new SnapshotCommand(sceneService, assetService, pre, post));
            sceneService.PopulateScene(assetService);
        }
    }

    private void DeleteObject(MapObject obj, EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        if (obj == null) return;
        var doc = sceneService.Document;
        var pre = doc.Serialize();
        doc.Objects.Remove(obj);
        _selectedObjects.Remove(obj);
        if (_selectedObject == obj) _selectedObject = _selectedObjects.FirstOrDefault();
        var post = doc.Serialize();
        history.PushCommand(new SnapshotCommand(sceneService, assetService, pre, post));
        sceneService.PopulateScene(assetService);
    }

    private void DuplicateObjects(List<MapObject> objs, EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        if (objs == null || objs.Count == 0) return;
        var doc = sceneService.Document;
        var pre = doc.Serialize();
        
        var cloneDoc = MapDocument.Parse(doc.Serialize());
        if (cloneDoc == null) return;

        var duplicates = new List<MapObject>();
        foreach (var obj in objs)
        {
            int index = doc.Objects.IndexOf(obj);
            if (index >= 0 && index < cloneDoc.Objects.Count)
            {
                var clone = cloneDoc.Objects[index];
                clone.Id += "_copy";
                doc.Objects.Insert(index + 1, clone);
                duplicates.Add(clone);
            }
        }

        if (duplicates.Count > 0)
        {
            SceneNameManager.EnsureAllUnique(doc);
            
            _selectedObjects.Clear();
            foreach (var dup in duplicates)
            {
                _selectedObjects.Add(dup);
            }
            _selectedObject = duplicates.LastOrDefault();
            
            var post = doc.Serialize();
            history.PushCommand(new SnapshotCommand(sceneService, assetService, pre, post));
            sceneService.PopulateScene(assetService);
        }
    }

    private void DeleteObjects(List<MapObject> objs, EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        if (objs == null || objs.Count == 0) return;
        var doc = sceneService.Document;
        var pre = doc.Serialize();
        
        bool anyRemoved = false;
        foreach (var obj in objs)
        {
            if (doc.Objects.Remove(obj))
            {
                _selectedObjects.Remove(obj);
                anyRemoved = true;
            }
        }
        
        if (anyRemoved)
        {
            if (_selectedObject != null && !doc.Objects.Contains(_selectedObject))
            {
                _selectedObject = _selectedObjects.FirstOrDefault();
            }
            var post = doc.Serialize();
            history.PushCommand(new SnapshotCommand(sceneService, assetService, pre, post));
            sceneService.PopulateScene(assetService);
        }
    }

    private static void LaunchGame(EditorSceneService sceneService)
    {
        sceneService.SaveMap();
        string mapFile = Path.GetFileName(sceneService.MapPath);
        System.Diagnostics.Process.Start("Fuse.exe", mapFile);
    }

    private void HandleKeyboardShortcuts(EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        var io = ImGui.GetIO();
        
        // Handle shortcuts only if we're not typing in text inputs
        if (io.WantTextInput) return;

        if (io.KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Z)) history.Undo();
            if (ImGui.IsKeyPressed(ImGuiKey.Y)) history.Redo();
            if (ImGui.IsKeyPressed(ImGuiKey.S)) sceneService.SaveMap();
            if (ImGui.IsKeyPressed(ImGuiKey.D) && _selectedObjects.Count > 0)
            {
                DuplicateObjects(_selectedObjects.ToList(), sceneService, assetService, history);
            }
        }
        else if (io.KeyShift)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.D) && _selectedObjects.Count > 0)
            {
                DuplicateObjects(_selectedObjects.ToList(), sceneService, assetService, history);
            }
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && _selectedObjects.Count > 0)
        {
            DeleteObjects(_selectedObjects.ToList(), sceneService, assetService, history);
        }
        
        if (ImGui.IsKeyPressed(ImGuiKey.F5))
        {
            LaunchGame(sceneService);
        }

        if (ImGui.IsKeyPressed(ImGuiKey.LeftBracket))
        {
            _snapGrid = MathF.Max(0.0625f, _snapGrid * 0.5f);
        }
        if (ImGui.IsKeyPressed(ImGuiKey.RightBracket))
        {
            _snapGrid = MathF.Min(64.0f, _snapGrid * 2.0f);
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
                if (ImGui.MenuItem("Play", "F5"))
                {
                    LaunchGame(sceneService);
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

    private void DrawViewportWindow(
        EditorWindow window,
        EditorViewport viewport3D, 
        EditorViewport viewportTop, 
        EditorViewport viewportFront, 
        EditorViewport viewportSide, 
        EditorSceneService sceneService, 
        EditorAssetService assetService, 
        CommandHistory history)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        if (ImGui.Begin("Scene Viewports", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (ImGui.RadioButton("Select (Esc)", _currentMode == EditorMode.Select)) _currentMode = EditorMode.Select;
            ImGui.SameLine();
            if (ImGui.RadioButton("Brush (B)", _currentMode == EditorMode.DrawBrush)) _currentMode = EditorMode.DrawBrush;
            ImGui.SameLine();
            ImGui.Text("|");
            ImGui.SameLine();
            if (ImGui.RadioButton("Translate (W)", _gizmoOperation == GizmoOperation.Translate)) _gizmoOperation = GizmoOperation.Translate;
            ImGui.SameLine();
            if (ImGui.RadioButton("Rotate (E)", _gizmoOperation == GizmoOperation.Rotate)) _gizmoOperation = GizmoOperation.Rotate;
            ImGui.SameLine();
            if (ImGui.RadioButton("Scale (R)", _gizmoOperation == GizmoOperation.Scale)) _gizmoOperation = GizmoOperation.Scale;

            if (!ImGui.IsMouseDown(ImGuiMouseButton.Right) && !ImGui.GetIO().WantTextInput)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.Escape)) { _currentMode = EditorMode.Select; _hasPreviewBrush = false; }
                if (ImGui.IsKeyPressed(ImGuiKey.B)) _currentMode = EditorMode.DrawBrush;
                if (ImGui.IsKeyPressed(ImGuiKey.W)) _gizmoOperation = GizmoOperation.Translate;
                if (ImGui.IsKeyPressed(ImGuiKey.E)) _gizmoOperation = GizmoOperation.Rotate;
                if (ImGui.IsKeyPressed(ImGuiKey.R)) _gizmoOperation = GizmoOperation.Scale;
                
                if (_currentMode == EditorMode.DrawBrush && _hasPreviewBrush && ImGui.IsKeyPressed(ImGuiKey.Enter))
                {
                    CommitBrush(sceneService, assetService, history);
                }
            }

            var availSize = ImGui.GetContentRegionAvail();
            var size = new Vector2(availSize.X / 2f - 4, availSize.Y / 2f - 4);

            // Row 1: Top & Front
            DrawSubViewport(window, viewportTop, "Top (X/Z)", size, sceneService, assetService, history);
            ImGui.SameLine();
            DrawSubViewport(window, viewportFront, "Front (X/Y)", size, sceneService, assetService, history);

            // Row 2: Side & 3D Perspective
            DrawSubViewport(window, viewportSide, "Side (Z/Y)", size, sceneService, assetService, history);
            ImGui.SameLine();
            DrawSubViewport(window, viewport3D, "Camera 3D", size, sceneService, assetService, history);
        }
        ImGui.End();
        ImGui.PopStyleVar(1);
    }

    private void DrawSubViewport(
        EditorWindow window,
        EditorViewport viewport, 
        string title, 
        Vector2 size, 
        EditorSceneService sceneService, 
        EditorAssetService assetService, 
        CommandHistory history)
    {
        ImGui.BeginChild(title, size, ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        
        ImGui.Text(title);

        var vpPos = ImGui.GetCursorScreenPos();
        var vpSize = ImGui.GetContentRegionAvail();

        int targetWidth = Math.Max(8, ((int)vpSize.X + 3) & ~3);
        int targetHeight = Math.Max(8, ((int)vpSize.Y + 3) & ~3);

        if (targetWidth != viewport.Width || targetHeight != viewport.Height)
        {
            viewport.CreateFbo(targetWidth, targetHeight);
        }

        ImGui.Image((IntPtr)viewport.ColorTexture, vpSize, new Vector2(0, 1), new Vector2(1, 0));

        bool isHovered = ImGui.IsItemHovered();

        // 2D Handle Detection & State Setup
        bool showHandles = false;
        Vector3 boxMin = Vector3.Zero;
        Vector3 boxMax = Vector3.Zero;
        bool isPreview = false;
        Vector2[] handlePositions = new Vector2[10];

        if (viewport.Camera.IsOrthographic)
        {
            if (_currentMode == EditorMode.DrawBrush && _hasPreviewBrush)
            {
                showHandles = true;
                boxMin = Vector3.Min(_previewBrushMin, _previewBrushMax);
                boxMax = Vector3.Max(_previewBrushMin, _previewBrushMax);
                isPreview = true;
            }
            else if (_currentMode == EditorMode.Select && _selectedObject is Brush brush && brush.Body != null && brush.Body.HalfExtents.HasValue)
            {
                showHandles = true;
                boxMin = brush.Body.Position - brush.Body.HalfExtents.Value;
                boxMax = brush.Body.Position + brush.Body.HalfExtents.Value;
                isPreview = false;
            }
        }

        if (showHandles)
        {
            Vector3[] corners = new Vector3[8]
            {
                new Vector3(boxMin.X, boxMin.Y, boxMin.Z),
                new Vector3(boxMax.X, boxMin.Y, boxMin.Z),
                new Vector3(boxMin.X, boxMax.Y, boxMin.Z),
                new Vector3(boxMax.X, boxMax.Y, boxMin.Z),
                new Vector3(boxMin.X, boxMin.Y, boxMax.Z),
                new Vector3(boxMax.X, boxMin.Y, boxMax.Z),
                new Vector3(boxMin.X, boxMax.Y, boxMax.Z),
                new Vector3(boxMax.X, boxMax.Y, boxMax.Z)
            };

            float sMinX = float.MaxValue, sMinY = float.MaxValue;
            float sMaxX = float.MinValue, sMaxY = float.MinValue;
            foreach (var c in corners)
            {
                Vector2 screenPos = WorldToScreen(c, viewport, vpPos, vpSize);
                if (screenPos.X < sMinX) sMinX = screenPos.X;
                if (screenPos.Y < sMinY) sMinY = screenPos.Y;
                if (screenPos.X > sMaxX) sMaxX = screenPos.X;
                if (screenPos.Y > sMaxY) sMaxY = screenPos.Y;
            }

            handlePositions[(int)HandleType.Left] = new Vector2(sMinX, (sMinY + sMaxY) * 0.5f);
            handlePositions[(int)HandleType.Right] = new Vector2(sMaxX, (sMinY + sMaxY) * 0.5f);
            handlePositions[(int)HandleType.Top] = new Vector2((sMinX + sMaxX) * 0.5f, sMinY);
            handlePositions[(int)HandleType.Bottom] = new Vector2((sMinX + sMaxX) * 0.5f, sMaxY);
            handlePositions[(int)HandleType.TopLeft] = new Vector2(sMinX, sMinY);
            handlePositions[(int)HandleType.TopRight] = new Vector2(sMaxX, sMinY);
            handlePositions[(int)HandleType.BottomLeft] = new Vector2(sMinX, sMaxY);
            handlePositions[(int)HandleType.BottomRight] = new Vector2(sMaxX, sMaxY);

            // Handle hover and click interaction BEFORE picking or drawing code
            var mousePos = ImGui.GetMousePos();
            if (!_isDraggingHandle)
            {
                bool selectionDelayActive = (ImGui.GetTime() - _lastSelectionTime) < 0.5;
                if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && (isPreview || !selectionDelayActive))
                {
                    for (int h = 1; h <= 8; h++)
                    {
                        if (Vector2.Distance(mousePos, handlePositions[h]) < 8f)
                        {
                            _isDraggingHandle = true;
                            _activeHandle = (HandleType)h;
                            _draggingHandleViewport = viewport;
                            _draggingHandleIsPreview = isPreview;
                            _preEditState = sceneService.Document.Serialize();
                            break;
                        }
                    }
                }
            }
        }

        bool isDraggingActiveInThisViewport = _isDraggingHandle && _draggingHandleViewport == viewport;
        bool normalInteractionAllowed = isHovered && !EditorGizmo.IsUsing() && !EditorGizmo.IsHovered && !_isDraggingHandle;

        if (_selectedObject != null && _selectedObject.Body != null && sceneService.Document.Objects.Contains(_selectedObject) && !_isDraggingHandle)
        {
            var body = _selectedObject.Body;
            var view = viewport.Camera.ViewMatrix;
            var proj = viewport.Camera.ProjectionMatrix(vpSize.X / vpSize.Y);

            bool isUsing = EditorGizmo.IsUsing();
            if (isUsing && !_wasUsingGizmo) _preEditState = _frameBeginState;

            float snapVal = _snapEnabled ? _snapGrid : 0.0f;
            float angleSnap = _snapEnabled ? _snapAngle : 0.0f;
            bool changed = false;

            bool canManipulate = (isHovered && _activeDraggingViewport == null) || (_activeDraggingViewport == viewport);

            if (canManipulate)
            {
                if (isUsing && _activeDraggingViewport == null)
                {
                    _activeDraggingViewport = viewport;
                }

                bool selectionDelayActive = (ImGui.GetTime() - _lastSelectionTime) < 0.5;

                if (_gizmoOperation == GizmoOperation.Translate)
                {
                    if (EditorGizmo.ManipulateTranslation(body.Position, view, proj, vpPos, vpSize, out Vector3 newPos, snapVal, !selectionDelayActive))
                    {
                        Vector3 delta = newPos - body.Position;
                        if (delta.LengthSquared() > 0.00001f)
                        {
                            foreach (var obj in _selectedObjects)
                            {
                                if (obj.Body != null)
                                {
                                    obj.Body.Position += delta;
                                }
                            }
                            changed = true;
                        }
                    }
                }
                else if (_gizmoOperation == GizmoOperation.Rotate)
                {
                    if (viewport.Camera.ViewType == CameraViewType.Perspective3D)
                    {
                        if (EditorGizmo.ManipulateRotation(body.Position, body.Rotation, view, proj, vpPos, vpSize, out Quaternion newRot, angleSnap, !selectionDelayActive))
                        {
                            Quaternion normalizedNewRot = Quaternion.Normalize(newRot);
                            Quaternion deltaRot = normalizedNewRot * Quaternion.Inverse(body.Rotation);
                            Vector3 pivot = body.Position;

                            foreach (var obj in _selectedObjects)
                            {
                                if (obj.Body != null)
                                {
                                    if (obj != _selectedObject)
                                    {
                                        Vector3 relativePos = obj.Body.Position - pivot;
                                        Vector3 rotatedPos = Vector3.Transform(relativePos, deltaRot);
                                        obj.Body.Position = pivot + rotatedPos;
                                    }
                                    obj.Body.Rotation = Quaternion.Normalize(deltaRot * obj.Body.Rotation);
                                }
                            }
                            changed = true;
                        }
                    }
                }
                else if (_gizmoOperation == GizmoOperation.Scale)
                {
                    Vector3 currentScale = Vector3.One;
                    if (body.Shape == MapShapeType.Box && body.HalfExtents.HasValue) currentScale = body.HalfExtents.Value * 2.0f;
                    else if (body.Shape == MapShapeType.Sphere && body.Radius.HasValue) currentScale = new Vector3(body.Radius.Value * 2.0f);
                    else currentScale = new Vector3(_selectedObject.ModelScale);

                    if (EditorGizmo.ManipulateScale(body.Position, currentScale, view, proj, vpPos, vpSize, out Vector3 newScale, snapVal, !selectionDelayActive))
                    {
                        Vector3 scaleMult = new Vector3(
                            currentScale.X > 0.0001f ? newScale.X / currentScale.X : 1f,
                            currentScale.Y > 0.0001f ? newScale.Y / currentScale.Y : 1f,
                            currentScale.Z > 0.0001f ? newScale.Z / currentScale.Z : 1f
                        );

                        Vector3 pivot = body.Position;

                        foreach (var obj in _selectedObjects)
                        {
                            if (obj.Body != null)
                            {
                                if (obj != _selectedObject)
                                {
                                    Vector3 relativePos = obj.Body.Position - pivot;
                                    obj.Body.Position = pivot + relativePos * scaleMult;
                                }

                                if (obj.Body.Shape == MapShapeType.Box && obj.Body.HalfExtents.HasValue)
                                {
                                    obj.Body.HalfExtents = Vector3.Max(new Vector3(0.05f), obj.Body.HalfExtents.Value * scaleMult);
                                }
                                else if (obj.Body.Shape == MapShapeType.Sphere && obj.Body.Radius.HasValue)
                                {
                                    float avgMult = (scaleMult.X + scaleMult.Y + scaleMult.Z) / 3.0f;
                                    obj.Body.Radius = MathF.Max(0.05f, obj.Body.Radius.Value * avgMult);
                                }
                                else
                                {
                                    float maxMult = MathF.Max(scaleMult.X, MathF.Max(scaleMult.Y, scaleMult.Z));
                                    obj.ModelScale = MathF.Max(0.01f, obj.ModelScale * maxMult);
                                }
                            }
                        }
                        changed = true;
                    }
                }

                if (changed)
                {
                    foreach (var obj in _selectedObjects)
                    {
                        if (obj.Body == null) continue;

                        if (obj is Brush brush && obj.Body.HalfExtents.HasValue)
                        {
                            brush.UpdatePlanesFromHalfExtents(obj.Body.HalfExtents.Value);
                            assetService.InvalidateMesh(brush.Id);
                        }

                        var entity = sceneService.Scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                        if (entity != null)
                        {
                            entity.Transform.Position = obj.Body.Position;
                            entity.Transform.Rotation = obj.Body.Rotation;
                            
                            if (obj is Brush brushObj)
                            {
                                entity.Transform.Scale = Vector3.One;
                                entity.Mesh = assetService.GetOrCreateMesh(brushObj);
                            }
                            else if (obj.Body.Shape == MapShapeType.Box && obj.Body.HalfExtents.HasValue)
                                entity.Transform.Scale = obj.Body.HalfExtents.Value * 2.0f;
                            else if (obj.Body.Shape == MapShapeType.Sphere && obj.Body.Radius.HasValue)
                                entity.Transform.Scale = new Vector3(obj.Body.Radius.Value * 2.0f);
                            else
                                entity.Transform.Scale = new Vector3(obj.ModelScale);
                        }
                    }
                }

                if (!isUsing && _wasUsingGizmo)
                {
                    var postEditState = sceneService.Document.Serialize();
                    history.PushCommand(new SnapshotCommand(sceneService, assetService, _preEditState, postEditState));
                }
                _wasUsingGizmo = isUsing;
            }
        }

        if (normalInteractionAllowed)
        {
            if (_currentMode == EditorMode.Select)
            {
                    viewport.HandleInput(ImGui.GetIO(), ImGui.GetIO().DeltaTime, window.Glfw, window.Handle, vpPos, vpSize);

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    EditorGizmo.GetMouseRay(ImGui.GetIO().MousePos, viewport.Camera.ViewMatrix, viewport.Camera.ProjectionMatrix(vpSize.X / vpSize.Y), vpPos, vpSize, out Vector3 rayOrigin, out Vector3 rayDir);
                    
                    MapObject? hitObj = PickObject(rayOrigin, rayDir, sceneService);
                    if (hitObj != null)
                    {
                        if (ImGui.GetIO().KeyCtrl)
                        {
                            if (_selectedObjects.Contains(hitObj))
                            {
                                _selectedObjects.Remove(hitObj);
                                if (_selectedObject == hitObj)
                                {
                                    _selectedObject = _selectedObjects.FirstOrDefault();
                                }
                            }
                            else
                            {
                                _selectedObjects.Add(hitObj);
                                _selectedObject = hitObj;
                            }
                        }
                        else
                        {
                            _selectedObjects.Clear();
                            _selectedObjects.Add(hitObj);
                            _selectedObject = hitObj;
                        }
                    }
                    else
                    {
                        if (!ImGui.GetIO().KeyCtrl)
                        {
                            _selectedObjects.Clear();
                            _selectedObject = null;
                        }
                    }
                }
            }
            else if (_currentMode == EditorMode.DrawBrush)
            {
                // Let right click still navigate the camera
                if (ImGui.IsMouseDown(ImGuiMouseButton.Right) || ImGui.IsMouseDown(ImGuiMouseButton.Middle))
                viewport.HandleInput(ImGui.GetIO(), ImGui.GetIO().DeltaTime, window.Glfw, window.Handle, vpPos, vpSize);

                if (viewport.Camera.IsOrthographic)
                {
                    EditorGizmo.GetMouseRay(ImGui.GetIO().MousePos, viewport.Camera.ViewMatrix, viewport.Camera.ProjectionMatrix(vpSize.X / vpSize.Y), vpPos, vpSize, out Vector3 rayOrigin, out Vector3 rayDir);
                    
                    Vector3 hitPoint = Vector3.Zero;
                    float t = 0;
                    if (viewport.Camera.ViewType == CameraViewType.Top && MathF.Abs(rayDir.Y) > 0.001f) { t = -rayOrigin.Y / rayDir.Y; hitPoint = rayOrigin + rayDir * t; }
                    else if (viewport.Camera.ViewType == CameraViewType.Front && MathF.Abs(rayDir.Z) > 0.001f) { t = -rayOrigin.Z / rayDir.Z; hitPoint = rayOrigin + rayDir * t; }
                    else if (viewport.Camera.ViewType == CameraViewType.Side && MathF.Abs(rayDir.X) > 0.001f) { t = -rayOrigin.X / rayDir.X; hitPoint = rayOrigin + rayDir * t; }

                    hitPoint = ApplySnap(hitPoint, _snapGrid);

                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _isDrawingBrush = true;
                        _drawingViewport = viewport;
                        
                        if (!_hasPreviewBrush)
                        {
                            _previewBrushMin = hitPoint;
                            _previewBrushMax = hitPoint;
                            if (viewport.Camera.ViewType == CameraViewType.Top) { _previewBrushMin.Y = -1; _previewBrushMax.Y = 1; }
                            if (viewport.Camera.ViewType == CameraViewType.Front) { _previewBrushMin.Z = -1; _previewBrushMax.Z = 1; }
                            if (viewport.Camera.ViewType == CameraViewType.Side) { _previewBrushMin.X = -1; _previewBrushMax.X = 1; }
                        }
                        else
                        {
                            if (viewport.Camera.ViewType == CameraViewType.Top) { _previewBrushMin.X = hitPoint.X; _previewBrushMin.Z = hitPoint.Z; }
                            if (viewport.Camera.ViewType == CameraViewType.Front) { _previewBrushMin.X = hitPoint.X; _previewBrushMin.Y = hitPoint.Y; }
                            if (viewport.Camera.ViewType == CameraViewType.Side) { _previewBrushMin.Z = hitPoint.Z; _previewBrushMin.Y = hitPoint.Y; }
                        }
                    }
                    
                    if (_isDrawingBrush && _drawingViewport == viewport)
                    {
                        if (viewport.Camera.ViewType == CameraViewType.Top) { _previewBrushMax.X = hitPoint.X; _previewBrushMax.Z = hitPoint.Z; }
                        if (viewport.Camera.ViewType == CameraViewType.Front) { _previewBrushMax.X = hitPoint.X; _previewBrushMax.Y = hitPoint.Y; }
                        if (viewport.Camera.ViewType == CameraViewType.Side) { _previewBrushMax.Z = hitPoint.Z; _previewBrushMax.Y = hitPoint.Y; }
                        _hasPreviewBrush = true;
                    }
                }
            }
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && _isDrawingBrush)
        {
            _isDrawingBrush = false;
            _drawingViewport = null;
            Vector3 min = Vector3.Min(_previewBrushMin, _previewBrushMax);
            Vector3 max = Vector3.Max(_previewBrushMin, _previewBrushMax);
            _previewBrushMin = min;
            _previewBrushMax = max;
        }

        // Handle Dragging Update (Mouse position follow-up)
        if (isDraggingActiveInThisViewport)
        {
            EditorGizmo.GetMouseRay(ImGui.GetIO().MousePos, viewport.Camera.ViewMatrix, viewport.Camera.ProjectionMatrix(vpSize.X / vpSize.Y), vpPos, vpSize, out Vector3 rayOrigin, out Vector3 rayDir);
            Vector3 hitPoint = Vector3.Zero;
            float t = 0;
            if (viewport.Camera.ViewType == CameraViewType.Top && MathF.Abs(rayDir.Y) > 0.001f) { t = -rayOrigin.Y / rayDir.Y; hitPoint = rayOrigin + rayDir * t; }
            else if (viewport.Camera.ViewType == CameraViewType.Front && MathF.Abs(rayDir.Z) > 0.001f) { t = -rayOrigin.Z / rayDir.Z; hitPoint = rayOrigin + rayDir * t; }
            else if (viewport.Camera.ViewType == CameraViewType.Side && MathF.Abs(rayDir.X) > 0.001f) { t = -rayOrigin.X / rayDir.X; hitPoint = rayOrigin + rayDir * t; }

            hitPoint = ApplySnap(hitPoint, _snapGrid);

            Vector3 currentMin = boxMin;
            Vector3 currentMax = boxMax;

            UpdateBoundsFromDrag(viewport.Camera.ViewType, _activeHandle, hitPoint, ref currentMin, ref currentMax);

            if (_draggingHandleIsPreview)
            {
                _previewBrushMin = currentMin;
                _previewBrushMax = currentMax;
            }
            else if (_selectedObject is Brush brush && brush.Body != null)
            {
                Vector3 newSize = currentMax - currentMin;
                if (newSize.X > 0.1f && newSize.Y > 0.1f && newSize.Z > 0.1f)
                {
                    brush.Body.Position = currentMin + newSize * 0.5f;
                    brush.Body.HalfExtents = newSize * 0.5f;

                    brush.UpdatePlanesFromHalfExtents(brush.Body.HalfExtents.Value);
                    assetService.InvalidateMesh(brush.Id);

                    var entity = sceneService.Scene.Entities.FirstOrDefault(e => e.Id == brush.Id);
                    if (entity != null)
                    {
                        entity.Transform.Position = brush.Body.Position;
                        entity.Transform.Scale = Vector3.One;
                        entity.Mesh = assetService.GetOrCreateMesh(brush);
                    }
                }
            }

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _isDraggingHandle = false;
                _activeHandle = HandleType.None;
                _draggingHandleViewport = null;
                
                if (!_draggingHandleIsPreview)
                {
                    var postEditState = sceneService.Document.Serialize();
                    history.PushCommand(new SnapshotCommand(sceneService, assetService, _preEditState, postEditState));
                }
            }
        }

        // Draw selection outlines for all other selected objects
        if (viewport.Camera.IsOrthographic && _currentMode == EditorMode.Select && _selectedObjects.Count > 1)
        {
            var drawList = ImGui.GetWindowDrawList();
            uint otherSelColor = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 0.7f));

            foreach (var selObj in _selectedObjects)
            {
                if (selObj == _selectedObject) continue;
                if (selObj.Body == null || !selObj.Visible) continue;

                Vector3 sMin = Vector3.Zero;
                Vector3 sMax = Vector3.Zero;

                if (selObj.Body.Shape == MapShapeType.Box && selObj.Body.HalfExtents.HasValue)
                {
                    sMin = selObj.Body.Position - selObj.Body.HalfExtents.Value;
                    sMax = selObj.Body.Position + selObj.Body.HalfExtents.Value;
                }
                else if (selObj.Body.Shape == MapShapeType.Sphere && selObj.Body.Radius.HasValue)
                {
                    float r = selObj.Body.Radius.Value;
                    sMin = selObj.Body.Position - new Vector3(r);
                    sMax = selObj.Body.Position + new Vector3(r);
                }
                else
                {
                    float r = 1.0f;
                    if (selObj.Body.Shape == MapShapeType.Capsule && selObj.Body.Height.HasValue) r = selObj.Body.Height.Value;
                    if (selObj.IsModel) r = selObj.ModelScale * 1.5f;
                    sMin = selObj.Body.Position - new Vector3(r);
                    sMax = selObj.Body.Position + new Vector3(r);
                }

                Vector3[] sCorners = new Vector3[8]
                {
                    new Vector3(sMin.X, sMin.Y, sMin.Z),
                    new Vector3(sMax.X, sMin.Y, sMin.Z),
                    new Vector3(sMin.X, sMax.Y, sMin.Z),
                    new Vector3(sMax.X, sMax.Y, sMin.Z),
                    new Vector3(sMin.X, sMin.Y, sMax.Z),
                    new Vector3(sMax.X, sMin.Y, sMax.Z),
                    new Vector3(sMin.X, sMax.Y, sMax.Z),
                    new Vector3(sMax.X, sMax.Y, sMax.Z)
                };

                float selMinX = float.MaxValue, selMinY = float.MaxValue;
                float selMaxX = float.MinValue, selMaxY = float.MinValue;
                foreach (var c in sCorners)
                {
                    Vector2 screenPos = WorldToScreen(c, viewport, vpPos, vpSize);
                    if (screenPos.X < selMinX) selMinX = screenPos.X;
                    if (screenPos.Y < selMinY) selMinY = screenPos.Y;
                    if (screenPos.X > selMaxX) selMaxX = screenPos.X;
                    if (screenPos.Y > selMaxY) selMaxY = screenPos.Y;
                }

                drawList.AddRect(new Vector2(selMinX, selMinY), new Vector2(selMaxX, selMaxY), otherSelColor, 0f, ImDrawFlags.None, 1.0f);
            }
        }

        // Draw Handles & Bounding Box Outline
        if (showHandles)
        {
            Vector3 finalMin = _draggingHandleIsPreview ? _previewBrushMin : (_selectedObject != null && _selectedObject.Body != null && _selectedObject.Body.HalfExtents.HasValue ? _selectedObject.Body.Position - _selectedObject.Body.HalfExtents.Value : boxMin);
            Vector3 finalMax = _draggingHandleIsPreview ? _previewBrushMax : (_selectedObject != null && _selectedObject.Body != null && _selectedObject.Body.HalfExtents.HasValue ? _selectedObject.Body.Position + _selectedObject.Body.HalfExtents.Value : boxMax);
            
            Vector3 orderedMin = Vector3.Min(finalMin, finalMax);
            Vector3 orderedMax = Vector3.Max(finalMin, finalMax);

            Vector3[] finalCorners = new Vector3[8]
            {
                new Vector3(orderedMin.X, orderedMin.Y, orderedMin.Z),
                new Vector3(orderedMax.X, orderedMin.Y, orderedMin.Z),
                new Vector3(orderedMin.X, orderedMax.Y, orderedMin.Z),
                new Vector3(orderedMax.X, orderedMax.Y, orderedMin.Z),
                new Vector3(orderedMin.X, orderedMin.Y, orderedMax.Z),
                new Vector3(orderedMax.X, orderedMin.Y, orderedMax.Z),
                new Vector3(orderedMin.X, orderedMax.Y, orderedMax.Z),
                new Vector3(orderedMax.X, orderedMax.Y, orderedMax.Z)
            };

            float sMinX = float.MaxValue, sMinY = float.MaxValue;
            float sMaxX = float.MinValue, sMaxY = float.MinValue;
            foreach (var c in finalCorners)
            {
                Vector2 screenPos = WorldToScreen(c, viewport, vpPos, vpSize);
                if (screenPos.X < sMinX) sMinX = screenPos.X;
                if (screenPos.Y < sMinY) sMinY = screenPos.Y;
                if (screenPos.X > sMaxX) sMaxX = screenPos.X;
                if (screenPos.Y > sMaxY) sMaxY = screenPos.Y;
            }

            var drawList = ImGui.GetWindowDrawList();
            uint boxColor = isPreview ? ImGui.GetColorU32(new Vector4(0.0f, 1.0f, 1.0f, 1.0f)) : ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
            drawList.AddRect(new Vector2(sMinX, sMinY), new Vector2(sMaxX, sMaxY), boxColor, 0f, ImDrawFlags.None, 1.5f);

            Vector2[] finalHandlePositions = new Vector2[10];
            finalHandlePositions[(int)HandleType.Left] = new Vector2(sMinX, (sMinY + sMaxY) * 0.5f);
            finalHandlePositions[(int)HandleType.Right] = new Vector2(sMaxX, (sMinY + sMaxY) * 0.5f);
            finalHandlePositions[(int)HandleType.Top] = new Vector2((sMinX + sMaxX) * 0.5f, sMinY);
            finalHandlePositions[(int)HandleType.Bottom] = new Vector2((sMinX + sMaxX) * 0.5f, sMaxY);
            finalHandlePositions[(int)HandleType.TopLeft] = new Vector2(sMinX, sMinY);
            finalHandlePositions[(int)HandleType.TopRight] = new Vector2(sMaxX, sMinY);
            finalHandlePositions[(int)HandleType.BottomLeft] = new Vector2(sMinX, sMaxY);
            finalHandlePositions[(int)HandleType.BottomRight] = new Vector2(sMaxX, sMaxY);

            for (int h = 1; h <= 8; h++)
            {
                Vector2 p = finalHandlePositions[h];
                drawList.AddRectFilled(p - new Vector2(4), p + new Vector2(4), ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)));
                drawList.AddRect(p - new Vector2(4), p + new Vector2(4), ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 1.0f)));
            }
        }

        ImGui.EndChild();
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
            float[] gridSizes = { 0.0625f, 0.125f, 0.25f, 0.5f, 1.0f, 2.0f, 4.0f, 8.0f, 16.0f, 32.0f, 64.0f };
            int currentIdx = Array.IndexOf(gridSizes, _snapGrid);
            if (currentIdx == -1) currentIdx = 3; // Default 0.5
            
            string[] gridSizeLabels = gridSizes.Select(g => g.ToString("0.0000").TrimEnd('0').TrimEnd(',').TrimEnd('.')).ToArray();
            ImGui.SetNextItemWidth(120);
            if (ImGui.Combo("Grid Snap", ref currentIdx, gridSizeLabels, gridSizeLabels.Length))
            {
                _snapGrid = gridSizes[currentIdx];
            }
            ImGui.SameLine();
            ImGui.Text("[Halve with [, Double with ]]");

            ImGui.DragFloat("Angle Snap", ref _snapAngle, 1.0f, 1.0f, 90.0f);
        }
        ImGui.Separator();

        // --- Creation Controls ---
        if (ImGui.Button("Add Box")) AddNewObject(sceneService, assetService, history, MapShapeType.Box);
        ImGui.SameLine();
        if (ImGui.Button("Add Sphere")) AddNewObject(sceneService, assetService, history, MapShapeType.Sphere);
        ImGui.SameLine();
        if (ImGui.Button("Add Capsule")) AddNewObject(sceneService, assetService, history, MapShapeType.Capsule);
        ImGui.SameLine();
        if (ImGui.Button("Add Model"))
        {
            _showModelImportDialog = true;
            RefreshModelFileList(assetService.FuseResPath);
        }

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
            bool isSelected = _selectedObjects.Contains(obj);
            var flags = ImGuiTreeNodeFlags.FramePadding | (isSelected ? ImGuiTreeNodeFlags.Selected : 0);
            
            bool open = ImGui.TreeNodeEx($"##obj{i}", flags, $"{i}: {obj.Id}");
            
            if (ImGui.IsItemClicked())
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    if (_selectedObjects.Contains(obj))
                    {
                        _selectedObjects.Remove(obj);
                        if (_selectedObject == obj)
                        {
                            _selectedObject = _selectedObjects.FirstOrDefault();
                        }
                    }
                    else
                    {
                        _selectedObjects.Add(obj);
                        _selectedObject = obj;
                    }
                }
                else
                {
                    _selectedObjects.Clear();
                    _selectedObjects.Add(obj);
                    _selectedObject = obj;
                }
            }

            ImGui.SameLine();
            ImGui.TextColored(obj.Visible ? new Vector4(1, 1, 1, 1) : new Vector4(0.5f, 0.5f, 0.5f, 1),
                obj.IsModel ? obj.Model : (obj.Mesh ?? (obj is Brush ? "brush" : "none")));

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
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                string uniqueId = SceneNameManager.GetUniqueName(sceneService.Document, obj, obj.Id);
                if (uniqueId != obj.Id)
                {
                    var entity = scene.Entities.FirstOrDefault(e => e.Id == obj.Id);
                    if (entity != null) entity.Id = uniqueId;
                    obj.Id = uniqueId;
                }
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
                                if (obj is Brush brush)
                                {
                                    brush.UpdatePlanesFromHalfExtents(body.HalfExtents.Value);
                                    assetService.InvalidateMesh(brush.Id);
                                    if (entity != null)
                                    {
                                        entity.Mesh = assetService.GetOrCreateMesh(brush);
                                    }
                                }
                                if (entity != null && body.HalfExtents.HasValue) 
                                {
                                    if (obj is Brush)
                                        entity.Transform.Scale = Vector3.One;
                                    else
                                        entity.Transform.Scale = body.HalfExtents.Value * 2.0f;
                                }
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
            DeleteObject(objectToDelete, sceneService, assetService, history);
        }
        else if (objectToDuplicate != null)
        {
            DuplicateObject(objectToDuplicate, sceneService, assetService, history);
        }

        DrawModelImportDialog(sceneService, assetService, history);

        ImGui.End();
    }

    private void AddNewObject(EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history, MapShapeType shape)
    {
        var pre = sceneService.Document.Serialize();
        var doc = sceneService.Document;

        MapObject obj;
        obj = new MapObject
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
        SceneNameManager.EnsureAllUnique(doc);
        _selectedObject = obj; // Auto select new object
        _selectedObjects.Clear();
        _selectedObjects.Add(obj);

        var post = sceneService.Document.Serialize();
        history.PushCommand(new SnapshotCommand(sceneService, assetService, pre, post));
        sceneService.PopulateScene(assetService);
    }

    private void CommitBrush(EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        if (!_hasPreviewBrush) return;

        Vector3 min = Vector3.Min(_previewBrushMin, _previewBrushMax);
        Vector3 max = Vector3.Max(_previewBrushMin, _previewBrushMax);
        Vector3 size = max - min;
        
        // Prevent flat brushes
        if (size.X < 0.1f) size.X = 1.0f;
        if (size.Y < 0.1f) size.Y = 1.0f;
        if (size.Z < 0.1f) size.Z = 1.0f;

        Vector3 pos = min + size * 0.5f;

        var pre = sceneService.Document.Serialize();
        var brush = Brush.CreateCube(pos, size);
        brush.Texture = "Textures/dev_measurecrate01.bmp";

        sceneService.Document.Objects.Add(brush);
        SceneNameManager.EnsureAllUnique(sceneService.Document);
        _selectedObject = brush;
        _selectedObjects.Clear();
        _selectedObjects.Add(brush);
        _currentMode = EditorMode.Select;
        _hasPreviewBrush = false;

        var post = sceneService.Document.Serialize();
        history.PushCommand(new SnapshotCommand(sceneService, assetService, pre, post));
        sceneService.PopulateScene(assetService);
    }

    private void RefreshModelFileList(string fuseResPath)
    {
        _modelFiles.Clear();
        _selectedModelIndex = -1;
        _detectedTexturePath = null;

        string modelsDir = Path.Combine(fuseResPath, "Models");
        if (Directory.Exists(modelsDir))
        {
            var files = Directory.GetFiles(modelsDir, "*.obj");
            foreach (var f in files)
            {
                _modelFiles.Add(Path.GetFileName(f));
            }
        }
    }

    private string? FindTextureInModel(string objFullPath, string fuseResPath)
    {
        try
        {
            if (!File.Exists(objFullPath)) return null;

            string mtlFilename = null!;
            foreach (var line in File.ReadLines(objFullPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("mtllib ", StringComparison.OrdinalIgnoreCase))
                {
                    mtlFilename = trimmed.Substring(7).Trim();
                    break;
                }
            }

            if (string.IsNullOrEmpty(mtlFilename)) return null;

            string mtlFullPath = Path.Combine(Path.GetDirectoryName(objFullPath) ?? "", mtlFilename);
            if (!File.Exists(mtlFullPath)) return null;

            string textureFilename = null!;
            foreach (var line in File.ReadLines(mtlFullPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("map_Kd ", StringComparison.OrdinalIgnoreCase))
                {
                    textureFilename = trimmed.Substring(7).Trim();
                    break;
                }
            }

            if (string.IsNullOrEmpty(textureFilename)) return null;

            string nameOnly = Path.GetFileName(textureFilename);
            string targetTexturePath = Path.Combine(fuseResPath, "Textures", nameOnly);

            if (File.Exists(targetTexturePath))
            {
                return $"Textures/{nameOnly}";
            }
            else
            {
                Logger.Warn($"Model import: Texture '{nameOnly}' defined in mtl but not found in '{targetTexturePath}'");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error parsing model for texture: {ex.Message}");
        }

        return null;
    }

    private void ImportSelectedModel(string filename, string? texturePath, EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        var doc = sceneService.Document;
        var pre = doc.Serialize();

        var obj = new MapObject
        {
            Id = Path.GetFileNameWithoutExtension(filename),
            Visible = true,
            Model = $"Models/{filename}",
            ModelScale = 1.0f,
            Body = new MapBody
            {
                Shape = MapShapeType.Trimesh,
                Position = new Vector3(0, 1, 0),
                Rotation = Quaternion.Identity,
                Mass = 0,
                Friction = 0.5f,
                Restitution = 0.0f
            }
        };

        if (!string.IsNullOrEmpty(texturePath))
        {
            obj.Texture = texturePath;
        }

        doc.Objects.Add(obj);
        SceneNameManager.EnsureAllUnique(doc);
        
        _selectedObject = obj;
        _selectedObjects.Clear();
        _selectedObjects.Add(obj);

        var post = doc.Serialize();
        history.PushCommand(new SnapshotCommand(sceneService, assetService, pre, post));
        sceneService.PopulateScene(assetService);
    }

    private void DrawModelImportDialog(EditorSceneService sceneService, EditorAssetService assetService, CommandHistory history)
    {
        if (!_showModelImportDialog) return;

        ImGui.OpenPopup("Import Model");
        
        bool open = true;
        if (ImGui.BeginPopupModal("Import Model", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Select a model file from the Models directory:");
            ImGui.Separator();

            if (_modelFiles.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "No .obj files found in Models/ directory.");
            }
            else
            {
                string[] filesArray = _modelFiles.ToArray();
                if (ImGui.ListBox("##ModelsList", ref _selectedModelIndex, filesArray, filesArray.Length, 6))
                {
                    if (_selectedModelIndex >= 0 && _selectedModelIndex < _modelFiles.Count)
                    {
                        string selectedFile = _modelFiles[_selectedModelIndex];
                        string modelFullPath = Path.Combine(assetService.FuseResPath, "Models", selectedFile);
                        _detectedTexturePath = FindTextureInModel(modelFullPath, assetService.FuseResPath);
                    }
                }
            }

            ImGui.Separator();

            if (_selectedModelIndex >= 0 && _selectedModelIndex < _modelFiles.Count)
            {
                ImGui.Text($"Selected: {_modelFiles[_selectedModelIndex]}");
                if (!string.IsNullOrEmpty(_detectedTexturePath))
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Texture found: {_detectedTexturePath}");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "No texture associated (or not found in res/Textures).");
                }
            }

            ImGui.Separator();

            ImGui.BeginDisabled(_selectedModelIndex < 0);
            if (ImGui.Button("Import", new Vector2(120, 0)))
            {
                string selectedFile = _modelFiles[_selectedModelIndex];
                ImportSelectedModel(selectedFile, _detectedTexturePath, sceneService, assetService, history);
                _showModelImportDialog = false;
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                _showModelImportDialog = false;
            }

            ImGui.EndPopup();
        }

        if (!open)
        {
            _showModelImportDialog = false;
        }
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

    private Vector2 WorldToScreen(Vector3 worldPos, EditorViewport viewport, Vector2 vpPos, Vector2 vpSize)
    {
        var view = viewport.Camera.ViewMatrix;
        var proj = viewport.Camera.ProjectionMatrix(vpSize.X / vpSize.Y);
        Vector4 clip = Vector4.Transform(new Vector4(worldPos, 1.0f), view * proj);
        if (clip.W == 0.0f) return Vector2.Zero;
        Vector3 ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        float x = vpPos.X + (ndc.X + 1.0f) * 0.5f * vpSize.X;
        float y = vpPos.Y + (1.0f - ndc.Y) * 0.5f * vpSize.Y;
        return new Vector2(x, y);
    }

    private void UpdateBoundsFromDrag(CameraViewType viewType, HandleType handle, Vector3 hitPoint, ref Vector3 min, ref Vector3 max)
    {
        int hAxis = 0;
        int vAxis = 0;
        bool hInverted = false;
        bool vInverted = false;

        if (viewType == CameraViewType.Top)
        {
            hAxis = 0; // X
            vAxis = 2; // Z
            vInverted = false;
        }
        else if (viewType == CameraViewType.Front)
        {
            hAxis = 0; // X
            vAxis = 1; // Y
            vInverted = true; // Top is Max Y
        }
        else if (viewType == CameraViewType.Side)
        {
            hAxis = 2; // Z
            vAxis = 1; // Y
            hInverted = true;
            vInverted = true; // Top is Max Y
        }

        bool dragLeft = handle == HandleType.Left || handle == HandleType.TopLeft || handle == HandleType.BottomLeft;
        bool dragRight = handle == HandleType.Right || handle == HandleType.TopRight || handle == HandleType.BottomRight;
        bool dragTop = handle == HandleType.Top || handle == HandleType.TopLeft || handle == HandleType.TopRight;
        bool dragBottom = handle == HandleType.Bottom || handle == HandleType.BottomLeft || handle == HandleType.BottomRight;

        if (dragLeft)
        {
            if (hInverted)
                SetComponent(ref max, hAxis, GetComp(hitPoint, hAxis));
            else
                SetComponent(ref min, hAxis, GetComp(hitPoint, hAxis));
        }
        if (dragRight)
        {
            if (hInverted)
                SetComponent(ref min, hAxis, GetComp(hitPoint, hAxis));
            else
                SetComponent(ref max, hAxis, GetComp(hitPoint, hAxis));
        }

        if (dragTop)
        {
            if (vInverted)
                SetComponent(ref max, vAxis, GetComp(hitPoint, vAxis));
            else
                SetComponent(ref min, vAxis, GetComp(hitPoint, vAxis));
        }
        if (dragBottom)
        {
            if (vInverted)
                SetComponent(ref min, vAxis, GetComp(hitPoint, vAxis));
            else
                SetComponent(ref max, vAxis, GetComp(hitPoint, vAxis));
        }

        Vector3 realMin = Vector3.Min(min, max);
        Vector3 realMax = Vector3.Max(min, max);
        min = realMin;
        max = realMax;
    }

    private float GetComp(Vector3 v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        2 => v.Z,
        _ => 0
    };

    private void SetComponent(ref Vector3 v, int axis, float val)
    {
        if (axis == 0) v.X = val;
        else if (axis == 1) v.Y = val;
        else if (axis == 2) v.Z = val;
    }

    public void DrawPreviewDebug(Fuse.Debug.DebugDrawer drawer)
    {
        if (_hasPreviewBrush)
        {
            Vector3 min = Vector3.Min(_previewBrushMin, _previewBrushMax);
            Vector3 max = Vector3.Max(_previewBrushMin, _previewBrushMax);
            Vector3 size = max - min;
            Vector3 pos = min + size * 0.5f;

            // Draw as cyan/light blue outline
            drawer.DrawBox(pos, Quaternion.Identity, size * 0.5f, new Vector3(0.0f, 1.0f, 1.0f));
        }
    }
}
