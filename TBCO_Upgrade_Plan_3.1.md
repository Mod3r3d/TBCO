# TBCO 3.1+ --- Upgrade & Engineering Plan

**Repository:** https://github.com/Mod3r3d/TBCO\
**Baseline reviewed:** `main`, TBCO 3.0.5, .NET 8 WPF, Windows x64\
**Review method:** Static source inspection. Build, tests, benchmarks
and live game capture were not executed in this review.

## 1. Executive summary

TBCO 3.0.5 already contains capture buffering, frame-change detection,
dialogue tracking/stitching, Windows OCR/OneOCR/Tesseract routing,
preprocessing, Gemini API Key Pool, multi-provider routing, DeepL,
Google Web, Custom API, Ollama, SQLite Translation Memory, game
profiles, speaker/character context, glossary, hotkeys, overlay, TTS,
evaluation harness and OCR benchmarks.

**Do not rewrite the application.** Keep C#/.NET 8/WPF and improve
reliability, responsiveness, dialogue correctness and measurable
quality. MORT should remain a technical reference for
OCR/capture/preprocessing and usability, not a codebase to clone.

### Priority summary

  -----------------------------------------------------------------------
  Priority                Area                    Objective
  ----------------------- ----------------------- -----------------------
  P0                      Translation queue       Bound memory; prevent
                                                  stale jobs accumulating

  P0                      Result ordering         Avoid callbacks under
                                                  locks and stale
                                                  subtitles

  P0                      Long dialogue           Preserve structured
                                                  provider results and
                                                  partial-failure status

  P0                      API Key Pool            Try eligible keys
                                                  fairly; classify
                                                  failures correctly

  P0                      Cancellation            Propagate cancellation
                                                  and shut down cleanly

  P0                      Capture                 Validate DPI,
                                                  coordinates,
                                                  target-window lifecycle

  P1                      Dialogue stitching      Improve typewriter and
                                                  scrolling dialogue

  P1                      Translation Memory      Protect approved
                                                  entries and
                                                  profile/language
                                                  isolation

  P1                      UI architecture         Reduce MainWindow
                                                  code-behind
                                                  responsibilities

  P1                      Evaluation              Expand OCR and
                                                  translation regression
                                                  corpora

  P2                      Offline AI              Make Ollama readiness
                                                  and cloud-free behavior
                                                  explicit

  P2                      Release                 Improve clean-machine
                                                  packaging, migration
                                                  and documentation
  -----------------------------------------------------------------------

## 2. Constraints and non-goals

1.  Keep C#, .NET 8, WPF and Windows x64.
2.  Evolve the current architecture incrementally; no rewrite in Rust,
    WinForms, Electron or another framework.
3.  Preserve existing providers, OCR engines, profiles, glossary, API
    credentials, SQLite memory, session history and user workflows.
4.  Do not clone MORT wholesale.
5.  Offline Mode must never silently route content to cloud providers.
6.  User-approved translations must never be overwritten by generated or
    fallback output.
7.  Do not claim performance or OCR-quality gains without repeatable
    benchmarks.
8.  Do not remove a provider or OCR engine without evidence and explicit
    approval.

## 3. Repository baseline

Observed layout:

``` text
TBCO/
├── .github/workflows/release.yml
├── TranslateBot.sln
├── TranslateBot/
│   ├── Capture/
│   ├── Database/
│   ├── Diagnostics/
│   ├── Dialogue/
│   ├── Evaluation/
│   ├── GameProfiles/
│   ├── Hotkeys/
│   ├── Infrastructure/
│   ├── Native/
│   ├── OCR/Preprocessing/
│   ├── Overlay/
│   ├── Session/
│   ├── TTS/
│   ├── Translation/Providers/
│   ├── UI/
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
├── TranslateBot.Tests/
├── evaluation/
├── README.md
├── BASELINE.md
├── BaselineReport.md
├── TBCO_3.0_User_Guide.md
├── build-release.ps1
└── publish.ps1
```

Existing modules to preserve:

-   Capture: `CaptureService`, `CaptureBufferPool`,
    `FrameChangeDetector`, `WindowEnumerator`.
