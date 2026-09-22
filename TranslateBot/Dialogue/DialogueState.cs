namespace TranslateBot.Dialogue
{
    // Lifecycle đầy đủ theo Section 7 của plan V4/V5. 4 trạng thái đầu do
    // DialogueTracker quản lý (câu thoại đang hình thành trên màn hình); 4 trạng thái
    // sau do TranslationWorker/UI quản lý (câu thoại đã chốt, đang đi qua pipeline dịch).
    public enum DialogueState
    {
        Partial,     // Mới xuất hiện vài chữ
        Extending,   // Đang dài ra thêm (chữ đang chạy kiểu typewriter)
        Stable,      // Đã ngừng thay đổi, đang chờ xác nhận đủ ổn định
        Confirmed,   // Đã chốt nội dung cuối cùng, sẵn sàng đem đi dịch (trước đây gọi là "Completed")
        Queued,      // Đã nằm trong hàng đợi dịch, chờ tới lượt
        Translating, // Đang gọi API dịch
        Translated,  // Đã có kết quả dịch (có thể là dịch thật hoặc fallback)
        Displayed    // Đã hiển thị lên UI cho người chơi thấy
    }
}
