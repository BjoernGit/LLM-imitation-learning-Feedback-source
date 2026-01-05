using System.Threading;
using UnityEngine;

public class LmStudioDemo : MonoBehaviour
{
    private CancellationTokenSource _cts;

    private async void Start()
    {
        _cts = new CancellationTokenSource();

        var client = new LmStudioClient("http://localhost:1234/v1", "lm-studio");

        var reply = await client.ChatAsync(
            model: "your-model-identifier",
            messages: new[]
            {
                ("system", "You are a helpful assistant."),
                ("user", "Tell me in one sentence how to talk to LM Studio cleanly from Unity.")
            },
            temperature: 0.4f,
            maxTokens: 200,
            ct: _cts.Token
        );

        Debug.Log(reply);
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
