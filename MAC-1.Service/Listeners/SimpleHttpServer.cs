using System.Net;
using System.Text;
using System.Text.Json;

namespace MAC_1.Service.Listeners;

public class SimpleHttpServer
{
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    private readonly List<ReceivedSession> _sessions = new();
    private readonly Core.PipeServer _pipeServer;

    public bool IsRunning => _isRunning;

    // Event: when a session is received, forward JSON to pipe
    public event Action<string>? SessionReceived;

    public SimpleHttpServer(int port, Core.PipeServer pipeServer)
    {
        _port = port;
        _pipeServer = pipeServer;
    }

    public void Start()
    {
        if (_isRunning) return;
        try
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
            _isRunning = true;
            _ = AcceptConnectionsAsync(_cts.Token);
            Log("HTTP server started on port " + _port);
        }
        catch (Exception ex) { Log("Failed to start: " + ex.Message); }
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
    }

    private async Task AcceptConnectionsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isRunning)
        {
            try
            {
                var context = await _listener!.GetContextAsync();
                _ = HandleRequestAsync(context);
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Log("Accept error: " + ex.Message); }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS") { response.StatusCode = 200; response.Close(); return; }

            string path = request.Url!.AbsolutePath.ToLower();

            if (path == "/api/health" && request.HttpMethod == "GET")
                await HandleHealth(response);
            else if (path == "/api/session" && request.HttpMethod == "POST")
                await HandleSession(request, response);
            else if (path == "/api/sessions" && request.HttpMethod == "GET")
                await HandleGetSessions(response);
            else if (path == "/api/start-download" && request.HttpMethod == "POST")
                await HandleStartDownload(request, response);
            else if (path == "/api/pause-download" && request.HttpMethod == "POST")
                await HandlePauseDownload(request, response);
            else if (path == "/api/resume-download" && request.HttpMethod == "POST")
                await HandleResumeDownload(request, response);
            else if (path == "/api/cancel-download" && request.HttpMethod == "POST")
                await HandleCancelDownload(request, response);
            else if (path == "/api/retry-download" && request.HttpMethod == "POST")
                await HandleRetryDownload(request, response);
            else { response.StatusCode = 404; await WriteJson(response, new { error = "Not found" }); }
        }
        catch (Exception ex) { Log("Error: " + ex.Message); response.StatusCode = 500; await WriteJson(response, new { error = ex.Message }); }
        finally { try { response.Close(); } catch { } }
    }

    private async Task HandleHealth(HttpListenerResponse response)
    {
        await WriteJson(response, new { status = "ok", service = "MAC-1 Background Service", version = "0.2.0", sessions = _sessions.Count });
    }

    private async Task HandleSession(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                body = await reader.ReadToEndAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var session = JsonSerializer.Deserialize<ReceivedSession>(body, options);

            if (session == null || string.IsNullOrEmpty(session.Url))
            {
                response.StatusCode = 400;
                await WriteJson(response, new { error = "Invalid session data" });
                return;
            }

            session.Id = Guid.NewGuid().ToString("N")[..12];
            session.ReceivedAt = DateTime.Now;
            _sessions.Add(session);

            // === DETAILED CAPTURE REPORT ===
            PrintCaptureReport(session);

            // === Forward to WPF via Pipe (include session ID) ===
            // Add sessionId to the body
            var bodyDict = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();
            bodyDict["sessionId"] = session.Id;
            var bodyWithId = JsonSerializer.Serialize(bodyDict);
            SessionReceived?.Invoke(bodyWithId);

            await WriteJson(response, new { success = true, message = "Session received", sessionId = session.Id });
        }
        catch (Exception ex)
        {
            Log("Session error: " + ex.Message);
            response.StatusCode = 500;
            await WriteJson(response, new { error = ex.Message });
        }
    }

    private void PrintCaptureReport(ReceivedSession s)
    {
        int headerCount = s.Headers?.Count ?? 0;
        int rawHeaderCount = s.RawHeaders?.Count ?? 0;
        int cookieCount = s.Cookies?.Count ?? 0;
        int responseHeaderCount = s.ResponseHeaders?.Count ?? 0;

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           MAC-1 SESSION CAPTURE REPORT                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  URL:         {s.Url}");
        Console.WriteLine($"  Final URL:   {s.FinalUrl}");
        Console.WriteLine($"  Filename:    {s.Filename}");
        Console.WriteLine($"  File Size:   {s.FileSize} bytes ({FormatSize(s.FileSize)})");
        Console.WriteLine($"  MIME Type:   {s.MimeType ?? "unknown"}");
        Console.WriteLine($"  Method:      {s.Method}");
        Console.WriteLine($"  Referrer:    {s.Referrer ?? "none"}");
        Console.WriteLine();
        Console.WriteLine("  --- CAPTURE SUMMARY ---");
        Console.WriteLine($"  Request Headers:  {rawHeaderCount} captured");
        Console.WriteLine($"  Response Headers: {responseHeaderCount} captured");
        Console.WriteLine($"  Cookies:          {cookieCount} captured");
        Console.WriteLine($"  Post Data:        {(s.PostData != null ? "YES" : "NO")}");
        Console.WriteLine($"  Tab Info:         {(s.Tab != null ? s.Tab.Title : "none")}");
        Console.WriteLine();

        // Key headers check
        Console.WriteLine("  --- KEY HEADERS CHECK ---");
        bool hasUA = s.Headers?.ContainsKey("user-agent") == true;
        bool hasReferer = s.Headers?.ContainsKey("referer") == true || s.Headers?.ContainsKey("referrer") == true;
        bool hasOrigin = s.Headers?.ContainsKey("origin") == true;
        bool hasCookie = s.Headers?.ContainsKey("cookie") == true;
        bool hasAcceptEnc = s.Headers?.ContainsKey("accept-encoding") == true;
        bool hasSecFetchDest = s.Headers?.ContainsKey("sec-fetch-dest") == true;
        bool hasSecFetchMode = s.Headers?.ContainsKey("sec-fetch-mode") == true;
        bool hasSecFetchSite = s.Headers?.ContainsKey("sec-fetch-site") == true;
        bool hasSecFetchUser = s.Headers?.ContainsKey("sec-fetch-user") == true;
        bool hasSecChUa = s.Headers?.ContainsKey("sec-ch-ua") == true;
        bool hasSecChUaMobile = s.Headers?.ContainsKey("sec-ch-ua-mobile") == true;
        bool hasSecChUaPlatform = s.Headers?.ContainsKey("sec-ch-ua-platform") == true;

        Console.WriteLine($"  User-Agent:        {(hasUA ? "✅" : "❌")} {Truncate(s.Headers?.GetValueOrDefault("user-agent"), 70)}");
        Console.WriteLine($"  Referer:           {(hasReferer ? "✅" : "❌")} {Truncate(s.Headers?.GetValueOrDefault("referer") ?? s.Headers?.GetValueOrDefault("referrer"), 70)}");
        Console.WriteLine($"  Origin:            {(hasOrigin ? "✅" : "❌")} {s.Headers?.GetValueOrDefault("origin") ?? "missing"}");
        Console.WriteLine($"  Cookie:            {(hasCookie ? "✅" : "❌")}");
        Console.WriteLine($"  Accept-Encoding:   {(hasAcceptEnc ? "✅" : "❌")} {s.Headers?.GetValueOrDefault("accept-encoding") ?? "missing"}");
        Console.WriteLine($"  Sec-Fetch-Dest:    {(hasSecFetchDest ? "✅" : "❌")} {s.Headers?.GetValueOrDefault("sec-fetch-dest") ?? "missing"}");
        Console.WriteLine($"  Sec-Fetch-Mode:    {(hasSecFetchMode ? "✅" : "❌")} {s.Headers?.GetValueOrDefault("sec-fetch-mode") ?? "missing"}");
        Console.WriteLine($"  Sec-Fetch-Site:    {(hasSecFetchSite ? "✅" : "❌")} {s.Headers?.GetValueOrDefault("sec-fetch-site") ?? "missing"}");
        Console.WriteLine($"  Sec-Fetch-User:    {(hasSecFetchUser ? "✅" : "❌")} {s.Headers?.GetValueOrDefault("sec-fetch-user") ?? "missing"}");
        Console.WriteLine($"  Sec-CH-UA:         {(hasSecChUa ? "✅" : "❌")} {Truncate(s.Headers?.GetValueOrDefault("sec-ch-ua"), 50)}");
        Console.WriteLine($"  Sec-CH-UA-Mobile:  {(hasSecChUaMobile ? "✅" : "❌")} {s.Headers?.GetValueOrDefault("sec-ch-ua-mobile") ?? "missing"}");
        Console.WriteLine($"  Sec-CH-UA-Platform:{(hasSecChUaPlatform ? "✅" : "❌")} {s.Headers?.GetValueOrDefault("sec-ch-ua-platform") ?? "missing"}");
        Console.WriteLine();

        // All headers
        Console.WriteLine("  --- ALL REQUEST HEADERS ---");
        if (s.RawHeaders != null)
        {
            foreach (var h in s.RawHeaders)
                Console.WriteLine($"    {h.Name}: {Truncate(h.Value, 120)}");
        }
        else
        {
            Console.WriteLine("    (no raw headers captured)");
        }
        Console.WriteLine();

        // All cookies
        Console.WriteLine("  --- ALL COOKIES ---");
        if (s.Cookies != null && s.Cookies.Count > 0)
        {
            foreach (var c in s.Cookies)
                Console.WriteLine($"    {c.Name}={Truncate(c.Value, 40)} (domain={c.Domain}, path={c.Path}, secure={c.Secure})");
        }
        else
        {
            Console.WriteLine("    (no cookies captured)");
        }
        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  TOTAL: {rawHeaderCount} request headers, {responseHeaderCount} response headers, {cookieCount} cookies");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine();
    }

    private async Task HandleGetSessions(HttpListenerResponse response)
    {
        await WriteJson(response, new
        {
            success = true,
            count = _sessions.Count,
            sessions = _sessions.Select(s => new
            {
                id = s.Id, url = s.Url, filename = s.Filename,
                fileSize = s.FileSize, receivedAt = s.ReceivedAt?.ToString("o")
            })
        });
    }

    // === DOWNLOAD ENGINE ===
    private Core.DownloadEngine? _currentEngine;
    private readonly Dictionary<string, long> _failedBytesDownloaded = new();

    private async Task HandleStartDownload(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                body = await reader.ReadToEndAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var downloadReq = JsonSerializer.Deserialize<StartDownloadRequest>(body, options);

            if (downloadReq == null || string.IsNullOrEmpty(downloadReq.SessionId))
            {
                response.StatusCode = 400;
                await WriteJson(response, new { error = "Missing sessionId" });
                return;
            }

            // Find the session
            var session = _sessions.FirstOrDefault(s => s.Id == downloadReq.SessionId);
            if (session == null)
            {
                response.StatusCode = 404;
                await WriteJson(response, new { error = "Session not found" });
                return;
            }

            // Cancel any existing download
            _currentEngine?.Cancel();
            _currentEngine?.Dispose();

            // Extract filename from response headers if not set
            string filename = session.Filename ?? "download";
            if (filename == "download" && session.ResponseHeaders != null)
            {
                foreach (var h in session.ResponseHeaders)
                {
                    if (h.Name?.ToLower() == "content-disposition")
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(h.Value ?? "",
                            @"filename\*?=(?:UTF-8''|"")([^"";\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            try { filename = Uri.UnescapeDataString(match.Groups[1].Value); }
                            catch { filename = match.Groups[1].Value; }
                        }
                        break;
                    }
                }
            }
            if (filename == "download" && !string.IsNullOrEmpty(session.MimeType))
            {
                // Try to guess extension from MIME type
                filename = "download" + GetExtensionFromMime(session.MimeType);
            }

            // Determine download method - CDN URLs always use GET
            string downloadMethod = session.Method ?? "GET";
            if (!string.IsNullOrEmpty(session.FinalUrl) && session.FinalUrl != session.Url)
            {
                // Cross-domain redirect (e.g., datavaults.co -> CDN) - force GET
                downloadMethod = "GET";
                Log($"Cross-domain redirect detected, forcing GET method");
            }

            // Create download request from session
            var dlRequest = new Core.DownloadRequest
            {
                Url = session.Url,
                FinalUrl = session.FinalUrl,
                Filename = downloadReq.SavePath != null ? Path.GetFileName(downloadReq.SavePath) : filename,
                SavePath = downloadReq.SavePath ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads",
                    filename),
                FileSize = session.FileSize,
                MimeType = session.MimeType,
                Method = downloadMethod,
                Headers = session.Headers,
                Cookies = session.Cookies?.Select(c => new Core.CookieInfo
                {
                    Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path
                }).ToList()
            };

            // Create engine and wire up events
            _currentEngine = new Core.DownloadEngine();
            _currentEngine.ProgressChanged += async (sender, progress) =>
            {
                var progressJson = JsonSerializer.Serialize(new
                {
                    type = "progress",
                    sessionId = downloadReq.SessionId,
                    bytesDownloaded = progress.BytesDownloaded,
                    totalBytes = progress.TotalBytes,
                    progress = Math.Round(progress.Progress, 1),
                    speed = Math.Round(progress.Speed, 1),
                    eta = Math.Round(progress.ETA, 1)
                });
                await _pipeServer.SendToWpf(progressJson);
            };

            _currentEngine.DownloadCompleted += async (sender, result) =>
            {
                var resultJson = JsonSerializer.Serialize(new
                {
                    type = "completed",
                    sessionId = downloadReq.SessionId,
                    filename = result.Filename,
                    savePath = result.SavePath,
                    bytesDownloaded = result.BytesDownloaded,
                    fileSize = result.FileSize
                });
                await _pipeServer.SendToWpf(resultJson);
                Log($"Download completed: {result.Filename}");
            };

            _currentEngine.DownloadFailed += async (sender, result) =>
            {
                // Store bytes downloaded for retry
                _failedBytesDownloaded[downloadReq.SessionId] = result.BytesDownloaded;
                var resultJson = JsonSerializer.Serialize(new
                {
                    type = "failed",
                    sessionId = downloadReq.SessionId,
                    error = result.Error,
                    bytesDownloaded = result.BytesDownloaded,
                    totalBytes = result.FileSize
                });
                await _pipeServer.SendToWpf(resultJson);
                Log($"Download failed: {result.Error} (at {result.BytesDownloaded} bytes)");
            };

            // Start download in background
            _ = Task.Run(async () =>
            {
                var result = await _currentEngine.StartDownloadAsync(dlRequest);
                _currentEngine?.Dispose();
                _currentEngine = null;
            });

            await WriteJson(response, new
            {
                success = true,
                message = "Download started",
                sessionId = downloadReq.SessionId,
                savePath = dlRequest.SavePath
            });

            Log($"Download started for session {downloadReq.SessionId}: {session.Filename}");
        }
        catch (Exception ex)
        {
            Log("Start download error: " + ex.Message);
            response.StatusCode = 500;
            await WriteJson(response, new { error = ex.Message });
        }
    }

    private async Task HandlePauseDownload(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                body = await reader.ReadToEndAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var pauseReq = JsonSerializer.Deserialize<CancelDownloadRequest>(body, options);

            if (_currentEngine != null && _currentEngine.IsDownloading)
            {
                // Get bytes downloaded before cancelling
                long bytesDownloaded = _currentEngine.GetBytesDownloaded();
                _currentEngine.Cancel();
                Log($"Download paused at {bytesDownloaded} bytes");

                // Send pause event to WPF
                var pauseJson = JsonSerializer.Serialize(new
                {
                    type = "paused",
                    sessionId = pauseReq?.SessionId ?? "",
                    bytesDownloaded = bytesDownloaded
                });
                await _pipeServer.SendToWpf(pauseJson);
            }

            await WriteJson(response, new { success = true, message = "Download paused" });
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            await WriteJson(response, new { error = ex.Message });
        }
    }

    private async Task HandleResumeDownload(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                body = await reader.ReadToEndAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var resumeReq = JsonSerializer.Deserialize<ResumeDownloadRequest>(body, options);

            if (resumeReq == null || string.IsNullOrEmpty(resumeReq.SessionId))
            {
                response.StatusCode = 400;
                await WriteJson(response, new { error = "Missing sessionId" });
                return;
            }

            // Try to get session data from request first (works after service restart)
            // Fall back to in-memory sessions if request doesn't have full data
            string url = resumeReq.Url ?? "";
            string finalUrl = resumeReq.FinalUrl ?? "";
            string filename = resumeReq.Filename ?? "download";
            long fileSize = resumeReq.FileSize;
            string mimeType = resumeReq.MimeType ?? "";
            string method = resumeReq.Method ?? "GET";
            Dictionary<string, string>? headers = resumeReq.Headers;
            List<Core.CookieInfo>? cookies = resumeReq.Cookies;

            // If URL is missing, try in-memory session
            if (string.IsNullOrEmpty(url))
            {
                var session = _sessions.FirstOrDefault(s => s.Id == resumeReq.SessionId);
                if (session == null)
                {
                    response.StatusCode = 400;
                    await WriteJson(response, new { error = "No session data available for resume" });
                    return;
                }
                url = session.Url;
                finalUrl = session.FinalUrl;
                filename = session.Filename;
                fileSize = session.FileSize;
                mimeType = session.MimeType ?? "";
                method = session.Method ?? "GET";
                headers = session.Headers;
                cookies = session.Cookies?.Select(c => new Core.CookieInfo
                {
                    Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path
                }).ToList();
            }

            // CDN cross-domain redirects always use GET
            if (!string.IsNullOrEmpty(finalUrl) && !string.IsNullOrEmpty(url) && finalUrl != url)
                method = "GET";

            string savePath = resumeReq.SavePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", filename);

            Log($"Resume: session={resumeReq.SessionId}, from={FormatSize(resumeReq.BytesDownloaded)}, url={url}");

            var dlRequest = new Core.DownloadRequest
            {
                Url = url,
                FinalUrl = finalUrl,
                Filename = Path.GetFileName(savePath),
                SavePath = savePath,
                FileSize = fileSize,
                MimeType = mimeType,
                Method = method,
                ResumeFromBytes = resumeReq.BytesDownloaded,
                Headers = headers,
                Cookies = cookies
            };

            // Cancel any existing download
            _currentEngine?.Cancel();
            _currentEngine?.Dispose();

            // Create new engine and wire up events
            _currentEngine = new Core.DownloadEngine();
            _currentEngine.ProgressChanged += async (sender, progress) =>
            {
                var progressJson = JsonSerializer.Serialize(new
                {
                    type = "progress",
                    sessionId = resumeReq.SessionId,
                    bytesDownloaded = progress.BytesDownloaded,
                    totalBytes = progress.TotalBytes,
                    progress = Math.Round(progress.Progress, 1),
                    speed = Math.Round(progress.Speed, 1),
                    eta = Math.Round(progress.ETA, 1)
                });
                await _pipeServer.SendToWpf(progressJson);
            };

            _currentEngine.DownloadCompleted += async (sender, result) =>
            {
                _failedBytesDownloaded.Remove(resumeReq.SessionId);
                var resultJson = JsonSerializer.Serialize(new
                {
                    type = "completed",
                    sessionId = resumeReq.SessionId,
                    filename = result.Filename,
                    savePath = result.SavePath,
                    bytesDownloaded = result.BytesDownloaded,
                    fileSize = result.FileSize
                });
                await _pipeServer.SendToWpf(resultJson);
                Log($"Download completed: {result.Filename}");
            };

            _currentEngine.DownloadFailed += async (sender, result) =>
            {
                _failedBytesDownloaded[resumeReq.SessionId] = result.BytesDownloaded;
                var resultJson = JsonSerializer.Serialize(new
                {
                    type = "failed",
                    sessionId = resumeReq.SessionId,
                    error = result.Error,
                    bytesDownloaded = result.BytesDownloaded,
                    totalBytes = result.FileSize
                });
                await _pipeServer.SendToWpf(resultJson);
                Log($"Download failed: {result.Error} (at {result.BytesDownloaded} bytes)");
            };

            // Start download in background
            _ = Task.Run(async () =>
            {
                var result = await _currentEngine.StartDownloadAsync(dlRequest);
                _currentEngine?.Dispose();
                _currentEngine = null;
            });

            await WriteJson(response, new
            {
                success = true,
                message = "Download resumed",
                sessionId = resumeReq.SessionId,
                resumeFromBytes = resumeReq.BytesDownloaded
            });
            Log($"Download resumed from {resumeReq.BytesDownloaded} bytes");
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            await WriteJson(response, new { error = ex.Message });
        }
    }

    private async Task HandleCancelDownload(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                body = await reader.ReadToEndAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var cancelReq = JsonSerializer.Deserialize<CancelDownloadRequest>(body, options);

            if (_currentEngine != null && _currentEngine.IsDownloading)
            {
                long bytesDownloaded = _currentEngine.GetBytesDownloaded();
                _currentEngine.Cancel();
                Log($"Download paused (cancel) at {bytesDownloaded} bytes");

                var pauseJson = JsonSerializer.Serialize(new
                {
                    type = "paused",
                    sessionId = cancelReq?.SessionId ?? "",
                    bytesDownloaded = bytesDownloaded
                });
                await _pipeServer.SendToWpf(pauseJson);
            }
            else if (_currentEngine != null)
            {
                _currentEngine.Cancel();
                Log("Download cancelled");
            }

            await WriteJson(response, new { success = true, message = "Download paused" });
        }
        catch (Exception ex)
        {
            response.StatusCode = 500;
            await WriteJson(response, new { error = ex.Message });
        }
    }

    private async Task HandleRetryDownload(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                body = await reader.ReadToEndAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var retryReq = JsonSerializer.Deserialize<ResumeDownloadRequest>(body, options);

            if (retryReq == null || string.IsNullOrEmpty(retryReq.SessionId))
            {
                response.StatusCode = 400;
                await WriteJson(response, new { error = "Missing sessionId" });
                return;
            }

            // Use session data from the request (works after service restart)
            string url = retryReq.Url ?? "";
            string filename = retryReq.Filename ?? "download";
            long fileSize = retryReq.FileSize;

            // Get byte offset from stored data or file on disk
            long resumeFromBytes = 0;
            _failedBytesDownloaded.TryGetValue(retryReq.SessionId, out resumeFromBytes);
            if (resumeFromBytes == 0 && !string.IsNullOrEmpty(retryReq.SavePath) && File.Exists(retryReq.SavePath))
            {
                resumeFromBytes = new FileInfo(retryReq.SavePath).Length;
                Log($"Detecting existing file size: {FormatSize(resumeFromBytes)}");
            }

            if (string.IsNullOrEmpty(url))
            {
                response.StatusCode = 400;
                await WriteJson(response, new { error = "No session data for retry" });
                return;
            }

            Log($"Retry: session={retryReq.SessionId}, from={FormatSize(resumeFromBytes)}, url={url}");

            string method = retryReq.Method ?? "GET";
            if (!string.IsNullOrEmpty(retryReq.FinalUrl) && retryReq.FinalUrl != url)
                method = "GET";

            var dlRequest = new Core.DownloadRequest
            {
                Url = url,
                FinalUrl = retryReq.FinalUrl ?? "",
                Filename = filename,
                SavePath = retryReq.SavePath ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads", filename),
                FileSize = fileSize,
                MimeType = retryReq.MimeType ?? "",
                Method = method,
                ResumeFromBytes = resumeFromBytes,
                Headers = retryReq.Headers,
                Cookies = retryReq.Cookies
            };

            _currentEngine?.Cancel();
            _currentEngine?.Dispose();

            _currentEngine = new Core.DownloadEngine();
            _currentEngine.ProgressChanged += async (sender, progress) =>
            {
                var progressJson = JsonSerializer.Serialize(new
                {
                    type = "progress",
                    sessionId = retryReq.SessionId,
                    bytesDownloaded = progress.BytesDownloaded,
                    totalBytes = progress.TotalBytes,
                    progress = Math.Round(progress.Progress, 1),
                    speed = Math.Round(progress.Speed, 1),
                    eta = Math.Round(progress.ETA, 1)
                });
                await _pipeServer.SendToWpf(progressJson);
            };

            _currentEngine.DownloadCompleted += async (sender, result) =>
            {
                _failedBytesDownloaded.Remove(retryReq.SessionId);
                var resultJson = JsonSerializer.Serialize(new
                {
                    type = "completed",
                    sessionId = retryReq.SessionId,
                    filename = result.Filename,
                    savePath = result.SavePath,
                    bytesDownloaded = result.BytesDownloaded,
                    fileSize = result.FileSize
                });
                await _pipeServer.SendToWpf(resultJson);
                Log($"Download completed after retry: {result.Filename}");
            };

            _currentEngine.DownloadFailed += async (sender, result) =>
            {
                _failedBytesDownloaded[retryReq.SessionId] = result.BytesDownloaded;
                var resultJson = JsonSerializer.Serialize(new
                {
                    type = "failed",
                    sessionId = retryReq.SessionId,
                    error = result.Error,
                    bytesDownloaded = result.BytesDownloaded,
                    totalBytes = result.FileSize
                });
                await _pipeServer.SendToWpf(resultJson);
                Log($"Download failed during retry: {result.Error}");
            };

            _ = Task.Run(async () =>
            {
                var result = await _currentEngine.StartDownloadAsync(dlRequest);
                _currentEngine?.Dispose();
                _currentEngine = null;
            });

            await WriteJson(response, new
            {
                success = true,
                message = "Download retrying",
                sessionId = retryReq.SessionId,
                resumeFromBytes = resumeFromBytes
            });
        }
        catch (Exception ex)
        {
            Log("Retry download error: " + ex.Message);
            response.StatusCode = 500;
            await WriteJson(response, new { error = ex.Message });
        }
    }

    private async Task WriteJson(HttpListenerResponse response, object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }

    private static string GetExtensionFromMime(string mimeType)
    {
        return mimeType?.ToLower() switch
        {
            "application/pdf" => ".pdf",
            "application/zip" => ".zip",
            "application/x-rar" or "application/vnd.rar" => ".rar",
            "application/x-7z-compressed" => ".7z",
            "application/x-tar" or "application/gzip" => ".tar.gz",
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.ms-excel" => ".xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "application/vnd.ms-powerpoint" => ".ppt",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/x-msvideo" => ".avi",
            "audio/mpeg" => ".mp3",
            "audio/wav" => ".wav",
            "audio/flac" => ".flac",
            "text/html" => ".html",
            "text/plain" => ".txt",
            "application/json" => ".json",
            "application/xml" => ".xml",
            "application/x-executable" or "application/x-msdownload" => ".exe",
            "application/x-msi" => ".msi",
            "application/x-dmg" => ".dmg",
            "application/vnd.android.package-archive" => ".apk",
            "application/x-iso9660-image" => ".iso",
            _ => ""
        };
    }

    private static string Truncate(string? s, int max) => string.IsNullOrEmpty(s) ? "missing" : s.Length <= max ? s : s[..max] + "...";

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int i = 0; double v = bytes;
        while (v >= 1024 && i < sizes.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {sizes[i]}";
    }

    private void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [HTTP] {message}");
}

