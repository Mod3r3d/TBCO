using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using TranslateBot.Infrastructure;

namespace TranslateBot.Session
{
    public enum SessionExportFormat
    {
        Csv,
        Json,
        Markdown
    }

    // Section 37: Quản lý phiên chơi (Session) và xuất nhật ký đa định dạng (CSV, JSON, Markdown).
    public class SessionManager
    {
        private readonly List<SessionRecord> _records = new();
        private readonly object _lock = new();
        private string? _currentSessionFilePath;

        public string SessionId { get; private set; } = string.Empty;
        public DateTime StartTime { get; private set; } = DateTime.Now;
        public bool IsSessionActive { get; private set; } = false;
        public bool AutoSave { get; set; } = true;
        public string SessionsDirectory { get; set; } = "sessions";

        public SessionManager(string sessionsDirectory = "sessions")
        {
            SessionsDirectory = sessionsDirectory;
        }

        public IReadOnlyList<SessionRecord> Records
        {
            get
            {
                lock (_lock) { return _records.ToArray(); }
            }
        }

        public int RecordCount
        {
            get
            {
                lock (_lock) { return _records.Count; }
            }
        }

        public void StartSession()
        {
            lock (_lock)
            {
                StartTime = DateTime.Now;
                SessionId = $"session_{StartTime:yyyyMMdd_HHmmss}";
                _records.Clear();
                IsSessionActive = true;

                if (AutoSave)
                {
                    try
                    {
                        if (!Directory.Exists(SessionsDirectory))
                        {
                            Directory.CreateDirectory(SessionsDirectory);
                        }
                        _currentSessionFilePath = Path.Combine(SessionsDirectory, $"{SessionId}.jsonl");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"[SESSION] Không thể khởi tạo thư mục session: {ex.Message}");
                    }
                }
            }
        }

        public void EndSession()
        {
            lock (_lock)
            {
                IsSessionActive = false;
            }
        }

        public SessionRecord AddRecord(int sequenceId, string sourceText, string translatedText, 
            string? speaker = null, string provider = "Gemini", double latencyMs = 0, bool isCacheHit = false)
        {
            var record = new SessionRecord
            {
                Timestamp = DateTime.Now,
                SequenceId = sequenceId,
                Speaker = speaker,
                SourceText = sourceText,
                TranslatedText = translatedText,
                Provider = provider,
                LatencyMs = latencyMs,
                IsCacheHit = isCacheHit
            };

            lock (_lock)
            {
                _records.Add(record);

                if (IsSessionActive && AutoSave && !string.IsNullOrEmpty(_currentSessionFilePath))
                {
                    try
                    {
                        string jsonLine = JsonConvert.SerializeObject(record);
                        File.AppendAllText(_currentSessionFilePath, jsonLine + Environment.NewLine, Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"[SESSION] Lỗi ghi file jsonl: {ex.Message}");
                    }
                }
            }

            return record;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Multi-Format Export (Section 37)
        // ═══════════════════════════════════════════════════════════════════

        public string ToCsvString()
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                // CSV Header
                sb.AppendLine("Timestamp,SequenceId,Speaker,SourceText,TranslatedText,Provider,LatencyMs,IsCacheHit");

                foreach (var r in _records)
                {
                    sb.Append(EscapeCsv(r.FormattedTime)).Append(',');
                    sb.Append(r.SequenceId).Append(',');
                    sb.Append(EscapeCsv(r.Speaker ?? "")).Append(',');
                    sb.Append(EscapeCsv(r.SourceText)).Append(',');
                    sb.Append(EscapeCsv(r.TranslatedText)).Append(',');
                    sb.Append(EscapeCsv(r.Provider)).Append(',');
                    sb.Append(r.LatencyMs.ToString("F1")).Append(',');
                    sb.Append(r.IsCacheHit ? "TRUE" : "FALSE");
                    sb.AppendLine();
                }

                return sb.ToString();
            }
        }

        public string ToJsonString()
        {
            lock (_lock)
            {
                return JsonConvert.SerializeObject(_records, Formatting.Indented);
            }
        }

        public string ToMarkdownString()
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# FGO Game Dialogue Transcript — {StartTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"*Tổng số câu: {_records.Count} | Session: {SessionId}*");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();

                foreach (var r in _records)
                {
                    string spk = string.IsNullOrWhiteSpace(r.Speaker) ? "" : $"**{r.Speaker}**: ";
                    sb.AppendLine($"### [{r.FormattedTime}] #{r.SequenceId:D4} — {spk}{r.TranslatedText}");
                    sb.AppendLine($"> *Gốc:* {r.SourceText}");
                    sb.AppendLine($"> *Info: {r.Provider} ({r.LatencyMs:F0}ms)*");
                    sb.AppendLine();
                }

                return sb.ToString();
            }
        }

        public void ExportToFile(string filePath, SessionExportFormat format)
        {
            string fmt = format switch
            {
                SessionExportFormat.Json => "json",
                SessionExportFormat.Markdown => "markdown",
                _ => "csv"
            };
            ExportToFile(filePath, fmt);
        }

        public void ExportToFile(string filePath, string format = "csv")
        {
            string content = format.ToLowerInvariant() switch
            {
                "json" => ToJsonString(),
                "md" or "txt" or "markdown" => ToMarkdownString(),
                "csv" or _ => ToCsvString()
            };

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(filePath, content, Encoding.UTF8);
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return $"\"{value}\"";
        }
    }
}
