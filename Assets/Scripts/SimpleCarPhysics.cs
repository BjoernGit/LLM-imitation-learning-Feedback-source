using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Simple, non-physical car motion with integrated LLM command parsing.
/// Handles observation building, JSON parsing, keyboard debug, and movement.
/// </summary>
public class SimpleCarPhysics : MonoBehaviour, ILlmControllable
{
    [Header("Input")]
    [SerializeField] private bool enableKeyboardDebug = true;

    [Header("Motion Tuning")]
    [SerializeField] private float maxSpeed = 12f;
    [SerializeField] private float acceleration = 8f;
    [SerializeField] private float drag = 2f;
    [SerializeField] private float maxSteerAngle = 35f;
    [SerializeField, Range(0f, 1f)] private float steerAtMaxSpeed = 0.4f;
    [SerializeField, Range(0f, 1f)] private float steerAtZeroSpeed = 1f;

    [Header("Debug")]
    [SerializeField] private bool logParseErrors = true;
    [SerializeField] private bool logIncomingJson = false;

    [SerializeField] private CarCommand _current;
    [SerializeField] private float _currentSpeed;
    private bool _llmActive;

    [Serializable]
    public struct CarCommand
    {
        public string logic;
        public float steer;
        public float throttle;

        public void Clamp()
        {
            steer = Mathf.Clamp(steer, -1f, 1f);
            throttle = Mathf.Clamp(throttle, -1f, 1f);
        }
    }

    public CarCommand Current => _current;

    // --- ILlmControllable ---

    public bool TryApplyJson(string json)
    {
        try
        {
            if (logIncomingJson)
                Debug.Log($"Car actuator JSON received:\n{json}");

            var cleaned = StripLineComments(json);
            var cmd = JsonUtility.FromJson<CarCommand>(cleaned);
            cmd.Clamp();
            _current = cmd;
            _llmActive = true;
            return true;
        }
        catch (Exception ex)
        {
            if (logParseErrors)
                Debug.LogWarning($"Failed to parse car actuator JSON: {ex.Message}\nInput: {json}");
            return false;
        }
    }

    public string BuildObservationJson(Vector3 velocity, Transform target)
    {
        var targetPos = target ? target.position : Vector3.zero;
        var obs = new CarObservation
        {
            position = transform.position,
            forward = transform.forward,
            velocity = velocity,
            speed = velocity.magnitude,
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

    // --- Keyboard debug ---

    private void ApplyKeyboardDebug()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var kb = Keyboard.current;
        if (kb == null)
            return;

        bool anyKey = kb.wKey.isPressed || kb.sKey.isPressed || kb.aKey.isPressed || kb.dKey.isPressed;

        if (anyKey)
            _llmActive = false;

        if (_llmActive)
            return;

        _current.steer = (kb.aKey.isPressed ? -1f : 0f) + (kb.dKey.isPressed ? 1f : 0f);
        _current.throttle = (kb.wKey.isPressed ? 1f : 0f) + (kb.sKey.isPressed ? -1f : 0f);
#else
        bool anyKey = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D);

        if (anyKey)
            _llmActive = false;

        if (_llmActive)
            return;

        _current.steer = Input.GetAxisRaw("Horizontal");
        _current.throttle = Input.GetAxisRaw("Vertical");
#endif
    }

    // --- Motion ---

    private void ApplyMotion(float dt)
    {
        // Acceleration towards target speed
        float targetSpeed = _current.throttle * maxSpeed;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, acceleration * dt);

        // Drag always applies (rolling resistance / air resistance)
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, 0f, drag * dt);

        transform.position += transform.forward * (_currentSpeed * dt);

        // Steering — lerp between configurable rates at zero vs max speed
        if (Mathf.Abs(_currentSpeed) > 0.01f)
        {
            float speedRatio = Mathf.Clamp01(Mathf.Abs(_currentSpeed) / maxSpeed);
            float steerScale = Mathf.Lerp(steerAtZeroSpeed, steerAtMaxSpeed, speedRatio);
            float steerAmount = _current.steer * maxSteerAngle * steerScale;
            float direction = Mathf.Sign(_currentSpeed);
            transform.Rotate(0f, steerAmount * direction * dt, 0f, Space.Self);
        }
    }

    // --- Helpers ---

    [Serializable]
    private struct CarObservation
    {
        public Vector3 position;
        public Vector3 forward;
        public Vector3 velocity;
        public float speed;
        public Vector3 targetPosition;
        public CarCommand lastCommand;
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
