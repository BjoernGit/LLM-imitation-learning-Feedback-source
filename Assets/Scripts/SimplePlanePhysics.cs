using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Simple, non-physical plane motion with integrated LLM command parsing.
/// Handles observation building, JSON parsing, keyboard debug, and movement.
/// </summary>
public class SimplePlanePhysics : MonoBehaviour, ILlmControllable
{
    [Header("Input")]
    [SerializeField] private bool enableKeyboardDebug = true;

    [Header("Input Tuning")]
    [SerializeField] private float throttleChangeRate = 0.6f;

    [Header("Motion Tuning")]
    [SerializeField] private float maxForwardSpeed = 5f;
    [SerializeField] private float rollRate = 90f;
    [SerializeField] private float pitchRate = 70f;
    [SerializeField] private float yawRate = 50f;
    [SerializeField] private float brakeSlowdown = 0.3f;

    [Header("Debug")]
    [SerializeField] private bool logParseErrors = true;
    [SerializeField] private bool logIncomingJson = false;

    [SerializeField] private ActuatorCommand _current;
    private float _manualThrottle;
    private bool _manualThrottleInitialized;

    [Serializable]
    public struct ActuatorCommand
    {
        public string logic;
        public float aileron;
        public float elevator;
        public float rudder;
        public float throttle;
        public float airbrake;
        public float wheelBrakes;

        public void Clamp()
        {
            aileron = Mathf.Clamp(aileron, -1f, 1f);
            elevator = Mathf.Clamp(elevator, -1f, 1f);
            rudder = Mathf.Clamp(rudder, -1f, 1f);
            throttle = Mathf.Clamp(throttle, -1f, 1f);
            airbrake = Mathf.Clamp01(airbrake);
            wheelBrakes = Mathf.Clamp01(wheelBrakes);
        }
    }

    public ActuatorCommand Current => _current;

    // --- ILlmControllable ---

    public bool TryApplyJson(string json)
    {
        try
        {
            if (logIncomingJson)
                Debug.Log($"Plane actuator JSON received:\n{json}");

            var cleaned = StripLineComments(json);
            var cmd = JsonUtility.FromJson<ActuatorCommand>(cleaned);
            cmd.Clamp();
            ApplyLlmDirectionalInput(cmd);
            return true;
        }
        catch (Exception ex)
        {
            if (logParseErrors)
                Debug.LogWarning($"Failed to parse plane actuator JSON: {ex.Message}\nInput: {json}");
            return false;
        }
    }

    public string BuildObservationJson(Vector3 velocity, Transform target)
    {
        var targetPos = target ? target.position : Vector3.zero;
        var obs = new PlaneObservation
        {
            position = transform.position,
            forward = transform.forward,
            up = transform.up,
            velocity = velocity,
            targetPosition = targetPos,
            lastCommand = _current
        };
        return JsonUtility.ToJson(obs, true);
    }

    // --- Update loop ---

    private void Update()
    {
        if (enableKeyboardDebug)
            ApplyKeyboardDebug();

        ApplyMotion(Time.deltaTime);
    }

    // --- LLM input ---

    private void ApplyLlmDirectionalInput(ActuatorCommand cmd)
    {
        if (!_manualThrottleInitialized)
        {
            _manualThrottle = _current.throttle;
            _manualThrottleInitialized = true;
        }

        _current.aileron = cmd.aileron;
        _current.elevator = cmd.elevator;
        _current.rudder = cmd.rudder;

        float throttleDir = cmd.throttle;
        _manualThrottle = Mathf.Clamp01(_manualThrottle + throttleDir * throttleChangeRate * Time.deltaTime);
        _current.throttle = _manualThrottle;

        _current.airbrake = cmd.airbrake > 0.5f ? 1f : 0f;
        _current.wheelBrakes = cmd.wheelBrakes > 0.5f ? 1f : 0f;
    }

    // --- Keyboard debug ---

    private void ApplyKeyboardDebug()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = Keyboard.current;
        if (kb == null)
            return;

