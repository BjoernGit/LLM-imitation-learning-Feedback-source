using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Ticker that periodically queries LM Studio with observations and applies the response as an ActuatorCommand.
/// Lives on the same GameObject as PlaneActuatorController.
/// </summary>
[RequireComponent(typeof(PlaneActuatorController))]
public class LmStudioActuatorTicker : MonoBehaviour
{
    [Header("LM Studio Connection")]
    [SerializeField] private string baseUrl = "http://localhost:1234/v1";
    [SerializeField] private string apiKey = "lm-studio";
    [SerializeField] private string model = "your-model-identifier";
    [SerializeField, TextArea(3, 10)] private string systemPrompt =
        "You control an aircraft. Respond only with one JSON object. No markdown, no code fences, no extra text. " +
        "Include a reasoning string field named \"logic\". In logic, follow these steps EXACTLY:\n\n" +
        "STEP 1 - ATTITUDE CHECK (PRIORITY!):\n" +
        "Compute: right = cross(up, forward). Check right.y to determine bank angle:\n" +
        "- right.y > 0.1 => aircraft is banking RIGHT (right wing is lower)\n" +
        "- right.y < -0.1 => aircraft is banking LEFT (left wing is lower)\n" +
        "- |right.y| <= 0.1 => aircraft is upright\n" +
        "Also check up.y: up.y=1 upright, up.y near 0 on its side, up.y<0 inverted.\n" +
        "- The aircraft MUST fly upright at all times. Only allow banking during an active turn.\n" +
        "- If banking and NOT turning: correct roll (banking right => aileron=-1, banking left => aileron=+1), rudder=0, elevator=0.\n\n" +
        "STEP 2 - NAVIGATION:\n" +
        "1) toTarget = targetPosition - position (SUBTRACT position from target!)\n" +
        "2) desiredDir = normalize(toTarget)\n" +
        "3) navCross = cross(forward, desiredDir)\n" +
        "4) If navCross.y > 0 => target is to the RIGHT => rudder=+1, aileron=+1. If navCross.y < 0 => target is LEFT => rudder=-1, aileron=-1. If nearly aligned (|navCross.y| < 0.1) => rudder=0, aileron=0.\n" +
        "5) dotUp = dot(up, desiredDir). If dotUp > 0 => target is ABOVE => elevator=+1. If dotUp < 0 => target is BELOW => elevator=-1.\n" +
        "6) Choose throttle: +1 to speed up, 0 to hold, -1 to slow down.\n\n" +
        "STEP 3 - DESCRIBE YOUR MANEUVER in the logic field:\n" +
        "- State current attitude: upright / banking left / banking right / inverted.\n" +
        "- State planned maneuver: straight flight / turning left / turning right / leveling out / correcting roll.\n" +
        "- State if current bank angle is proportional to the maneuver or needs correction.\n\n" +
        "Directional input fields (like holding a keyboard key): " +
        "aileron (-1 = roll left, 0 = neutral, 1 = roll right), " +
        "elevator (-1 = pitch down, 0 = neutral, 1 = pitch up), " +
        "rudder (-1 = yaw left, 0 = neutral, 1 = yaw right), " +
        "throttle (-1 = decrease, 0 = hold, 1 = increase), " +
        "airbrake (0 = off, 1 = on). " +
        "All values must be exactly -1, 0, or 1 (integers). Do not include wheel brakes or any other fields. Example:\n" +
        "{\n" +
        "  \"logic\": \"ATTITUDE: right=cross(up,fwd)=cross((0,0.95,0.3),(1,0,0))=(0,0.3,-0.95); right.y=0.3>0.1 => banking right. " +
        "up.y=0.95 => mostly upright. " +
        "NAV: toTarget=target-pos=(-5,-15,-38); desiredDir=norm(toTarget)=(-0.12,-0.36,-0.92); " +
        "navCross=cross(fwd,desiredDir)=(0,0.92,-0.36); navCross.y=0.92>0 => target is RIGHT. " +
        "dotUp=dot(up,desiredDir)=-0.36<0 => target is BELOW. " +
        "MANEUVER: turning right, bank is proportional to turn, pitch down to descend.\",\n" +
        "  \"aileron\": 1,\n" +
        "  \"elevator\": -1,\n" +
        "  \"rudder\": 1,\n" +
        "  \"throttle\": 1,\n" +
        "  \"airbrake\": 0\n" +
        "}";
    [SerializeField] private float temperature = 0.4f;
    [SerializeField] private int maxTokens = 300;

