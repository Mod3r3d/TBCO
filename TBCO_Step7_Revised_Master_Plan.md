# TBCO-Step7 → TBCO 3.x Upgrade Plan
## Bản kế hoạch được lập lại dựa trực tiếp trên TBCO-step7.zip

> **Nguồn chính:** trạng thái code thực tế của `TBCO-step7.zip`.
>
> **Mục tiêu:** không xây lại TBCO từ đầu. Giữ lại các phần Step 7 đã hoạt động, sau đó nâng cấp theo hướng MORT-inspired, FGO-specific, multi-provider, API key rotation, local fallback và hotkey-driven UX.

---

# 1. Đánh giá trạng thái hiện tại của TBCO-Step7

## 1.1 Những phần đã có và nên giữ

Step 7 đã có nền tảng khá đầy đủ:

```text
Capture
 ├─ Screen capture
 ├─ Window-relative capture
 ├─ CaptureBufferPool
 └─ FrameChangeDetector

OCR
 ├─ Windows OCR
 ├─ OneOCR
 ├─ OCR fallback
 ├─ OCR areas
 └─ Exclusion areas

Dialogue
 ├─ DialogueTracker
 ├─ DialogueDeduplicator
 ├─ TextSimilarity
 ├─ SpeakerDetector
 ├─ DialogueHistory
 └─ LongDialogueHandler

Translation
 ├─ ITranslationProvider
 ├─ GeminiProvider
 ├─ GoogleWebTranslateProvider
 ├─ TranslationWorker
 ├─ TranslationMemory
 ├─ TranslationContext
 ├─ GlossaryManager
 └─ CharacterProfileManager

Output
 ├─ Overlay
 ├─ Detached subtitle
 ├─ TTS
 ├─ SessionManager
 └─ History

Diagnostics
 ├─ PerformanceMetrics
 ├─ CaptureBenchmark
 └─ tests

UX
 ├─ Floating toolbar
 ├─ Window selector
 ├─ OCR area editor
 ├─ API key dialog
 └─ global hotkey nền tảng
```

## 1.2 Các cải tiến Step 7 đã giải quyết

### Dialogue

`DialogueTracker` đã xử lý:

- text tăng dần kiểu typewriter
- fuzzy prefix
- OCR correction
- stable timeout
- transition flush
- empty gap
- lọc timestamp
- bỏ fragment OCR quá ngắn

Hiện tại thời gian stable mặc định là khoảng:

```text
300 ms
```

### Dedup

`DialogueDeduplicator` đang có:

```text
4 giây
5 entry gần nhất
```

### Translation Memory

Đã có:

```text
Exact LRU cache
+
Fuzzy cache
```

Nhưng hiện tại cache nằm trong RAM.

### Translation Worker

Đã hỗ trợ:

```text
3 worker song song
+
ReorderBuffer
```

để kết quả vẫn hiển thị theo SequenceId.

### Gemini

Đã có:

```text
timeout
retry
500 / 503 / 504 handling
429 detection
quota cooldown
Google Web Translate fallback
```

### Capture

Đã tối ưu buffer reuse để giảm allocation và có benchmark.

### Testing

Đã có test cho:

- FrameChangeDetector
- CaptureBufferPool
- PerformanceMetrics
- DialogueTracker
- TranslationWorker failure
- Overlay
- Floating toolbar
- Detached subtitle

---

# 2. Những điểm Step 7 còn thiếu

Đây là phần quan trọng nhất của roadmap mới.

## 2.1 API architecture vẫn đang là single-key

Hiện tại Gemini đang theo mô hình:

```text
AppConfig.ApiKey
      ↓
GeminiProvider(apiKey)
      ↓
Gemini API
```

Và khi 429:

```text
Gemini
  ↓
cooldown
  ↓
Google Web Translate
```

Chưa có:

```text
Key Pool
Key health
Credential rotation
Provider router
Quota state persistence
```

### Quyết định

Đây là **P0**.

---

# 3. Kiến trúc TBCO mục tiêu

Không rewrite toàn bộ.

