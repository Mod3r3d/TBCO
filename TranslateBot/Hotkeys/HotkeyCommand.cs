namespace TranslateBot.Hotkeys
{
    /// <summary>
    /// Các lệnh tương ứng với hotkey trong toàn bộ ứng dụng (TBCO Master Plan Section 5).
    /// </summary>
    public enum HotkeyCommand
    {
        None = 0,

        // Capture
        ToggleCapture,
        CaptureOnce,
        Snapshot,
        SelectWindow,
        SelectRegion,
        EditExclusionRegion,
        PauseResume,

        // Overlay
        ToggleOverlay,
        LockOverlay,
        ToggleClickThrough,

        // OCR
        OCRNow,
        OCRNextEngine,
        OpenOCRSettings,

        // Dialogue
        ToggleStabilizer,
        FinalizeDialogue,
        SkipDialogue,
        Retranslate,

        // API & Provider
        OpenApiKeyManager,
        NextCredential,
        RetryCurrentCredential,
        SwitchProvider,

        // Memory
        OpenTranslationMemory,
        SaveCurrentTranslation,

        // Audio & Local
        ToggleTTS,
        ToggleLocalAI,

        // History & Diagnostics
        OpenHistory,
        ExportSession,
        OpenDiagnostics,
        ToggleDebugHUD,
        OpenHotkeyHelp,
        DetachSubtitle,

        // Emergency
        EmergencyStop
    }
}
