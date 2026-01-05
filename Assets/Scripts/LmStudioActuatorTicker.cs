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
    [SerializeField, TextArea] private string systemPrompt =
        "You control an aircraft. Respond only with a JSON object containing the fields " +
        "aileron (-1..1), elevator (-1..1), rudder (-1..1), throttle (0..1), airbrake (0..1), wheelBrakes (0..1).";
    [SerializeField] private float temperature = 0.4f;
    [SerializeField] private int maxTokens = 120;

    [Header("Timing")]
    [SerializeField] private float tickIntervalSeconds = 0.5f;
    [SerializeField] private float requestTimeoutSeconds = 8f;
    [SerializeField] private bool autoStart = true;
    [SerializeField] private bool logRawReply = false;

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
        _ = RunLoopAsync(_cts.Token);
    }

    public void StopLoop()
    {
        _loopRunning = false;
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
        return new PlaneObservation
        {
            position = transform.position,
            forward = transform.forward,
            up = transform.up,
            velocity = _velocity,
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
        public PlaneActuatorController.ActuatorCommand lastCommand;
    }
}
