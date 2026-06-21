using System;
using System.Numerics;

namespace Blowtorch;

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

    public Vector3 Target
    {
        get => _target;
        set => _target = value;
    }

    public void Orbit(float deltaX, float deltaY)
    {
        _yaw += deltaX * Sensitivity;
        _pitch += deltaY * Sensitivity;
        _pitch = float.Clamp(_pitch, -89.0f, 89.0f);
    }

    public void Zoom(float delta)
    {
        _distance -= delta * ScrollSpeed;
        _distance = float.Clamp(_distance, MinDistance, MaxDistance);
    }

    public void Pan(float deltaX, float deltaY)
    {
        var fwd = Vector3.Normalize(Front);
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, fwd));
        float panScale = _distance * PanSpeed;
        _target += -right * deltaX * panScale + up * deltaY * panScale;
    }

    public void Fly(float forward, float rightInput, float upInput, float dt)
    {
        var fwd = Vector3.Normalize(Front);
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        
        _target += (fwd * forward + right * rightInput + Vector3.UnitY * upInput) * FlySpeed * dt;
    }

    public Matrix4x4 ViewMatrix =>
        Matrix4x4.CreateLookAt(Position, _target, Vector3.UnitY);

    public Matrix4x4 ProjectionMatrix(float aspect) =>
        Matrix4x4.CreatePerspectiveFieldOfView(
            float.DegreesToRadians(45.0f), aspect, 0.1f, 500.0f);

    public Vector3 Position
    {
        get
        {
            float yawRad = float.DegreesToRadians(_yaw);
            float pitchRad = float.DegreesToRadians(_pitch);
            var offset = new Vector3(
                MathF.Cos(yawRad) * MathF.Cos(pitchRad),
                MathF.Sin(pitchRad),
                MathF.Sin(yawRad) * MathF.Cos(pitchRad)) * _distance;
            return _target + offset;
        }
    }

    public Vector3 Front
    {
        get
        {
            float yawRad = float.DegreesToRadians(_yaw);
            float pitchRad = float.DegreesToRadians(_pitch);
            return Vector3.Normalize(new Vector3(
                -MathF.Cos(yawRad) * MathF.Cos(pitchRad),
                -MathF.Sin(pitchRad),
                -MathF.Sin(yawRad) * MathF.Cos(pitchRad)));
        }
    }
}