```text
                    ┌───────────────────┐
                    │    Game Window    │
                    └─────────┬─────────┘
                              ↓
                     ┌─────────────────┐
                     │ CaptureService  │
                     └────────┬────────┘
                              ↓
                     ┌─────────────────┐
                     │ Image Pipeline  │
                     └────────┬────────┘
                              ↓
                     ┌─────────────────┐
                     │   OCR Router    │
                     └────────┬────────┘
                              ↓
                     ┌─────────────────┐
                     │ Text Normalizer │
                     └────────┬────────┘
                              ↓
                  ┌────────────────────────┐
                  │ Dialogue Stabilizer    │
                  │ + Deduplicator         │
                  └───────────┬────────────┘
                              ↓
                    ┌───────────────────┐
                    │ FGO Context       │
                    │ Speaker/Scene     │
                    │ Glossary/Profile  │
                    └─────────┬─────────┘
                              ↓
                    ┌───────────────────┐
                    │ TranslationMemory │
                    └─────────┬─────────┘
                              ↓
                   ┌──────────────────────┐
                   │ TranslationRouter    │
                   └──────────┬───────────┘
                              ↓
       ┌──────────────────────┼───────────────────────┐
       ↓                      ↓                       ↓
┌───────────────┐      ┌────────────────┐      ┌─────────────┐
│ Gemini Pool   │      │ Other Providers│      │ Local AI    │
│ Key A/B/C/... │      │ DeepL/Google   │      │ Ollama      │
└───────────────┘      └────────────────┘      └─────────────┘
       └──────────────────────┬───────────────────────┘
                              ↓
                    ┌───────────────────┐
                    │ Overlay / TTS    │
                    │ History / Session │
                    └───────────────────┘
```

---

# 4. Phase 0 — Ổn định Step 7 trước khi mở rộng

## Mục tiêu

Không làm mất những gì đang hoạt động.

## Công việc

- [ ] Chạy toàn bộ test hiện tại.
- [ ] Chụp baseline metrics.
- [ ] Ghi nhận OCR latency.
- [ ] Ghi nhận translation latency.
- [ ] Ghi nhận cache hit/miss.
- [ ] Ghi nhận tỷ lệ duplicate.
- [ ] Ghi nhận số request Gemini.
- [ ] Tạo `BaselineReport.md`.
- [ ] Không thay đổi hành vi hiện tại nếu chưa có test bảo vệ.

## Kết quả

Từ đây mỗi tính năng mới đều có regression test.

Priority: **P0**

---

# 5. Phase 1 — Hotkey Manager

Step 7 đã có global hotkey nền tảng nhưng mới dùng trực tiếp trong `MainWindow`; F8/F9 đã được gắn cho snapshot/overlay lock, còn F10 mới chỉ tồn tại như ID dự phòng.

Không tiếp tục hard-code hotkey trong `MainWindow`.

## Kiến trúc

```text
HotkeyManager
 ├─ Register
 ├─ Unregister
 ├─ ConflictDetector
 ├─ CommandMap
 └─ Settings persistence
```

## Command IDs

```text
ToggleCapture
CaptureOnce
Snapshot
ToggleOverlay
LockOverlay
SelectWindow
SelectRegion
OCRNow
OCRNextEngine
FinalizeDialogue
SkipDialogue
Retranslate
OpenHistory
OpenApiKeyManager
SwitchProvider
ToggleTTS
ToggleLocalAI
OpenDiagnostics
EmergencyStop
```

## Default hotkeys

### Capture

```text
F6              Toggle Continuous
F7              Capture frame
Shift+F7        Capture + OCR + Translate
Ctrl+F7         Snapshot
F8              Select OCR Region
Ctrl+F8         Edit Exclusion Region
F9              Pause / Resume
```

### Dialogue

```text
Ctrl+D          Enable/Disable Stabilizer
Ctrl+Shift+D    Force Finalize
Alt+D           Skip current dialogue
Shift+D         Retranslate current dialogue
```

### OCR

```text
Ctrl+O          OCR Settings
Shift+O         Next OCR Engine
Alt+O            OCR current frame
```

