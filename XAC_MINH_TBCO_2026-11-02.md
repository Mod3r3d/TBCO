# Xác minh lại TBCO — Đối chiếu với BAO_CAO_DANH_GIA_TBCO_2026-11

> Ngày xác minh: 02/11/2026 | Phiên bản: **v3.0.5** (`b29a86f`) | Trạng thái git: có thay đổi chưa commit (M BASELINE.md, M README.md, D ocr-benchmark-report.md, M 2 script, ?? SYNTHETIC-smoke-report.md, ?? run-all.ps1)
> Phương pháp: đọc trực tiếp mã nguồn + build thực tế. KHÔNG tin tài liệu mù quáng.

---

## 1. Các mục đã được SỬA THẬT (xác minh trong code)

| # | Phát hiện cũ | Trạng thái xác minh |
|---|---|---|
| E1 | Tầng "bằng chứng" giả: `ocr-benchmark-report.md` viện dẫn profile `genshin-en` + preset `GenshinDialogue` không tồn tại, chạy trên ảnh GDI+ synthetic nhưng ghi CER/WER như thật | ✅ **ĐÃ SỬA ĐÚNG BẢN CHẤT.** File giả đã bị xóa. Thay bằng `SYNTHETIC-smoke-report.md` có 3 dòng cảnh báo đỏ: "KHÔNG đại diện hiệu năng thực tế", "không dùng để kết luận", ghi rõ "Không có profile genshin-en hay preset GenshinDialogue". `run-harness.ps1`/`run-ocr-benchmark.ps1` giờ ghi rõ chỉ chạy unit test, không gọi AI. `run-all.ps1` sinh báo cáo có git SHA/OS/dotNET. |
| E2 | `BASELINE.md` lỗi thời (ghi 3.0.4/142 test) | ⚠️ Đã cập nhật lên 3.0.5/177 test — nhưng §4.3 sai (xem mục 3). |
| P0-4 | FGO JP chạy OCR tiếng Anh (`en-US` hardcode) | ✅ **ĐÃ SỬA.** `MapSourceLanguageToOcrTag` map `ja→ja-JP`, `en→en-US`, `auto→ja-JP` (MainWindow.xaml.cs:1384). |
| P1-1 | `TranslationEngine`/`TranslationWorker` không `IDisposable` | ✅ **ĐÃ SỬA.** `TranslationEngine.Dispose()` huỷ CTS + Stop worker (L583). |
| Build/Test | 177 test PASS | ✅ **XÁC NHẬN THỰC TẾ:** `dotnet build -c Release` → 0 Error, 0 Warning. Đếm `[TestMethod]`/`*Test` = **177** method, 25 file (BASELINE ghi 22 file — lệch nhẹ, không ảnh hưởng). |

---

## 2. Các mục vẫn HỎNG (tái xác minh, KHÔNG đổi)

| # | Vấn đề | Bằng chứng |
|---|---|---|
| D1 | **4 hotkey vẫn chết** — `ToggleStabilizer`(Ctrl+D), `SkipDialogue`(Alt+D), `ToggleLocalAI`(Ctrl+L), `OpenDiagnostics`(Ctrl+F12) được đăng ký trong `HotkeyManager` (L47-56) nhưng **không có `case`** trong switch `HandleHotkeyCommand`, và switch **không có `default`** → bấm không làm gì (silent no-op). Đây chính xác là 4 lỗi cũ: trước kia "chưa đăng ký", giờ "đăng ký rồi nhưng quên thêm case". | grep `AddBinding` vs `case HotkeyCommand`; switch kết thúc tại `EmergencyStop` không có default. |
| D2 | `RestartEngineAsync` gọi `_sessionManager.StartSession()` (L143) → **xoá lịch sử phiên** (`_records.Clear()`) + tạo file JSONL mới mỗi khi đổi provider/API key. | SessionManager.StartSession L50-80. |
| D3 | 6 chỗ `_ = RestartEngineAsync()` fire-and-forget (L1371,1395,1604,1644,1770,1860) → không bắt exception, có thể "restart storm". | grep. |
| D4 | **5** instance `new HttpClient`, không dispose, không `IHttpClientFactory`. | grep `new HttpClient`. |
| D5 | `AppLogger` ghi **đồng bộ** tại **181** call site (dù đã chuyển đường dẫn về `%LOCALAPPDATA%`, vẫn là sync I/O trên đường nóng). | grep `AppLogger.`. |
| D6 | `MainWindow` là god-class **2.262 dòng** (không giảm so với báo cáo trước). | `wc -l`. |

