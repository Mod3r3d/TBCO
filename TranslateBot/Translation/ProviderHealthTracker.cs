using System;
using System.Collections.Generic;
using System.Linq;

namespace TranslateBot.Translation
{
    public enum ProviderStatus
    {
        Healthy,    // Hoạt động tốt, tỷ lệ thành công cao
        Degraded,   // Đang có lỗi rải rác hoặc độ trễ cao
        Unhealthy,  // Thất bại liên tiếp hoặc bị rate limit
        Offline     // Mất kết nối hoàn toàn
    }

    public class ProviderHealthMetrics
    {
        public string ProviderName { get; init; } = string.Empty;
        public ProviderStatus Status { get; set; } = ProviderStatus.Healthy;
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int ConsecutiveFailures { get; set; }
        public double AverageLatencyMs { get; set; }
        public double HealthScore { get; set; } = 1.0; // Thang điểm 0.0 - 1.0
        public DateTime LastSuccessAt { get; set; } = DateTime.MinValue;
        public DateTime LastFailureAt { get; set; } = DateTime.MinValue;
    }

    /// <summary>
    /// Giám sát và tính toán chỉ số sức khỏe của các Provider dịch thuật (Section 13 của Master Plan)
    /// </summary>
    public class ProviderHealthTracker
    {
        private readonly Dictionary<string, ProviderHealthMetrics> _metrics = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public ProviderHealthMetrics GetOrCreate(string providerName)
        {
            lock (_lock)
            {
                if (!_metrics.TryGetValue(providerName, out var m))
                {
                    m = new ProviderHealthMetrics { ProviderName = providerName };
                    _metrics[providerName] = m;
                }
                return m;
            }
        }

        public void ReportSuccess(string providerName, TimeSpan latency)
        {
            lock (_lock)
            {
                var m = GetOrCreate(providerName);
                m.SuccessCount++;
                m.ConsecutiveFailures = 0;
                m.LastSuccessAt = DateTime.Now;

                // Cập nhật độ trễ trung bình trượt (Moving Average)
                m.AverageLatencyMs = m.AverageLatencyMs == 0
                    ? latency.TotalMilliseconds
                    : (m.AverageLatencyMs * 0.8) + (latency.TotalMilliseconds * 0.2);

                // Tự phục hồi điểm sức khỏe (+0.15 cho mỗi lần thành công, tối đa 1.0)
                m.HealthScore = Math.Min(1.0, m.HealthScore + 0.15);

                m.Status = m.HealthScore >= 0.7 ? ProviderStatus.Healthy : ProviderStatus.Degraded;
            }
        }

        public void ReportFailure(string providerName, string reason = "")
        {
            lock (_lock)
            {
                var m = GetOrCreate(providerName);
                m.FailureCount++;
                m.ConsecutiveFailures++;
                m.LastFailureAt = DateTime.Now;

                // Trừ điểm sức khỏe theo số lần lỗi liên tiếp
                m.HealthScore = Math.Max(0.0, m.HealthScore - 0.25);

                if (m.ConsecutiveFailures >= 3 || m.HealthScore <= 0.25)
                {
                    m.Status = ProviderStatus.Unhealthy;
                }
                else
                {
                    m.Status = ProviderStatus.Degraded;
                }
            }
        }

        public void ReportOffline(string providerName)
        {
            lock (_lock)
            {
                var m = GetOrCreate(providerName);
                m.Status = ProviderStatus.Offline;
                m.HealthScore = 0.0;
            }
        }

        public IReadOnlyList<ProviderHealthMetrics> GetAllMetrics()
        {
            lock (_lock)
            {
                return _metrics.Values.ToList().AsReadOnly();
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _metrics.Clear();
            }
        }
    }
}