-   Dialogue: `DialogueTracker`, `DialogueDeduplicator`,
    `TextSimilarity`, `SpeakerDetector`, `SpeakerResolver`.
-   OCR: `OcrRouter`, `OcrEngineWithFallback`, Windows OCR, OneOCR,
    Tesseract.
-   Preprocessing: `ImagePreprocessor`, `ColorFilter`,
    `ThresholdFilter`, presets.
-   Translation: `TranslationWorker`, `TranslationRouter`,
    `ReorderBuffer`, `LongDialogueHandler`.
-   Providers: Gemini, DeepL, Google Web, Custom API, Ollama.
-   Memory: `TranslationMemory`, `SqliteMemoryStore`, SQLite WAL.
-   Context: `GameProfileManager`, `GameDetector`, `FgoContextEngine`,
    character profiles and glossary.
-   UX: WPF UI, overlay, detached subtitle, hotkeys, history and TTS.
-   Evaluation: translation harness and OCR benchmark suite.

The README advertises **177/177 passing tests**. This was not
independently reproduced during this review. The README also contains a
developer instruction mentioning 170 tests, while `BaselineReport.md`
records an older 47-test baseline. Reconcile these counts and publish
the actual CI result.

## 4. Static review findings

These are source-level observations, not runtime failures independently
reproduced in this review.

### 4.1 P0 --- TranslationWorker uses an unbounded Channel

**File:** `TranslateBot/Translation/TranslationWorker.cs`

`CreateQueue()` uses `Channel.CreateUnbounded<DialogueJob>()`. Worker
concurrency is limited, but queue capacity is not.

**Risk:** During fast dialogue, rapid skipping or slow provider
responses, jobs can accumulate. Marking jobs cancelled does not
immediately remove them from the channel. Queue latency and memory use
can grow.

**Fix:** - Replace with a bounded channel or bounded priority queue. -
Give confirmed dialogue priority over prefetch. - Discard stale prefetch
first. - Never silently lose confirmed dialogue on saturation; define an
explicit latest-relevant policy. - Track queue depth, oldest job age,
backpressure and dropped sequence IDs. - Ensure cancellation does not
leave reorder state waiting forever.

**Acceptance:** A 10-minute synthetic burst with a slow fake provider
keeps memory and queue size bounded; latest relevant confirmed dialogue
is eventually displayed.

### 4.2 P0 --- ReorderBuffer invokes callback while holding its lock

**File:** `TranslateBot/Translation/ReorderBuffer.cs`

`Submit()` invokes `OnOrderedResult` inside `lock (_lock)`.

**Risk:** UI dispatch or callback re-entry can hold the lock for too
long and create contention or deadlock paths.

**Fix:** Decide acceptance and update sequence state under lock, copy
callback/result to locals, release lock, then invoke callback. Add
concurrent-submit and callback-re-entry tests.

### 4.3 P0 --- Long-dialogue path bypasses structured TranslationResult

**File:** `TranslateBot/Translation/TranslationWorker.cs`

Normal requests use `TranslateRequestAsync()`. Long dialogue calls
`TranslateLongDialogueAsync()`, which calls `_provider.TranslateAsync()`
for each chunk and then constructs a success result.

**Risk:** Per-chunk provider, fallback, error and latency metadata can
be lost. Failed chunks may be replaced with source text while the
overall operation is treated as successful.

**Fix:** - Route each chunk through the structured request contract. -
Preserve cancellation, context, provider/model, status and latency per
chunk. - Mark partial failure explicitly. - Do not cache
partial/degraded output as canonical. - Merge using source spans/overlap
metadata rather than only translated-prefix matching.

### 4.4 P0 --- API key rotation tries at most two keys per request

**File:** `TranslateBot/Translation/TranslationRouter.cs`

`TryTranslateWithGeminiPoolWithKeyAsync()` acquires a primary key and at
most one secondary key.

**Risk:** Pools with more than two eligible keys may fall back while
other configured keys remain unused.

**Fix:** - Attempt each eligible key at most once, subject to a
configurable request budget. - Exclude already attempted keys. - Stop
for non-retryable errors. - Distinguish 429/quota, invalid key and
transient network/server failures. - Use provider-specific cooldown and
jittered retry. - Never log full secrets.

