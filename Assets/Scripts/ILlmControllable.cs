using UnityEngine;

/// <summary>
/// Interface for any agent that can be controlled by an LLM.
/// The implementer decides which observation data to expose and how to parse commands.
/// </summary>
public interface ILlmControllable
{
    /// <summary>
    /// Build a JSON string describing the current state for the LLM.
    /// </summary>
    string BuildObservationJson(Vector3 velocity, Transform target);

    /// <summary>
    /// Parse and apply the JSON command returned by the LLM.
    /// </summary>
    bool TryApplyJson(string json);
}