### Translation

```text
Ctrl+T          Retranslate
Shift+T         Next Provider
Ctrl+Shift+T    Auto Provider
```

### API

```text
Ctrl+K          API Key Manager
Ctrl+Shift+K    Next Credential
Alt+K           Retry current credential
Ctrl+Alt+K      Toggle Key Pool
```

### Memory

```text
Ctrl+M          Translation Memory
Ctrl+Shift+M    Save current translation
```

### Overlay

```text
Ctrl+H          Show/Hide Overlay
Shift+H         Lock/Unlock Overlay
Ctrl+Shift+H    Click-through
```

### Profile

```text
Ctrl+G          Game Profile
Shift+G         Next Profile
Alt+G           Auto Detect Game
```

### TTS

```text
Ctrl+Y          Speak current line
Shift+Y         Pause/Resume
Alt+Y           Stop
```

### Local AI

```text
Ctrl+L          Toggle Local AI
Shift+L         Translate with Local AI
```

### History

```text
Ctrl+J          Open History
Ctrl+Shift+J    Export Session
```

### Diagnostics

```text
Ctrl+F12        Diagnostics
Shift+F12       Debug HUD
Ctrl+Shift+F12  Export Diagnostic Bundle
```

### Emergency

```text
Ctrl+Shift+Esc  Emergency Stop
F12             Control Panel
```

Tất cả phải có thể rebind.

Priority: **P0**

---

# 6. Phase 2 — Translation Memory 2.0

Step 7 đã có Exact + Fuzzy LRU cache nhưng cache chỉ tồn tại trong runtime.

## Mục tiêu

Đưa Memory thành persistent database.

## Kiến trúc

```text
TranslationMemory
        ↓
IMemoryStore
        ↓
SQLite
```

## Tables

```text
Translations
TranslationVariants
Glossary
Characters
Sessions
GameProfiles
```

## Fields đề xuất

```text
OriginalText
NormalizedText
Translation
Language
Game
Scene
Speaker
Provider
Model
CreatedAt
LastUsedAt
UseCount
Confidence
```

## Fuzzy Memory

Giữ logic hiện tại nhưng bổ sung:

```text
exact
↓
canonical variant
↓
fuzzy similarity
```

## Coalescing

Nếu nhiều job cùng normalized text:

```text
A
A
A
```

chỉ tạo:

```text
1 network request
```

Priority: **P0**

---

# 7. Phase 3 — Progressive Dialogue 2.0

Giữ `DialogueTracker` hiện tại.

Không thay ngay logic 300ms.

## Thêm

```text
StabilityConfig
 ├─ StableDurationMs
 ├─ SimilarityThreshold
 ├─ MaxWaitMs
 ├─ MinTextLength
 └─ EmptyGapMs
```

Cho phép chỉnh theo game.

## FGO preset

```text
Fast
Balanced
Safe
```

Ví dụ concept:

```text
Fast:
dịch nhanh, ít chờ

Balanced:
cân bằng latency/độ ổn định

Safe:
chờ thêm để tránh typewriter split
```

## Adaptive stabilization

Dựa trên:

```text
text length
growth speed
OCR confidence
similarity
```

Không cố định toàn bộ thời gian.

Priority: **P0**

---

# 8. Phase 4 — OCR Pipeline 2.0

Step 7 đã có:

```text
Windows OCR
OneOCR
Fallback
```

## Mở rộng

```text
IOcrEngine
 ├─ WindowsOCR
 ├─ OneOCR
 ├─ Tesseract
 └─ Optional cloud OCR
```

## Image preprocessing

Thêm:

```text
Resize
Grayscale
Contrast
Threshold
RGB filter
HSV filter
Erode
Sharpen
```

## Preset theo OCR area

```text
FGO Dialogue
FGO Speaker
VN Dialogue
White Text
Dark Text
```

## OCR router

```text
Fastest
Balanced
Best Quality
Custom
```

Priority: **P0/P1**

---

# 9. Phase 5 — Text Normalization 2.0

Tách phần normalize khỏi `DialogueTracker`.

