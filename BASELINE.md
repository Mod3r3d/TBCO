# Báo cáo Baseline và Hiện trạng Kỹ thuật TBCO

> Được lập theo **Giai đoạn 0** của [plan.md](plan.md).  
> Ngày thực hiện: 26/10/2026.  
> Phiên bản phần mềm: **3.0.4** | Branch: **main** | Commit: **1adf530**.

---

## 1. Mốc Baseline Hệ thống

| Tiêu chí | Giá trị xác nhận | Nguồn đối chiếu |
|---|---|---|
| **Git Commit** | `1adf530` (`feat(core): release v3.0.4 with persistent config, Ctrl+F8 OCR sync, and uncluttered subtitles`) | `git log -n 1` |
| **Git Branch** | `main` (up-to-date with `origin/main`) | `git status` |
| **Assembly Version** | `3.0.4.0` / Package Version: `3.0.4` | `TranslateBot/TranslateBot.csproj` |
| **Target Framework** | `.NET 8.0 Windows` (`net8.0-windows10.0.19041.0`) | `TranslateBot.csproj` |
| **Trạng thái Build** | Thành công (0 Error, 0 Warning) | `dotnet build` |
| **Trạng thái Unit Tests** | **142 / 142 tests PASS** (100%) | `dotnet test` |
| **Đường dẫn dữ liệu runtime** | `%LOCALAPPDATA%\TBCO` | `TranslateBot/Infrastructure/AppPaths.cs` |

---

## 2. Phân loại và Kiểm kê 142 Unit & Integration Tests

Toàn bộ 142 bài kiểm tra tự động đã được kiểm tra và phân loại theo 4 nhóm chức năng:

| Nhóm kiểm thử | Số lượng test | Các file kiểm thử chính | Nội dung bảo vệ |
|---|:---:|---|---|
| **Unit Tests** | **55** | `ImagePreprocessingTests.cs` (8)<br>`AdaptiveStabilizationTests.cs` (4)<br>`HotkeyTests.cs` (7)<br>`PerformanceTests.cs` (11)<br>`SpeakerResolverAndContextTests.cs` (6)<br>`OcrRouterTests.cs` (5)<br>`PersistentMemoryTests.cs` (3)<br>`MultiProviderTests.cs` (5)<br>`SecurityAndConfigTests.cs` (6) | Bộ lọc ảnh, nhận dạng phím tắt, băm sai biệt khung hình, xử lý xưng hô thoại/ngoặc/quote, mã hóa DPAPI SecretVault, pool bộ đệm capture. |
| **Integration Tests** | **27** | `ApiKeyPoolAndRouterTests.cs` (8)<br>`RouterMultiProviderTests.cs` (8)<br>`QueueAndConcurrencyTests.cs` (4)<br>`GameProfileTests.cs` (7) | Luân chuyển Round-Robin API key, cooldown 429, hủy key 401, chuỗi fallback đa tầng (Gemini → DeepL → CustomAPI → LocalAI → GoogleWeb), giới hạn đồng thời động, lưu/nạp Game Profile. |
| **UI & Runtime Tests** | **29** | `OverlayTests.cs` (9)<br>`ReliabilityAndFixesTests.cs` (12)<br>`PackagingSanityTests.cs` (7)<br>`Test1.cs` (1) | Preset hiển thị phụ đề (Chaldea, HighContrast, LayerMode), khởi tạo STA an toàn cho cửa sổ phụ đề nổi (Detached Subtitle), lọc rác timestamp player, kiểm tra tính toàn vẹn file manifest/tessdata/config. |
| **Evaluation / Regression Tests** | **16** | `EvaluationRegressionTests.cs` (16) | Kiểm thử hồi quy các lỗi kiến trúc P0/P1: Tọa độ ExclusionArea, ngôn ngữ OCR Windows, khôi phục circuit breaker, ghép câu cuộn (scroll dialogue stitching), đồng bộ vùng quét Ctrl+F8, cờ huy hiệu Speaker. |
| **TỔNG CỘNG** | **142** | **19 file kiểm thử** | **Đạt 100% tỉ lệ vượt qua.** |

---

## 3. Đối chiếu các hạng mục cải tiến kiến trúc (So với báo cáo cũ)

