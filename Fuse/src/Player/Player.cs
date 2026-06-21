using System.Numerics;
using JoltPhysicsSharp;
using Fuse.Renderer;
using Fuse.Core;

namespace Fuse.Player;

public class Player : IDisposable
{
    private readonly Physics.PhysicsWorld _world;
    private readonly Camera _camera;
    private readonly CharacterVirtual _character;
    private readonly BodyLockInterface _bli;
    private readonly CharacterVirtualSettings _settings = new();
    private ObjectLayer _objectLayer;

    private float _currentEyeHeight = 0.9f;

    private float _maxSpeedGround = 4.0f;
    private float _maxSpeedAir = 0.8f;
    private float _groundAccel = 10.0f;
    private float _airAccel = 150.0f;
    private float _frictionValue = 4.0f;
    private float _stopSpeed = 1.5f;
    private float _jumpForce = 3.8f;

    private bool _isSprinting;
    private float _sprintSpeedMul = 1.5f;

    private bool _isCrouching;
    private float _capsuleRadius = 0.5f;
    private float _standCapsuleH = 1.8f;
    private float _crouchCapsuleH = 0.8f;
    private float _standEyeH = 0.9f;
    private float _crouchEyeH = 0.5f;
    private float _crouchSpeedMul = 0.5f;

    private bool _noclip;
    private Vector3 _noclipPosition;
    private float _noclipSpeed = 10.0f;
    private float _noclipSprintSpeed = 15.0f;

    public Player(Physics.PhysicsWorld world, Vector3 position)
    {
        _world = world;
        _camera = new Camera(position) { FOV = 75 };
        _objectLayer = world.ObjectLayer;
        _bli = world.Native.BodyLockInterface;

        _settings.Mass = 80.0f;
        _settings.Shape = new CapsuleShape(_standCapsuleH * 0.5f, _capsuleRadius);
        _settings.MaxSlopeAngle = float.DegreesToRadians(45.0f);
        _settings.CharacterPadding = 0.02f;
        _settings.PenetrationRecoverySpeed = 0.9f;
        _settings.PredictiveContactDistance = 0.05f;
        _settings.MaxCollisionIterations = 10;
        _settings.MaxConstraintIterations = 30;
        _settings.MinTimeRemaining = 1.0e-4f;
        _settings.CollisionTolerance = 1.0e-3f;
        _settings.MaxNumHits = 256;
        _settings.HitReductionCosMaxAngle = 0.999f;

        Vector3 posVec = position;
        Quaternion identity = Quaternion.Identity;
        _character = new CharacterVirtual(
            _settings,
            ref posVec,
            ref identity,
            0,
            _world.Native);

        _character.OnContactAdded += OnContactAdded;
        _character.OnContactPersisted += OnContactPersisted;

        _camera.SetRotation(-90.0f, 0.0f);
        SyncCamera();
    }

    public void Dispose()
    {
        _character.OnContactAdded -= OnContactAdded;
        _character.OnContactPersisted -= OnContactPersisted;
        _character.Dispose();
    }

    public void Update(float dt)
    {
        HandleNoclipToggle();
        HandleSprint();

        if (_noclip)
        {
            UpdateNoclip(dt);
            SyncCamera();
            return;
        }

        HandleCrouch();
        ApplyMovement(dt);

        var updSettings = new ExtendedUpdateSettings
        {
            StickToFloorStepDown = new Vector3(0, -0.5f, 0),
            WalkStairsStepUp = new Vector3(0, 0.5f, 0),
            WalkStairsMinStepForward = 0.02f,
            WalkStairsStepForwardTest = 0.15f,
            WalkStairsCosAngleForwardContact = float.Cos(float.DegreesToRadians(75.0f)),
            WalkStairsStepDownExtra = Vector3.Zero,
        };

        using var bodyFilter = new Physics.DefaultBodyFilter();
        using var shapeFilter = new Physics.DefaultShapeFilter();
        _character.ExtendedUpdate(dt, updSettings, ref _objectLayer, _world.Native, bodyFilter, shapeFilter);

        Vector3 charVel = _character.LinearVelocity;
        if (charVel.Y > 0.0f)
        {
            var contacts = _character.GetActiveContacts();
            foreach (var c in contacts)
            {
                if (c.ContactNormal.Y < -0.7f)
                {
                    charVel.Y = 0.0f;
                    _character.LinearVelocity = charVel;
                    break;
                }
            }
        }

        SyncCamera();
        PushDynamicBodies();
    }