### 4.5 P0 --- Cancellation and shutdown need one end-to-end contract

**Files:** `TranslationWorker.cs`, `TranslationRouter.cs`, provider
implementations, capture/OCR loop and `MainWindow.xaml.cs`.

**Fix:** - Create application/session cancellation token sources. - Link
cancellation for app shutdown, session stop, profile/window change and
superseded jobs. - Propagate tokens through provider HTTP calls, Ollama,
OCR and queue waits. - A timeout must cancel underlying work where
supported; `Task.WhenAny` alone does not. - Await worker completion
during graceful shutdown. - Dispose engines, buffers, database
connections and event subscriptions deterministically. - Never
synchronously block the WPF Dispatcher on async work.

### 4.6 P0/P1 --- Capture coordinates and DPI need stronger validation

**File:** `TranslateBot/Capture/CaptureService.cs`

Window-relative capture converts client origin to screen coordinates and
then uses screen capture. This requires explicit validation across DPI
scaling, multiple monitors, borderless/fullscreen, minimized windows and
overlays.

**Fix:** - Normalize coordinates to physical pixels. - Validate
requested rectangles against client and virtual-screen bounds. - Handle
negative monitor coordinates. - Detect stale HWND/process restart. -
Return typed statuses: success, minimized, invalid window,
out-of-bounds, access denied, unsupported path. - Verify buffer
ownership when capture buffers are reused. - Test 100/125/150/200% DPI
and multi-monitor setups.

### 4.7 P1 --- Add session/generation identity to translation jobs

**Files:** `DialogueTracker.cs`, `DialogueJob.cs`,
`TranslationRequest.cs`, `TranslationWorker.cs`, `ReorderBuffer.cs`.

**Risk:** An old provider response may arrive after
profile/window/session reset.

**Fix:** Add `SessionId`/generation to every job and request. Increment
generation on target window change, profile change, scene reset and
manual clear. Ignore completion from old generations. Keep sequence IDs
monotonic within a generation.

### 4.8 P1 --- Translation Memory invariants need explicit tests

**Files:** `TranslationMemory.cs`, `SqliteMemoryStore.cs`,
`TranslationCacheKey.cs`, `TranslationWorker.cs`.

Existing implementation already has profile/language-aware keys, SQLite
schema v2, fallback classification and user-approved entry types.

**Fix:** - Formalize precedence: user-approved \> validated model
output/imported glossary; degraded fallback excluded from canonical
lookup. - Ensure approved entries cannot be overwritten. - Verify fuzzy
matching never crosses profile/language boundaries. - Version
normalization and prompt policy. - Add backup/restore and corruption
recovery tests. - Verify SQLite concurrency, transactions and WAL
shutdown behavior.

### 4.9 P1 --- MainWindow code-behind is very large

**Files:** `MainWindow.xaml`, `MainWindow.xaml.cs`.

`MainWindow.xaml.cs` is approximately 108 KB in the repository tree.

**Risk:** UI, capture, OCR, translation, settings, hotkeys and provider
lifecycle are tightly coupled.

**Fix incrementally, not as a rewrite:**

``` text
MainWindow
  └── MainViewModel
       ├── CaptureOrchestrator
       ├── DialoguePipeline
       ├── TranslationSessionService
       ├── GameProfileService
       ├── OverlayCoordinator
       ├── HotkeyCoordinator
       └── SettingsCoordinator
```

Extract one responsibility at a time. Preserve XAML bindings and
existing workflows.

### 4.10 P1 --- OCR benchmark corpus is too small for broad claims

**File:** `evaluation/reports/ocr-benchmark-report.md`

The report is dated 2026-10-26 and reports eight samples per engine.

**Fix:** - Expand representative FGO JP/NA, Blue Archive and generic VN
samples. - Split by language, scene, font size, background, animation
and capture mode. - Report CER/WER, named-entity accuracy, empty rate,
clipping detection, failure rate and p50/p95/p99 latency. - Record OS,
engine version, language pack, preprocessing, hardware and DPI. - Keep
fixed regression and exploratory corpora separate. - Avoid universal
accuracy claims from a narrow sample set.

