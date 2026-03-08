using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Simple, non-physical car motion that responds to actuator inputs.
/// Pulls commands from CarActuatorController and offers keyboard debug override.
/// Attach to the same GameObject as CarActuatorController.
/// </summary>
[RequireComponent(typeof(CarActuatorController))]
public class SimpleCarPhysics : MonoBehaviour
{
    [Header("Input Sources")]
    [SerializeField] private CarActuatorController actuatorSource;
    [SerializeField] private bool pullFromLlm = true;
    [SerializeField] private bool enableKeyboardDebug = true;

    [Header("Motion Tuning")]
    [SerializeField] private float maxSpeed = 12f;           // m/s at full throttle
    [SerializeField] private float acceleration = 8f;        // m/s^2
    [SerializeField] private float brakeDeceleration = 15f;  // m/s^2
    [SerializeField] private float drag = 2f;                // natural slowdown m/s^2
    [SerializeField] private float maxSteerAngle = 35f;      // degrees per second at full lock
    [SerializeField] private float steerSpeedFactor = 1f;    // steering tightens with speed (higher = more effect)

    private CarActuatorController.CarCommand _inputs;
    private float _currentSpeed;

    private void Awake()
    {
        if (!actuatorSource)
            actuatorSource = GetComponent<CarActuatorController>();
    }

    private void Update()
    {
        if (pullFromLlm && actuatorSource != null)
            _inputs = actuatorSource.Current;

        if (enableKeyboardDebug)
            ApplyKeyboardDebug();

        ApplyMotion(Time.deltaTime);
    }

    private void ApplyKeyboardDebug()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = Keyboard.current;
        if (kb == null)
            return;

        float steerInput = (kb.aKey.isPressed ? -1f : 0f) + (kb.dKey.isPressed ? 1f : 0f);
        float throttleInput = (kb.wKey.isPressed ? 1f : 0f) + (kb.sKey.isPressed ? -1f : 0f);
        float brakeInput = kb.spaceKey.isPressed ? 1f : 0f;
        float handbrakeInput = kb.leftShiftKey.isPressed ? 1f : 0f;

        _inputs.steer = steerInput;
        _inputs.throttle = throttleInput;
        _inputs.brake = brakeInput;
        _inputs.handbrake = handbrakeInput;
#else
        float steerInput = Input.GetAxisRaw("Horizontal");
        float throttleInput = Input.GetAxisRaw("Vertical");
        float brakeInput = Input.GetKey(KeyCode.Space) ? 1f : 0f;
        float handbrakeInput = Input.GetKey(KeyCode.LeftShift) ? 1f : 0f;

        _inputs.steer = steerInput;
        _inputs.throttle = throttleInput;
        _inputs.brake = brakeInput;
        _inputs.handbrake = handbrakeInput;
#endif
    }

    private void ApplyMotion(float dt)
    {
        // Acceleration / braking
        float targetSpeed = _inputs.throttle * maxSpeed;
        float totalBrake = Mathf.Max(_inputs.brake, _inputs.handbrake);

        if (totalBrake > 0.01f)
        {
            // Braking: decelerate towards zero
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, brakeDeceleration * totalBrake * dt);
        }
        else
        {
            // Accelerate towards target speed
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, acceleration * dt);
        }

        // Natural drag
        if (Mathf.Abs(_inputs.throttle) < 0.01f && totalBrake < 0.01f)
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, drag * dt);

        // Forward movement
        transform.position += transform.forward * (_currentSpeed * dt);

        // Steering — tighter at low speed, wider at high speed
        if (Mathf.Abs(_currentSpeed) > 0.1f)
        {
            float speedRatio = Mathf.Abs(_currentSpeed) / maxSpeed;
            float steerAmount = _inputs.steer * maxSteerAngle * Mathf.Lerp(1f, 0.4f, speedRatio * steerSpeedFactor);
            float direction = Mathf.Sign(_currentSpeed); // reverse steering when going backwards
            transform.Rotate(0f, steerAmount * direction * dt, 0f, Space.Self);
        }
    }
}