    private void ApplyMovement(float dt)
    {
        Vector3 velocity = _character.LinearVelocity;
        bool onGround = _character.GroundState == GroundState.OnGround;

        Vector2 wishDir = BuildWishDir();

        if (onGround)
        {
            Vector2 horiz = new(velocity.X, velocity.Z);
            horiz = ApplyFriction(horiz, dt);
            float groundSpeed = _maxSpeedGround;
            if (_isSprinting)
                groundSpeed *= _sprintSpeedMul;
            else if (_isCrouching)
                groundSpeed *= _crouchSpeedMul;
            horiz = Accelerate(horiz, wishDir, groundSpeed, _groundAccel, dt);

            velocity.X = horiz.X;
            velocity.Y = 0.0f;
            velocity.Z = horiz.Y;

            if (horiz.Length() > 0.01f)
            {
                Vector3 groundNormal = _character.GroundNormal;
                if (groundNormal.Y < 0.99f)
                {
                    Vector3 moveDir = Vector3.Normalize(new Vector3(horiz.X, 0.0f, horiz.Y));
                    float dot = Vector3.Dot(moveDir, groundNormal);
                    if (dot < 0.0f)
                    {
                        float slopeFactor = 1.0f - groundNormal.Y;
                        velocity.Y = -dot * slopeFactor * horiz.Length();
                    }
                }
            }

            if (Input.Input.KeyDown(Input.KeyCodes.Space))
                velocity.Y = _jumpForce;
        }
        else
        {
            Vector2 horiz = new(velocity.X, velocity.Z);
            horiz = Accelerate(horiz, wishDir, _maxSpeedAir, _airAccel, dt);
            velocity.X = horiz.X;
            velocity.Z = horiz.Y;
        }

        if (!onGround)
            velocity += _world.Gravity * dt;

        _character.LinearVelocity = velocity;
    }

    private Vector2 BuildWishDir()
    {
        if (!Input.Input.IsCursorDisabled())
            return Vector2.Zero;
        Vector3 dir = Vector3.Zero;
        if (Input.Input.KeyDown(Input.KeyCodes.W)) dir += _camera.Front;
        if (Input.Input.KeyDown(Input.KeyCodes.S)) dir -= _camera.Front;
        if (Input.Input.KeyDown(Input.KeyCodes.A)) dir -= _camera.Right;
        if (Input.Input.KeyDown(Input.KeyCodes.D)) dir += _camera.Right;
        dir.Y = 0.0f;
        if (dir.Length() < 0.01f) return Vector2.Zero;
        dir = Vector3.Normalize(dir);
        return new Vector2(dir.X, dir.Z);
    }

    private static Vector2 Accelerate(Vector2 velocity, Vector2 wishDir, float wishSpeed, float accel, float dt)
    {
        if (wishDir.Length() < 0.01f) return velocity;
        float currentSpeed = Vector2.Dot(velocity, wishDir);
        float addSpeed = wishSpeed - currentSpeed;
        if (addSpeed <= 0.0f) return velocity;
        float accelSpeed = float.Min(accel * dt * wishSpeed, addSpeed);
        return velocity + wishDir * accelSpeed;
    }

    private Vector2 ApplyFriction(Vector2 velocity, float dt)
    {
        float speed = velocity.Length();
        if (speed < 0.1f) return Vector2.Zero;
        float control = (speed < _stopSpeed) ? _stopSpeed : speed;
        float drop = control * _frictionValue * dt;
        float newSpeed = float.Max(speed - drop, 0.0f);
        return velocity * (newSpeed / speed);
    }

    private void SyncCamera()
    {
        var charPos = _character.Position;
        _camera.Position = new Vector3(charPos.X, (float)charPos.Y + _currentEyeHeight, charPos.Z);
    }

    public Camera Camera => _camera;
    public Vector3 Position => new((float)_character.Position.X, (float)_character.Position.Y, (float)_character.Position.Z);
    public Vector3 EyePosition => new((float)_character.Position.X, (float)_character.Position.Y + _currentEyeHeight, (float)_character.Position.Z);
    public CharacterVirtual NativeCharacter => _character;
    public bool IsOnGround => _character.GroundState == GroundState.OnGround;
    public bool IsCrouching => _isCrouching;
    public bool IsSprinting => _isSprinting;
    public bool IsNoclip => _noclip;
    public Vector3 FeetPosition
    {
        get
        {
            Vector3 pos = Position;
            float halfH = (_isCrouching ? _crouchCapsuleH : _standCapsuleH) * 0.5f;
            pos.Y -= halfH + _capsuleRadius;
            return pos;
        }
    }

    public void ToggleNoclip()
    {
        _noclip = !_noclip;
        if (_noclip)
            _noclipPosition = Position;
        else
        {
        _character.Position = new Vector3(_noclipPosition.X, _noclipPosition.Y, _noclipPosition.Z);
        _character.LinearVelocity = Vector3.Zero;
        }
    }

    public void SetSpeed(float speed) => _maxSpeedGround = speed;
    public float Speed => _maxSpeedGround;

    private void HandleSprint()
    {
        _isSprinting = Input.Input.KeyDown(Input.KeyCodes.LeftShift) && !_isCrouching;
    }