---

## 3. VẤN ĐỀ MỚI — TÀI LIỆU LỆCH THỰC TẾ (rủi ro đúng như đã cảnh báo)

Dự án sửa được file "bằng chứng giả" nhưng **lại đưa thông tin sai vào `BASELINE.md` §4.3** — chính là rủi ro "số liệu sai nguy hiểm hơn bug" tôi đã nêu:

| Dòng BASELINE §4.3 | Thực tế (HotkeyManager) | Sai? |
|---|---|---|
| `F9`: Chụp ảnh / dịch nhanh 1 lần | `F9` = `LockOverlay` (khóa/mở click-through phụ đề) | ❌ |
| `F10`: Bật/Tắt tự động nhận diện (Hook Engine) | `F10` = `ToggleOverlay` (ẩn/hiện HUD phụ đề) | ❌ |
| `Ctrl+F`: Mở quản lý Hồ sơ trò chơi (Game Profiles) | `Ctrl+F` **không được đăng ký ở đâu**; grep `GameProfileWindow`/`OpenGameProfile` = rỗng → tính năng không tồn tại | ❌ |
| `Ctrl+D`: Mở cửa sổ phụ đề tách rời | `Ctrl+D` = `ToggleStabilizer` (và là 1 trong 4 hotkey **chết** D1) | ❌ |

Cộng thêm: **README dòng 91** quảng cáo "`Ctrl+L`: Bật/Tắt Local AI" — nhưng `Ctrl+L` = `ToggleLocalAI`, một trong 4 hotkey chết (D1). Tức là README đang quảng cáo một tính năng không bấm được.

> Kết luận: Sửa file giả là đúng hướng, nhưng độ tin cậy của tài liệu vẫn chưa cao — `BASELINE.md` tự nó giờ là nguồn có thể gây hiểu nhầm.

---

## 4. Điểm tổng & phán quyết

- **Điểm: 6.6 → 6.7/10.** Được cộng nhẹ vì bằng chứng đã được minh bạch hoá đúng cách; bị trừ vì tài liệu hotkey mới bị sai và 4 hotkey vẫn chết.
- **Tín hiệu tích cực nhất:** Dự án *phản hồi có chủ đích* với phê bình — chứng tỏ quy trình review hữu ích.
- **Hành động ưu tiên tiếp theo (nhỏ, < 1 ngày):**
  1. Sửa §4.3 `BASELINE.md` theo đúng bảng `HotkeyManager` (F6/F7/F8/Ctrl+F8/F9/F10/F11/Ctrl+Shift+H/Ctrl+T/Ctrl+D/Ctrl+Shift+D/Alt+D/Ctrl+K/Ctrl+L/Ctrl+J/Ctrl+F12/F1).
  2. Hoặc — tốt hơn — **xoá 4 dòng đăng ký hotkey chết** (D1) cho đến khi có handler, tránh quảng cáo tính năng rỗng.
  3. Sửa README dòng 91 (Ctrl+L) tương ứng.
- **Vẫn giữ nguyên lộ trình Giai đoạn 1–4** từ `BAO_CAO_DANH_GIA_TBCO_2026-11.md`: CLI `TBCO.Eval` sinh report thật có git SHA + bộ ảnh OCR thật; `EngineController` có await/debounce + `ResumeSession()`; `IHttpClientFactory`; logger async.

---
*Ghi chú: Không thay đổi mã nguồn. Chỉ đọc và build để xác minh.*
