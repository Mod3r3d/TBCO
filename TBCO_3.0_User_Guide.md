# Hướng Dẫn Sử Dụng Chi Tiết — TBCO (Translate Bot Conversation by OCR)

Tài liệu hướng dẫn toàn diện dành cho người dùng ứng dụng **TBCO (Translate Bot Conversation by OCR)**.

---

## MỤC LỤC
1. [Cài đặt & Khởi động lần đầu](#1-cài-đặt--khởi-động-lần-đầu)
2. [Quản lý Khóa API & Nhà Cung Cấp Dịch](#2-quản-lý-khóa-api--nhà-cung-cấp-dịch)
   - [Google Gemini & Multi-Key Pool](#21-google-gemini--multi-key-pool)
   - [DeepL Free & Pro](#22-deepl-free--pro)
   - [Custom API (OpenAI-compatible, LM Studio, OpenRouter)](#23-custom-api)
   - [Local AI với Ollama (Ngoại tuyến 100%)](#24-local-ai-với-ollama)
3. [Sử Dụng Hồ Sơ Game (Game Profiles)](#3-sử-dụng-hồ-sơ-game-game-profiles)
   - [Hồ sơ có sẵn (FGO, Blue Archive, Visual Novel)](#31-hồ-sơ-có-sẵn)
   - [Tự động nhận diện cửa sổ game](#32-tự-động-nhận-diện-cửa-sổ-game)
   - [Chuyển đổi phân cảnh (Scenes) & Ghi đè thuật ngữ](#33-chuyển-đổi-phân-cảnh)
   - [Tạo hồ sơ game mới](#34-tạo-hồ-sơ-game-mới)
4. [Bộ Nhận Diện Ký Tự (OCR Pipeline & Tesseract)](#4-bộ-nhận-diện-ký-tự-ocr-pipeline--tesseract)
   - [Windows OCR & OneOCR](#41-windows-ocr--oneocr)
   - [Cài đặt Tesseract OCR ngoại tuyến](#42-cài-đặt-tesseract-ocr-ngoại-tuyến)
   - [Vùng quét OCR & Vùng loại trừ nút bấm](#43-vùng-quét-ocr--vùng-loại-trừ-nút-bấm)
5. [Bộ Nhớ Bản Dịch (Translation Memory & SQLite)](#5-bộ-nhớ-bản-dịch-translation-memory--sqlite)
6. [Hệ Thống Phím Tắt Toàn Cục](#6-hệ-thống-phím-tắt-toàn-cục)
7. [Các Câu Hỏi Thường Gặp (FAQ)](#7-các-câu-hỏi-thường-gặp-faq)

---

## 1. Cài đặt & Khởi động lần đầu

1. Tải bản phát hành `TBCO-3.0-win-x64.zip` và giải nén vào thư mục bạn muốn lưu trữ (ví dụ: `C:\Games\TBCO`).
2. Yêu cầu hệ thống: Windows 10/11 64-bit và cài đặt [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
3. Nhấp đúp chuột vào `TBCO.exe` để khởi chạy.
4. Lần đầu khởi động, ứng dụng sẽ tự động khởi tạo:
   - Thư mục `profiles/` chứa 4 hồ sơ game chuẩn.
   - Cơ sở dữ liệu SQLite `tbco.db` lưu trữ bộ nhớ bản dịch.
   - Thư mục `logs/` ghi nhận nhật ký vận hành.

---

## 2. Quản lý Khóa API & Nhà Cung Cấp Dịch

Mở cửa sổ quản lý khóa bằng cách nhấn vào nút **[Quản lý API Key]** trên thanh công cụ.

### 2.1. Google Gemini & Multi-Key Pool
- Lấy khóa API miễn phí từ [Google AI Studio](https://aistudio.google.com/).
- Nhấn **[Thêm Key]**, nhập nhãn (Label, ví dụ: "Key 1") và dán API Key.
- **Cơ chế Multi-Key Pool**: Bạn có thể thêm 3-5 keys miễn phí. TBCO sẽ tự động xoay vòng từng key (Round-Robin). Khi một key chạm hạn mức (lỗi 429), ứng dụng sẽ tự động cho key đó nghỉ ngơi (cooldown) và chuyển ngay sang key kế tiếp, giúp bạn dịch truyện liên tục mà không bị khựng.
- Mọi API Key đều được mã hóa bằng **Windows DPAPI** bảo mật cấp hệ điều hành.

### 2.2. DeepL Free & Pro
- Lấy khóa từ trang chủ DeepL API.
- Nếu dùng gói miễn phí (Key kết thúc bằng `:fx`), TBCO sẽ tự động định tuyến đến `api-free.deepl.com`.
- Bản dịch DeepL được tự động kèm ngữ cảnh câu chuyện để giọng văn tự nhiên nhất.

### 2.3. Custom API (OpenAI-compatible)
- Hỗ trợ kết nối với LM Studio, vLLM, OpenRouter, Groq, hoặc bất kỳ máy chủ tương thích OpenAI nào.
- Nhập Endpoint: ví dụ `http://localhost:1234/v1/chat/completions`.
- Chọn Model tương ứng chạy trên máy chủ.

### 2.4. Local AI với Ollama (Ngoại tuyến 100%)
- Cài đặt [Ollama](https://ollama.com/) trên máy và tải mô hình dịch chất lượng cao:
  ```bash
  ollama run qwen2.5:7b
  ```
- Trong TBCO, chọn Provider là **Ollama** hoặc nhấn phím tắt **`Ctrl+L`** để bật **Offline Mode**.
- Khi bật Offline Mode, TBCO tuyệt đối không gửi dữ liệu ra Internet.

---

## 3. Sử Dụng Hồ Sơ Game (Game Profiles)

### 3.1. Hồ sơ có sẵn
TBCO đi kèm sẵn 4 hồ sơ được tinh chỉnh chuyên sâu:
1. **Fate/Grand Order (JP)**: Tiếng Nhật -> Tiếng Việt. Có sẵn vùng quét thoại, vùng nhận diện tên Servant, vùng che nút Skip, danh sách nhân vật Chaldea và từ điển Fate.
2. **Fate/Grand Order (NA)**: Tiếng Anh -> Tiếng Việt.
3. **Blue Archive**: Tiếng Nhật -> Tiếng Việt. Nhận diện chuẩn xưng hô "Thầy" (Sensei), Arona, Plana, Yuuka, Kivotos...
4. **Visual Novel (Generic)**: Hồ sơ chuẩn cho mọi game visual novel thông thường.

### 3.2. Tự động nhận diện cửa sổ game
- Khi mở game trên PC hoặc trên giả lập (BlueStacks, LDPlayer, Nox, MuMu), TBCO sẽ tự động nhận diện cửa sổ và nạp hồ sơ game tương ứng.
- Hoặc bạn có thể bấm **[Đổi cửa sổ]** để tự tay gán cửa sổ game đang chạy.

### 3.3. Chuyển đổi phân cảnh (Scenes)
- Mỗi game có nhiều phân cảnh (ví dụ trong FGO: Phân cảnh *Chaldea* thường nhật, phân cảnh *Lostbelt* sinh tồn chiến đấu).
- Khi đổi phân cảnh trên giao diện, TBCO sẽ cập nhật văn phong gửi đến AI và tự động nạp các thuật ngữ riêng của phân cảnh đó (ví dụ: "Tree of Emptiness" -> "Cây Hư Không").

### 3.4. Tạo hồ sơ game mới
- Bạn có thể tạo file `.json` mới trong thư mục `profiles/` hoặc sao chép từ `vn-generic.json`.
- Mọi trường dữ liệu đều là văn bản JSON trực quan, dễ dàng tùy chỉnh theo sở thích.

---

## 4. Bộ Nhận Diện Ký Tự (OCR Pipeline & Tesseract)

### 4.1. Windows OCR & OneOCR
- Ứng dụng mặc định sử dụng **Windows WinRT OCR** — bộ nhận diện tích hợp sẵn trong Windows 10/11 siêu nhanh (chỉ mất 15-30ms mỗi khung hình).

### 4.2. Cài đặt Tesseract OCR ngoại tuyến
Nếu muốn dùng Tesseract OCR:
1. Tải file `jpn.traineddata` (hoặc `eng.traineddata`) từ GitHub Tesseract.
2. Đặt file vào thư mục `tessdata/` trong thư mục TBCO.
3. Khởi động lại ứng dụng. OCR Router sẽ tự động bổ sung Tesseract vào chuỗi fallback.

### 4.3. Vùng quét OCR & Vùng loại trừ nút bấm
- Nhấn **`F10`** để mở trình biên tập vùng quét:
  - **Dialogue Area**: Vùng khung thoại nhân vật.
  - **Speaker Area**: Vùng tên nhân vật nói.
  - **Exclusion Area**: Vùng che các nút bấm không cần dịch (ví dụ nút `AUTO`, `SKIP`, `LOG`).

---

## 5. Bộ Nhớ Bản Dịch (Translation Memory & SQLite)

- Khi một câu thoại được dịch xong, TBCO sẽ tự động ghi nhớ vào bộ nhớ hai tầng:
  - **L1 RAM**: Truy xuất trong 0.05 mili-giây.
  - **L2 SQLite Disk (`tbco.db`)**: Lưu trữ vĩnh viễn trên ổ cứng.
- Khi bạn chơi lại hoặc câu thoại lặp lại (kể cả khi chữ OCR hơi mờ hoặc sai lệch nhẹ do hiệu ứng chữ chạy), hệ thống sẽ lấy ngay bản dịch cũ từ Memory mà **KHÔNG GỌI API**, giúp tiết kiệm tối đa chi phí.

---

## 6. Hệ Thống Phím Tắt Toàn Cục

| Phím Tắt | Chức Năng | Ghi Chú |
|:---|:---|:---|
| **`F8`** | **Snapshot OCR** | Chụp nhanh khung thoại và dịch tức thì (không cần bật tự động) |
| **`F9`** | **Tạm dừng / Tiếp tục** | Tạm dừng bot bắt hình hoặc tiếp tục theo dõi game |
| **`F10`** | **Quản lý vùng OCR** | Mở cửa sổ chỉnh sửa tọa độ các khung quét |
| **`F11`** | **Tách / Gắn phụ đề** | Bật cửa sổ phụ đề nổi độc lập (Glassmorphism) |
| **`Ctrl + L`** | **Bật/Tắt Local AI** | Chuyển đổi nhanh giữa chế độ Cloud và Offline Mode |

*Lưu ý: Mọi phím tắt đều được kiểm tra xung đột tự động bằng `HotkeyConflictDetector`.*

---

## 7. Các Câu Hỏi Thường Gặp (FAQ)

**Q: Ứng dụng báo lỗi "429 Quota Exceeded"?**  
A: Nếu bạn chỉ dùng 1 key miễn phí của Google, Google giới hạn 5-15 request/phút. Hãy vào **[Quản lý API Key]** và thêm thêm 2-3 key nữa; TBCO sẽ tự động xoay tua để bạn chơi game mượt mà.

**Q: Cửa sổ phụ đề có bấm xuyên chuột được không?**  
A: Có! Nhấn nút **[F9]** hoặc nút Khóa trên thanh phụ đề, chuột sẽ click xuyên qua phụ đề thẳng vào game, không gây cản trở thao tác chiến đấu.

**Q: Tôi chơi game không kết nối Internet được không?**  
A: Hoàn toàn được. Cài đặt Ollama trên máy tính, nhấn **`Ctrl+L`** trong TBCO để bật Offline Mode. Mọi quá trình OCR và dịch thuật đều diễn ra cục bộ 100%.
