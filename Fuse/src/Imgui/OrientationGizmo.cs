using System.Numerics;
using ImGuiNET;
using Fuse.Renderer;

namespace Fuse.Imgui;

public static class OrientationGizmo
{
    private static void DrawArrowHead(ImDrawListPtr drawList, Vector2 origin, Vector2 tip, uint color, float headSize, float thickness)
    {
        var dir = tip - origin;
        float lenSq = dir.LengthSquared();
        if (lenSq <= 0.0001f) return;

        float len = MathF.Sqrt(lenSq);
        var dirNorm = dir / len;
        var side = new Vector2(-dirNorm.Y, dirNorm.X);

        var wingA = new Vector2(
            tip.X - dirNorm.X * headSize + side.X * (headSize * 0.55f),
            tip.Y - dirNorm.Y * headSize + side.Y * (headSize * 0.55f));
        var wingB = new Vector2(
            tip.X - dirNorm.X * headSize - side.X * (headSize * 0.55f),
            tip.Y - dirNorm.Y * headSize - side.Y * (headSize * 0.55f));

        drawList.AddLine(tip, wingA, color, thickness);
        drawList.AddLine(tip, wingB, color, thickness);
    }

    public static void Draw(Camera camera)
    {
        float radius = 42;
        float margin = 28;
        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;

        var center = new Vector2(displaySize.X - radius - margin, displaySize.Y - radius - margin);
        var drawList = ImGui.GetForegroundDrawList();

        uint bgColor = ImGui.GetColorU32(new Vector4(8f / 255f, 12f / 255f, 18f / 255f, 180f / 255f));
        uint borderColor = ImGui.GetColorU32(new Vector4(190f / 255f, 210f / 255f, 230f / 255f, 90f / 255f));
        uint dotColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 220f / 255f));

        drawList.AddCircleFilled(center, radius + 12, bgColor, 48);
        drawList.AddCircle(center, radius + 12, borderColor, 48, 1);
        drawList.AddCircleFilled(center, 3, dotColor, 16);

        var right = camera.Right;
        var up = camera.Up;
        var front = camera.Front;

        uint colX = ImGui.GetColorU32(new Vector4(1, 96f / 255f, 96f / 255f, 1));
        uint colY = ImGui.GetColorU32(new Vector4(96f / 255f, 1, 128f / 255f, 1));
        uint colZ = ImGui.GetColorU32(new Vector4(96f / 255f, 170f / 255f, 1, 1));

        var axisData = new (float depth, Vector2 screenDir, string label, uint color)[3];

        void AddAxis(int i, Vector3 worldDir, string label, uint col)
        {
            float depth = Vector3.Dot(worldDir, front);
            float sx = Vector3.Dot(worldDir, right);
            float sy = Vector3.Dot(worldDir, up);
            var screenDir = new Vector2(sx * radius, -sy * radius);
            axisData[i] = (depth, screenDir, label, col);
        }

        AddAxis(0, Vector3.UnitX, "X", colX);
        AddAxis(1, Vector3.UnitY, "Y", colY);
        AddAxis(2, Vector3.UnitZ, "Z", colZ);

        Array.Sort(axisData, (a, b) => a.depth.CompareTo(b.depth));

        foreach (var (depth, screenDir, label, color) in axisData)
        {
            var tip = center + screenDir;
            float thickness = depth >= 0 ? 2.5f : 1.5f;
            float alphaMul = depth >= 0 ? 1.0f : 0.55f;

            byte aSrc = (byte)((color >> 24) & 0xFF);
            uint lineColor = (color & 0x00FFFFFF) | ((uint)(aSrc * alphaMul) << 24);

            drawList.AddLine(center, tip, lineColor, thickness);
            DrawArrowHead(drawList, center, tip, lineColor, 8, thickness);

            var labelOffset = screenDir * 0.10f;
            var labelPos = new Vector2(tip.X + labelOffset.X - 4, tip.Y + labelOffset.Y - 8);
            drawList.AddText(labelPos, lineColor, label);
        }
    }
}