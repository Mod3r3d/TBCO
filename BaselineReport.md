# TBCO Baseline Report (Phase 0 — Step 7 Baseline)

**Thời điểm ghi nhận:** 2026-09-22  
**Hệ điều hành:** Windows (x64)  
**Target Framework:** .NET 8.0 Windows (`net8.0-windows10.0.19041.0`)  
**Mục đích:** Thiết lập điểm mốc hiệu năng, độ trễ và tỷ lệ vượt qua bài kiểm thử trước khi tiến hành nâng cấp lên TBCO 2.x/3.x.

---

## 1. Kết quả Kiểm thử Baseline (Test Suites)

- **Tổng số test:** 47 test cases
- **Trạng thái:** 47 Passed, 0 Failed, 0 Skipped
- **Thời gian chạy toàn bộ suite:** ~291 ms
- **Danh mục bài test đã bảo vệ:**
  - `FrameChangeDetector`:
    - Phát hiện thay đổi pixel nhỏ 0.8% (phù hợp video FGO chuyển cảnh thoại).
    - Bỏ qua nhiễu dưới ngưỡng tỷ lệ pixel.
    - Nhận diện đúng frame đầu tiên và frame đồng nhất.
  - `CaptureBufferPool`:
    - Tái sử dụng buffer bộ nhớ đệm bitmap không cấp phát thừa (zero-allocation per frame).
  - `DialogueTracker`:
    - Xử lý typewriter, fuzzy prefix, stable timeout (300ms).
    - Lọc sạch timestamp video player (ví dụ `1:38 / 17:10`).
    - Bỏ các đoạn OCR rác ký tự đơn hoặc bullet (`g`, `...`, `•`).
    - Reset trạng thái thoại khi đổi cửa sổ/vùng chụp.
  - `TranslationMemory`:
    - Cache hit với Exact LRU cache (1000 items).
    - Cache hit với Fuzzy match (ngưỡng tương đồng cao).
  - `TranslationWorker` & `ReorderBuffer`:
    - Đảm bảo kết quả trả về đúng theo `SequenceId` khi chạy 3 worker song song.
    - Xử lý kịch bản provider lỗi (trả về tag cảnh báo fallback an toàn).
  - `Overlay & Subtitle`:
    - Detached Subtitle Window, Floating Toolbar, Opacity, Click-through.

---

## 2. Các chỉ số hiệu năng cơ sở (Performance Metrics)

Dựa trên các bài benchmark trong `CaptureBenchmark.cs` và `PerformanceTests.cs`:
- **Frame Difference Latency (`AvgDiffLatencyMs`):** ~0.2 – 0.5 ms / frame (nhờ thuật toán lấy mẫu `SampleScale = 2`).
- **Capture Latency (`AvgCaptureLatencyMs`):** ~2.0 – 5.0 ms / frame (BitBlt / PrintWindow GDI).
- **OCR Execution Latency (`AvgOcrLatencyMs`):**
  - Windows OCR: ~25 – 45 ms / frame.
  - OneOCR (nếu kích hoạt): ~35 – 60 ms / frame.
- **Tỷ lệ bỏ qua OCR khi không đổi hình (`OcrSkipPercentage`):** > 80% trên gameplay tĩnh hoặc thoại đứng yên.
- **Adaptive Sleep Time:**
  - Đang gõ chữ (Typewriter): ~120 – 150 ms.
  - Ổn định (Stable): ~250 – 300 ms.
  - Tĩnh (Idle / No change): ~400 – 600 ms.

---

## 3. Kiến trúc ban đầu cần nâng cấp

1. **Hotkey:** F8 và F9 đang được đăng ký cứng qua `NativeMethods.RegisterHotKey` trong `MainWindow.xaml.cs`. Cần chuyển thành `HotkeyManager` tập trung với khả năng rebind và chống xung đột.
2. **Translation Queue:** Sử dụng cố định 3 worker đọc từ `Channel.CreateUnbounded<DialogueJob>`. Cần có `AdaptiveConcurrency` giảm xuống 1 worker khi gặp lỗi hoặc 429 để tránh bão request, và `Request Coalescing` để hủy prefetch lỗi thời.
3. **API Key & Config:** Khóa API lưu dưới dạng plaintext trong `AppConfig.ApiKey`. Cần cơ chế bảo mật Windows DPAPI và migration tương thích ngược.
4. **Metrics:** Mới chỉ đo FPS, Cap/Diff/OCR latency. Cần bổ sung theo dõi Provider latency, 429 counter, cache hit rate và queue depth.
