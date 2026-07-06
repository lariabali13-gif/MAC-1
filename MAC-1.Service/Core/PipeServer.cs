using System.IO.Pipes;
using System.Text;

namespace MAC_1.Service.Core;

public class PipeServer
{
    private const string PipeName = "MAC-1-Extension";
    private bool _isRunning;
    private CancellationTokenSource? _cts;
    private NamedPipeServerStream? _server;
    private readonly List<string> _pendingMessages = new();
    private readonly object _lock = new();

    public event Action<string>? MessageSent;
    public bool IsConnected => _server?.IsConnected == true;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _isRunning = true;
        _ = AcceptClientsAsync(_cts.Token);
        Log("Pipe server started on: " + PipeName);
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        try { _server?.Dispose(); } catch { }
    }

    private async Task AcceptClientsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isRunning)
        {
            try
            {
                // Create a fresh pipe server for each connection
                _server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                Log("Waiting for WPF client...");
                await _server.WaitForConnectionAsync(token);
                Log("WPF client connected!");

                // Send pending messages
                lock (_lock)
                {
                    foreach (var msg in _pendingMessages)
                    {
                        _ = SendToClient(msg);
                    }
                    _pendingMessages.Clear();
                }

                // Read from client
                await HandleClientAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log($"Pipe error: {ex.Message}");
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (_server != null)
                    {
                        if (_server.IsConnected) _server.Disconnect();
                        _server.Dispose();
                    }
                } catch { }
                _server = null;
                Log("WPF client disconnected, waiting for reconnection...");
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(CancellationToken token)
    {
        try
        {
            var buffer = new byte[65536];
            while (_server != null && _server.IsConnected && !token.IsCancellationRequested)
            {
                int bytesRead = await _server.ReadAsync(buffer, token);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Log($"Received from WPF: {message[..Math.Min(100, message.Length)]}");

                if (message.Contains("ready") || message.Contains("heartbeat"))
                {
                    await SendToClient("{\"type\":\"heartbeat_ack\"}");
                }
            }
        }
        catch { }
    }

    public async Task SendToWpf(string json)
    {
        // json already contains the full message with type field
        if (_server != null && _server.IsConnected)
        {
            await SendToClient(json);
            Log($"Sent to WPF: {json[..Math.Min(80, json.Length)]}...");
        }
        else
        {
            Log("WPF not connected, launching popup mode...");
            lock (_lock) { _pendingMessages.Add(json); }

            // If this is a session message, launch WPF in popup mode
            if (json.Contains("\"type\":\"session\"") || json.Contains("\"sessionId\""))
            {
                LaunchWpfPopupMode();
            }
        }
    }

    private bool _wpfPopupLaunched;
    private DateTime _lastLaunchTime = DateTime.MinValue;

    private void LaunchWpfPopupMode()
    {
        // Throttle: don't launch more than once per 5 seconds
        if ((DateTime.Now - _lastLaunchTime).TotalSeconds < 5) return;
        _lastLaunchTime = DateTime.Now;

        try
        {
            // Find MAC-1.exe relative to the service executable
            string serviceDir = AppContext.BaseDirectory;
            string wpfExe = Path.Combine(serviceDir, "..", "..", "..", "..",
                "bin", "Debug", "net8.0-windows", "MAC-1.exe");

            // Fallback: try same directory
            if (!File.Exists(wpfExe))
                wpfExe = Path.Combine(serviceDir, "MAC-1.exe");

            // Fallback: try relative to service project
            if (!File.Exists(wpfExe))
                wpfExe = Path.Combine(serviceDir, "..", "..", "..", "..",
                    "bin", "Debug", "net8.0-windows", "MAC-1.exe");

            if (File.Exists(wpfExe))
            {
                Log($"Launching WPF in popup mode: {wpfExe}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = wpfExe,
                    Arguments = "--popup",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else
            {
                Log($"WPF executable not found at {wpfExe}");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to launch WPF: {ex.Message}");
        }
    }

    // Keep backward compat for session events
    public async Task SendSessionToWpf(string json)
    {
        var message = $"{{\"type\":\"session\",\"data\":{json}}}";
        await SendToWpf(message);
    }

    private async Task SendToClient(string message)
    {
        try
        {
            if (_server == null || !_server.IsConnected) return;
            var bytes = Encoding.UTF8.GetBytes(message);
            await _server.WriteAsync(bytes, 0, bytes.Length);
            await _server.FlushAsync();
        }
        catch (Exception ex)
        {
            Log($"Send error: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Pipe] {message}");
    }
}