```text
ITextNormalizer
```

Pipeline:

```text
Raw OCR
 ↓
Noise removal
 ↓
Whitespace
 ↓
Punctuation
 ↓
Full-width normalization
 ↓
OCR correction
 ↓
Speaker parsing
 ↓
Final normalized text
```

## OCR correction dictionary

```json
{
  "Senpaii": "Senpai",
  "Chaldea ": "Chaldea"
}
```

Priority: **P0**

---

# 10. Phase 6 — FGO Context Engine 2.0

Step 7 đã có:

```text
TranslationContext
SpeakerProfile
PreviousLines
ActiveGlossary
```

và `SpeakerDetector`.

## Cần nâng cấp thành

```text
Global Context
        ↓
Game Context
        ↓
Story Arc
        ↓
Scene
        ↓
Speaker
        ↓
Recent Dialogue
        ↓
Current Dialogue
```

## SpeakerResolver

Nguồn:

```text
Speaker OCR Area
+
"Speaker: text"
+
【Speaker】
+
Japanese quote pattern
+
history
```

## Character profile

Hỗ trợ:

```text
Aliases
SpeakingStyle
Pronouns
AddressingRules
CharacterGlossary
SceneContext
```

## Character conflict handling

Nếu OCR speaker không chắc chắn:

```text
SpeakerConfidence < threshold
```

→ không ép LLM theo profile đó.

Priority: **P0**

---

# 11. Phase 7 — Characters & Glossary 2.0

Giữ file JSON để dễ chỉnh sửa.

Nâng schema.

## Character

```json
{
  "Name": "Mash Kyrielight",
  "Aliases": [],
  "SpeakingStyle": "",
  "PreferredPronouns": "",
  "AddressingRules": "",
  "Contexts": [],
  "GlossaryOverrides": [],
  "Priority": 100
}
```

## Glossary

```json
{
  "Source": "Noble Phantasm",
  "Target": "Bảo Khí",
  "Tier": "Game",
  "Level": "Locked",
  "Context": "FGO",
  "Character": null,
  "Aliases": [],
  "Notes": ""
}
```

## Context groups

```text
Global
FGO
Singularity
Lostbelt
Chaldea
Combat
Summoning
My Room
Event
Character
```

Priority: **P0**

---

# 12. Phase 8 — API Key Pool

Đây là thay đổi lớn nhất so với Step 7.

## Hiện tại

```text
AppConfig.ApiKey
```

## Mục tiêu

```text
ApiCredential[]
```

## Interface

```csharp
IApiKeyPool
{
    ApiCredential? Acquire();
    void ReportSuccess(ApiCredential key, TimeSpan latency);
    void ReportRateLimit(ApiCredential key, TimeSpan cooldown);
    void ReportInvalid(ApiCredential key);
    void Release(ApiCredential key);
}
```

## Credential model

```csharp
class ApiCredential
{
    string Id;
    string Provider;
    string Secret;
    ApiKeyStatus Status;
    DateTime? CooldownUntil;
    int ConsecutiveErrors;
    long SuccessCount;
    long FailureCount;
    double AverageLatencyMs;
}
```

## Status

```text
Active
Cooldown
Exhausted
Invalid
Disabled
```

## Rotation

```text
Key A
 ↓ 429
Cooldown A
 ↓
Key B
 ↓ success
done
```

## Không xoay key mù

Phân biệt:

```text
429 → cooldown
401 → invalid
403 → permission/policy
408 → timeout
500 → retry
503 → retry/fallback
```

## Retry

```text
retry 1 → 500ms
retry 2 → 1s
retry 3 → 2s
```

Sau ngưỡng:

```text
fallback provider
```

Không retry vô hạn.

## Persist state

Không cần lưu secret plaintext.

Lưu:

```text
Credential metadata
CooldownUntil
Health
Usage
```

Secret dùng:

```text
Windows DPAPI / Credential Manager
```

Priority: **P0**

---

# 13. Phase 9 — Translation Router

Gemini không còn là provider duy nhất trong kiến trúc.

```text
ITranslationProvider
```

