using System.Net.Http;
using MAC_1.Services;

namespace MAC_1.Utils
{
    public static class ServiceLocator
    {
        public static DataService DataService => DataService.Instance;
        public static PopupService PopupService => PopupService.Instance;

        private static readonly HttpClient _sharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public static HttpClient HttpClient => _sharedHttpClient;
    }
}
