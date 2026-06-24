using System;
using System.Numerics;

namespace Blowtorch;

public enum CameraViewType
{
    Perspective3D,
    Top,
    Front,
    Side
}

public class ViewportCamera
{
    private float _distance = 8.0f;
    private float _yaw;
    private float _pitch = -20.0f;
    private Vector3 _target;

    public float Sensitivity { get; set; } = 0.3f;
    public float ScrollSpeed { get; set; } = 1.5f;
    public float PanSpeed { get; set; } = 0.005f;
    public float FlySpeed { get; set; } = 15.0f;
    public float MinDistance { get; set; } = 0.5f;
    public float MaxDistance { get; set; } = 200.0f;

    public CameraViewType ViewType { get; set; } = CameraViewType.Perspective3D;
    public float OrthoSize { get; set; } = 10.0f;

    public bool IsOrthographic => ViewType != CameraViewType.Perspective3D;

    public Vector3 Target
    {
        get => _target;
        set => _target = value;
    }

    public void Orbit(float deltaX, float deltaY)
    {
        if (IsOrthographic) return; // No orbit in ortho views
        _yaw += deltaX * Sensitivity;
        _pitch += deltaY * Sensitivity;
        _pitch = float.Clamp(_pitch, -89.0f, 89.0f);
    }

    public void Zoom(float delta, Vector2 mousePos, Vector2 viewportSize)
    {
        if (IsOrthographic)
        {
            // Zoom to mouse logic
            float nx = (mousePos.X / viewportSize.X) * 2.0f - 1.0f;
            float ny = 1.0f - (mousePos.Y / viewportSize.Y) * 2.0f;
            float aspect = viewportSize.X / viewportSize.Y;

            Vector3 offsetBefore = Right * (nx * OrthoSize * aspect * 0.5f) + Up * (ny * OrthoSize * 0.5f);
            
            OrthoSize -= delta * (OrthoSize * 0.1f);
            OrthoSize = float.Clamp(OrthoSize, 0.1f, 10000.0f);
            
            Vector3 offsetAfter = Right * (nx * OrthoSize * aspect * 0.5f) + Up * (ny * OrthoSize * 0.5f);
            _target += offsetBefore - offsetAfter;
        }
        else
        {
            _distance -= delta * ScrollSpeed;
            _distance = float.Clamp(_distance, MinDistance, MaxDistance);
        }
    }

    public void Pan(float deltaX, float deltaY, float viewportHeight)
    {
        float panScale;

        if (IsOrthographic)
        {
            // Pixel-perfect pan
            panScale = OrthoSize / MathF.Max(viewportHeight, 1.0f);
        }
        else
        {
            panScale = _distance * PanSpeed;
        }

        _target += -Right * deltaX * panScale + Up * deltaY * panScale;
    }

    public void Fly(float forward, float rightInput, float upInput, float dt)
    {
        if (IsOrthographic) return; // No fly in ortho views
        
        _target += (Front * forward + Right * rightInput + Vector3.UnitY * upInput) * FlySpeed * dt;
    }

    public Matrix4x4 ViewMatrix
    {
        get
        {
            switch (ViewType)
            {
                case CameraViewType.Top:
                    return Matrix4x4.CreateLookAt(_target + new Vector3(0, 100, 0), _target, -Vector3.UnitZ);
                case CameraViewType.Front:
                    return Matrix4x4.CreateLookAt(_target + new Vector3(0, 0, 100), _target, Vector3.UnitY);
                case CameraViewType.Side:
                    return Matrix4x4.CreateLookAt(_target + new Vector3(100, 0, 0), _target, Vector3.UnitY);
                default:
                    return Matrix4x4.CreateLookAt(Position, _target, Vector3.UnitY);
            }
        }
    }

    public Matrix4x4 ProjectionMatrix(float aspect)
    {
        if (IsOrthographic)
        {
            return Matrix4x4.CreateOrthographic(OrthoSize * aspect, OrthoSize, -10000.0f, 10000.0f);
        }
        return Matrix4x4.CreatePerspectiveFieldOfView(
            float.DegreesToRadians(45.0f), aspect, 0.1f, 500.0f);
    }

    public Vector3 Position
    {
        get
        {
            switch (ViewType)
            {
                case CameraViewType.Top:
                    return _target + new Vector3(0, 100, 0);
                case CameraViewType.Front:
                    return _target + new Vector3(0, 0, 100);
                case CameraViewType.Side:
                    return _target + new Vector3(100, 0, 0);
                default:
                    float yawRad = float.DegreesToRadians(_yaw);
                    float pitchRad = float.DegreesToRadians(_pitch);
                    var offset = new Vector3(
                        MathF.Cos(yawRad) * MathF.Cos(pitchRad),
                        MathF.Sin(pitchRad),
                        MathF.Sin(yawRad) * MathF.Cos(pitchRad)) * _distance;
                    return _target + offset;
            }
        }
    }

    public Vector3 Front
    {
        get
        {
            switch (ViewType)
            {
                case CameraViewType.Top: return -Vector3.UnitY;
                case CameraViewType.Front: return -Vector3.UnitZ;
                case CameraViewType.Side: return -Vector3.UnitX;
                default:
                    float yawRad = float.DegreesToRadians(_yaw);
                    float pitchRad = float.DegreesToRadians(_pitch);
                    return Vector3.Normalize(new Vector3(
                        -MathF.Cos(yawRad) * MathF.Cos(pitchRad),
                        -MathF.Sin(pitchRad),
                        -MathF.Sin(yawRad) * MathF.Cos(pitchRad)));
            }
        }
    }

    public Vector3 Right
    {
        get
        {
            switch (ViewType)
            {
                case CameraViewType.Top: return Vector3.UnitX;
                case CameraViewType.Front: return Vector3.UnitX;
                case CameraViewType.Side: return -Vector3.UnitZ;
                default:
                    return Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            }
        }
    }

    public Vector3 Up
    {
        get
        {
            switch (ViewType)
            {
                case CameraViewType.Top: return -Vector3.UnitZ;
                case CameraViewType.Front: return Vector3.UnitY;
                case CameraViewType.Side: return Vector3.UnitY;
                default:
                    return Vector3.Normalize(Vector3.Cross(Right, Front));
            }
        }
    }
}