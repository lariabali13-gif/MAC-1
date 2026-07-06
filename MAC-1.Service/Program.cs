using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using MAC_1.Service.Core;

// Force TLS settings globally — fixes servers that reject default .NET TLS config
ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
ServicePointManager.DefaultConnectionLimit = 100;
ServicePointManager.Expect100Continue = false;
// Accept all certificates globally
ServicePointManager.ServerCertificateValidationCallback = (_, _, _, _) => true;

// Set default SSL protocols for all HttpClient instances
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", false);

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