Backend:

```text
Gemini
Google Web
DeepL
Custom API
Local/Ollama
```

## Router policy

```text
Memory
 ↓
Primary provider
 ↓
Credential rotation
 ↓
Secondary provider
 ↓
Local AI
```

## Ví dụ FGO

```text
Gemini Key Pool
 ↓ unavailable
Google/other configured provider
 ↓ unavailable
Ollama
```

## Quan trọng

Google Web Translate hiện có thể tiếp tục là fallback tạm thời, nhưng không nên để `GeminiProvider` tự biết tất cả provider.

Tách:

```text
GeminiProvider
```

khỏi:

```text
TranslationRouter
```

Priority: **P0**

---

# 14. Phase 10 — Local AI

MORT cho thấy hướng Custom API/local model đáng được tận dụng.

TBCO thêm:

```text
LocalTranslationProvider
```

Backend đầu tiên:

```text
Ollama
```

Model do người dùng chọn.

## Policy

```text
Cloud preferred
Local fallback
```

hoặc:

```text
Offline mode
→ Local only
```

## Hotkey

```text
Ctrl+L = Toggle Local AI
Shift+L = Force Local AI
```

Priority: **P1**

---

# 15. Phase 11 — Game Profile

Step 7 hiện có config chung.

Cần tách:

```text
profiles/
    FGO-JP.json
    FGO-NA.json
    BlueArchive.json
    VisualNovel.json
```

Profile gồm:

```text
Window matching
Capture
OCR Areas
Exclusion Areas
OCR engine
Preprocessing
Dialogue stability
Characters
Glossary
Provider
API policy
Overlay
TTS
Hotkeys
```

## Auto load

```text
Detect process/window
        ↓
Load profile
        ↓
Restore settings
```

Priority: **P0/P1**

---

# 16. Phase 12 — Overlay nâng cấp

Giữ overlay hiện tại.

Thêm:

```text
Background opacity
Font outline
Shadow
Auto contrast
Auto color
Multiple regions
Original + Translation
Click-through
Lock
Resize
```

## Hotkey

```text
Ctrl+H
Shift+H
Ctrl+Shift+H
Alt+H
```

Priority: **P1**

---

# 17. Phase 13 — TTS 2.0

Step 7 đã có Windows SpeechSynthesizer.

Giữ:

```text
AutoRead
Manual
Stop
Volume
Rate
Voice
```

Thêm:

```text
Character Voice Profile
```

Ví dụ:

```text
Mash → Voice A
Gilgamesh → Voice B
```

Không bắt buộc đổi engine TTS.

Priority: **P1**

---

# 18. Phase 14 — Session / History 2.0

Step 7 đã có JSONL autosave + CSV/JSON/Markdown export.

Giữ chức năng này.

Nâng thành:

```text
SQLite Session DB
```

Record:

```text
Timestamp
Sequence
Speaker
Original
Translation
Provider
CredentialId
Model
Latency
CacheHit
OCRConfidence
```

Không lưu API secret.

## Replay

```text
Session
 ↓
Select line
 ↓
Retranslate
 ↓
Compare old/new
```

Priority: **P1**

---

# 19. Phase 15 — Diagnostics 2.0

Step 7 đã có PerformanceMetrics.

Thêm:

```text
OCR confidence
Translation latency
Provider latency
429 count
Key rotations
Cache hit rate
Fallback count
Dropped frames
Queue depth
TTS latency
```

## Dashboard

```text
Capture      60 FPS
OCR          41 ms
Memory       78%
Translation  320 ms

Gemini:
 Key01 Healthy
 Key02 Cooldown 41s
 Key03 Healthy

Cache:
 Hit 82%
 Miss 18%
```

Priority: **P0**

---

# 20. Phase 16 — Fault Injection Test

Tạo fake provider:

```csharp
FakeTranslationProvider
```

Cases:

```text
Success
429
401
403
500
503
Timeout
Empty response
Invalid JSON
Network disconnected
```

## API Pool tests

```text
Key A → 429
Expected:
A = Cooldown
B selected
```

