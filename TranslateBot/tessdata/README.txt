TBCO 3.0 - Tesseract OCR Data Directory (tessdata)
===================================================

TBCO 3.0 hỗ trợ nhận diện chữ ngoại tuyến (offline) bằng Tesseract OCR v5.2.

HƯỚNG DẪN CÀI ĐẶT NGÔN NGỮ TESSDATA:
1. Tải file ngôn ngữ tương ứng từ kho tessdata chính thức của Tesseract:
   - Tiếng Nhật (Best/Fast): jpn.traineddata, jpn_vert.traineddata
   - Tiếng Anh: eng.traineddata
   - Link tải: https://github.com/tesseract-ocr/tessdata_fast hoặc https://github.com/tesseract-ocr/tessdata_best

2. Đặt các file .traineddata vào thư mục này (`tessdata/`).

3. Khởi động lại TBCO. Hệ thống sẽ tự động phát hiện và kích hoạt Tesseract trong OCR Router.

LƯU Ý:
- Nếu chưa có file .traineddata, TBCO sẽ tự động sử dụng Windows OCR (WinRT OCR) và OneOCR làm engine nhận diện chính, không làm gián đoạn trải nghiệm dịch game của bạn.