### 4.11 P1 --- Provider fallback needs typed outcomes and strict offline policy

**Files:** `TranslationRouter.cs`, provider classes,
`TranslationWorker.cs`, `TranslationMemory.cs`.

**Fix:** - Define typed outcomes: success, empty, rate limit, invalid
credential, timeout, network error, server error, rejected content,
cancelled. - Do not fallback after cancellation. - Offline Mode must be
local-only. - Preserve provider/model/latency/fallback metadata. - Mark
degraded output and keep it out of canonical memory. - Do not retry the
same provider repeatedly within one request.

### 4.12 P2 --- Config/secret lifecycle and dependency audit

**Files:** `ConfigManager.cs`, `SecretVault.cs`, `AppPaths.cs`,
`.env.example`, `config.template.json`, `TranslateBot.csproj`.

**Fix:** - Add config schema version and range validation. - Write
config atomically with backup. - Recover from corrupt JSON without
deleting the original. - Test DPAPI migration/decrypt failures. - Never
log decrypted secrets. - Clarify portable mode versus LocalAppData
mode. - Audit package compatibility with .NET 8 Windows x64; pin
versions and add vulnerability scanning. - Verify native OCR
dependencies and language packs in clean-machine releases.

------------------------------------------------------------------------

## 5. Target architecture

``` text
Target Game Window
  ↓
Window Detection + DPI/Coordinate Normalization
  ↓
Capture Scheduler + Buffer Pool
  ↓
Frame Change Detector ── no change → skip OCR
  ↓
Profile-based Image Preprocessing
  ↓
OCR Router (OneOCR / Windows OCR / Tesseract)
  ↓
OCR Validation + Clipping Detection
  ↓
Dialogue Tracker
  ├─ typewriter stabilization
  ├─ scroll/continuation stitching
  ├─ speaker detection
  ├─ deduplication
  └─ session/generation identity
  ↓
Bounded Translation Queue
  ├─ confirmed dialogue priority
  ├─ prefetch priority
  ├─ backpressure
  └─ stale-job cancellation
  ↓
Translation Memory (RAM + SQLite)
  ↓
Translation Router
  ├─ Gemini Key Pool
  ├─ DeepL
  ├─ Custom API
  ├─ Ollama (offline/local)
  └─ Google Web (policy-controlled)
  ↓
Structured TranslationResult
  ↓
Session-aware Result Gate
  ↓
Subtitle Overlay / UI / History / TTS
```

### Architecture rules

-   UI thread never performs OCR, preprocessing, provider HTTP or
    synchronous database work.
-   Every asynchronous job carries cancellation and session identity.
-   Every queue has a capacity and overflow policy.
-   Every provider result preserves status, provider, model, latency and
    fallback metadata.
-   Every cache entry preserves profile/language context and provenance.
-   OCR distinguishes empty text from engine failure.
-   Every displayed translation traces to source dialogue and session.
-   Logs never expose secrets.

------------------------------------------------------------------------

## 6. Implementation roadmap

### Phase 0 --- Reproducible baseline

**Goal:** Know exactly what works before changing behavior.

Tasks: 1. Record branch and commit SHA. 2. Run
`dotnet restore TranslateBot.sln`. 3. Run
`dotnet build TranslateBot.sln -c Release`. 4. Run
`dotnet test TranslateBot.Tests/TranslateBot.Tests.csproj -c Release`.
5. Run `evaluation/run-harness.ps1`. 6. Run
`evaluation/run-ocr-benchmark.ps1`. 7. Build with `build-release.ps1`.
8. Test package on a clean Windows x64 machine. 9. Save logs, test
count, duration and benchmark outputs. 10. Create regression samples for
FGO JP/NA and Blue Archive.

Deliverables: `docs/BASELINE_3_0_5.md`, test summary, OCR report,
translation harness report and package smoke-test checklist.

Exit: baseline reproducible from clean checkout; no failing test hidden
or silently skipped.

### Phase 1 --- Queue, backpressure and result safety (P0)