```text
Key A → 401
Expected:
A = Invalid
B selected
```

```text
A/B/C → unavailable
Expected:
fallback provider
```

Priority: **P0**

---

# 21. Phase 17 — Replay Benchmark

Đây là tính năng quan trọng để tối ưu FGO mà không cần chạy game thật.

## Dataset

```text
testdata/
  fgo/
    dialogue_001.png
    dialogue_002.png
    dialogue_003.png
```

hoặc video frame extraction.

Pipeline:

```text
Frame
 ↓
OCR
 ↓
Normalize
 ↓
DialogueTracker
 ↓
Context
 ↓
Memory
 ↓
Translation
```

Metrics:

```text
OCR accuracy
duplicate rate
false transition rate
API requests
cache hit rate
latency
```

Priority: **P1**

---

# 22. Phase 18 — MORT-inspired features nên thêm

Không nhất thiết đưa hết vào ngay.

## P1

```text
Image preprocessing presets
Snapshot mode
Once mode
Multiple OCR areas
Game profile
Custom API
Advanced overlay
```

## P2

```text
Mouse-follow OCR
Clipboard input
Pipe input
External text hook
```

---

# 23. Phase 19 — Performance

Không rewrite Rust ở giai đoạn này.

Step 7 đã có:

```text
CaptureBufferPool
FrameChangeDetector
PerformanceMetrics
CaptureBenchmark
ManagedAccelerator
```

Tiếp tục tối ưu theo profiling.

## Ưu tiên

```text
1. Skip unchanged frame
2. Buffer pooling
3. OCR throttling
4. Translation Memory
5. Request coalescing
6. Queue backpressure
7. Async
```

## Translation queue

Không để 3 worker tạo quá nhiều request khi provider đang limit.

Thêm:

```text
AdaptiveConcurrency
```

Ví dụ:

```text
Healthy provider
→ concurrency 3

429 detected
→ concurrency 1

Repeated 429
→ cooldown/fallback
```

Priority: **P0**

---

# 24. Phase 20 — Queue/backpressure

Step 7 dùng:

```text
Channel
+
3 workers
+
ReorderBuffer
```

Cần thêm:

```text
Queue policy
```

## Priority

```text
Current dialogue      HIGH
Prefetch              LOW
History retranslation LOW
```

## Coalescing

```text
A
A+B
A+B+C
```

Nếu request chưa gửi:

```text
cancel/replace stale prefetch
```

để giảm API usage.

Priority: **P0**

---

# 25. Config migration

Không phá config hiện tại.

## Step 7

```json
{
  "ApiKey": "...",
  "AiModel": "...",
  "TargetLanguage": "vi"
}
```

## New

```json
{
  "Translation": {
    "PrimaryProvider": "Gemini",
    "Credentials": [],
    "FallbackProviders": [],
    "LocalEnabled": false
  }
}
```

## Migration

Khi khởi động:

```text
ApiKey cũ
 ↓
migrate
 ↓
Credential #1
```

Sau đó:

```text
ApiKey deprecated
```

Không yêu cầu người dùng nhập lại key nếu key cũ còn hợp lệ.

Priority: **P0**

---

# 26. API Key Manager UI

Giao diện đề xuất:

```text
API PROVIDERS

Gemini
────────────────────────
● Key 01   Healthy
◐ Key 02   Cooldown 43s
✕ Key 03   Invalid

[Add Key]
[Edit]
[Enable]
[Disable]
[Test]
[Remove]

Current:
Gemini / Key 01

Fallback:
Google Web
Ollama
```

## Usage

```text
Requests
Success
429
Failures
Avg latency
Last used
```

Priority: **P0/P1**

---

# 27. Hotkey Manager UI

```text
Settings
  → Hotkeys

Capture
  Toggle Capture       F6
  Capture Once         Shift+F7
  Snapshot             Ctrl+F7

OCR
  OCR Now              Alt+O
  Next OCR             Shift+O

Dialogue
  Finalize             Ctrl+Shift+D
  Retranslate          Shift+D

API
  Key Manager          Ctrl+K
  Next Credential      Ctrl+Shift+K

Overlay
  Show/Hide            Ctrl+H

...
```