| Mã | Hạng mục | Trạng thái hiện tại | Vị trí mã nguồn đối chiếu |
|---|---|:---:|---|
| **P0-1** | `RestartEngineAsync` thread-safe | `Đã có trong mã` | [MainWindow.xaml.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/MainWindow.xaml.cs#L108-L156) sử dụng `SemaphoreSlim(1,1)` |
| **P0-2** | Loại bỏ đăng ký trùng lặp `DialogueHistory.OnNewEntry` | `Đã có trong mã` | [MainWindow.xaml.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/MainWindow.xaml.cs#L294-L295) (Unregister trước khi Register) |
| **P0-3** | Kích hoạt và ưu tiên xử lý vùng OCR `Speaker` trước `Dialogue` | `Đã có trong mã` | [TranslationEngine.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/Infrastructure/TranslationEngine.cs#L218-L220) (`OrderBy(a => a.Type == OcrAreaType.Speaker ? 0 : 1)`) |
| **P0-4** | Ánh xạ động mã ngôn ngữ Windows OCR kèm fallback an toàn | `Đã có trong mã` | [MainWindow.xaml.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/MainWindow.xaml.cs#L1384-L1425) (`MapSourceLanguageToOcrTag` & `CreateSafeWindowsOcr`) |
| **P0-5** | Quy đổi tọa độ `ExclusionArea` theo offset khung capture | `Đã có trong mã` | [ExclusionManager.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/OCR/ExclusionManager.cs#L46-L81) (Hỗ trợ `offsetX`, `offsetY` và tính intersection bounding-box) |
| **P0-6** | `SecretVault` fail-closed (ném lỗi thay vì lưu plaintext) | `Đã có trong mã` | [SecretVault.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/Infrastructure/Security/SecretVault.cs#L22-L38) (`CryptographicException`) |
| **P0-7** | Tập trung hóa đường dẫn runtime tại `%LOCALAPPDATA%\TBCO` | `Đã có trong mã` | [AppPaths.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/Infrastructure/AppPaths.cs) |
| **P0-8** | Điều tiết đồng thời động không khóa (`DynamicConcurrencyLimiter`) | `Đã có trong mã` | [DynamicConcurrencyLimiter.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/Translation/DynamicConcurrencyLimiter.cs) |
| **P1-1** | Triển khai `IDisposable` giải phóng worker & engine | `Đã có trong mã` | [TranslationEngine.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/Infrastructure/TranslationEngine.cs#L583-L598), [TranslationWorker.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/Translation/TranslationWorker.cs#L330-L343) |
| **P1-2** | Tránh phóng to 2 lần ảnh nhỏ trong `WindowsOcrEngine` | `Đã có trong mã` | [WindowsOcrEngine.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/OCR/WindowsOcrEngine.cs#L19) (`AutoUpscaleSmallImages = false`) |
| **P1-3** | Đồng bộ chu kỳ quét `ScanIntervalMs` từ UI vào vòng lặp engine | `Đã có trong mã` | [MainWindow.xaml.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/MainWindow.xaml.cs#L1319-L1328) |
| **P1-4** | Tự động copy bản dịch vào Clipboard khi có kết quả mới | `Đã có trong mã` | [MainWindow.xaml.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/MainWindow.xaml.cs#L278-L289) |
| **P1-5** | Báo cáo đầy đủ độ trễ thật (`LatencyMs`), `IsCacheHit`, provider | `Đã có trong mã` | [MainWindow.xaml.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/MainWindow.xaml.cs#L262-L273) |
| **P1-7** | Circuit breaker phục hồi Half-Open trong `ProviderHealthTracker` | `Đã có trong mã` | [ProviderHealthTracker.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/Translation/ProviderHealthTracker.cs#L45-L72) |
| **P1-10**| Thread-safety cho `DialogueTracker` và `PerformanceMetrics` | `Đã có trong mã` | [PerformanceMetrics.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/Diagnostics/PerformanceMetrics.cs) (`Interlocked` và lock cục bộ) |
| **P1-12**| Giới hạn dung lượng và tự động cắt tỉa SQLite Translation Memory | `Đã có trong mã` | [SqliteMemoryStore.cs](file:///c:/Users/ad/Documents/Workspace/TBCO-fixed/TBCO/TranslateBot/Database/SqliteMemoryStore.cs#L115-L135) |

---

## 4. Kiểm kê Cấu hình Runtime Hiện tại

### 4.1. Nhà cung cấp dịch (Translation Providers)
- **Gemini**: Hỗ trợ xoay tua ApiKeyPool, quản lý qua `Ctrl+K`. Các model hỗ trợ:
  - `gemini-2.5-flash`
  - `gemini-2.5-pro`
  - `gemini-3-flash`
  - `gemini-3-pro`
  - `gemini-3.5-flash` (Mặc định được khuyến nghị cho tốc độ và văn phong)
  - `gemini-3.5-pro`
- **DeepL**: Sử dụng Auth Key từ tab Cài đặt AI, hỗ trợ endpoint Free & Pro.
- **Custom API**: Tương thích chuẩn OpenAI (hỗ trợ OpenAI, Groq, OpenRouter, v.v.).
- **Local AI (Ollama)**: Dịch ngoại tuyến nội bộ qua localhost (port 11434).
- **Google Web (Fallback)**: Dịch máy dự phòng khẩn cấp khi tất cả provider AI không khả dụng.

### 4.2. Bộ máy nhận diện ký tự (OCR Engines)
- **Windows OCR**: Tích hợp sẵn qua WinRT native API. Hỗ trợ đa ngôn ngữ (`ja-JP`, `en-US`, `zh-Hans-CN`, `zh-Hant-TW`, `ko-KR`, `vi-VN`).
- **OneOCR**: Engine tiên tiến thử nghiệm, tự động bọc trong `OcrEngineWithFallback` để quay về Windows OCR nếu máy người dùng thiếu runtime.
- **Tesseract OCR**: Hỗ trợ offline truyền thống với dữ liệu ngôn ngữ trong thư mục `tessdata`.

### 4.3. Phím tắt hệ thống (Hotkeys)
- **F9**: Chụp ảnh màn hình / dịch nhanh 1 lần.
- **F10**: Bật / Tắt chế độ tự động nhận diện theo thời gian thực (Hook Engine).
- **Ctrl + F8**: Chọn trực tiếp vùng nhận dạng thoại trên màn hình (đồng bộ tức thì vào `OcrAreaManager`).
- **Ctrl + K**: Mở giao diện quản lý đa khóa API Key Pool.
- **Ctrl + F**: Mở trình quản lý Hồ sơ trò chơi (Game Profiles).
- **Ctrl + D**: Mở cửa sổ phụ đề tách rời (Detached Subtitles).

---

## 5. Rủi ro Kỹ thuật & Định hướng thực thi tiếp theo

1. **Rủi ro Context & Provider Attribution khi chạy song song**:
   - Hiện tại, `TranslationWorker` xử lý các job qua router dùng chung. Nếu hai request chạy đồng thời mà provider bị fallback, `TranslationRouter.CurrentProviderName` có thể phản ánh sai trạng thái của request trước đó.
   - *Giải pháp*: Chuẩn hóa Hợp đồng dữ liệu `TranslationRequest` & `TranslationResult` mang metadata snapshot độc lập theo từng request (thực hiện tại **Giai đoạn 3**).

2. **Chất lượng dịch và kiểm thử hồi quy prompt**:
   - Hiện app chưa có công cụ kiểm thử ngoại tuyến (offline runner) để tự động chấm điểm ngữ cảnh, xưng hô ("ta - cậu", "tôi - cậu", "senpai - em") và thuật ngữ game mà không cần mở giao diện WPF.
   - *Giải pháp*: Xây dựng **Translation Quality Harness** tại **Giai đoạn 1**.

3. **Yêu cầu người dùng đối với Bước 4 (Translation Memory)**:
   - Theo chỉ thị của người dùng: *"bỏ qua bước 4 thực hiện tuần tự hoàn thiện từng bước 1 rồi mới đến bước tiếp theo"*.
   - Do đó, **Giai đoạn 4 (Cache theo ngữ cảnh và Translation Memory an toàn)** sẽ được **bỏ qua** để giữ nguyên vẹn cơ chế SQLite hiện tại, chuyển tiếp từ Giai đoạn 3 sang Giai đoạn 5 (Prompt, context và văn phong dịch tiếng Việt).

---

## 6. Kết luận Giai đoạn 0

- Trạng thái hiện tại của source code đạt độ ổn định cao: toàn bộ 142 bài kiểm tra tự động vượt qua, các sửa đổi nền tảng từ báo cáo cũ đã được tích hợp đầy đủ.
- Hệ thống đã sẵn sàng để chuyển sang **Giai đoạn 1 — Translation Quality Harness**.
