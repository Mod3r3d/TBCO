namespace TranslateBot.OCR
{
    // Một vùng bị loại trừ khỏi OCR (Section 17). Pixel trong vùng này bị tô đen
    // TRƯỚC khi gửi cho OCR engine, ngăn chữ UI game (AUTO, MENU, SKIP...) bị đọc
    // thành dialogue. Giải pháp tốt hơn nhiều so với filter keyword sau OCR, vì:
    //   - "Mage" là tên class nhưng cũng là từ thật trong thoại
    //   - Filter keyword có false positive; exclusion area thì chính xác theo vị trí
    public class ExclusionArea
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "Untitled";
        public bool Enabled { get; set; } = true;

        // Tọa độ tương đối với VÙNG OCR CHA (OcrArea), không phải cửa sổ game.
        // VD: nút AUTO nằm ở góc dưới-phải của dialogue box → vị trí relative
        // với dialogue OCR area.
        public int RegionX { get; set; }
        public int RegionY { get; set; }
        public int RegionWidth { get; set; }
        public int RegionHeight { get; set; }
    }
}