    [Header("Timing")]
    [SerializeField] private float tickIntervalSeconds = 0.5f;
    [SerializeField] private float requestTimeoutSeconds = 8f;
    [SerializeField] private bool autoStart = true;
    [SerializeField] private bool logRawReply = false;
    [SerializeField] private float llmTimeScale = 0.1f;

    [Header("Target")]
    [SerializeField] private Transform packageTarget;

    [Header("References")]
    [SerializeField] private PlaneActuatorController actuator;

    private LmStudioClient _client;
    private CancellationTokenSource _cts;
    private bool _loopRunning;
    private Vector3 _lastPos;
    private bool _hasLastPos;
    private Vector3 _velocity;

    private void Awake()
    {
        if (!actuator)
            actuator = GetComponent<PlaneActuatorController>();

        _client = new LmStudioClient(baseUrl, apiKey);
    }

    private void Start()
    {
        if (autoStart)
            StartLoop();
    }

    private void Update()
    {
        var pos = transform.position;
        if (_hasLastPos)
        {
            var dt = Mathf.Max(Time.deltaTime, 1e-4f);
            _velocity = (pos - _lastPos) / dt;
        }
        _lastPos = pos;
        _hasLastPos = true;
    }

    public void StartLoop()
    {
        if (_loopRunning || string.IsNullOrWhiteSpace(model))
        {
            if (string.IsNullOrWhiteSpace(model))
                Debug.LogWarning("LM ticker: model is empty, not starting.");
            return;
        }

        _cts = new CancellationTokenSource();
        _loopRunning = true;
        Time.timeScale = llmTimeScale;
        _ = RunLoopAsync(_cts.Token);
    }

    public void StopLoop()
    {
        _loopRunning = false;
        Time.timeScale = 1f;
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        var first = true;
        while (!token.IsCancellationRequested)
        {
            if (!first)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0.05f, tickIntervalSeconds)), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            first = false;

            // Skip tick while the game is paused
            if (Time.timeScale == 0f)
                continue;
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPaused)
                continue;
#endif

            try
            {
                var reply = await QueryOnceAsync(token);
                if (!token.IsCancellationRequested && !string.IsNullOrWhiteSpace(reply))
                    ApplyResponse(reply);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"LM ticker: request failed ({ex.Message})");
            }
        }
    }

    private async Task<string> QueryOnceAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (requestTimeoutSeconds > 0f)
            linked.CancelAfter(TimeSpan.FromSeconds(requestTimeoutSeconds));

        var messages = BuildMessages();
        var reply = await _client.ChatAsync(model, messages, temperature, maxTokens, linked.Token);
        if (logRawReply)
            Debug.Log($"LM reply: {reply}");
        return reply;
    }

    private List<(string role, string content)> BuildMessages()
    {
        var obs = BuildObservation();
        var obsJson = JsonUtility.ToJson(obs, true);
        return new List<(string role, string content)>
        {
            ("system", systemPrompt),
            ("user", $"Observation:\n{obsJson}\n\nReturn only the JSON with the actuator values.")
        };
    }

    private PlaneObservation BuildObservation()
    {
        var cmd = actuator ? actuator.Current : default;
        var targetPos = packageTarget ? packageTarget.position : Vector3.zero;
        return new PlaneObservation
        {
            position = transform.position,
            forward = transform.forward,
            up = transform.up,
            velocity = _velocity,
            targetPosition = targetPos,
            lastCommand = cmd
        };
    }

    private void ApplyResponse(string reply)
    {
        if (!actuator)
            return;

        var json = ExtractFirstJson(reply);
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("LM reply did not contain parsable JSON.");
            return;
        }

        actuator.TryApplyJson(json);
    }

    private static string ExtractFirstJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start)
            return string.Empty;
        return text.Substring(start, end - start + 1);
    }

    private void OnDestroy()
    {
        StopLoop();
    }

    [Serializable]
    private struct PlaneObservation
    {
        public Vector3 position;
        public Vector3 forward;
        public Vector3 up;
        public Vector3 velocity;
        public Vector3 targetPosition;
        public PlaneActuatorController.ActuatorCommand lastCommand;
    }
}
