using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class LmStudioClient
{
    private readonly string _baseUrl;
    private readonly string _apiKey;

    public LmStudioClient(string baseUrl = "http://localhost:1234/v1", string apiKey = "lm-studio")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
    }

    public async Task<string> ChatAsync(
        string model,
        IReadOnlyList<(string role, string content)> messages,
        float temperature = 0.7f,
        int maxTokens = 512,
        CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/chat/completions";

        var req = new ChatCompletionsRequest
        {
            model = model,
            temperature = temperature,
            max_tokens = maxTokens,
            messages = BuildMessages(messages)
        };

        var json = JsonUtility.ToJson(req);
        using var uwr = new UnityWebRequest(url, "POST");

        var bodyRaw = Encoding.UTF8.GetBytes(json);
        uwr.uploadHandler = new UploadHandlerRaw(bodyRaw);
        uwr.downloadHandler = new DownloadHandlerBuffer();
        uwr.SetRequestHeader("Content-Type", "application/json");

        // Some OpenAI-compatible clients/servers accept a placeholder key; keep it for compatibility.
        if (!string.IsNullOrEmpty(_apiKey))
            uwr.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

        var op = uwr.SendWebRequest();

        while (!op.isDone)
        {
            if (ct.IsCancellationRequested)
            {
                uwr.Abort();
                ct.ThrowIfCancellationRequested();
            }
            await Task.Yield();
        }

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            var errBody = uwr.downloadHandler?.text;
            throw new Exception($"LM Studio request failed: {uwr.responseCode} {uwr.error}\n{errBody}");
        }

        var respJson = uwr.downloadHandler.text;
        var resp = JsonUtility.FromJson<ChatCompletionsResponse>(respJson);

        if (resp?.choices == null || resp.choices.Length == 0 || resp.choices[0]?.message == null)
            throw new Exception($"LM Studio response parsing failed.\n{respJson}");

        return resp.choices[0].message.content ?? string.Empty;
    }

    private static ChatMessage[] BuildMessages(IReadOnlyList<(string role, string content)> messages)
    {
        var list = new List<ChatMessage>(messages.Count);
        for (int i = 0; i < messages.Count; i++)
            list.Add(new ChatMessage { role = messages[i].role, content = messages[i].content });
        return list.ToArray();
    }

    [Serializable]
    private sealed class ChatCompletionsRequest
    {
        public string model;
        public float temperature;
        public int max_tokens;
        public ChatMessage[] messages;
    }

    [Serializable]
    private sealed class ChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    private sealed class ChatCompletionsResponse
    {
        public Choice[] choices;
    }

    [Serializable]
    private sealed class Choice
    {
        public int index;
        public ChatMessage message;
        public string finish_reason;
    }
}
