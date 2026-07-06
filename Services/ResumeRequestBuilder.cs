using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using MAC_1.Models;

namespace MAC_1.Services
{
    public static class ResumeRequestBuilder
    {
        public static StringContent BuildResumeContent(DownloadTask task)
        {
            var request = new
            {
                sessionId = task.ServiceSessionId ?? task.Id,
                savePath = Path.Combine(task.SaveFolder, task.Filename),
                bytesDownloaded = task.DownloadedSize,
                // Full session data so resume works after service restart
                url = task.Url,
                finalUrl = task.Url,
                filename = task.Filename,
                fileSize = task.TotalSize,
                mimeType = task.SessionMimeType ?? "",
                method = task.SessionMethod ?? "GET",
                headers = task.SessionHeaders,
                cookies = task.SessionCookies?.Select(c => new { name = c.Name, value = c.Value, domain = c.Domain, path = c.Path }).ToList()
            };
            var json = JsonSerializer.Serialize(request);
            return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        public static StringContent BuildPauseContent(DownloadTask task)
        {
            var request = new { sessionId = task.ServiceSessionId ?? task.Id };
            var json = JsonSerializer.Serialize(request);
            return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }
    }
}
