using System;
using System.Collections.Generic;

namespace TranslateBot.Translation
{
    public interface IApiKeyPool
    {
        /// <summary>
        /// Lấy một credential khả dụng từ pool theo thuật toán Round-Robin / Health-based.
        /// </summary>
        ApiCredential? AcquireKey(string provider = "Gemini");

        /// <summary>
        /// Báo cáo request thành công để cập nhật độ trễ và tỷ lệ sức khỏe key.
        /// </summary>
        void ReportSuccess(ApiCredential key, TimeSpan latency);

        /// <summary>
        /// Báo cáo 429 Quota Exceeded để chuyển key sang trạng thái Cooldown.
        /// </summary>
        void ReportRateLimit(ApiCredential key, TimeSpan cooldownDuration, string reason = "429 Rate Limit");

        /// <summary>
        /// Báo cáo 401 Unauthorized / Token Invalid để vô hiệu hóa key.
        /// </summary>
        void ReportInvalid(ApiCredential key, string reason = "401 Invalid Token");

        /// <summary>
        /// Báo cáo lỗi tạm thời (500, 503, 504, Timeout).
        /// </summary>
        void ReportTransientError(ApiCredential key, int statusCode, string message);

        /// <summary>
        /// Giải phóng key sau khi sử dụng (nếu cần tracking lease).
        /// </summary>
        void ReleaseKey(ApiCredential key);

        /// <summary>
        /// Thêm hoặc cập nhật credential vào pool.
        /// </summary>
        void AddOrUpdateCredential(ApiCredential credential);

        /// <summary>
        /// Xóa credential khỏi pool.
        /// </summary>
        bool RemoveCredential(string id);

        /// <summary>
        /// Lấy toàn bộ danh sách credential (bao gồm cả trạng thái chi tiết).
        /// </summary>
        IReadOnlyList<ApiCredential> GetAllCredentials();
    }
}
