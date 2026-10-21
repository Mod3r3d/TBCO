# TBCO — Translate Bot Conversation by OCR (v3.0.4 PRO)

<p align="center">
  <img src="TranslateBot/TBCO-Icon.png" alt="TBCO Logo" width="120" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D6?logo=windows" alt="Platform Windows" />
  <img src="https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/AI-Gemini%203.x%20%7C%20DeepL%20%7C%20Google%20Web%20%7C%20Ollama-4285F4?logo=google" alt="AI Providers" />
  <img src="https://img.shields.io/badge/Release-v3.0.4%20PRO-10B981" alt="Version 3.0.4" />
  <img src="https://img.shields.io/badge/Tests-142%2F142%20Passing-success" alt="Tests 142" />
</p>

---

## ⚡ Tải Về Nhanh (Download)

> 🟢 **TẢI BẢN DỰNG SẴN MỚI NHẤT:** [**👉 Bấm vào đây để tải TBCO-win-x64.zip (v3.0.4)**](https://github.com/Mod3r3d/TBCO/releases/download/v3.0.4/TBCO-win-x64.zip)

> [!IMPORTANT]
> **DÀNH CHO NGƯỜI CHƠI GAME (KHÔNG PHẢI LẬP TRÌNH VIÊN):**
> - Hãy tải file **`TBCO-win-x64.zip`** từ mục **Releases** (hoặc link xanh ở trên).
> - **⚠️ TUYỆT ĐỐI KHÔNG TẢI `Source code (zip)`**: File mã nguồn chỉ chứa code lập trình `.cs`, không có file chạy `.exe`.
> - Gói phát hành `TBCO-win-x64.zip` được sắp xếp theo cấu trúc **2 tầng cực kỳ gọn gàng**: giải nén ra bạn sẽ thấy ngay file **`TBCO.exe`** nổi bật ở thư mục gốc để mở game.

---

## 📖 Giới thiệu

**TBCO (Translate Bot Conversation by OCR)** là ứng dụng dịch thuật thời gian thực chuyên sâu dành cho visual novel, anime RPG và game cốt truyện (đặc biệt tối ưu hóa sâu cho *Fate/Grand Order* và *Blue Archive*). 

Kế thừa các công nghệ cốt lõi từ MORT và mở rộng với kiến trúc LLM đa tầng, TBCO 3.0 mang lại tốc độ phản hồi tính bằng mili-giây, khả năng dịch ngoại tuyến 100%, bộ nhớ bản dịch SQLite hai tầng không trùng lặp, cơ chế xoay vòng Multi-Key chống lỗi Rate Limit 429, cùng hệ thống Hồ sơ Game (Game Profiles) tự động nhận diện cửa sổ và phân cảnh câu chuyện.

---

## 🚀 Các Tính Năng Đột Phá Trên TBCO 3.0 PRO

### 1. 🔑 Multi-Key Pool & Khả Năng Tự Phục Hồi (Self-Healing)
- **Quản lý đa khóa API**: Xoay vòng linh hoạt nhiều key Gemini (Round-robin leasing).
- **Tự động lưu tức thì (Auto-Persistence)**: Mọi thao tác thêm, xóa, bật/tắt hoặc dán hàng loạt API Key và toàn bộ thiết lập ứng dụng đều được tự động lưu vĩnh viễn trên đĩa, không bao giờ bị mất hoặc reset khi tắt app.
- **Tự động xử lý lỗi 429 / 400**: Khi một key chạm hạn mức (HTTP 429), hệ thống tự động đưa vào danh sách chờ nguội (cooldown timer) và chuyển mượt sang key tiếp theo. Key không hợp lệ (`API_KEY_INVALID` 400) sẽ được đánh dấu cảnh báo và tự động fallback sang Google Web Translate để không ngắt quãng trận game.
- **Bảo mật Secret Vault (Windows DPAPI)**: Mọi khóa API (Gemini, DeepL) được mã hóa bằng thuật toán DPAPI của Windows, không bao giờ lưu plaintext trong ổ cứng.

### 2. 🧠 Chuỗi Nhà Cung Cấp Đa Tầng (Multi-Provider Router & DeepL)
- **Linh hoạt lựa chọn bộ dịch**: Hỗ trợ trực tiếp từ giao diện:
  - **Google Gemini AI 3.x**: Các model tiên tiến nhất (`gemini-3.5-flash`, `gemini-3.6-flash`, `gemini-3.1-flash-lite`, `gemini-3.5-flash-lite`, `gemini-3.1-pro-preview`); đã loại bỏ hoàn toàn các bản 1.5/2.5 đã lỗi thời.
  - **DeepL Translate**: Dịch tự nhiên, văn phong chuẩn quốc tế, tự động hỗ trợ cả DeepL Free (`:fx`) và DeepL Pro API.
  - **Google Web Translate**: Dịch nhanh miễn phí 100% không yêu cầu API Key.
  - **Local AI (Ollama `qwen2.5:7b`)**: Dịch 100% ngoại tuyến, bảo mật tuyệt đối.
- **Fallback tự động liên tầng**: Khi bộ dịch chính gặp lỗi mạng hoặc hết quota, router tự động chuyển đổi thông minh xuống các tầng kế tiếp để không làm gián đoạn việc chơi game.

### 3. 📜 Tự Động Ghép Nối Cuộn Dòng Thoại (Auto-Stitch Scroll Dialogue)
- **Giải quyết triệt để vấn đề câu thoại dài**: Trong visual novel/JRPG khi hộp thoại cuộn qua 2-3 dòng (dòng cũ bị trôi, dòng mới xuất hiện), TBCO tự động phân tích độ trùng lặp hậu tố/tiền tố để ghép hoàn chỉnh câu nói, dịch trọn vẹn ngữ nghĩa cả câu thay vì chỉ dịch mẩu cuối.

### 4. 🖥️ Tương Thích Mọi Màn Hình (14", 16" & HiDPI)
- **Giao diện co giãn thông minh**: Hỗ trợ chuẩn xác từ màn hình laptop nhỏ 14 inch (tối thiểu 520x460) đến màn hình 16 inch và 4K.
- **DPI Aware PerMonitorV2**: Giao diện và chữ phụ đề luôn hiển thị sắc nét, không bị nhòe vỡ hay tràn màn hình khi thay đổi tỷ lệ thu phóng (125%, 150%, 200%).

### 5. 💾 Bộ Nhớ Bản Dịch Hai Tầng Siêu Tốc (Two-Level Translation Memory)
- **L1 RAM LRU Cache**: Truy xuất tức thì (< 0.1 ms) cho các câu thoại xuất hiện nhiều lần.
- **L2 SQLite Persistent Storage**: Lưu trữ vĩnh viễn trên đĩa với chế độ WAL siêu tốc, ghi nhớ toàn bộ hội thoại và biến thể OCR fuzzy giữa các phiên chơi.
- **Bỏ qua gọi API**: Câu thoại cũ đã có trong Memory sẽ trả kết quả ngay, tiết kiệm 100% quota và chi phí API.

### 6. 👁️ Pipeline Tiền Xử Lý Ảnh & OCR Router (MORT Parity)
- **Tiền xử lý ảnh chuyên sâu**: Phóng to 2x sắc nét giữ cạnh font chữ (Scale2xNearest), chuẩn hóa sắc độ xám ITU-R BT.601, tách ngưỡng tự động Otsu, và bộ lọc cô lập màu vàng `#FFD700` đặc trưng cho tên Servant FGO.
- **OCR Router Fallback**: Điều phối linh hoạt giữa **Windows WinRT OCR**, **OneOCR**, và **Tesseract 5.2.0**.

### 7. 🎮 Hồ Sơ Game & Phân Cảnh (Game Profiles & Scene Intelligence)
- **Tự động nhận diện (Game Detector)**: Tính điểm cửa sổ dựa trên Process Name và Window Title để tự động nạp hồ sơ game tương ứng.
- **Hồ sơ tích hợp sẵn**:
  - `Fate/Grand Order (JP)` & `Fate/Grand Order (NA)`: Vùng quét thoại, vùng tên người nói, vùng che nút Skip/Menu, hồ sơ nhân vật (Mash, Ritsuka, Da Vinci, Romani, Gilgamesh...), từ điển game (Master, Servant, Noble Phantasm, Saint Quartz, Lostbelt...).
  - `Blue Archive`: Nhận diện Sensei, Arona, Plana, Yuuka, Kivotos, Schale...
  - `Visual Novel (Generic)`: Preset chuẩn cho game VN.
- **Chuyển đổi Phân cảnh (Scene Switching)**: Thay đổi ngữ cảnh câu chuyện giữa các chương hồi (ví dụ: Chaldea thường nhật sang Lostbelt sinh tồn), tự động bổ sung thuật ngữ riêng của chương hồi mà không làm bẩn từ điển gốc.

### 8. 🪟 Giao Diện Độc Lập & Bảng Phím Tắt Tiện Dụng
- **Cửa sổ phụ đề tách rời (Detached Subtitle)**: Phong cách Dark Glassmorphism, trong suốt xuyên chuột (`Click-Through`), ghim nổi trên cùng.
- **Phụ đề gọn gàng không che khuất màn hình**: Hỗ trợ ẩn nhãn tên người nói trên bảng dịch (mặc định tắt) để giao diện luôn thoáng sạch, không vướng mắt khi đọc thoại. Đồng thời câu dịch từ AI được tinh lọc loại bỏ tiền tố tên nhân vật bị lặp.
- **Đồng bộ vùng quét tức thì**: Kéo chọn vùng bằng `Ctrl + F8` sẽ tự động đồng bộ và lưu vào danh sách Quản lý Vùng OCR.
- **Bảng tra cứu phím tắt (Hotkey Cheatsheet)**: Nhấn **[F1]** hoặc nút `[⌨ Phím tắt]` trên màn hình để mở bảng tra cứu phím trực quan.
- **Hệ thống phím an toàn (không xung đột gõ văn bản Windows)**:
  - `F6`: Bật/Tắt quét liên tục tự động (Hook Loop).
  - `F7`: Chụp và dịch 1 khung hình.
  - `F8`: Chụp nhanh lưu ảnh & dịch tức thì (Snapshot).
  - `Ctrl+F8`: Chọn và lưu vùng quét OCR thoại mới.
  - `F9`: Khóa/Mở khóa xuyên chuột phụ đề (Click-Through).
  - `F10`: Ẩn/Hiện thanh phụ đề nổi (Overlay HUD).
  - `F11`: Tách rời / Gắn lại cửa sổ phụ đề độc lập.
  - `Ctrl+T`: Dịch lại câu thoại gần nhất.
  - `Ctrl+L`: Bật/Tắt chế độ Local AI ngoại tuyến.
  - `F1`: Mở bảng tra cứu phím tắt.

---

## 📥 Hướng Dẫn Cài Đặt & Sử Dụng

### Yêu Cầu Hệ Thống
- Hệ điều hành: Windows 10 (bản 19041 trở lên) hoặc Windows 11 (x64).
- Cài đặt sẵn [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (hoặc sử dụng bản đóng gói SelfContained).

### Khởi Chạy Nhanh
1. Tải file **`TBCO-win-x64.zip`** từ mục [GitHub Releases](https://github.com/Mod3r3d/TBCO/releases/latest).
2. Giải nén file zip vào bất kỳ thư mục nào (ví dụ `D:\Games\TBCO`).
3. Cấu trúc thư mục sau khi giải nén cực kỳ gọn gàng:
   ```text
   📁 TBCO-win-x64/
     ├── 🚀 TBCO.exe              <--- Nhấp đúp vào đây để mở ứng dụng ngay!
     ├── ⚡ KHOI_CHAY_TBCO.bat    <--- File khởi chạy nhanh dự phòng
     ├── 📄 HUONG_DAN_SU_DUNG.txt <--- Hướng dẫn chi tiết bằng tiếng Việt
     └── 📁 app/                  <--- Toàn bộ thư viện DLL và dữ liệu (không cần can thiệp)
   ```
4. Nhấp đúp vào file **`TBCO.exe`** để mở ứng dụng.
5. Vào mục **[Cài đặt & API]** để chọn bộ dịch ưa thích (Google Gemini, DeepL Translate hoặc Google Web Translate miễn phí không cần key).
6. Mở game, TBCO sẽ tự động nhận diện cửa sổ và nạp cấu hình tối ưu nhất!

---

## 🛠️ Dành Cho Nhà Phát Triển

### Biên dịch & Chạy kiểm thử tự động
```powershell
# Chạy toàn bộ 142 test cases
dotnet test TranslateBot.Tests\TranslateBot.Tests.csproj

# Biên dịch Release
dotnet build -c Release
```

### Đóng gói phát hành tự động
Sử dụng script đóng gói 2 tầng chuẩn phát hành:
```powershell
.\build-release.ps1
```
Bản phát hành hoàn chỉnh cùng tệp nén `TBCO-win-x64.zip` sẽ được tạo tự động tại thư mục `release/`.

---

## 📜 Giấy Phép & Bản Quyền
Dự án được xây dựng và chia sẻ phi lợi nhuận cho cộng đồng game thủ yêu thích tiểu thuyết hình ảnh và anime game. Mọi đóng góp và báo lỗi đều được trân trọng tiếp nhận!
