using System;
using System.Threading.Tasks;
using TranslateBot.Infrastructure;

namespace TranslateBot.OCR
{
    // Phase 2 của TBCO_MORT_upgrade_plan.md: "OCR Router" - OneOCR fail -> Windows OCR.
    //
    // VẤN ĐỀ ĐÃ SỬA: OneOcrEngine khởi tạo DLL native theo kiểu lazy + async (chỉ chạy
    // lúc gọi OCR lần đầu, KHÔNG ném exception khi thất bại - chỉ âm thầm đánh dấu
    // "không khả dụng"). Vì vậy try/catch quanh `new OneOcrEngine()` ở nơi tạo engine
    // KHÔNG BAO GIỜ bắt được lỗi thật (lỗi xảy ra sau đó, bên trong ExtractTextAsync).
    // Hậu quả trước khi có class này: nếu máy người dùng thiếu DLL OneOCR, bot sẽ
    // IM LẶNG NGỪNG NHẬN DIỆN CHỮ HOÀN TOÀN thay vì tự chuyển về Windows OCR.
    //
    // Class này bọc ngoài engine chính (OneOCR) + engine dự phòng (Windows OCR):
    // gọi engine chính trước; nếu nó báo "không khả dụng" SAU KHI đã thử init thật,
    // tự động chuyển toàn bộ các lần gọi tiếp theo sang engine dự phòng, chỉ log 1 lần
    // duy nhất lúc chuyển (không spam log mỗi frame).
    public class OcrEngineWithFallback : IOcrEngine
    {
        private readonly OneOcrEngine _primary;
        private readonly IOcrEngine _fallback;
        private bool _switchedToFallback;
        private bool _hasLoggedSwitch;

        public OcrEngineWithFallback(OneOcrEngine primary, IOcrEngine fallback)
        {
            _primary = primary;
            _fallback = fallback;
        }

        public async Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height)
        {
            // Đã xác nhận primary hỏng từ trước -> đi thẳng fallback, không thử lại primary
            // nữa (primary tự nó cũng đã short-circuit nhanh, nhưng đi thẳng fallback rõ ràng
            // và tránh phụ thuộc vào chi tiết cài đặt bên trong OneOcrEngine).
            if (_switchedToFallback)
            {
                return await _fallback.ExtractTextAsync(bgraPixels, width, height);
            }

            var result = await _primary.ExtractTextAsync(bgraPixels, width, height);

            // Sau lệnh gọi trên, nếu OneOCR đã thử init xong (HasAttemptedInit) mà vẫn
            // không khả dụng (IsAvailable == false) -> đây chính là trường hợp lỗi native
            // DLL không thể bắt được bằng try/catch ở nơi khởi tạo. Chuyển hẳn sang fallback
            // từ đây trở đi.
            if (_primary.HasAttemptedInit && !_primary.IsAvailable)
            {
                _switchedToFallback = true;
                if (!_hasLoggedSwitch)
                {
                    _hasLoggedSwitch = true;
                    AppLogger.Warn(
                        "[OCR_ROUTER] OneOCR không khả dụng trên máy này (thiếu DLL hoặc lỗi khởi " +
                        "tạo native) - đã tự động chuyển sang Windows OCR cho toàn bộ phiên làm việc " +
                        "này. Xem log [ONEOCR] phía trên để biết lý do cụ thể.");
                }

                // Gọi lại NGAY bằng fallback cho đúng frame hiện tại, để không mất luôn dòng
                // thoại đầu tiên (thay vì phải đợi tới lần OCR tiếp theo).
                return await _fallback.ExtractTextAsync(bgraPixels, width, height);
            }

            return result;
        }
    }
}
