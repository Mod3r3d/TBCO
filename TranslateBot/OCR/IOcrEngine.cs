using System.Threading.Tasks;

namespace TranslateBot.OCR
{
    // Kết quả OCR — chứa text + metadata bổ sung (VD: text có bị cắt ở mép không)
    public struct OcrResult
    {
        public string Text { get; set; }

        // True nếu engine phát hiện text có dấu hiệu bị cắt cụt ở mép dưới
        // vùng chụp (dòng cuối sát viền dưới). Caller nên log cảnh báo.
        public bool IsTextClipped { get; set; }

        public static OcrResult Empty => new() { Text = string.Empty, IsTextClipped = false };
    }

    public interface IOcrEngine
    {
        // Nhận vào mảng byte ảnh (pixel), chiều rộng, chiều cao và trả về kết quả OCR
        Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height);
    }
}