Files: `TranslationWorker.cs`, `ReorderBuffer.cs`, `DialogueJob.cs`,
`TranslationRequest.cs`, `PerformanceMetrics.cs`, queue tests.

Tasks: 1. Replace unbounded channel with bounded queue. 2. Define
configurable capacity. 3. Prioritize confirmed dialogue over prefetch.
4. Drop stale prefetch first. 5. Add queue depth, oldest age and
backpressure metrics. 6. Add session/generation identity. 7. Ignore
stale results after window/profile/session changes. 8. Invoke
ReorderBuffer callback outside lock. 9. Define skipped/cancelled
sequence behavior. 10. Ensure older slow requests cannot block latest
relevant dialogue forever. 11. Add per-job cancellation. 12. Test
repeated Start/Stop and worker lifecycle.

Tests: slow provider burst, saturation, prefetch priority, old session
completion, concurrent reorder submissions, callback re-entry, stop
during active request.

Exit: bounded memory, latest relevant confirmed dialogue eventually
appears, no stale subtitle after session change, no callback under
internal lock.

### Phase 2 --- Structured translation and provider reliability (P0/P1)

Files: `TranslationRouter.cs`, `ApiKeyPool.cs`, `ApiCredential.cs`,
`TranslationResult.cs`, `TranslationWorker.cs`,
`ProviderHealthTracker.cs`, provider implementations and router tests.

Tasks: 1. Use structured results for long and normal dialogue. 2. Add
typed error/status categories. 3. Attempt all eligible Gemini keys
within request budget. 4. Never retry a key twice in one request. 5.
Classify 429, invalid key, timeout, network and 5xx separately. 6. Add
provider-specific cooldown and jitter. 7. Never fallback after
cancellation. 8. Enforce local-only Offline Mode. 9. Honor provider
order and user selection. 10. Add global deadline and per-provider
timeout. 11. Preserve provider/model/key ID (not secret), latency and
fallback reason. 12. Mark partial long-dialogue failure explicitly. 13.
Keep degraded output out of canonical memory.

Tests: pools of 1/2/3/5 keys, first key 429 then next success, invalid
key, all keys cooling down, cancellation, offline mode, long dialogue
partial failure.

Exit: all eligible keys considered within budget; fallback policy
predictable; offline mode cloud-free; partial output not recorded as
full success.

### Phase 3 --- OCR and capture reliability (P0/P1)

Files: `CaptureService.cs`, `CaptureBufferPool.cs`,
`FrameChangeDetector.cs`, `WindowEnumerator.cs`, `OcrRouter.cs`, engine
fallback, preprocessing and benchmark tests.

Tasks: 1. Normalize coordinates to physical pixels. 2. Add bounds checks
for client and virtual desktop. 3. Detect
minimized/closed/restarted/inaccessible windows. 4. Return typed capture
statuses. 5. Verify buffer ownership and lifetime. 6. Add OCR
cancellation/timeouts where supported. 7. Preserve engine fallback
reason and latency. 8. Validate clipped/partial/suspicious OCR output.
9. Select preprocessing by profile and scene. 10. Add engine health
tracking and temporary circuit breaker. 11. Expose OCR engine status in
diagnostics.

MORT-inspired improvements: optional multi-pass preprocessing,
profile-specific scaling/grayscale/contrast/threshold/color isolation,
preserve original frame for comparison, candidate crops only where
needed, lightweight fast path for stable dialogue.

Tests: 100/125/150/200% DPI, negative monitor coordinates,
moving/resizing windows, minimized/restarted target, concurrent buffer
use, empty vs engine failure, clipped text, missing OneOCR DLL, engine
timeout.

Exit: no buffer corruption; capture failure distinct from no text;
fallback observable.

### Phase 4 --- Dialogue intelligence and long/scrolling text (P1)

Files: Dialogue tracker, text similarity, history, deduplicator, speaker
detection/resolution, long dialogue handler, FGO context and tests.

