using System.Collections.Generic;
using System.Threading.Tasks;

namespace TranslateBot.OCR
{
    public struct OcrBoundingBox
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }

    // Kết quả OCR — chứa text + metadata bổ sung (confidence, bounding boxes, raw text, latency, failure reason)
    public struct OcrResult
    {
        public string Text { get; set; }
        public string? RawText { get; set; }

        // True nếu engine phát hiện text có dấu hiệu bị cắt cụt ở mép dưới
        // vùng chụp (dòng cuối sát viền dưới). Caller nên log cảnh báo.
        public bool IsTextClipped { get; set; }

        // Độ tin cậy nhận diện (0.0 đến 1.0, null nếu engine không hỗ trợ)
        public float? Confidence { get; set; }

        public string? EngineName { get; set; }
        public double LatencyMs { get; set; }

        private bool? _success;
        public bool Success
        {
            get => _success ?? string.IsNullOrEmpty(ErrorMessage);
            set => _success = value;
        }

        public string? ErrorMessage { get; set; }
        public List<OcrBoundingBox>? BoundingBoxes { get; set; }

        public static OcrResult Empty => new()
        {
            Text = string.Empty,
            RawText = string.Empty,
            IsTextClipped = false,
            Success = true,
            LatencyMs = 0
        };

        public static OcrResult CreateFailure(string engineName, string errorMessage, double latencyMs = 0) => new()
        {
            Text = string.Empty,
            RawText = string.Empty,
            IsTextClipped = false,
            EngineName = engineName,
            Success = false,
            ErrorMessage = errorMessage,
            LatencyMs = latencyMs
        };
    }

    public interface IOcrEngine
    {
        // Nhận vào mảng byte ảnh (pixel), chiều rộng, chiều cao và trả về kết quả OCR
        Task<OcrResult> ExtractTextAsync(byte[] bgraPixels, int width, int height);
    }
}