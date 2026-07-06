using MAC_1.Service.Core;

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║       MAC-1 Background Service          ║");
Console.WriteLine("║   Download Manager Backend              ║");
Console.WriteLine("╚══════════════════════════════════════════╝");

var host = new ServiceHost();
host.Start();

Console.WriteLine("[READY] Service is running on http://127.0.0.1:57575");
Console.WriteLine("[READY] Press Ctrl+C to stop.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    host.Stop();
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (TaskCanceledException) { }
