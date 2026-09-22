namespace TranslateBot.OCR
{
    // Loại vai trò của vùng OCR — quyết định text đi vào pipeline nào.
    public enum OcrAreaType
    {
        Dialogue,   // Thoại chính → DialogueTracker → Translation
        Speaker,    // Tên nhân vật đang nói → metadata cho Context Manager (Stage 5)
        Narration,  // Lời dẫn/narration → pipeline giống Dialogue
        Custom      // Tuỳ chỉnh (người dùng tự quyết cách xử lý)
    }

    // Một vùng OCR trên màn hình, tương đối cửa sổ game (window-relative mode)
    // hoặc tuyệt đối (screen-absolute mode). Section 16 của plan: thay 1 hard-coded
    // rectangle bằng hệ thống nhiều area, mỗi area có role riêng.
    public class OcrArea
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "Untitled";
        public OcrAreaType Type { get; set; } = OcrAreaType.Dialogue;
        public bool Enabled { get; set; } = true;

        // Tọa độ vùng — relative to client area (nếu window-relative mode)
        // hoặc screen-absolute (nếu chế độ cũ).
        public int RegionX { get; set; }
        public int RegionY { get; set; }
        public int RegionWidth { get; set; }
        public int RegionHeight { get; set; }

        // Ngôn ngữ OCR cho vùng này (mỗi vùng có thể khác ngôn ngữ)
        public string Language { get; set; } = "en-US";

        // Tiền xử lý hình ảnh tối ưu riêng cho vùng này (Section 8 của Master Plan)
        public Preprocessing.PreprocessPreset Preset { get; set; } = Preprocessing.PreprocessPreset.None;
        public Preprocessing.PreprocessingOptions? CustomPreprocessing { get; set; }

        public Preprocessing.PreprocessingOptions GetEffectivePreprocessing()
        {
            if (CustomPreprocessing != null) return CustomPreprocessing;
            if (Preset != Preprocessing.PreprocessPreset.None) return Preprocessing.PreprocessingOptions.FromPreset(Preset);

            return Type switch
            {
                OcrAreaType.Speaker => Preprocessing.PreprocessingOptions.FromPreset(Preprocessing.PreprocessPreset.FgoSpeaker),
                OcrAreaType.Dialogue or OcrAreaType.Narration => Preprocessing.PreprocessingOptions.FromPreset(Preprocessing.PreprocessPreset.FgoDialogue),
                _ => Preprocessing.PreprocessingOptions.FromPreset(Preprocessing.PreprocessPreset.None)
            };
        }
    }
}