Tasks: 1. Build corpus for typewriter, line-wrap, scroll overlap,
partial overlap, speaker changes and scene transitions. 2. Preserve raw
OCR separately from stitched/normalized text. 3. Track source spans
during stitching. 4. Merge using source-aware spans rather than
translated-prefix guesses. 5. Add Japanese-aware segmentation, including
punctuation-free dialogue. 6. Handle one very long sentence. 7. Preserve
speaker/scene context across chunks. 8. Define fast-forward and max
dialogue age. 9. Prevent OCR oscillation from duplicating requests. 10.
Add manual correction/retranslate without corrupting raw OCR.

Exit: no duplicate fragments, no missing clauses, typewriter
stabilization works, speaker identity remains stable.

### Phase 5 --- Translation Memory, FGO context and quality (P1/P2)

Files: `TranslationMemory.cs`, `SqliteMemoryStore.cs`, cache key, FGO
context, character profiles, glossary, game profiles and tests.

Tasks: 1. Formalize precedence: user-approved \> validated model
output/imported glossary; degraded fallback excluded from canonical
lookup. 2. Ensure approved entries cannot be overwritten. 3. Verify
fuzzy matching profile/language isolation. 4. Version normalization and
prompt policy. 5. Add user approve/edit workflow. 6. Store raw OCR,
normalized source, translation, profile, scene, speaker, provider, model
and provenance. 7. Add backup/restore and schema migration tests. 8. Add
glossary conflict detection/import/export validation. 9. Expand
character profiles for pronouns, relationships, voice and titles. 10.
Add scene/chapter glossary overlays without mutating global glossary.
11. Add regression tests for FGO terms and consistent pronouns.

Suggested context:

``` text
TranslationContext
  ├── GameProfileId
  ├── SceneId / ChapterId
  ├── SourceLanguage
  ├── TargetLanguage
  ├── SpeakerId
  ├── CharacterRelationship
  ├── GlossaryVersion
  ├── StylePreset
  └── RecentDialogueSummary
```

Only include context that improves quality; do not send excessive
history with every request.

Exit: approved translations always win; terminology consistent; fuzzy
matching isolated; scene context does not pollute global glossary.

### Phase 6 --- UI responsiveness and maintainability (P1/P2)

Files: `MainWindow.xaml`, `MainWindow.xaml.cs`, overlay/UI, new
ViewModels/Services.

Tasks: 1. Inventory MainWindow responsibilities. 2. Extract capture
lifecycle. 3. Extract translation session orchestration. 4. Extract
settings persistence/validation. 5. Extract overlay coordination. 6.
Extract hotkey dispatch. 7. Introduce ViewModels incrementally. 8.
Dispatch UI-bound state through WPF Dispatcher. 9. Avoid synchronous
`.Wait()`/`.Result` in UI paths. 10. Expose provider/OCR/queue/profile
status. 11. Provide actionable error messages.

Exit: no OCR/provider/database work on Dispatcher; MainWindow becomes a
shell; workflows and hotkeys remain compatible.

### Phase 7 --- Offline AI, resource use and performance (P2)

Tasks: 1. Ollama health check: reachable, model installed, loaded and
ready. 2. Distinguish offline mode from cloud provider outage. 3. Never
route offline requests to cloud. 4. Add configurable model and context
length. 5. Add warm-up/idle-unload options. 6. Record first-token and
total latency. 7. Track RAM, CPU, capture FPS, OCR calls/minute, queue
and provider request rates. 8. Adapt capture interval to frame
stability. 9. Run expensive preprocessing only when justified. 10. Add
low-end laptop resource profile. 11. Keep TTS off translation worker/UI
critical paths. 12. Optional privacy setting to disable session
persistence.

Exit: offline mode provably cloud-free; missing local model clearly
reported; static scenes reduce capture/OCR work; stable long sessions.

### Phase 8 --- Evaluation, CI and release (P1)

Tasks: 1. Run restore/build/tests/harness/benchmark in CI where
practical. 2. Add dependency vulnerability scanning and analyzers. 3.
Publish test results and benchmark artifacts. 4. Add clean-machine
packaging smoke test. 5. Verify native OCR dependencies. 6. Verify
self-contained release without SDK/dev tools. 7. Verify LocalAppData
paths and migrations. 8. Verify release archive structure against
README. 9. Reconcile test counts and baseline docs. 10. Add rollback and
database backup instructions.