public class ReceivedSession
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string FinalUrl { get; set; } = "";
    public string Filename { get; set; } = "";
    public long FileSize { get; set; }
    public string? MimeType { get; set; }
    public string? Method { get; set; }
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public List<RawHeader>? RawHeaders { get; set; }
    public List<RawHeader>? ResponseHeaders { get; set; }
    public List<CookieInfo>? Cookies { get; set; }
    public object? PostData { get; set; }
    public TabInfo? Tab { get; set; }
    public DateTime? ReceivedAt { get; set; }
}

public class RawHeader
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public class CookieInfo
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Path { get; set; } = "/";
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
    public string? SameSite { get; set; }
    public double? Expires { get; set; }
}

public class TabInfo
{
    public string? Url { get; set; }
    public string? Title { get; set; }
}

public class StartDownloadRequest
{
    public string? SessionId { get; set; }
    public string? SavePath { get; set; }
}

public class CancelDownloadRequest
{
    public string? SessionId { get; set; }
}

public class ResumeDownloadRequest
{
    public string? SessionId { get; set; }
    public string? SavePath { get; set; }
    public long BytesDownloaded { get; set; }
    // Full session data sent by WPF so resume works even after service restart
    public string? Url { get; set; }
    public string? FinalUrl { get; set; }
    public string? Filename { get; set; }
    public long FileSize { get; set; }
    public string? MimeType { get; set; }
    public string? Method { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public List<Core.CookieInfo>? Cookies { get; set; }
}