        bool anyKey = kb.wKey.isPressed || kb.sKey.isPressed || kb.aKey.isPressed || kb.dKey.isPressed
                   || kb.qKey.isPressed || kb.eKey.isPressed || kb.leftShiftKey.isPressed
                   || kb.leftCtrlKey.isPressed || kb.bKey.isPressed || kb.spaceKey.isPressed;
        if (!anyKey)
            return;

        if (!_manualThrottleInitialized)
        {
            _manualThrottle = _current.throttle;
            _manualThrottleInitialized = true;
        }

        _current.aileron = (kb.aKey.isPressed ? -1f : 0f) + (kb.dKey.isPressed ? 1f : 0f);
        _current.elevator = (kb.sKey.isPressed ? -1f : 0f) + (kb.wKey.isPressed ? 1f : 0f);
        _current.rudder = (kb.qKey.isPressed ? -1f : 0f) + (kb.eKey.isPressed ? 1f : 0f);

        float throttleDelta = 0f;
        if (kb.leftShiftKey.isPressed) throttleDelta += throttleChangeRate * Time.deltaTime;
        if (kb.leftCtrlKey.isPressed) throttleDelta -= throttleChangeRate * Time.deltaTime;
        _manualThrottle = Mathf.Clamp01(_manualThrottle + throttleDelta);
        _current.throttle = _manualThrottle;

        _current.airbrake = kb.bKey.isPressed ? 1f : 0f;
        _current.wheelBrakes = kb.spaceKey.isPressed ? 1f : 0f;
#else
        bool anyKey = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D)
                   || Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.LeftShift)
                   || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.B) || Input.GetKey(KeyCode.Space);
        if (!anyKey)
            return;

        if (!_manualThrottleInitialized)
        {
            _manualThrottle = _current.throttle;
            _manualThrottleInitialized = true;
        }

        _current.aileron = Input.GetAxisRaw("Horizontal");
        _current.elevator = Input.GetAxisRaw("Vertical");

        float yawInput = 0f;
        if (Input.GetKey(KeyCode.Q)) yawInput -= 1f;
        if (Input.GetKey(KeyCode.E)) yawInput += 1f;
        _current.rudder = yawInput;

        float throttleDelta = 0f;
        if (Input.GetKey(KeyCode.LeftShift)) throttleDelta += throttleChangeRate * Time.deltaTime;
        if (Input.GetKey(KeyCode.LeftControl)) throttleDelta -= throttleChangeRate * Time.deltaTime;
        _manualThrottle = Mathf.Clamp01(_manualThrottle + throttleDelta);
        _current.throttle = _manualThrottle;

        _current.airbrake = Input.GetKey(KeyCode.B) ? 1f : 0f;
        _current.wheelBrakes = Input.GetKey(KeyCode.Space) ? 1f : 0f;
#endif
    }

    // --- Motion ---

    private void ApplyMotion(float dt)
    {
        float brake = Mathf.Max(_current.airbrake, _current.wheelBrakes);
        float forwardSpeed = _current.throttle * maxForwardSpeed;
        forwardSpeed = Mathf.Lerp(forwardSpeed, forwardSpeed * brakeSlowdown, brake);

        transform.position += transform.forward * (forwardSpeed * dt);

        float roll = _current.aileron * rollRate * dt;
        float pitch = _current.elevator * pitchRate * dt;
        float yaw = _current.rudder * yawRate * dt;

        transform.Rotate(pitch, yaw, -roll, Space.Self);
    }

    // --- Helpers ---

    [Serializable]
    private struct PlaneObservation
    {
        public Vector3 position;
        public Vector3 forward;
        public Vector3 up;
        public Vector3 velocity;
        public Vector3 targetPosition;
        public ActuatorCommand lastCommand;
    }

    private static string StripLineComments(string json)
    {
        if (string.IsNullOrEmpty(json))
            return json;

        var lines = json.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf("//", StringComparison.Ordinal);
            if (idx >= 0)
                lines[i] = lines[i].Substring(0, idx);
        }
        return string.Join("\n", lines);
    }
}