Có:

```text
[Change]
[Reset]
[Reset All]
```

Conflict detector:

```text
✕ Ctrl+T đã được Translation bind
```

Priority: **P0**

---

# 28. Security

## Không log

```text
API key
Authorization header
full provider secret
```

## Log

```text
CredentialId=gemini-02
Status=Cooldown
RetryAfter=60s
```

## Secret storage

Ưu tiên:

```text
Windows DPAPI
Credential Manager
```

Fallback:

```text
encrypted local secret store
```

Không commit:

```text
.env
config secret
API keys
```

Priority: **P0**

---

# 29. Milestone mới

## TBCO 2.0 — Step 7 Hardening

Mục tiêu:

```text
Không phá chức năng hiện tại
```

- [ ] Baseline test
- [ ] Hotkey Manager
- [ ] Queue/backpressure
- [ ] Metrics mở rộng
- [ ] Config migration
- [ ] Security

---

## TBCO 2.1 — API Resilience

- [ ] ApiCredential
- [ ] ApiKeyPool
- [ ] 429 cooldown
- [ ] 401 invalid
- [ ] 403 classification
- [ ] Retry/backoff
- [ ] Provider Router
- [ ] Key Manager UI
- [ ] Fault tests

---

## TBCO 2.2 — Translation Intelligence

- [ ] Persistent Translation Memory
- [ ] Request coalescing
- [ ] Fuzzy memory
- [ ] Adaptive stabilization
- [ ] Context Engine
- [ ] Speaker Resolver
- [ ] Characters 2.0
- [ ] Glossary 2.0

---

## TBCO 2.3 — OCR/MORT Parity

- [ ] Image preprocessing
- [ ] Tesseract
- [ ] OCR Router
- [ ] Once
- [ ] Snapshot
- [ ] OCR presets
- [ ] Multiple OCR area nâng cao

---

## TBCO 2.4 — Multi-Provider + Local

- [ ] DeepL/other configured provider
- [ ] Custom API
- [ ] Ollama
- [ ] Local model
- [ ] Offline mode
- [ ] Provider health score

---

## TBCO 2.5 — Game Intelligence

- [ ] Game Profiles
- [ ] Scene Profiles
- [ ] Character context
- [ ] Story context
- [ ] per-game glossary
- [ ] per-character overrides
- [ ] auto profile loading

---

## TBCO 3.0 — Production

- [ ] Replay benchmark
- [ ] Crash recovery
- [ ] Installer
- [ ] Portable
- [ ] Dependency checker
- [ ] Updater
- [ ] Diagnostic bundle
- [ ] Documentation

---

# 30. Dependency order

Không triển khai ngẫu nhiên.

```text
Step 7 Baseline
      ↓
Hotkey Manager
      ↓
Queue/Backpressure
      ↓
Persistent Memory
      ↓
API Key Pool
      ↓
Provider Router
      ↓
Context Engine
      ↓
OCR preprocessing
      ↓
Game Profiles
      ↓
Local AI
      ↓
Replay benchmark
```

---

# 31. Các file/class dự kiến cần thêm

## API

```text
Translation/
 ├─ TranslationRouter.cs
 ├─ ApiKeyPool.cs
 ├─ ApiCredential.cs
 ├─ ApiCredentialStore.cs
 ├─ ProviderHealthTracker.cs
 ├─ RateLimitManager.cs
 └─ TranslationPolicy.cs
```

## Hotkey

```text
Hotkeys/
 ├─ HotkeyManager.cs
 ├─ HotkeyBinding.cs
 ├─ HotkeyCommand.cs
 └─ HotkeyConflictDetector.cs
```

## Memory

```text
Database/
 ├─ TBCODatabase.cs
 ├─ TranslationRepository.cs
 ├─ SessionRepository.cs
 └─ CredentialRepository.cs
```

## Context

```text
Dialogue/
 ├─ SpeakerResolver.cs
 ├─ DialogueStabilizer.cs
 └─ DialogueContextBuilder.cs
```

