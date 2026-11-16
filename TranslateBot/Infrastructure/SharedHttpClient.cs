using System;
using System.Net.Http;

namespace TranslateBot.Infrastructure
{
    /// <summary>
    /// Cung cấp HttpClient dùng chung với SocketsHttpHandler và PooledConnectionLifetime (P1-3).
    /// Loại bỏ triệt để tình trạng cạn kiệt socket (socket exhaustion) hoặc rò rỉ TCP connection khi khởi tạo lại provider.
    /// </summary>
    public static class SharedHttpClient
    {
        private static readonly SocketsHttpHandler _handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 16,
            EnableMultipleHttp2Connections = true
        };

        private static readonly HttpClient _instance = new(_handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public static HttpClient Instance => _instance;
    }
}