    private void HandleCrouch()
    {
        bool wantsCrouch = Input.Input.KeyDown(Input.KeyCodes.LeftControl);

        if (wantsCrouch && !_isCrouching)
        {
            _isCrouching = true;
            _currentEyeHeight = _crouchEyeH;
            bool wasOnGround = IsOnGround;
            RebuildCapsule(_crouchCapsuleH);

            if (wasOnGround)
            {
                var pos = _character.Position;
                float lowerBy = (_standCapsuleH - _crouchCapsuleH) * 0.5f;
                _character.Position = new Vector3(pos.X, (float)pos.Y - lowerBy, pos.Z);
            }
        }
        else if (!wantsCrouch && _isCrouching)
        {
            var pos = _character.Position;
            float crouchHalfH = _crouchCapsuleH * 0.5f;
            float standHalfH = _standCapsuleH * 0.5f;

            float topY = (float)pos.Y + crouchHalfH + _capsuleRadius;
            float checkDist = standHalfH - crouchHalfH;
            var origin = new Vector3(pos.X, topY, pos.Z);
            var dir = new Vector3(0, 1, 0) * checkDist;
            Ray ray = new(ref origin, ref dir);

            using var bpFilter = new Physics.DefaultBroadPhaseLayerFilter();
            using var olFilter = new Physics.DefaultObjectLayerFilter();
            using var bodyFilter = new Physics.DefaultBodyFilter();
            bool blocked = _world.NarrowPhaseQuery.CastRay(ray, out _, bpFilter, olFilter, bodyFilter);

            if (!blocked)
            {
                _isCrouching = false;
                _currentEyeHeight = _standEyeH;
                bool wasOnGround = IsOnGround;
                RebuildCapsule(_standCapsuleH);

                if (wasOnGround)
                {
                    float lowerBy = (_standCapsuleH - _crouchCapsuleH) * 0.5f;
                    _character.Position = new Vector3(pos.X, (float)pos.Y + lowerBy, pos.Z);
                }
            }
        }
    }

    private void RebuildCapsule(float height)
    {
        Vector3 vel = _character.LinearVelocity;
        var newShape = new CapsuleShape(height * 0.5f, _capsuleRadius);

        using var bodyFilter = new Physics.DefaultBodyFilter();
        using var shapeFilter = new Physics.DefaultShapeFilter();
        _character.SetShape(0.0f, newShape, 1.0f, ref _objectLayer, _world.Native, bodyFilter, shapeFilter);
        _character.LinearVelocity = vel;
    }

    private void HandleNoclipToggle()
    {
        if (!Input.Input.KeyPressed(Input.KeyCodes.F1)) return;
        ToggleNoclip();
    }

    private void UpdateNoclip(float dt)
    {
        Vector3 dir = Vector3.Zero;
        if (Input.Input.KeyDown(Input.KeyCodes.W)) dir += _camera.Front;
        if (Input.Input.KeyDown(Input.KeyCodes.S)) dir -= _camera.Front;
        if (Input.Input.KeyDown(Input.KeyCodes.A)) dir -= _camera.Right;
        if (Input.Input.KeyDown(Input.KeyCodes.D)) dir += _camera.Right;
        if (Input.Input.KeyDown(Input.KeyCodes.Space)) dir += new Vector3(0, 1, 0);
        if (Input.Input.KeyDown(Input.KeyCodes.LeftControl)) dir -= new Vector3(0, 1, 0);

        if (dir.LengthSquared() > 0.000001f)
            dir = Vector3.Normalize(dir);

        float speed = _isSprinting ? _noclipSprintSpeed : _noclipSpeed;
        _noclipPosition += dir * speed * dt;
        _character.Position = new Vector3(_noclipPosition.X, _noclipPosition.Y, _noclipPosition.Z);
    }

    private void PushDynamicBodies()
    {
        var contacts = _character.GetActiveContacts();
        foreach (var c in contacts)
        {
            if (!c.HadCollision) continue;

            BodyID bodyID = c.BodyB;
            BodyLockWrite bodyLock = default;
            _bli.LockWrite(bodyID, out bodyLock);

            if (bodyLock.Succeeded)
            {
                if (bodyLock.Body.IsDynamic)
                {
                    float mass = 1.0f / bodyLock.Body.MotionProperties.InverseMassUnchecked;
                    float relVel = -Vector3.Dot(_character.LinearVelocity, c.ContactNormal);
                    if (relVel > 0.0f)
                    {
                        float impulseMag = relVel * mass * 0.3f;
                        Vector3 impulse = -c.ContactNormal * impulseMag;
                        bodyLock.Body.AddImpulse(impulse);
                    }
                }
                _bli.UnlockWrite(bodyLock);
            }
        }
    }

    private void OnContactAdded(CharacterVirtual character, in BodyID bodyID2, SubShapeID subShapeID2,
        in RVector3 contactPosition, in Vector3 contactNormal, ref CharacterContactSettings settings)
    {
        BodyLockRead bodyLock = default;
        _bli.LockRead(bodyID2, out bodyLock);
        if (bodyLock.Succeeded)
        {
            if (bodyLock.Body.IsStatic && contactNormal.Y > 0.707f)
                settings.CanPushCharacter = false;
            _bli.UnlockRead(bodyLock);
        }
    }

    private void OnContactPersisted(CharacterVirtual character, in BodyID bodyID2, SubShapeID subShapeID2,
        in RVector3 contactPosition, in Vector3 contactNormal, ref CharacterContactSettings settings)
    {
        OnContactAdded(character, bodyID2, subShapeID2, contactPosition, contactNormal, ref settings);
    }
}