## OCR

```text
OCR/
 ├─ OcrRouter.cs
 ├─ TesseractOcrEngine.cs
 └─ Preprocessing/
      ├─ ImagePreprocessor.cs
      ├─ ThresholdFilter.cs
      ├─ ColorFilter.cs
      └─ PreprocessPreset.cs
```

## Game profile

```text
GameProfiles/
 ├─ GameProfile.cs
 ├─ GameProfileManager.cs
 └─ GameDetector.cs
```

---

# 32. Những phần không nên sửa mạnh

Các phần này Step 7 đã có hướng đúng:

```text
CaptureBufferPool
FrameChangeDetector
DialogueTracker
DialogueDeduplicator
ReorderBuffer
TranslationContext
Overlay
SessionManager
PerformanceMetrics
```

Chỉ refactor interface hoặc mở rộng test khi cần.

---

# 33. Các rủi ro cần kiểm soát

## Rủi ro 1 — Key rotation không thật sự tăng quota

Nhiều credential có thể vẫn bị giới hạn chung ở project/account/provider.

Biện pháp:

```text
Key Pool
+
Provider fallback
+
Memory
+
Local AI
```

Không dựa hoàn toàn vào số lượng key.

---

## Rủi ro 2 — 3 worker tạo quá nhiều request

Biện pháp:

```text
AdaptiveConcurrency
+
Queue backpressure
+
Request coalescing
```

---

## Rủi ro 3 — Fuzzy cache dịch nhầm

Biện pháp:

```text
High threshold
+
game context
+
speaker context
+
exact cache ưu tiên
```

---

## Rủi ro 4 — Speaker OCR sai

Biện pháp:

```text
Speaker confidence
+
history
+
profile matching
+
manual override
```

---

## Rủi ro 5 — Hotkey xung đột

Biện pháp:

```text
Central Hotkey Manager
+
Conflict Detector
+
Rebind UI
```

---

# 34. Definition of Done

TBCO nâng cấp chỉ được coi là hoàn thành khi:

## Dialogue

```text
FGO text chạy từng chữ
→ không spam request
```

## Memory

```text
câu cũ
→ cache hit
→ không gọi API
```

## API

```text
Key A 429
→ cooldown
→ Key B
```

## Provider

```text
Gemini unavailable
→ secondary provider
→ local fallback
```

## Context

```text
speaker + scene + history + glossary
→ prompt phù hợp
```

## UX

```text
các thao tác chính
→ hotkey
```

## Reliability

```text
429/500/timeout
→ không crash
```

## Performance

```text
unchanged frame
→ skip OCR
```

## Recovery

```text
game restart
→ reconnect window
→ tiếp tục pipeline
```

---

# 35. Kết luận kiến trúc

TBCO-Step7 **không cần viết lại từ đầu**.

Nền tảng hiện tại đã có:

```text
Capture
+
OCR
+
Dialogue stabilization
+
Dedup
+
Context
+
Glossary
+
Characters
+
Translation Memory
+
Gemini fallback
+
Overlay
+
TTS
+
Session
+
Diagnostics
```

Khoảng trống lớn nhất hiện tại là:

```text
Single API key
Single translation route
RAM-only memory
Hard-coded hotkeys
Chưa có Game Profile chuẩn
Chưa có Local AI
Chưa có Adaptive queue
```

Do đó hướng phát triển hợp lý nhất là:

```text
STEP 7
  ↓
Hardening
  ↓
Translation Memory 2.0
  ↓
API Key Pool
  ↓
Translation Router
  ↓
FGO Context Engine
  ↓
OCR/MORT features
  ↓
Game Profiles
  ↓
Local AI
  ↓
Replay Benchmark
  ↓
TBCO 3.0
```

**Quyết định cốt lõi:** ưu tiên làm TBCO **ổn định và tự phục hồi** trước khi thêm thật nhiều tính năng. API Key Pool không phải giải pháp duy nhất; nó phải nằm trong một hệ thống lớn hơn gồm **Memory → Key Pool → Provider Fallback → Local AI**.
