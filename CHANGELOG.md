# Changelog

Tất cả những thay đổi đáng chú ý của dự án TBCO (Translate Bot Conversation by OCR) sẽ được ghi nhận tại đây.

Định dạng dựa trên [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
và dự án tuân thủ [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [3.1.0] - 2026-11-16

### Added
- **Bounded Translation Queue & Backpressure**: Thay thế Unbounded Channel bằng Bounded Channel (sức chứa 32 slots), ưu tiên thoại Confirmed trước Prefetch, tự động drop prefetch cũ khi hàng đợi bão hòa để ngăn rò rỉ RAM.
- **Generation ID Lifecycle & Sequence Safety**: Bổ sung `GenerationId` vào `DialogueJob`, `ReorderBuffer`, và `TranslationWorker`. Tự động vô hiệu hóa và loại bỏ các bản dịch trễ từ vùng chụp/cửa sổ/hồ sơ trước đó mà không gây nhầm lẫn phụ đề.
- **Thread-Safe ReorderBuffer**: Đưa việc gọi callback `OnOrderedResult` ra ngoài phạm vi `lock (_lock)` để triệt tiêu nguy cơ deadlock với WPF Dispatcher.
- **Structured Long Dialogue & Partial Failure Handling**: Phân mảnh hội thoại dài thành các chunk có cấu trúc, trả về `TranslationResult` bảo toàn trạng thái, provider metadata và gắn cờ `PartialFailure` khi một chunk bị lỗi để loại trừ khỏi bộ nhớ Translation Memory canonical.
- **Multi-Key ApiKeyPool Rotation**: Nâng cấp thuật toán xoay vòng khóa API Gemini, cho phép duyệt qua tất cả key hợp lệ trong pool (tối đa theo ngân sách yêu cầu, ví dụ 8 key) thay vì bị giới hạn cứng ở 2 key; phân loại chính xác mã lỗi 429 (Rate Limit/Quota), Invalid Key và lỗi mạng tạm thời.
- **Multi-Monitor & DPI VirtualScreen Support**: Hỗ trợ toàn diện cấu hình đa màn hình với toạ độ âm (`NativeMethods.GetVirtualScreenBounds`), bổ sung kiểm tra hợp lệ toạ độ cửa sổ (`CaptureStatus.InvalidBounds`, `CaptureStatus.Minimized`, `CaptureStatus.InvalidWindow`).
- **Asynchronous Rolling Logger**: Thay thế synchronous Console/Debug log bằng `AppLogger` bất đồng bộ qua `Channel<string>`, tự động xoay vòng file log (5MB / ngày) lưu trữ tại `%LOCALAPPDATA%\TBCO\logs` với cờ mở `FileShare.ReadWrite`.
- **SharedHttpClient**: Tạo bộ quản lý `SharedHttpClient` dùng chung dựa trên `SocketsHttpHandler` và `PooledConnectionLifetime` cho Gemini, DeepL, Custom API và Ollama nhằm giải phóng tài nguyên và chống rò rỉ socket/TCP connection.
- **Session Continuity**: Bổ sung `SessionManager.ResumeSession()` để duy trì lịch sử hội thoại khi người dùng thay đổi cấu hình hoặc khởi động lại Translation Engine.
- **Continuous Integration (CI)**: Thêm workflow GitHub Actions `.github/workflows/ci.yml` tự động build Release và chạy test 100% trên `windows-latest`.

### Fixed
- **Dispatcher / Async Void Exceptions**: Bọc `try-catch` và `AppLogger.Error` cho `DoSnapshot()` và đồng bộ hóa các lệnh gọi cấu hình bất đồng bộ trong `MainWindow.xaml.cs`.
- **Cancellation Propagation**: Khắc phục hiện tượng rơi rớt `CancellationToken` trong `TranslationRouter`, đảm bảo dừng ngay lập tức khi người dùng hủy bỏ thay vì tiếp tục thử lại các provider khác.
- **Dead Hotkeys & Cleanup**: Loại bỏ các phím tắt không hoạt động (F4, F7, F9, F11) trong `HotkeyManager.cs`, đồng bộ tài liệu phím tắt chuẩn tại `README.md` và `BASELINE.md`.
- **Untrack Config**: Gỡ bỏ `TranslateBot/config.json` khỏi git tracking, cung cấp `TranslateBot/config.template.json` sạch toạ độ.
- **Gemini Model Modernization**: Cập nhật danh mục mô hình Gemini chính thức (`gemini-3.5-flash`, `gemini-3.8-flash`, `gemini-3.7-flash`, `gemini-3.5-flash-lite`, `gemini-3.1-pro`), tự động chuẩn hóa và loại bỏ các phiên bản 1.5/2.0/2.5 đã ngừng hỗ trợ khỏi giao diện và cấu hình.
- **Evaluation Benchmark Transparency**: Cập nhật cảnh báo dữ liệu tổng hợp GDI+ cho `evaluation/reports/SYNTHETIC-smoke-report.md`, cung cấp script hợp nhất `evaluation/run-all.ps1`.

---

## [3.0.5] - 2026-10-26
- Baseline release: Hỗ trợ Windows OCR, OneOCR, Tesseract.
- Game Profiles (FGO JP, FGO NA, Blue Archive, VN).
- Detached Subtitle Window, Floating Toolbar, SQLite Translation Memory.