Exit: clean tagged build, verified tests, clean-machine package,
preserved user data and accurate release notes.

## 7. Recommended implementation order

    Order Work item                             Priority
  ------- ------------------------------------- ----------
        0 Reproduce 3.0.5 baseline              P0
        1 Bounded translation queue             P0
        2 ReorderBuffer callback outside lock   P0
        3 Session/generation identity           P0
        4 Structured long-dialogue results      P0
        5 Full eligible API key rotation        P0/P1
        6 Cancellation and graceful shutdown    P0
        7 Capture DPI/bounds validation         P0/P1
        8 Expand OCR corpus                     P1
        9 Dialogue stitching improvements       P1
       10 Translation Memory invariants         P1
       11 MainWindow extraction                 P1
       12 Ollama/offline reliability            P2
       13 Resource telemetry/adaptive capture   P2
       14 CI/package/release hardening          P1

Do not implement all phases in one large change. Use small PRs and keep
the baseline passing after each phase.

## 8. Metrics and initial targets

Collect baseline and post-change results using the same hardware, game
scene, resolution, DPI and provider configuration.

### Capture/OCR

-   Capture and frame-diff latency p50/p95/p99.
-   OCR latency per engine p50/p95/p99.
-   OCR calls/minute and skip percentage.
-   Empty-result, failure and fallback rate.
-   Clipped-dialogue detection.
-   CER/WER by profile and language.

### Dialogue/translation

-   OCR-to-confirmed delay.
-   Confirmed-to-first-translation delay.
-   End-to-end OCR-to-visible-subtitle delay.
-   Queue depth and oldest job age.
-   Cancelled/stale/dropped jobs.
-   Provider success, 429, timeout and fallback rate.
-   Provider latency p50/p95/p99.
-   Key rotations per 100 requests.
-   Translation Memory exact/fuzzy hit rate.
-   Partial long-dialogue failure rate.
-   Stale subtitle display count.

### Resource

-   RAM at idle, 1 hour and 4 hours.
-   CPU average/peak.
-   GDI handles and thread count.
-   SQLite size/write latency.
-   Capture buffer allocation rate.
-   Ollama memory and latency.

Suggested initial targets (to validate, not claims about current
behavior):

  Metric                                    Target
  ----------------------------------------- ------------------------------------------
  Queue                                     Bounded; configurable capacity
  Stale result after session change         0
  Approved memory overwritten               0
  Cloud requests in Offline Mode            0
  Deadlocks in queue/reorder stress tests   0
  Memory growth                             Stable during 4-hour soak
  OCR benchmark                             Reproducible corpus and environment
  Translation latency                       Establish baseline, then improve p50/p95

## 9. Test strategy

### Unit tests

-   Bounded queue policies, cancellation and priority.
-   ReorderBuffer concurrency and callback re-entry.
-   Session generation invalidation.
-   API key rotation and error classification.
-   Provider circuit breaker recovery.
-   Long dialogue chunking/merge.
-   Cache precedence/profile isolation.
-   Config schema migration.
-   DPI and capture rectangle math.
-   OCR status classification.

### Integration tests

-   TranslationWorker + fake provider + bounded queue.
-   Router + fake Gemini/DeepL/Google/Ollama providers.
-   OCR Router + fake engines with success/empty/failure/timeout.
-   SQLite migration + backup/restore.
-   Profile switch while request is in flight.
-   Shutdown while OCR/provider work is active.

### Manual game tests

-   **FGO JP:** typewriter, scrolling, long monologue, punctuation-free
    Japanese, speaker changes, scene transition, battle UI.
-   **FGO NA:** English punctuation, names/titles, UI overlays,
    different resolutions/DPI.
-   **Blue Archive:** speaker/dialogue separation, colored text, rapid
    exchanges.
-   **Generic VN:** dark/bright backgrounds, varied fonts/colors,
    windowed/borderless, multi-monitor.

### Soak tests

