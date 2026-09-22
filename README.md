# TBCO — Translate Bot Conversation by OCR (v3.0 PRO)

<p align="center">
  <img src="TranslateBot/TBCO-Icon.png" alt="TBCO Logo" width="120" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D6?logo=windows" alt="Platform Windows" />
  <img src="https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/AI-Gemini%20%7C%20DeepL%20%7C%20Ollama%20Local-4285F4?logo=google" alt="AI Providers" />
  <img src="https://img.shields.io/badge/Status-Release%20v3.0.0-10B981" alt="Version 3.0.0" />
  <img src="https://img.shields.io/badge/Tests-118%2F118%20Passing-success" alt="Tests 118" />
</p>

---

## 📖 Giới thiệu

**TBCO (Translate Bot Conversation by OCR)** là ứng dụng dịch thuật thời gian thực chuyên sâu dành cho visual novel, anime RPG và game cốt truyện (đặc biệt tối ưu hóa sâu cho *Fate/Grand Order* và *Blue Archive*). 

Kế thừa các công nghệ cốt lõi từ MORT và mở rộng với kiến trúc LLM đa tầng, TBCO 3.0 mang lại tốc độ phản hồi tính bằng mili-giây, khả năng dịch ngoại tuyến 100%, bộ nhớ bản dịch SQLite hai tầng không trùng lặp, cơ chế xoay vòng Multi-Key chống lỗi Rate Limit 429, cùng hệ thống Hồ sơ Game (Game Profiles) tự động nhận diện cửa sổ và phân cảnh câu chuyện.

---

## 🚀 Các Tính Năng Đột Phá Trên TBCO 3.0

### 1. 🔑 Multi-Key Pool & Khả Năng Tự Phục Hồi (Self-Healing)
- **Quản lý đa khóa API**: Xoay vòng linh hoạt nhiều key Gemini (Round-robin leasing).
- **Tự động xử lý lỗi 429 / 401**: Khi một key chạm hạn mức (HTTP 429), hệ thống tự động đưa vào danh sách chờ nguội (cooldown timer) và chuyển mượt sang key tiếp theo mà không làm gián đoạn câu dịch. Key sai (401) bị vô hiệu hóa an toàn.
- **Bảo mật Secret Vault (Windows DPAPI)**: Mọi khóa API được mã hóa bằng thuật toán DPAPI của hệ điều hành, không bao giờ lưu plaintext trong ổ cứng.

### 2. 🧠 Chuỗi Nhà Cung Cấp Đa Tầng (Multi-Provider Router & Local AI)
- **Đa dạng AI**: Hỗ trợ đồng thời **Google Gemini**, **DeepL (Free & Pro)**, **Custom API (OpenAI-compatible / LM Studio / OpenRouter / Groq)**, và **Local AI (Ollama `qwen2.5:7b`)**.
- **Chế độ Ngoại Tuyến (Offline Mode)**: Dịch 100% trên máy bằng mô hình ngôn ngữ cục bộ qua Ollama, hoàn toàn bảo mật và không gửi bất kỳ dữ liệu nào lên Internet.
- **Dự phòng khẩn cấp**: Fallback tự động đa tầng: `Gemini -> DeepL -> Custom API -> Local AI -> Google Web Translate`.

### 3. 💾 Bộ Nhớ Bản Dịch Hai Tầng Siêu Tốc (Two-Level Translation Memory)
- **L1 RAM LRU Cache**: Truy xuất tức thì (< 0.1 ms) cho các câu thoại xuất hiện nhiều lần.
- **L2 SQLite Persistent Storage**: Lưu trữ vĩnh viễn trên đĩa với chế độ WAL siêu tốc, ghi nhớ toàn bộ hội thoại và biến thể OCR fuzzy giữa các phiên chơi.
- **Bỏ qua gọi API**: Câu thoại cũ đã có trong Memory sẽ trả kết quả ngay, tiết kiệm 100% quota và chi phí API.

