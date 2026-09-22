using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using TranslateBot.Infrastructure;

namespace TranslateBot.TTS
{
    public enum TtsMode
    {
        Off,                    // Tắt đọc thoại
        AutoReadTranslated,     // Tự động đọc câu dịch ngay khi hiển thị
        AutoReadConfirmed,      // Chỉ đọc khi câu thoại đã ổn định (Confirmed)
        Manual                  // Chỉ đọc khi người dùng bấm nút thủ công
    }

    public interface ITtsService : IDisposable
    {
        TtsMode Mode { get; set; }
        int Volume { get; set; } // 0..100
        int Rate { get; set; }   // -10..10
        string? CurrentVoice { get; set; }

        IReadOnlyList<string> GetAvailableVoices();
        void SpeakAsync(string text, bool cancelPrevious = true);
        void Stop();

        event Action<string>? OnSpeakingStarted;
        event Action? OnSpeakingCompleted;
    }

    // Section 38: Text-To-Speech Service.
    // Chạy hoàn toàn bất đồng bộ (non-blocking) qua SpeechSynthesizer của Windows SAPI.
    // Đảm bảo không bao giờ làm chậm vòng lặp Capture hay OCR.
    public class TtsService : ITtsService
    {
        private readonly SpeechSynthesizer? _synthesizer;
        private readonly object _lock = new();
        private bool _disposed = false;

        public TtsMode Mode { get; set; } = TtsMode.Off;
        public int Volume
        {
            get => _synthesizer?.Volume ?? 85;
            set
            {
                if (_synthesizer != null)
                {
                    _synthesizer.Volume = Math.Clamp(value, 0, 100);
                }
            }
        }

        public int Rate
        {
            get => _synthesizer?.Rate ?? 0;
            set
            {
                if (_synthesizer != null)
                {
                    _synthesizer.Rate = Math.Clamp(value, -10, 10);
                }
            }
        }

        public string? CurrentVoice
        {
            get => _synthesizer?.Voice?.Name;
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && _synthesizer != null)
                {
                    try
                    {
                        _synthesizer.SelectVoice(value);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"[TTS] Không thể chọn giọng '{value}': {ex.Message}");
                    }
                }
            }
        }

        public event Action<string>? OnSpeakingStarted;
        public event Action? OnSpeakingCompleted;

        public TtsService()
        {
            try
            {
                _synthesizer = new SpeechSynthesizer();
                _synthesizer.Volume = 85;
                _synthesizer.Rate = 0;

                _synthesizer.SpeakStarted += (s, e) =>
                {
                    OnSpeakingStarted?.Invoke(e.Prompt?.ToString() ?? "");
                };

                _synthesizer.SpeakCompleted += (s, e) =>
                {
                    OnSpeakingCompleted?.Invoke();
                };
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[TTS] Không thể khởi tạo SpeechSynthesizer: {ex.Message}");
                _synthesizer = null;
            }
        }

        public IReadOnlyList<string> GetAvailableVoices()
        {
            if (_synthesizer == null) return Array.Empty<string>();
            try
            {
                return _synthesizer.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => v.VoiceInfo.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[TTS] Lỗi liệt kê giọng đọc: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        public void SpeakAsync(string text, bool cancelPrevious = true)
        {
            if (string.IsNullOrWhiteSpace(text) || _synthesizer == null || _disposed)
                return;

            lock (_lock)
            {
                try
                {
                    if (cancelPrevious)
                    {
                        // Ngắt câu thoại cũ nếu thoại game đã chuyển sang câu mới
                        _synthesizer.SpeakAsyncCancelAll();
                    }

                    _synthesizer.SpeakAsync(text);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[TTS] Lỗi phát giọng nói: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            if (_synthesizer == null || _disposed) return;
            lock (_lock)
            {
                try
                {
                    _synthesizer.SpeakAsyncCancelAll();
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"[TTS] Lỗi dừng giọng nói: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _synthesizer?.SpeakAsyncCancelAll();
                _synthesizer?.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}