-   1-hour normal play.
-   4-hour session.
-   Repeated profile/window changes.
-   Provider disconnect/reconnect.
-   Ollama stopped while offline mode active.
-   Isolated SQLite lock/corruption tests.
-   Close app during active translation.

## 10. Data safety and migration

Before config/database changes: 1. Back up
`%LOCALAPPDATA%\\TBCO\\config.json`. 2. Back up
`%LOCALAPPDATA%\\TBCO\\tbco_memory.db`. 3. Back up profiles, glossary
overrides and session history. 4. Use transactional SQLite migrations.
5. Keep old schema readable until migration succeeds. 6. Never delete
old data after failed migration. 7. Test DPAPI failure recovery. 8. Test
upgrade from previous published release, not only fresh install. 9.
Verify approved translations and API credentials after upgrade.

## 11. Release checklist

-   [ ] Release build succeeds.
-   [ ] All automated tests pass.
-   [ ] Translation harness meets regression thresholds.
-   [ ] OCR benchmark report generated.
-   [ ] No secrets in source, logs or artifacts.
-   [ ] SQLite migration tested.
-   [ ] Approved translations preserved.
-   [ ] Offline Mode tested with network disabled.
-   [ ] Long/scrolling dialogue tests pass.
-   [ ] DPI/multi-monitor smoke test passes.
-   [ ] Native OCR dependencies present or documented.
-   [ ] Self-contained package runs without developer tools.
-   [ ] Existing settings survive upgrade.
-   [ ] README, Vietnamese guide and release notes match actual package.

## 12. Definition of done

A phase is complete only when: 1. Scope is limited and documented. 2.
Existing behavior is preserved or migration documented. 3. Tests cover
changed behavior. 4. No unbounded queue, unobserved background task or
swallowed critical exception is introduced. 5. Logs/metrics expose
failure reasons without secrets. 6. UI remains responsive during slow
OCR/provider operations. 7. User data is preserved. 8. Acceptance
criteria are demonstrated. 9. Documentation is updated. 10. Clean
Release build succeeds.

## 13. Instructions for Antigravity / Codex

Use this document as an implementation specification, not permission to
rewrite the application.

### Mandatory workflow

1.  Inspect current repository, branch, commit SHA and version.
2.  Compare current code with this plan; identify already-completed
    items.
3.  Run baseline tests before editing.
4.  Implement one phase or cohesive sub-phase at a time.
5.  Before editing, list exact files and expected behavior changes.
6.  Preserve .NET 8, WPF, C#, profiles, user data and provider support.
7.  Add tests with each fix.
8.  Run targeted tests, then full tests before phase completion.
9.  Do not mark complete merely because code compiles.
10. Report changed files, tests, results, risks and remaining work.

### Prohibited shortcuts

-   No wholesale architecture rewrite.
-   No removing providers/OCR engines without evidence and approval.
-   No unbounded translation queue.
-   No silent dropping of confirmed dialogue.
-   No partial/fallback output stored as canonical approved memory.
-   No cloud calls during Offline Mode.
-   No logging API keys or decrypted secrets.
-   No claims of quality gains without evaluation corpus.
-   No unannounced hotkey/default changes.
-   No deleting/resetting user data during tests.

### Required report after each phase

``` text
Phase:
Baseline commit:
Files changed:
Behavior changed:
Tests added:
Tests executed:
Build result:
Benchmark result:
Data migration:
Known risks:
Remaining tasks:
```

## 14. Final recommendation

TBCO already has the major building blocks of a capable desktop game
translator. The next milestone should make the existing pipeline
reliable under real gameplay conditions.

**First implementation batch:** 1. Bounded translation queue and
explicit backpressure. 2. ReorderBuffer callback outside lock. 3.
Session/generation IDs to prevent stale results. 4. Structured
long-dialogue results. 5. Full API Key Pool rotation and typed provider
errors. 6. End-to-end cancellation and graceful shutdown.

Only after these are stable should substantial effort go into more game
profiles, advanced OCR passes or new UI features.

The primary success metric is not feature count. It is whether TBCO
consistently captures, understands, translates and displays the current
dialogue without queue buildup, duplicate fragments, stale subtitles or
loss of user-approved translation data.
