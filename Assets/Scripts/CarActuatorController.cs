using System;
using UnityEngine;

/// <summary>
/// Parses car actuator commands from an LLM and exposes the latest clamped values.
/// Attach this to your car object alongside SimpleCarPhysics.
/// </summary>
public class CarActuatorController : MonoBehaviour, ILlmControllable
{
    [SerializeField] private CarCommand _current;
    [SerializeField] private bool _logParseErrors = true;
    [SerializeField] private bool _logIncomingJson = false;

    public CarCommand Current => _current;

    [Serializable]
    public struct CarCommand
    {
        public string logic;
        public float steer;      // -1 (left) .. 1 (right)
        public float throttle;   // -1 (reverse) .. 1 (forward)
        public float brake;      // 0 .. 1
        public float handbrake;  // 0 .. 1

        public void Clamp()
        {
            steer = Mathf.Clamp(steer, -1f, 1f);
            throttle = Mathf.Clamp(throttle, -1f, 1f);
            brake = Mathf.Clamp01(brake);
            handbrake = Mathf.Clamp01(handbrake);
        }
    }

    public bool TryApplyJson(string json)
    {
        try
        {
            if (_logIncomingJson)
                Debug.Log($"Car actuator JSON received:\n{json}");

            var cleaned = StripLineComments(json);
            var cmd = JsonUtility.FromJson<CarCommand>(cleaned);
            cmd.Clamp();
            _current = cmd;
            return true;
        }
        catch (Exception ex)
        {
            if (_logParseErrors)
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
