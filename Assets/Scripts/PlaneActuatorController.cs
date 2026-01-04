using System;
using UnityEngine;

/// <summary>
/// Parses actuator commands coming from an LLM and exposes the latest clamped values.
/// Attach this to your aircraft object.
/// </summary>
public class PlaneActuatorController : MonoBehaviour
{
    [SerializeField] private ActuatorCommand _current; // Latest command after clamping
    [SerializeField] private bool _logParseErrors = true;

    /// <summary>
    /// Latest parsed and clamped command.
    /// </summary>
    public ActuatorCommand Current => _current;

    /// <summary>
    /// Call this with the JSON string returned by the LLM.
    /// Expects: aileron, elevator, rudder (-1..1), throttle (0..1), airbrake (0..1), wheelBrakes (0..1).
    /// </summary>
    public bool TryApplyJson(string json)
    {
        try
        {
            var cmd = JsonUtility.FromJson<ActuatorCommand>(json);
            cmd.Clamp01();
            _current = cmd;

            // TODO: apply to your flight model here (forces/inputs/animations).
            return true;
        }
        catch (Exception ex)
        {
            if (_logParseErrors)
                Debug.LogWarning($"Failed to parse actuator JSON: {ex.Message}\nInput: {json}");
            return false;
        }
    }

    /// <summary>
    /// Optional helper to inspect current command as JSON.
    /// </summary>
    public string CurrentAsJson() => JsonUtility.ToJson(_current, true);

    [Serializable]
    public struct ActuatorCommand
    {
        public float aileron;     // roll, -1..1
        public float elevator;    // pitch, -1..1
        public float rudder;      // yaw, -1..1
        public float throttle;    // 0..1
        public float airbrake;    // 0..1
        public float wheelBrakes; // 0..1

        public void Clamp01()
        {
            aileron = Mathf.Clamp(aileron, -1f, 1f);
            elevator = Mathf.Clamp(elevator, -1f, 1f);
            rudder = Mathf.Clamp(rudder, -1f, 1f);
            throttle = Mathf.Clamp01(throttle);
            airbrake = Mathf.Clamp01(airbrake);
            wheelBrakes = Mathf.Clamp01(wheelBrakes);
        }
    }
}
