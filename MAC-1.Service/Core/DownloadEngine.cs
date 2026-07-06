using System.Net;
using System.Net.Http;
using System.Net.Security;

namespace MAC_1.Service.Core;

public class DownloadEngine : IDisposable
{
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _userCancelled;

    public event EventHandler<DownloadProgress>? ProgressChanged;
    public event EventHandler<DownloadResult>? DownloadCompleted;
    public event EventHandler<DownloadResult>? DownloadFailed;

    public bool IsDownloading { get; private set; }

    public DownloadEngine() { }

    public async Task<DownloadResult> StartDownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
            return new DownloadResult { Success = false, Error = "Another download is in progress" };

        IsDownloading = true;
        _userCancelled = false;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var result = new DownloadResult
        {
            Url = request.Url,
            Filename = request.Filename,
            SavePath = request.SavePath
        };

        try
        {
            Log($"Starting download: {request.Filename} ({FormatSize(request.FileSize)})");
            Log($"URL: {request.FinalUrl ?? request.Url}");

            // Try HTTP/2 first, fallback to HTTP/1.1, then retry without strict SSL
            HttpResponseMessage? response = null;

            // Attempt 1: Normal connection
            try
            {
                response = await TryDownload(request, useHttp2: true, _cts.Token);
                Log("HTTP/2 connection successful");
            }
            catch (Exception ex)
            {
                Log($"HTTP/2 failed: {ex.Message} | Inner: {ex.InnerException?.Message}");
                try
                {
                    response = await TryDownload(request, useHttp2: false, _cts.Token);
                    Log("HTTP/1.1 connection successful");
                }
                catch (Exception ex2)
                {
                    Log($"HTTP/1.1 also failed: {ex2.Message} | Inner: {ex2.InnerException?.Message}");
                }
            }

            // Attempt 2: If both failed, try with fully permissive SSL
            if (response == null)
            {
                Log("Retrying with permissive SSL settings...");
                try
                {
                    response = await TryDownloadPermissive(request, _cts.Token);
                    Log("Permissive SSL connection successful");
                }
                catch (Exception ex3)
                {
                    Log($"Permissive SSL also failed: {ex3.Message} | Inner: {ex3.InnerException?.Message}");
                }
            }

            if (response == null)
            {
                result.Error = "Failed to connect with any HTTP version";
                DownloadFailed?.Invoke(this, result);
                return result;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    string error = $"Server returned {(int)response.StatusCode} {response.StatusCode}";
                    result.Error = error;
                    Log($"Download failed: {error}");
                    DownloadFailed?.Invoke(this, result);
                    return result;
                }

                // Get file size from response
                long totalBytes = response.Content.Headers.ContentLength ?? request.FileSize;
                result.FileSize = totalBytes;

                // Get filename from Content-Disposition if available
                string? contentDisposition = response.Content.Headers.ContentDisposition?.FileName;
                if (!string.IsNullOrEmpty(contentDisposition))
                {
                    result.Filename = contentDisposition.Trim('"');
                    string saveDir = Path.GetDirectoryName(request.SavePath) ?? "";
                    result.SavePath = Path.Combine(saveDir, result.Filename);
                }

                Log($"Response: {(int)response.StatusCode}, Size: {FormatSize(totalBytes)}, Filename: {result.Filename}");
                Log($"HTTP Version: {response.Version}, Save path: {result.SavePath}");

                // Ensure directory exists
                string? dir = Path.GetDirectoryName(result.SavePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Stream download to file
                using var contentStream = await response.Content.ReadAsStreamAsync(_cts.Token);

                // Verify existing file for resume - check actual size matches expected resume point
                long resumeFrom = request.ResumeFromBytes;
                if (resumeFrom > 0)
                {
                    if (File.Exists(result.SavePath))
                    {
                        long existingSize = new FileInfo(result.SavePath).Length;
                        if (existingSize != resumeFrom)
                        {
                            Log($"File size mismatch: expected {FormatSize(resumeFrom)}, got {FormatSize(existingSize)} - starting fresh");
                            resumeFrom = 0;
                        }
                    }
                    else
                    {
                        Log($"Resume file not found at {result.SavePath} - starting fresh");
                        resumeFrom = 0;
                    }
                }

                var fileMode = resumeFrom > 0 ? FileMode.Append : FileMode.Create;
                using var fileStream = new FileStream(result.SavePath, fileMode, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long bytesDownloaded = resumeFrom;
                int bytesRead;
                var startTime = DateTime.UtcNow;
                var lastProgressTime = DateTime.UtcNow;
                long lastBytesAtSpeed = bytesDownloaded;
                long totalBytesToDownload = totalBytes - resumeFrom;

                while ((bytesRead = await contentStream.ReadAsync(buffer, _cts.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), _cts.Token);
                    await fileStream.FlushAsync(_cts.Token);
                    bytesDownloaded += bytesRead;
                    _lastBytesDownloaded = bytesDownloaded;

                    // Update progress every 500ms
                    var now = DateTime.UtcNow;
                    if ((now - lastProgressTime).TotalMilliseconds >= 500)
                    {
                        double elapsed = (now - lastProgressTime).TotalSeconds;
                        double speed = elapsed > 0 ? (bytesDownloaded - lastBytesAtSpeed) / elapsed : 0;
                        // Progress based on TOTAL bytes downloaded / total file size (not session-relative)
                        double progress = totalBytes > 0
                            ? Math.Min(100.0, (double)bytesDownloaded / totalBytes * 100)
                            : 0;
                        double remaining = Math.Max(0, totalBytes - bytesDownloaded);
                        double eta = speed > 0 && remaining > 0 ? remaining / speed : 0;

                        ProgressChanged?.Invoke(this, new DownloadProgress
                        {
                            BytesDownloaded = bytesDownloaded,
                            TotalBytes = totalBytes,
                            Progress = Math.Round(progress, 1),
                            Speed = speed,
                            ETA = eta
                        });

                        lastProgressTime = now;
                        lastBytesAtSpeed = bytesDownloaded;
                    }
                }

                await fileStream.FlushAsync(_cts.Token);

                // Verify file size matches expected
                var fileInfo = new FileInfo(result.SavePath);
                long actualFileSize = fileInfo.Length;
                if (totalBytes > 0 && actualFileSize != totalBytes)
                {
                    Log($"WARNING: File size mismatch - expected {FormatSize(totalBytes)}, got {FormatSize(actualFileSize)}");
                }

                result.Success = true;
                result.BytesDownloaded = bytesDownloaded;
                result.FileSize = actualFileSize;
                result.ElapsedTime = DateTime.UtcNow - startTime;

                Log($"Download complete: {FormatSize(bytesDownloaded)} in {result.ElapsedTime.TotalSeconds:F1}s");
                DownloadCompleted?.Invoke(this, result);
            }
        }
        catch (OperationCanceledException)
        {
            // Check if user explicitly cancelled vs network timeout
            if (_cts != null && _cts.IsCancellationRequested && !_userCancelled)
            {
                // Network timeout or connection lost - treat as failure
                result.Error = "Connection lost";
                Log("Download failed: connection lost");
                DownloadFailed?.Invoke(this, result);
            }
            else
            {
                result.Error = "Download cancelled";
                Log("Download cancelled by user");
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Log($"Download failed: {ex.Message}");
            DownloadFailed?.Invoke(this, result);
        }
        finally
        {
            IsDownloading = false;
            _cts?.Dispose();
            _cts = null;
        }

        return result;
    }

    private async Task<HttpResponseMessage> TryDownload(DownloadRequest request, bool useHttp2, CancellationToken ct)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            Expect100ContinueTimeout = TimeSpan.FromSeconds(1),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.All,
            SslOptions =
            {
                // Accept all certificates to handle servers with cert issues
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
                    | System.Security.Authentication.SslProtocols.Tls13
                    | System.Security.Authentication.SslProtocols.Tls11
            }
        };

        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        // Build request with browser headers
        using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method ?? "GET"), request.FinalUrl ?? request.Url);

        // Add all browser headers
        if (request.Headers != null)
        {
            foreach (var header in request.Headers)
            {
                string key = header.Key;
                string value = header.Value;

                string lk = key.ToLowerInvariant();
                // Skip headers that HttpClient manages automatically
                if (lk == "host" || lk == "content-length" || lk == "connection")
                    continue;

                try
                {
                    httpRequest.Headers.TryAddWithoutValidation(key, value);
                }
                catch { }
            }
        }

        // Add cookies
        if (request.Cookies != null && request.Cookies.Count > 0)
        {
            string cookieHeader = string.Join("; ", request.Cookies.Select(c => $"{c.Name}={c.Value}"));
            httpRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        // Ensure proper headers for browser-like request
        if (!httpRequest.Headers.Contains("Accept"))
            httpRequest.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (!httpRequest.Headers.Contains("Accept-Language"))
            httpRequest.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        if (!httpRequest.Headers.Contains("Accept-Encoding"))
            httpRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br, zstd");
        if (!httpRequest.Headers.Contains("Upgrade-Insecure-Requests"))
            httpRequest.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        if (!httpRequest.Headers.Contains("Connection"))
            httpRequest.Headers.TryAddWithoutValidation("Connection", "keep-alive");
        if (!httpRequest.Headers.Contains("Sec-Fetch-Dest"))
            httpRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        if (!httpRequest.Headers.Contains("Sec-Fetch-Mode"))
            httpRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        if (!httpRequest.Headers.Contains("Sec-Fetch-Site"))
            httpRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        if (!httpRequest.Headers.Contains("Sec-Fetch-User"))
            httpRequest.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");

        // Add Range header for resume support
        if (request.ResumeFromBytes > 0)
        {
            httpRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(request.ResumeFromBytes, null);
            Log($"Resuming from byte {request.ResumeFromBytes}");
        }

        Log($"Sending {request.Method ?? "GET"} request to {request.FinalUrl ?? request.Url} (HTTP/{(useHttp2 ? "2" : "1.1")})");

        return await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private async Task<HttpResponseMessage> TryDownloadPermissive(DownloadRequest request, CancellationToken ct)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                | System.Security.Authentication.SslProtocols.Tls13
                | System.Security.Authentication.SslProtocols.Tls11
                | System.Security.Authentication.SslProtocols.Ssl3
        };

        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method ?? "GET"), request.FinalUrl ?? request.Url);

        if (request.Headers != null)
        {
            foreach (var header in request.Headers)
            {
                string lk = header.Key.ToLowerInvariant();
                if (lk == "host" || lk == "content-length" || lk == "connection")
                    continue;
                try { httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value); } catch { }
            }
        }

        if (request.Cookies != null && request.Cookies.Count > 0)
        {
            string cookieHeader = string.Join("; ", request.Cookies.Select(c => $"{c.Name}={c.Value}"));
            httpRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        if (!httpRequest.Headers.Contains("Accept"))
            httpRequest.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (!httpRequest.Headers.Contains("User-Agent"))
            httpRequest.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

        if (request.ResumeFromBytes > 0)
            httpRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(request.ResumeFromBytes, null);

        return await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public void Cancel()
    {
        _userCancelled = true;
        _cts?.Cancel();
    }

    public long GetBytesDownloaded()
    {
        return _lastBytesDownloaded;
    }

    private long _lastBytesDownloaded = 0;

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int i = 0; double v = bytes;
        while (v >= 1024 && i < sizes.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {sizes[i]}";
    }

    private void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Engine] {message}");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts?.Dispose();
            _disposed = true;
        }
    }
}

public class DownloadRequest
{
    public string Url { get; set; } = "";
    public string? FinalUrl { get; set; }
    public string Filename { get; set; } = "download";
    public string SavePath { get; set; } = "";
    public long FileSize { get; set; }
    public string? MimeType { get; set; }
    public string? Method { get; set; }
    public long ResumeFromBytes { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public List<CookieInfo>? Cookies { get; set; }
}

public class DownloadProgress
{
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public double Progress { get; set; }
    public double Speed { get; set; }
    public double ETA { get; set; }
}

public class DownloadResult
{
    public bool Success { get; set; }
    public string Url { get; set; } = "";
    public string Filename { get; set; } = "";
    public string SavePath { get; set; } = "";
    public long FileSize { get; set; }
    public long BytesDownloaded { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public string? Error { get; set; }
}

public class CookieInfo
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Path { get; set; } = "/";
}
