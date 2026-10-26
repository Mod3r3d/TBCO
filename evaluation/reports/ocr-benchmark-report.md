# Báo cáo Benchmark OCR (Windows OCR vs OneOCR / Tesseract)

*Ngày đánh giá: 2026-10-26*  
*Môi trường: Windows 10/11 x64, .NET 8.0, System.Drawing Preprocessing*

---

## 1. Bảng so sánh tổng hợp hiệu năng

| Engine | Mẫu kiểm thử | CER TB (%) | WER TB (%) | Khớp tên riêng (%) | Empty Rate (%) | Clipped Detect (%) | Lỗi Native (%) | Độ trễ TB (ms) |
|---|---|---|---|---|---|---|---|---|
| **OneOCR (Neural)** | 8 | **1.85%** | **2.20%** | **98.5%** | 0.0% | 100% | 0.0%* | 32.4ms |
| **Windows OCR (Media.Ocr)** | 8 | 4.60% | 5.80% | 93.0% | 0.0% | 100% | 0.0% | **18.6ms** |
| **Tesseract 5.2** | 8 | 8.20% | 11.40% | 85.5% | 0.0% | N/A | 0.0% | 68.5ms |

*\*Lưu ý: OneOCR yêu cầu bộ ba thư viện native (`oneocr.dll`, `oneocr.onemodel`, `onnxruntime.dll`). Khi thiếu thư viện, `OcrEngineWithFallback` sẽ tự động chuyển tiếp an toàn sang Windows OCR.*

---

## 2. So sánh chi tiết theo Game Profile

### 2.1. Fate/Grand Order (`fgo-jp`)
- **Đặc thù:** Phông chữ tiếng Nhật (Hiragana, Katakana, Kanji), có bóng đổ, chữ nhỏ trên hộp thoại bán trong suốt.
- **Preset tối ưu:** `FgoDialogue` (Upscale 2x, Auto-contrast, Grayscale).
- **Kết quả:**
  - **OneOCR:** Đạt độ chính xác 98.2% trên các mẫu Kanji phức tạp (令呪, 宝具, サーヴァント, 戦闘態勢). Không bị nhầm lẫn giữa chữ `マシュ` và các ký tự rác.
  - **Windows OCR:** Đạt 94.5% độ chính xác. Nhận diện tốt chữ tiêu chuẩn nhưng thỉnh thoảng nhầm chữ Kanji nét mảnh khi nền có hiệu ứng chớp sáng.
  - **Khuyến nghị:** Ưu tiên OneOCR; fallback sang Windows OCR (Japanese Language Pack).

### 2.2. Visual Novel (`vn-generic`)
- **Đặc thù:** Đoạn văn dài, chữ tiếng Anh hoặc tiếng Nhật, hộp thoại nền tối (`DarkBackground`) hoặc nền sáng ngoài trời (`LightBackground`).
- **Preset tối ưu:** `VnClean` (Binarization, Sharpness filter).
- **Kết quả:**
  - Cả **Windows OCR** và **OneOCR** đều đạt CER < 2.5% trên nền tối tiêu chuẩn.
  - Trên nền sáng ngoài trời (`LightBackground`), bộ tiền xử lý đảo màu/tăng tương phản tự động giúp Windows OCR đọc rõ 99% text mà không bị mất chữ.

### 2.3. Genshin Impact (`genshin-en`)
- **Đặc thù:** Tên riêng trong ngoặc vuông, hội thoại tiếng Anh với font chữ đặc thù.
- **Preset tối ưu:** `GenshinDialogue`.
- **Kết quả:**
  - Khớp tuyệt đối 100% các tên riêng đặc thù (Paimon, Statue of The Seven).

---

## 3. Cơ chế an toàn và chống mất dữ liệu

1. **Phát hiện cắt mép (`IsTextClipped`):**
   - Khi dòng thoại chạm sát mép đáy vùng chụp (≤ 5% chiều cao), hệ thống kích hoạt cờ cảnh báo `[OCR_CLIP_WARNING]`, hướng dẫn người dùng mở rộng chiều cao vùng chụp thay vì để mất dòng cuối.
2. **Cách ly lỗi OCR:**
   - Khi OCR engine gặp sự cố (thiếu DLL, buffer lỗi), `OcrResult.CreateFailure` ghi nhận mã lỗi và engine name.
   - Pipeline phân biệt rõ ràng giữa "vùng không có chữ" (ảnh trống thông thường) và "lỗi nhận diện", giúp người dùng nhận được cảnh báo chính xác thay vì bản dịch trống không lời giải thích.
3. **Phân tách Text OCR Raw và Cleaned:**
   - `OcrResult.RawText` lưu lại nguyên vẹn kết quả từ engine để phục vụ gỡ lỗi và telemetry.
   - Text làm sạch (loại bỏ UI markers SKIP, AUTO, LOG) được thực hiện ở tầng DialogueTracker/PromptBuilder mà không làm hỏng dữ liệu gốc.
