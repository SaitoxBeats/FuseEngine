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

    private const float Sensitivity = 0.3f;
    private const float ScrollSpeed = 1.5f;
    private const float PanSpeed = 0.02f;
    private const float FlySpeed = 15.0f;
    private const float MinDistance = 0.5f;
    private const float MaxDistance = 200.0f;

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

    public void Zoom(float delta)
    {
        if (IsOrthographic)
        {
            OrthoSize -= delta * (OrthoSize * 0.1f);
            OrthoSize = float.Clamp(OrthoSize, 0.1f, 1000.0f);
        }
        else
        {
            _distance -= delta * ScrollSpeed;
            _distance = float.Clamp(_distance, MinDistance, MaxDistance);
        }
    }

    public void Pan(float deltaX, float deltaY)
    {
        float panScale;
        Vector3 right, up;

        if (IsOrthographic)
        {
            panScale = OrthoSize * 0.0015f;
            switch (ViewType)
            {
                case CameraViewType.Top:
                    right = Vector3.UnitX;
                    up = -Vector3.UnitZ;
                    break;
                case CameraViewType.Front:
                    right = Vector3.UnitX;
                    up = Vector3.UnitY;
                    break;
                case CameraViewType.Side:
                    right = -Vector3.UnitZ;
                    up = Vector3.UnitY;
                    break;
                default:
                    right = Vector3.UnitX;
                    up = Vector3.UnitY;
                    break;
            }
        }
        else
        {
            var fwd = Vector3.Normalize(Front);
            right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
            up = Vector3.Normalize(Vector3.Cross(right, fwd));
            panScale = _distance * PanSpeed;
        }

        _target += -right * deltaX * panScale + up * deltaY * panScale;
    }

    public void Fly(float forward, float rightInput, float upInput, float dt)
    {
        if (IsOrthographic) return; // No fly in ortho views
        var fwd = Vector3.Normalize(Front);
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        
        _target += (fwd * forward + right * rightInput + Vector3.UnitY * upInput) * FlySpeed * dt;
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
            return Matrix4x4.CreateOrthographic(OrthoSize * aspect, OrthoSize, 0.1f, 1000.0f);
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
                case CameraViewType.Top:
                    return -Vector3.UnitY;
                case CameraViewType.Front:
                    return -Vector3.UnitZ;
                case CameraViewType.Side:
                    return -Vector3.UnitX;
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
}