using MAC_1.Service.Listeners;

namespace MAC_1.Service.Core;

public class ServiceHost
{
    private readonly SimpleHttpServer _httpServer;
    private readonly PipeServer _pipeServer;

    public ServiceHost()
    {
        _pipeServer = new PipeServer();
        _httpServer = new SimpleHttpServer(57575, _pipeServer);

        // When HTTP server receives a session, forward it to pipe
        _httpServer.SessionReceived += async sessionJson =>
        {
            await _pipeServer.SendSessionToWpf(sessionJson);
        };
    }

    public void Start()
    {
        _pipeServer.Start();
        _httpServer.Start();
        Log("Service started successfully");
        Log("  HTTP: http://127.0.0.1:57575/");
        Log("  Pipe: MAC-1-Extension");
    }

    public void Stop()
    {
        _httpServer.Stop();
        _pipeServer.Stop();
        Log("Service stopped");
    }

    private void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ServiceHost] {message}");
    }
}
