using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Ticker that periodically queries LM Studio with observations and applies the response as an ActuatorCommand.
/// Lives on the same GameObject as PlaneActuatorController.
/// </summary>
public class LmStudioActuatorTicker : MonoBehaviour
{
    [Header("LM Studio Connection")]
    [SerializeField] private string baseUrl = "http://localhost:1234/v1";
    [SerializeField] private string apiKey = "lm-studio";
    [SerializeField] private string model = "your-model-identifier";
    [SerializeField, TextArea(3, 10)] private string systemPrompt =
        "You control an aircraft. Respond only with one JSON object. No markdown, no code fences, no extra text. " +
        "Include a reasoning string field named \"logic\". In logic, follow these steps EXACTLY:\n\n" +
        "STEP 1 - BANK CORRECTION (HIGHEST PRIORITY!):\n" +
        "Compute: right = cross(up, forward). Check right.y:\n" +
        "- right.y > 0.1 => banking right => aileron=-1 (counter-roll left)\n" +
        "- right.y < -0.1 => banking left => aileron=+1 (counter-roll right)\n" +
        "- |right.y| <= 0.1 => level => aileron=0\n" +
        "Aileron is ONLY for keeping the aircraft level. NEVER use aileron to turn.\n\n" +
        "STEP 2 - NAVIGATION:\n" +
        "1) toTarget = targetPosition - position (SUBTRACT position from target!)\n" +
        "2) desiredDir = normalize(toTarget)\n" +
        "3) Compute navCrossY using this EXACT formula: navCrossY = forward.z * desiredDir.x - forward.x * desiredDir.z\n" +
        "4) If navCrossY > 0 => target is RIGHT => rudder=+1. If navCrossY < 0 => target LEFT => rudder=-1. If |navCrossY| < 0.1 => aligned => rudder=0.\n" +
        "5) dotUp = dot(up, desiredDir). If dotUp < -0.1 => target BELOW => elevator=+1 (pitches nose down). If dotUp > 0.1 => target ABOVE => elevator=-1 (pitches nose up). Else elevator=0.\n\n" +
        "STEP 3 - THROTTLE: +1 to speed up, 0 to hold, -1 to slow down.\n\n" +
        "STEP 4 - DESCRIBE in logic: bank status, heading, pitch, chosen action.\n\n" +
        "Directional input fields (like holding a keyboard key): " +
        "aileron (-1 = roll left, 0 = neutral, 1 = roll right), " +
        "elevator (-1 = pitch nose up, 0 = neutral, 1 = pitch nose down), " +
        "rudder (-1 = yaw left, 0 = neutral, 1 = yaw right), " +
        "throttle (-1 = decrease, 0 = hold, 1 = increase), " +
        "airbrake (0 = off, 1 = on). " +
        "All values must be exactly -1, 0, or 1 (integers). Do not include wheel brakes or any other fields. Example:\n" +
        "{\n" +
        "  \"logic\": \"toTarget=target-pos=(-5,-15,-38); desiredDir=(-0.12,-0.36,-0.92); " +
        "navCrossY = fwd.z*dir.x - fwd.x*dir.z = 0*(-0.12) - 1*(-0.92) = 0.92 > 0 => target RIGHT => rudder=+1. " +
        "dotUp=dot(up,desiredDir)=-0.36<-0.1 => target BELOW => elevator=+1 (nose down). Throttle up.\",\n" +
        "  \"aileron\": 0,\n" +
        "  \"elevator\": 1,\n" +
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

    private ILlmControllable _controllable;
    private LmStudioClient _client;
    private CancellationTokenSource _cts;
    private bool _loopRunning;
    private Vector3 _lastPos;
    private bool _hasLastPos;
    private Vector3 _velocity;

    private void Awake()
    {
        _controllable = GetComponent<ILlmControllable>();
        if (_controllable == null)
            Debug.LogError("LmStudioActuatorTicker: No ILlmControllable found on this GameObject.", this);

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
        var obsJson = _controllable.BuildObservationJson(_velocity, packageTarget);
        return new List<(string role, string content)>
        {
            ("system", systemPrompt),
            ("user", $"Observation:\n{obsJson}\n\nReturn only the JSON with the actuator values.")
        };
    }

    private void ApplyResponse(string reply)
    {
        if (_controllable == null)
            return;

        var json = ExtractFirstJson(reply);
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("LM reply did not contain parsable JSON.");
            return;
        }

        _controllable.TryApplyJson(json);
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

}
