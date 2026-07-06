using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MAC_1.Services;

public class PipeClient
{
    private const string PipeName = "MAC-1-Extension";
    private NamedPipeClientStream? _client;
    private CancellationTokenSource? _cts;
    private bool _isConnected;

    public event Action<string>? SessionReceived;
    public event Action<string>? ProgressReceived;
    public event Action<string>? DownloadCompleted;
    public event Action<string>? DownloadFailed;
    public event Action<string>? DownloadPaused;
    public event Action? Connected;
    public event Action? Disconnected;

    public bool IsConnected => _isConnected;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = ConnectLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _isConnected = false;
        _cts?.Cancel();
        try { _client?.Dispose(); } catch { }
    }

    private async Task ConnectLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                Log("Connecting to service pipe...");
                _client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _client.ConnectAsync(5000, token);
                _isConnected = true;
                Connected?.Invoke();
                Log("Connected to service pipe!");

                await SendMessageAsync("ready");

                var buffer = new byte[65536];
                var messageBuffer = new StringBuilder();

                while (_client.IsConnected && !token.IsCancellationRequested)
                {
                    try
                    {
                        int bytesRead = await _client.ReadAsync(buffer, 0, buffer.Length, token);
                        if (bytesRead == 0) break;

                        string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        messageBuffer.Append(chunk);

                        string pending = messageBuffer.ToString();
                        int jsonStart = pending.IndexOf('{');
                        int jsonEnd = FindJsonEnd(pending, jsonStart);

                        while (jsonStart >= 0 && jsonEnd > jsonStart)
                        {
                            string jsonMessage = pending.Substring(jsonStart, jsonEnd - jsonStart + 1);
                            messageBuffer.Clear();
                            messageBuffer.Append(pending.Substring(jsonEnd + 1));

                            HandleMessage(jsonMessage);

                            pending = messageBuffer.ToString();
                            jsonStart = pending.IndexOf('{');
                            jsonEnd = FindJsonEnd(pending, jsonStart);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Read error: {ex.Message}");
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log($"Connection error: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                Disconnected?.Invoke();
                try { _client?.Dispose(); } catch { }
                _client = null;
            }

            Log("Reconnecting in 2 seconds...");
            await Task.Delay(2000, token);
        }
    }

    private int FindJsonEnd(string text, int startIndex)
    {
        if (startIndex < 0) return -1;
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = startIndex; i < text.Length; i++)
        {
            char c = text[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }

            if (!inString)
            {
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
        }
        return -1;
    }

    private void HandleMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            string type = root.GetProperty("type").GetString() ?? "";

            Log($"Message type: {type}");

            switch (type)
            {
                case "session":
                    string sessionData = root.GetProperty("data").GetRawText();
                    SessionReceived?.Invoke(sessionData);
                    break;

                case "progress":
                    string progressData = root.GetRawText();
                    ProgressReceived?.Invoke(progressData);
                    break;

                case "completed":
                    string completedData = root.GetRawText();
                    DownloadCompleted?.Invoke(completedData);
                    break;

                case "failed":
                    string failedData = root.GetRawText();
                    DownloadFailed?.Invoke(failedData);
                    break;

                case "paused":
                    string pausedData = root.GetRawText();
                    DownloadPaused?.Invoke(pausedData);
                    break;

                case "heartbeat_ack":
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Parse error: {ex.Message}");
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (_client == null || !_client.IsConnected) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await _client.WriteAsync(bytes, 0, bytes.Length);
            await _client.FlushAsync();
        }
        catch { }
    }

    private void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[MAC-1] [Pipe] {message}");
        try
        {
            var logFile = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MAC-1", "wpf-app.log");
            System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] [Pipe] {message}\n");
        }
        catch { }
    }
}