### 4. 👁️ Pipeline Tiền Xử Lý Ảnh & OCR Router (MORT Parity)
- **Tiền xử lý ảnh chuyên sâu**: Phóng to 2x sắc nét giữ cạnh font chữ (Scale2xNearest), chuẩn hóa sắc độ xám ITU-R BT.601, tách ngưỡng tự động Otsu, và bộ lọc cô lập màu vàng `#FFD700` đặc trưng cho tên Servant FGO.
- **OCR Router Fallback**: Điều phối linh hoạt giữa **Windows WinRT OCR**, **OneOCR**, và **Tesseract 5.2.0**.

### 5. 🎮 Hồ Sơ Game & Phân Cảnh (Game Profiles & Scene Intelligence)
- **Tự động nhận diện (Game Detector)**: Tính điểm cửa sổ dựa trên Process Name và Window Title để tự động nạp hồ sơ game tương ứng.
- **Hồ sơ tích hợp sẵn**:
  - `Fate/Grand Order (JP)` & `Fate/Grand Order (NA)`: Vùng quét thoại, vùng tên người nói, vùng che nút Skip/Menu, hồ sơ nhân vật (Mash, Ritsuka, Da Vinci, Romani, Gilgamesh...), từ điển game (Master, Servant, Noble Phantasm, Saint Quartz, Lostbelt...).
  - `Blue Archive`: Nhận diện Sensei, Arona, Plana, Yuuka, Kivotos, Schale...
  - `Visual Novel (Generic)`: Preset chuẩn cho game VN.
- **Chuyển đổi Phân cảnh (Scene Switching)**: Thay đổi ngữ cảnh câu chuyện giữa các chương hồi (ví dụ: Chaldea thường nhật sang Lostbelt sinh tồn), tự động bổ sung thuật ngữ riêng của chương hồi mà không làm bẩn từ điển gốc.

### 6. 🪟 Giao Diện Độc Lập & Phím Tắt Toàn Cục
- **Cửa sổ phụ đề tách rời (Detached Subtitle)**: Phong cách Dark Glassmorphism, trong suốt xuyên chuột (`Click-Through`), ghim nổi trên cùng.
- **Hệ thống phím tắt toàn cục**:
  - `F8`: Chụp nhanh và dịch tức thì (Snapshot OCR).
  - `F9`: Tạm dừng / Tiếp tục bắt hình tự động.
  - `F10`: Mở bộ chỉnh sửa vùng quét OCR.
  - `F11`: Bật/tắt cửa sổ phụ đề tách rời.
  - `Ctrl+L`: Bật/tắt nhanh chế độ Local AI ngoại tuyến.

---

## 📥 Hướng Dẫn Cài Đặt & Sử Dụng

### Yêu Cầu Hệ Thống
- Hệ điều hành: Windows 10 (bản 19041 trở lên) hoặc Windows 11 (x64).
- Cài đặt sẵn [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (hoặc sử dụng bản đóng gói SelfContained).

### Khởi Chạy Nhanh
1. Tải file `TBCO-3.0-win-x64.zip` từ thư mục `publish/` hoặc GitHub Releases.
2. Giải nén vào một thư mục (ví dụ `D:\TBCO`).
3. Chạy file **`TBCO.exe`**.
4. Vào mục **[Quản lý API Key]** để thêm các khóa Gemini hoặc DeepL của bạn.
5. Mở game, TBCO sẽ tự động nhận diện cửa sổ và nạp cấu hình tối ưu nhất!

---

## 🛠️ Dành Cho Nhà Phát Triển

### Biên dịch & Chạy kiểm thử tự động
```powershell
# Chạy toàn bộ 118 test cases
dotnet test TranslateBot.Tests\TranslateBot.Tests.csproj

# Biên dịch Release
dotnet build -c Release
```

### Đóng gói phát hành tự động
Sử dụng script đóng gói tích hợp sẵn:
```powershell
.\publish.ps1
```
Bản phát hành hoàn chỉnh cùng tệp nén `.zip` sẽ được tạo tự động tại thư mục `publish/`.

---

## 📜 Giấy Phép & Bản Quyền
Dự án được xây dựng và chia sẻ phi lợi nhuận cho cộng đồng game thủ yêu thích tiểu thuyết hình ảnh và anime game. Mọi đóng góp và báo lỗi đều được trân trọng tiếp nhận!
