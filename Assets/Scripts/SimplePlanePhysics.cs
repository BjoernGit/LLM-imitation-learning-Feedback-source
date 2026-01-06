using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Simple, non-physical plane motion that responds to actuator inputs.
/// Pulls commands from the LLM controller and offers keyboard debug override.
/// </summary>
[RequireComponent(typeof(PlaneActuatorController))]
public class SimplePlanePhysics : MonoBehaviour
{
    [Header("Input Sources")]
    [SerializeField] private PlaneActuatorController actuatorSource;
    [SerializeField] private bool pullFromLmStudio = true;
    [SerializeField] private bool enableKeyboardDebug = true;

    [Header("Input Tuning")]
    [SerializeField] private float throttleChangeRate = 0.6f; // units per second when holding Shift/Ctrl

    [Header("Motion Tuning")]
    [SerializeField] private float maxForwardSpeed = 5f; // throttle 1.0 -> 5 m/s
    [SerializeField] private float rollRate = 90f;       // deg/s at aileron 1.0
    [SerializeField] private float pitchRate = 70f;      // deg/s at elevator 1.0
    [SerializeField] private float yawRate = 50f;        // deg/s at rudder 1.0
    [SerializeField] private float brakeSlowdown = 0.3f; // forward speed multiplier when brakes fully applied

    private PlaneActuatorController.ActuatorCommand _actuators;
    private float _manualThrottle;
    private bool _manualThrottleInitialized;

    private void Awake()
    {
        if (!actuatorSource)
            actuatorSource = GetComponent<PlaneActuatorController>();
    }

    private void Update()
    {
        if (pullFromLmStudio && actuatorSource != null)
            _actuators = actuatorSource.Current;

        if (enableKeyboardDebug)
            ApplyKeyboardDebug();

        ApplyMotion(Time.deltaTime);
    }

    /// <summary>Setter with clamping for roll (-1..1).</summary>
    public void SetAileron(float value) => _actuators.aileron = Mathf.Clamp(value, -1f, 1f);
    /// <summary>Setter with clamping for pitch (-1..1).</summary>
    public void SetElevator(float value) => _actuators.elevator = Mathf.Clamp(value, -1f, 1f);
    /// <summary>Setter with clamping for yaw (-1..1).</summary>
    public void SetRudder(float value) => _actuators.rudder = Mathf.Clamp(value, -1f, 1f);
    /// <summary>Setter with clamping for throttle (0..1).</summary>
    public void SetThrottle(float value) => _actuators.throttle = Mathf.Clamp01(value);
    /// <summary>Setter with clamping for airbrake (0..1).</summary>
    public void SetAirbrake(float value) => _actuators.airbrake = Mathf.Clamp01(value);
    /// <summary>Setter with clamping for wheel brakes (0..1).</summary>
    public void SetWheelBrakes(float value) => _actuators.wheelBrakes = Mathf.Clamp01(value);

    /// <summary>
    /// Apply a full actuator command coming from the LLM/controller (used by PlaneActuatorController).
    /// </summary>
    public void ApplyActuatorCommand(PlaneActuatorController.ActuatorCommand cmd)
    {
        _actuators = cmd;
        _manualThrottle = cmd.throttle;
        _manualThrottleInitialized = true;
    }

    /// <summary>
    /// Keyboard debug mapping: A/D roll, W/S pitch, Q/E yaw, Shift/Ctrl throttle, B airbrake, Space wheel brakes.
    /// Keyboard overrides the current frame.
    /// </summary>
    private void ApplyKeyboardDebug()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = Keyboard.current;
        if (kb == null)
            return;

        if (!_manualThrottleInitialized)
        {
            _manualThrottle = _actuators.throttle;
            _manualThrottleInitialized = true;
        }

        float rollInput = (kb.aKey.isPressed ? -1f : 0f) + (kb.dKey.isPressed ? 1f : 0f);
        float pitchInput = (kb.sKey.isPressed ? -1f : 0f) + (kb.wKey.isPressed ? 1f : 0f);
        float yawInput = (kb.qKey.isPressed ? -1f : 0f) + (kb.eKey.isPressed ? 1f : 0f);

        _actuators.aileron = Mathf.Clamp(rollInput, -1f, 1f);
        _actuators.elevator = Mathf.Clamp(pitchInput, -1f, 1f);
        _actuators.rudder = Mathf.Clamp(yawInput, -1f, 1f);

        float throttleDelta = 0f;
        if (kb.leftShiftKey.isPressed) throttleDelta += throttleChangeRate * Time.deltaTime;
        if (kb.leftCtrlKey.isPressed) throttleDelta -= throttleChangeRate * Time.deltaTime;
        _manualThrottle = Mathf.Clamp01(_manualThrottle + throttleDelta);
        _actuators.throttle = _manualThrottle;

        _actuators.airbrake = kb.bKey.isPressed ? 1f : 0f;
        _actuators.wheelBrakes = kb.spaceKey.isPressed ? 1f : 0f;
#else
        if (!_manualThrottleInitialized)
        {
            _manualThrottle = _actuators.throttle;
            _manualThrottleInitialized = true;
        }

        float rollInput = Input.GetAxisRaw("Horizontal"); // A/D
        float pitchInput = Input.GetAxisRaw("Vertical");  // W/S

        float yawInput = 0f;
        if (Input.GetKey(KeyCode.Q)) yawInput -= 1f;
        if (Input.GetKey(KeyCode.E)) yawInput += 1f;

        _actuators.aileron = Mathf.Clamp(rollInput, -1f, 1f);
        _actuators.elevator = Mathf.Clamp(pitchInput, -1f, 1f);
        _actuators.rudder = Mathf.Clamp(yawInput, -1f, 1f);

        float throttleDelta = 0f;
        if (Input.GetKey(KeyCode.LeftShift)) throttleDelta += throttleChangeRate * Time.deltaTime;
        if (Input.GetKey(KeyCode.LeftControl)) throttleDelta -= throttleChangeRate * Time.deltaTime;
        _manualThrottle = Mathf.Clamp01(_manualThrottle + throttleDelta);
        _actuators.throttle = _manualThrottle;

        _actuators.airbrake = Input.GetKey(KeyCode.B) ? 1f : 0f;
        _actuators.wheelBrakes = Input.GetKey(KeyCode.Space) ? 1f : 0f;
#endif
    }

    private void ApplyMotion(float dt)
    {
        float brake = Mathf.Max(_actuators.airbrake, _actuators.wheelBrakes);
        float forwardSpeed = _actuators.throttle * maxForwardSpeed;
        forwardSpeed = Mathf.Lerp(forwardSpeed, forwardSpeed * brakeSlowdown, brake);

        transform.position += transform.forward * (forwardSpeed * dt);

        float roll = _actuators.aileron * rollRate * dt;
        float pitch = _actuators.elevator * pitchRate * dt;
        float yaw = _actuators.rudder * yawRate * dt;

        // Unity uses Z as roll; negate to make positive aileron roll right.
        transform.Rotate(pitch, yaw, -roll, Space.Self);
    }
}
