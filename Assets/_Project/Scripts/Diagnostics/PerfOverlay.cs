using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Profiling;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.InputSystem;
using Pistol.Core;
using Pistol.Player;
using Pistol.PostFx;
using Pistol.Weapons;
using Pistol.Weapons.Revolver;

namespace Pistol.Diagnostics
{
    // Performance overlay. Creates itself when the game starts — nothing needs to be added to the scene.
    // Hidden by default.
    //
    // Keys (on a MacBook hold Fn if the F-keys act as media keys):
    //   3   — show / hide
    //   F5  — VSync on / off (turn it off to measure HEADROOM)
    //   F6  — Motion Blur on / off
    //   F7  — all post-processing on / off
    //   F8  — screen effects of aim and inspect (CameraEffects: focus, vignette, edge blur) on / off
    //   F9  — next resolution (Shift+F9 — previous). The list is built down from the screen resolution
    //   F10 — report: copied to the clipboard (paste it into the chat) and appended to a file on the Desktop
    //
    // In the browser (WebGL) the F-keys may be taken by the browser, so they are duplicated as Alt+digit
    // (Alt+6/7/8 — effects, Alt+0 — report). The report is downloaded as a file.
    // VSync can't be turned off in the browser (the browser syncs frames), so HEADROOM isn't measured there.
    // Cyrillic text in other OnGUI panels (MovementDebugUI) in the browser needs Assets/Resources/OverlayFont.ttf.
    //
    // Hitch trap: every frame that missed the display (with VSync — longer than 1.5 budgets,
    // without VSync — 2.5x longer than usual) is written to Pistol_perf_<date>.txt on the Desktop
    // together with what was happening in the game at that moment (running, aiming, shot, GC, etc.).
    public class PerfOverlay : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("PerfOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfOverlay>();
        }

        const float UpdateInterval = 0.5f;   // how often to refresh the text (s)
        const int HistorySize = 300;         // how many frames to keep (~5 s at 60 FPS)
        const int GraphFrames = 180;         // how many frames to draw on the graph
        const float SettleTime = 2f;         // seconds after a settings change that are excluded from stats
        const float EventWindow = 1f;        // which events to show next to a hitch (seconds before it)
        const bool WriteLogInEditor = false; // no log file in the editor (the F10 report is still copied)
        const float PanelWidth = 700f;

        bool visible = false;

        // ---------- Web (WebGL) ----------
#if UNITY_WEBGL && !UNITY_EDITOR
        static readonly bool IsWeb = true;
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void PerfOverlay_SaveText(string text, string fileName);   // PerfOverlay.jslib
#else
        static readonly bool IsWeb = false;
#endif
        readonly List<string> memLog = new List<string>();    // web: in-memory log (no file on disk)
        bool VSyncOn => IsWeb || QualitySettings.vSyncCount > 0;   // in the browser frames are always synced by the browser
        bool lastFocused = true;

        // ---------- Frame history ----------
        readonly float[] frameMs = new float[HistorySize];
        readonly bool[] frameHitch = new bool[HistorySize];
        readonly float[] sorted = new float[HistorySize];
        int head, count;

        int missedWindow, missedTotal, missedLastWindow;
        float missedWindowTimer;

        // ---------- Timings ----------
        readonly FrameTiming[] timings = new FrameTiming[1];
        float cpuMs, gpuMs = -1f;
        ProfilerRecorder systemMemory, gfxMemory, mainThreadRec, gcAllocRec;

        System.Diagnostics.Process process;
        TimeSpan lastCpuTime;
        float lastCpuStamp, processCpuPercent = -1f;
        bool processCpuAvailable = true;

        float reserveAvgMs, reserveWorstMs, frameAvgMs, frameWorstMs;
        bool reserveMeasurable;

        // ---------- Resolutions ----------
        int nativeW, nativeH;
        readonly List<Vector2Int> resList = new List<Vector2Int>();
        int resIndex;
        static readonly float[] ResScales = { 1f, 0.9f, 0.8f, 0.75f, 0.65625f, 0.5625f, 0.5f };

        // ---------- Effect toggles ----------
        PostProcessLayer ppLayer;
        readonly List<MotionBlur> motionBlurs = new List<MotionBlur>();
        bool mbSearched, motionBlurOn;
        bool screenFxOn = true;
        CameraEffects cameraEffects;

        // ---------- Game references ----------
        PlayerMovement movement;
        WeaponAim weaponAim;
        RevolverFire revolver;
        float refSearchTimer;

        // ---------- Game events ----------
        struct GameEvent { public float time; public string name; public bool first; }
        readonly List<GameEvent> events = new List<GameEvent>();
        readonly HashSet<string> seenEvents = new HashSet<string>();
        bool wasRunning, wasCrouch, wasAiming, wasOpen, wasExhausted, wasFastTurn, wasFocused = true;
        float airStart = -1f, turnSpeed;

        // ---------- Hitches ----------
        int gcLast, hitchCount;
        float emaMs = 16.7f, settleUntil, sessionStart;
        readonly List<string> hitchLines = new List<string>();
        readonly List<float> hitchTimes = new List<float>();
        readonly List<string> recentShort = new List<string>();
        readonly Dictionary<string, int> hintCounts = new Dictionary<string, int>();

        // ---------- Per-mode stats (resolution + VSync + effects) ----------
        // Constant-memory stats for one mode: frame time histogram (0.1 ms bins, up to 200 ms)
        class Segment
        {
            const float BinMs = 0.1f;
            readonly int[] bins = new int[2001];
            public int count, hitches;
            public float seconds, sumMs, maxMs;

            public void Add(float ms)
            {
                bins[Mathf.Min(bins.Length - 1, (int)(ms / BinMs))]++;
                count++;
                sumMs += ms;
                if (ms > maxMs) maxMs = ms;
            }

            // Average time of the slowest 1% of frames
            public float Worst1PercentMs()
            {
                int need = Mathf.Max(1, count / 100), taken = 0;
                float sum = 0f;
                for (int i = bins.Length - 1; i >= 0 && taken < need; i--)
                {
                    int n = Mathf.Min(bins[i], need - taken);
                    float ms = i == bins.Length - 1 ? maxMs : (i + 0.5f) * BinMs;
                    sum += n * ms;
                    taken += n;
                }
                return sum / need;
            }
        }
        readonly Dictionary<string, Segment> segments = new Dictionary<string, Segment>();
        readonly List<string> segmentOrder = new List<string>();

        // ---------- Log file ----------
        StreamWriter log;
        string logPath;
        string toast;
        float toastUntil;

        // ---------- Drawing ----------
        float accum;
        string text = "";
        bool textDirty = true;
        GUIContent content;
        float textHeight;
        GUIStyle style, labelStyle;
        Texture2D white;
        Material lineMaterial;
        float graphTop = 75f;

        // =====================================================================

        void OnEnable()
        {
            CreateKeys();
            systemMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            gfxMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            mainThreadRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            gcAllocRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            if (IsWeb) processCpuAvailable = false;   // not available in the browser
            else try
            {
                process = System.Diagnostics.Process.GetCurrentProcess();
                lastCpuTime = process.TotalProcessorTime;
                lastCpuStamp = Time.realtimeSinceStartup;
            }
            catch { processCpuAvailable = false; }

            gcLast = GC.CollectionCount(0);
            sessionStart = Time.realtimeSinceStartup;
            settleUntil = sessionStart + 3f;     // the first seconds are loading — don't count them
            BuildResolutionList();
            OpenLog();
        }

        void OnDisable()
        {
            DisposeKeys();
            if (log != null)
            {
                try { log.WriteLine(); log.WriteLine(BuildReport("SESSION SUMMARY (written on exit)")); log.Close(); }
                catch { }
                log = null;
            }
            systemMemory.Dispose();
            gfxMemory.Dispose();
            mainThreadRec.Dispose();
            gcAllocRec.Dispose();
            if (process != null) { process.Dispose(); process = null; }
        }

        // Budget of one frame — always based on the display refresh rate
        float BudgetMs()
        {
            float hz = 60f;
            double r = Screen.currentResolution.refreshRateRatio.value;
            if (r > 1) hz = (float)r;
            if (QualitySettings.vSyncCount > 1) hz /= QualitySettings.vSyncCount;
            return 1000f / hz;
        }

        float Now => Time.realtimeSinceStartup;
        float SessionTime => Now - sessionStart;

        // =====================================================================
        void Update()
        {
            HandleKeys();

            // Look objects up only while something is still missing
            bool missing = movement == null || weaponAim == null || revolver == null || ppLayer == null || cameraEffects == null;
            refSearchTimer -= Time.unscaledDeltaTime;
            if (missing && refSearchTimer <= 0f) { refSearchTimer = 1f; FindRefs(); }
            if (!mbSearched && Time.frameCount > 3) FindMotionBlur();

            FrameTimingManager.CaptureFrameTimings();
            float dtMs = Time.unscaledDeltaTime * 1000f;
            float budget = BudgetMs();
            float now = Now;

            PollState(now);

            // While the window is unfocused and for 2 s after it comes back — no stats (game sits in background)
            bool focused = Application.isFocused;
            if (!focused || focused != lastFocused) settleUntil = now + SettleTime;
            lastFocused = focused;

            // ---------- Hitch? ----------
            bool vsync = VSyncOn;
            float threshold = vsync ? budget * 1.5f : Mathf.Max(budget, emaMs * 2.5f);
            int gcNow = GC.CollectionCount(0);
            int gcDelta = gcNow - gcLast;
            gcLast = gcNow;
            bool hitch = Time.frameCount > 5 && dtMs > threshold;
            if (hitch) RegisterHitch(now, dtMs, threshold, gcDelta);
            else emaMs = Mathf.Lerp(emaMs, dtMs, 0.05f);

            // ---------- History ----------
            frameMs[head] = dtMs;
            frameHitch[head] = hitch;
            head = (head + 1) % HistorySize;
            if (count < HistorySize) count++;

            if (dtMs > budget * 1.5f) missedWindow += Mathf.Max(1, Mathf.RoundToInt(dtMs / budget) - 1);
            missedWindowTimer += Time.unscaledDeltaTime;
            if (missedWindowTimer >= 5f)
            {
                missedLastWindow = missedWindow;
                missedTotal += missedWindow;
                missedWindow = 0;
                missedWindowTimer = 0f;
            }

            // ---------- Per-mode stats ----------
            if (now >= settleUntil)
            {
                var seg = CurrentSegment();
                seg.Add(dtMs);
                seg.seconds += Time.unscaledDeltaTime;
            }

            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                cpuMs = (float)(timings[0].cpuMainThreadFrameTime - timings[0].cpuMainThreadPresentWaitTime);
                gpuMs = timings[0].gpuFrameTime > 0 ? (float)timings[0].gpuFrameTime : -1f;
            }

            accum += Time.unscaledDeltaTime;
            if (accum < UpdateInterval) return;
            accum = 0f;
            UpdateProcessCpu();
            BuildText(budget);
        }

        // =====================================================================
        // Keys. A debug tool, so it has its own actions instead of the player's controls.
        InputAction toggleKey, shiftKey, altKey, fireKey;
        InputAction[] fKeys;      // F5..F10
        InputAction[] webKeys;    // browser: Alt + 5..9, 0 duplicate F5..F10

        static InputAction CreateKey(string path)
        {
            var a = new InputAction(path, InputActionType.Button, path);
            a.Enable();
            return a;
        }

        void CreateKeys()
        {
            toggleKey = CreateKey("<Keyboard>/3");
            shiftKey = CreateKey("<Keyboard>/shift");
            altKey = CreateKey("<Keyboard>/alt");
            fireKey = CreateKey("<Mouse>/leftButton");
            fKeys = new[] { "f5", "f6", "f7", "f8", "f9", "f10" }.Select(k => CreateKey("<Keyboard>/" + k)).ToArray();
            webKeys = new[] { "5", "6", "7", "8", "9", "0" }.Select(k => CreateKey("<Keyboard>/" + k)).ToArray();
        }

        void DisposeKeys()
        {
            if (toggleKey == null) return;
            foreach (var a in new[] { toggleKey, shiftKey, altKey, fireKey }.Concat(fKeys).Concat(webKeys)) a.Dispose();
            toggleKey = null;
        }

        void HandleKeys()
        {
            bool toggle = toggleKey.WasPressedThisFrame();
            var pressed = new bool[fKeys.Length];
            for (int k = 0; k < fKeys.Length; k++) pressed[k] = fKeys[k].WasPressedThisFrame();
            bool shift = shiftKey.IsPressed();
            if (IsWeb && altKey.IsPressed())   // the browser may take the F-keys for itself
            {
                toggle = false;                 // Alt+3 is not the panel toggle
                for (int k = 0; k < webKeys.Length; k++) pressed[k] |= webKeys[k].WasPressedThisFrame();
            }
            bool f5 = pressed[0], f6 = pressed[1], f7 = pressed[2], f8 = pressed[3], f9 = pressed[4], f10 = pressed[5];

            if (toggle) visible = !visible;
            if (f5 && IsWeb)
            {
                // In the browser frames are synced by the browser itself. The "unlimited" mode (targetFrameRate)
                // was tried — the browser can't keep up: frames pile up and stutter, the measurement is meaningless.
                ShowToast("VSync can't be turned off in the browser — watch FPS, 1% low and hitches");
                textDirty = true;
            }
            else if (f5)
            {
                QualitySettings.vSyncCount = QualitySettings.vSyncCount > 0 ? 0 : 1;
                OnSettingsChanged(QualitySettings.vSyncCount > 0 ? "VSync on" : "VSync off");
            }
            if (f6) ToggleMotionBlur();
            if (f7) TogglePostFx();
            if (f8) ToggleScreenFx();
            if (f9) NextResolution(shift ? -1 : 1);
            if (f10) SaveReport();
        }

        void OnSettingsChanged(string what)
        {
            currentSegment = null;   // the mode changed — rebuild the key
            count = 0; head = 0;
            missedWindow = 0; missedWindowTimer = 0f;
            settleUntil = Now + SettleTime;
            AddEvent(what);
            WriteLog($"[{SessionTime,7:F1} s] -- {what}  ->  {SettingsString()}");
            ShowToast(what);
            textDirty = true;
        }

        void ShowToast(string msg) { toast = msg; toastUntil = Now + 4f; }

        // =====================================================================
        // Resolutions: from the screen resolution down, same aspect ratio
        void BuildResolutionList()
        {
            nativeW = Display.main.systemWidth;
            nativeH = Display.main.systemHeight;
            foreach (var r in Screen.resolutions)
                if ((long)r.width * r.height > (long)nativeW * nativeH) { nativeW = r.width; nativeH = r.height; }
            if (nativeW <= 0 || nativeH <= 0) { nativeW = Screen.currentResolution.width; nativeH = Screen.currentResolution.height; }

            resList.Clear();
            float aspect = nativeH / (float)nativeW;
            foreach (float s in ResScales)
            {
                int w = Mathf.RoundToInt(nativeW * s / 8f) * 8;           // width is a multiple of 8
                int h = Mathf.RoundToInt(w * aspect / 2f) * 2;            // height is even, screen aspect ratio
                if (w < 640) continue;
                var v = new Vector2Int(w, h);
                if (!resList.Contains(v)) resList.Add(v);
            }
            resIndex = NearestResolution();
        }

        int NearestResolution()
        {
            int best = 0, bestDiff = int.MaxValue;
            for (int i = 0; i < resList.Count; i++)
            {
                int d = Mathf.Abs(resList[i].x - Screen.width);
                if (d < bestDiff) { bestDiff = d; best = i; }
            }
            return best;
        }

        void NextResolution(int dir)
        {
            if (IsWeb)
            {
                ShowToast("In the browser the resolution is set by the page: config.devicePixelRatio in index.html");
                textDirty = true;
                return;
            }
            if (resList.Count == 0) return;
            resIndex = NearestResolution();
            // The list goes from larger to smaller: F9 — smaller, Shift+F9 — larger
            resIndex = (resIndex + dir + resList.Count) % resList.Count;
            var r = resList[resIndex];
            Screen.SetResolution(r.x, r.y, Screen.fullScreenMode);
            OnSettingsChanged($"resolution -> {r.x}x{r.y}");
        }

        // =====================================================================
        // Effect toggles
        void FindRefs()
        {
            currentSegment = null;   // found objects change the mode string
            if (movement == null) movement = FindFirstObjectByType<PlayerMovement>();
            if (weaponAim == null) weaponAim = FindFirstObjectByType<WeaponAim>();
            if (revolver == null) revolver = FindFirstObjectByType<RevolverFire>();
            if (ppLayer == null)
            {
                var cam = Camera.main;
                if (cam != null) ppLayer = cam.GetComponent<PostProcessLayer>();
                if (ppLayer == null) ppLayer = FindFirstObjectByType<PostProcessLayer>();
            }
            if (cameraEffects == null)
            {
                var cam = Camera.main;
                if (cam != null) cameraEffects = cam.GetComponent<CameraEffects>();
                if (cameraEffects != null) screenFxOn = !cameraEffects.Suppressed;
            }
        }

        void FindMotionBlur()
        {
            mbSearched = true;
            currentSegment = null;
            motionBlurs.Clear();
            foreach (var v in FindObjectsByType<PostProcessVolume>(FindObjectsSortMode.None))
            {
                // Only touch profiles that contain Motion Blur (CameraEffects volumes are left alone).
                // v.profile is a runtime copy of the profile — the profile asset in the project isn't changed.
                if (v.sharedProfile == null || !v.sharedProfile.HasSettings<MotionBlur>()) continue;
                if (v.profile.TryGetSettings(out MotionBlur mb)) motionBlurs.Add(mb);
            }
            motionBlurOn = motionBlurs.Exists(m => m.active && m.enabled.value);
        }

        void ToggleMotionBlur()
        {
            if (!mbSearched || motionBlurs.Count == 0) FindMotionBlur();
            if (motionBlurs.Count == 0) { ShowToast("No Motion Blur found in the profiles"); return; }
            motionBlurOn = !motionBlurOn;
            foreach (var mb in motionBlurs) { mb.active = motionBlurOn; mb.enabled.Override(motionBlurOn); }
            OnSettingsChanged(motionBlurOn ? "Motion Blur on" : "Motion Blur off");
        }

        void TogglePostFx()
        {
            FindRefs();
            if (ppLayer == null) { ShowToast("Post-process Layer not found"); return; }
            ppLayer.enabled = !ppLayer.enabled;
            OnSettingsChanged(ppLayer.enabled ? "post-processing on" : "post-processing off");
        }

        void ToggleScreenFx()
        {
            FindRefs();
            if (cameraEffects == null) { ShowToast("CameraEffects not found"); return; }
            // Suppress / restore without touching any effect settings
            screenFxOn = !screenFxOn;
            cameraEffects.SetSuppressed(!screenFxOn);
            OnSettingsChanged(screenFxOn ? "screen fx on" : "screen fx off");
        }

        static string OnOff(bool b) => b ? "on" : "off";

        string SettingsString()
        {
            bool pp = ppLayer == null || ppLayer.enabled;
            string mb = motionBlurs.Count == 0 ? "none" : OnOff(motionBlurOn);
            return $"{Screen.width}x{Screen.height}, VSync {OnOff(VSyncOn)}, " +
                   $"Motion Blur {mb}, post {OnOff(pp)}, screen fx {OnOff(screenFxOn)}";
        }

        // The mode key is rebuilt only when something changed — otherwise a new string would be created
        // every frame (GC garbage), and the overlay itself would cause the hitches it is catching.
        string segmentKey;
        int keyW, keyH;
        Segment currentSegment;

        Segment CurrentSegment()
        {
            if (currentSegment != null && keyW == Screen.width && keyH == Screen.height) return currentSegment;
            keyW = Screen.width;
            keyH = Screen.height;
            segmentKey = SettingsString();
            if (!segments.TryGetValue(segmentKey, out var seg))
            {
                seg = new Segment();
                segments[segmentKey] = seg;
                segmentOrder.Add(segmentKey);
            }
            currentSegment = seg;
            return seg;
        }

        // =====================================================================
        // What is happening in the game — to understand what preceded a hitch
        void PollState(float now)
        {
            if (movement != null)
            {
                Track(ref wasRunning, movement.IsRunning, "started running", "stopped running");
                Track(ref wasCrouch, movement.IsCrouching, "crouched", "stood up");
                Track(ref wasExhausted, movement.IsExhausted, "exhausted", "recovered");
                if (movement.JumpedThisFrame) AddEvent("jump");

                bool grounded = movement.IsGrounded;
                if (!grounded && airStart < 0f) airStart = now;
                if (grounded && airStart >= 0f)
                {
                    if (now - airStart > 0.25f) AddEvent("landing");
                    airStart = -1f;
                }

                turnSpeed = movement.AppliedLookDelta.magnitude / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                Track(ref wasFastTurn, turnSpeed > 300f, "fast turn", null);
            }
            if (weaponAim != null) Track(ref wasAiming, weaponAim.AimAmount > 0.01f, "aiming", "stopped aiming");
            if (revolver != null) Track(ref wasOpen, revolver.IsOpen, "opened cylinder", "closed cylinder");
            if (fireKey.WasPressedThisFrame() && Cursor.lockState == CursorLockMode.Locked) AddEvent("shot (LMB)");
            Track(ref wasFocused, Application.isFocused, "window focused", "window lost focus");
        }

        void Track(ref bool prev, bool cur, string onName, string offName)
        {
            if (cur == prev) return;
            prev = cur;
            string n = cur ? onName : offName;
            if (n != null) AddEvent(n);
        }

        void AddEvent(string name)
        {
            bool first = seenEvents.Add(name);
            events.Add(new GameEvent { time = Now, name = name, first = first });
            if (events.Count > 40) events.RemoveAt(0);
        }

        string RecentEvents(float now, out string firstEvent, out bool focusEvent)
        {
            firstEvent = null;
            focusEvent = false;
            var sb = new StringBuilder();
            foreach (var e in events)
            {
                float ago = now - e.time;
                if (ago > EventWindow) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(e.name);
                if (e.first) sb.Append(" (first time)");
                sb.Append($" {ago:F2} s ago");
                if (e.first && ago <= 0.6f) firstEvent = e.name;
                if (e.name.StartsWith("window")) focusEvent = true;
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        string StateString()
        {
            if (movement == null) return "player not found";
            string s;
            if (!movement.IsGrounded) s = "in air";
            else if (movement.IsRunning) s = movement.IsExhausted ? "tired run" : "running";
            else if (movement.ActualSpeed > 0.2f) s = movement.IsCrouching ? "crouch walk" : "walking";
            else s = movement.IsCrouching ? "crouching" : "standing";
            float aim = weaponAim != null ? weaponAim.AimAmount : 0f;
            return $"{s}, {movement.ActualSpeed:F1} m/s, turn {turnSpeed:F0} deg/s, aim {aim:F2}";
        }

        void RegisterHitch(float now, float dtMs, float threshold, int gcDelta)
        {
            bool settling = now < settleUntil || dtMs > 1000f;   // > 1 s — the game was paused (background, sleep, switching)
            float mainMs = mainThreadRec.Valid ? mainThreadRec.LastValue * 1e-6f : -1f;
            string alloc = gcAllocRec.Valid ? $"{gcAllocRec.LastValue / 1024f:F1} KB" : "n/a";
            string recent = RecentEvents(now, out string firstEvent, out bool focusEvent);

            // Possible causes (a hint, not a diagnosis)
            var hints = new List<string>();
            var keys = new List<string>();
            if (gcDelta > 0) { hints.Add("garbage collection (GC)"); keys.Add("garbage collection (GC)"); }
            if (firstEvent != null)
            {
                hints.Add($"first \"{firstEvent}\" this session — likely loading or shader compilation");
                keys.Add("first action this session (shaders/loading)");
            }
            if (!Application.isFocused || focusEvent) { hints.Add("window switch"); keys.Add("window switch"); }
            if (settling) { hints.Add("right after a settings change / startup"); keys.Add("after settings change"); }
            if (hints.Count == 0) { hints.Add("not visible from the game — GPU or OS"); keys.Add("no visible cause"); }

            string line =
                $"[{now - sessionStart,7:F1} s] HITCH {dtMs:F1} ms (threshold {threshold:F1}) | " +
                $"main thread {(mainMs >= 0f ? mainMs.ToString("F1") : "n/a")} ms | " +
                $"GC: {(gcDelta > 0 ? "yes" : "no")}, allocated this frame {alloc} | " +
                $"{StateString()} | {SettingsString()} | " +
                $"events in last {EventWindow:F0} s: {recent} | possible cause: {string.Join("; ", hints)}";
            WriteLog(line);

            if (settling) return;   // hitches during settings changes are not counted

            hitchCount++;
            CurrentSegment().hitches++;
            foreach (var k in keys) hintCounts[k] = hintCounts.TryGetValue(k, out int c) ? c + 1 : 1;
            hitchLines.Add(line);
            hitchTimes.Add(now);
            if (hitchLines.Count > 300) hitchLines.RemoveAt(0);

            string shortState = movement == null ? "" : StateString().Split(',')[0];
            recentShort.Insert(0, $"{dtMs:F0} ms · {shortState} · {hints[0]}");
            if (recentShort.Count > 3) recentShort.RemoveAt(3);
            textDirty = true;
        }

        // =====================================================================
        // Log file and report
        void OpenLog()
        {
            if (IsWeb)
            {
                memLog.Add("=== Pistol — performance log (browser) ===");
                memLog.Add(DeviceString());
                memLog.Add("Hitch = a frame that missed the display. Below — every hitch and what was happening in the game.");
                memLog.Add("");
                return;
            }
            if (Application.isEditor && !WriteLogInEditor) return;
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Application.persistentDataPath;
                logPath = Path.Combine(dir, $"Pistol_perf_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
                log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                log.WriteLine("=== Pistol — performance log ===");
                log.WriteLine(DeviceString());
                log.WriteLine("Hitch = a frame that missed the display. Below — every hitch and what was happening in the game.");
                log.WriteLine();
            }
            catch (Exception e)
            {
                Debug.LogWarning("PerfOverlay: failed to create the log file: " + e.Message);
                log = null;
            }
        }

        void WriteLog(string line)
        {
            if (IsWeb)
            {
                memLog.Add(line);
                if (memLog.Count > 5000) memLog.RemoveAt(4);   // keep the header (first 4 lines)
                return;
            }
            if (log == null) return;
            try { log.WriteLine(line); } catch { log = null; }
        }

        string DeviceString()
        {
            return
                $"Date: {DateTime.Now:yyyy-MM-dd HH:mm}\n" +
                $"Device: {SystemInfo.deviceModel}, {SystemInfo.processorType} ({SystemInfo.processorCount} cores), " +
                $"RAM {SystemInfo.systemMemorySize} MB\n" +
                $"GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})\n" +
                $"OS: {SystemInfo.operatingSystem}\n" +
                $"Unity {Application.unityVersion}, {(Application.isEditor ? "editor" : Debug.isDebugBuild ? "Development Build" : "release build")}, " +
                $"quality \"{QualitySettings.names[QualitySettings.GetQualityLevel()]}\"\n" +
                $"Display: {nativeW}x{nativeH}, {Screen.currentResolution.refreshRateRatio.value:F0} Hz, window mode {Screen.fullScreenMode}\n" +
                $"Current: {SettingsString()}";
        }

        string BuildReport(string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {title} ===");
            sb.AppendLine(DeviceString());
            sb.AppendLine($"Session length: {SessionTime / 60f:F1} min. Hitches (excluding the first {SettleTime:F0} s after settings changes): {hitchCount}");
            sb.AppendLine();
            sb.AppendLine("Modes:");
            foreach (var key in segmentOrder)
            {
                var seg = segments[key];
                if (seg.count < 30) continue;
                float avg = seg.sumMs / seg.count, low1 = seg.Worst1PercentMs(), max = seg.maxMs;
                float perMin = seg.seconds > 1f ? seg.hitches / (seg.seconds / 60f) : 0f;
                sb.AppendLine($"  • {key}");
                sb.AppendLine($"      {seg.seconds:F0} s: FPS {1000f / avg:F0}, 1% low {1000f / low1:F0}, min {1000f / max:F0}, " +
                              $"hitches {seg.hitches} ({perMin:F1} per minute)");
            }
            string period = PeriodString();
            if (period != null) { sb.AppendLine(); sb.AppendLine(period); }
            if (hintCounts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Possible hitch causes:");
                foreach (var kv in hintCounts) sb.AppendLine($"  • {kv.Key}: {kv.Value}");
            }
            sb.AppendLine();
            int n = Mathf.Min(30, hitchLines.Count);
            sb.AppendLine($"Recent hitches ({n} of {hitchLines.Count}):");
            for (int i = hitchLines.Count - n; i < hitchLines.Count; i++) sb.AppendLine(hitchLines[i]);
            return sb.ToString();
        }

        // Are hitches "on a timer": the typical interval between neighbouring hitches and how many intervals match it
        string PeriodString()
        {
            var gaps = new List<float>();
            for (int i = 1; i < hitchTimes.Count; i++)
            {
                float g = hitchTimes[i] - hitchTimes[i - 1];
                if (g > 0.2f && g < 5f) gaps.Add(g);
            }
            if (gaps.Count < 5) return null;
            gaps.Sort();
            float median = gaps[gaps.Count / 2];
            int near = 0;
            foreach (float g in gaps) if (Mathf.Abs(g - median) <= median * 0.1f) near++;
            float share = near / (float)gaps.Count * 100f;
            string verdict = share >= 50f ? "hitches are periodic — something fires on a timer" : "no clear rhythm";
            return $"Hitch rhythm: typical interval {median:F2} s, {near} of {gaps.Count} intervals match it ({share:F0}%) — {verdict}";
        }

        void SaveReport()
        {
            settleUntil = Mathf.Max(settleUntil, Now + 0.5f);   // the report itself may cause a hitch — don't count it
            string rep = BuildReport("PerfOverlay REPORT (F10)");
            if (IsWeb)
            {
                // Whole log + report — downloaded as a file (and copied to the clipboard if the browser allows)
                string full = string.Join("\n", memLog) + "\n\n" + rep;
                string file = $"Pistol_perf_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
#if UNITY_WEBGL && !UNITY_EDITOR
                try { PerfOverlay_SaveText(full, file); } catch (Exception e) { Debug.LogWarning("PerfOverlay: " + e.Message); }
#else
                _ = full;   // never reached outside the browser
#endif
                Debug.Log(rep);   // also to the browser console (F12 -> Console)
                ShowToast($"Report downloaded: {file}");
                textDirty = true;
                return;
            }
            GUIUtility.systemCopyBuffer = rep;
            WriteLog("");
            WriteLog(rep);
            ShowToast(log != null
                ? $"Report copied to the clipboard and written to {Path.GetFileName(logPath)} on the Desktop"
                : "Report copied to the clipboard — paste it into the chat (Cmd+V)");
        }

        // =====================================================================
        void UpdateProcessCpu()
        {
            if (!processCpuAvailable || process == null) return;
            try
            {
                process.Refresh();
                TimeSpan now = process.TotalProcessorTime;
                float stamp = Time.realtimeSinceStartup;
                double used = (now - lastCpuTime).TotalSeconds;
                double elapsed = stamp - lastCpuStamp;
                if (elapsed > 0.05)
                    processCpuPercent = (float)(used / (elapsed * Environment.ProcessorCount) * 100.0);
                lastCpuTime = now;
                lastCpuStamp = stamp;
            }
            catch { processCpuAvailable = false; processCpuPercent = -1f; }
        }

        void BuildText(float budget)
        {
            if (count == 0) return;   // right after a stats reset — nothing to show
            Array.Copy(frameMs, sorted, count);
            Array.Sort(sorted, 0, count);
            float sum = 0f;
            for (int i = 0; i < count; i++) sum += sorted[i];
            float avgMs = count > 0 ? sum / count : 0f;
            float maxMs = count > 0 ? sorted[count - 1] : 0f;
            int worst = Mathf.Max(1, count / 100);
            float worstSum = 0f;
            for (int i = count - worst; i < count; i++) worstSum += sorted[i];
            float low1Ms = count > 0 ? worstSum / worst : 0f;

            float avgFps = avgMs > 0 ? 1000f / avgMs : 0f;
            float low1Fps = low1Ms > 0 ? 1000f / low1Ms : 0f;
            float minFps = maxMs > 0 ? 1000f / maxMs : 0f;

            float variance = 0f;
            for (int i = 0; i < count; i++) { float d = sorted[i] - avgMs; variance += d * d; }
            float jitter = count > 1 ? Mathf.Sqrt(variance / (count - 1)) : 0f;

            bool vsync = VSyncOn;

            // ---------- Headroom ----------
            frameAvgMs = avgMs;
            frameWorstMs = low1Ms;
            reserveAvgMs = budget - avgMs;
            reserveWorstMs = budget - low1Ms;
            reserveMeasurable = !vsync;
            string reserveLine;
            if (!reserveMeasurable)
                reserveLine = IsWeb
                    ? "<b>HEADROOM:</b> <color=#AAAAAA>can't be measured in the browser — watch 1% low</color>"
                    : "<b>HEADROOM:</b> <color=#AAAAAA>can't be measured with VSync — press F5</color>";
            else
            {
                float pct = reserveWorstMs / budget * 100f;
                string verdict = pct >= 20f ? "<color=#7CFC8A>OK</color>"
                               : pct >= 0f ? "<color=#FFD84D>TIGHT</color>"
                               : "<color=#FF6B5B>OVER BUDGET</color>";
                reserveLine = $"<b>HEADROOM: {verdict}</b>   worst {Signed(reserveWorstMs)} ms ({pct:+0;-0}%)   avg {Signed(reserveAvgMs)} ms";
            }

            string fpsColor = low1Fps >= 1000f / budget * 0.9f ? "#7CFC8A" : low1Fps >= 1000f / budget * 0.6f ? "#FFD84D" : "#FF6B5B";
            string missedColor = missedLastWindow == 0 ? "#7CFC8A" : missedLastWindow < 5 ? "#FFD84D" : "#FF6B5B";
            string hitchColor = hitchCount == 0 ? "#7CFC8A" : "#FFD84D";

            // ---------- Load ----------
            string proc = processCpuPercent >= 0f ? $"{processCpuPercent:F0}% of {Environment.ProcessorCount} cores" : "n/a";
            string loadLines;
            if (vsync)
                loadLines = (IsWeb ? "" : $"Process CPU usage: {proc}\n") +
                            "<color=#AAAAAA>with VSync CPU/GPU wait for the display — ~100% is normal</color>";
            else
            {
                string gpu = gpuMs > 0f
                    ? $"{gpuMs:F1} ms" + (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Metal ? " <color=#AAAAAA>(Mac: inaccurate)</color>" : "")
                    : "n/a";
                loadLines = $"CPU {cpuMs:F1} ms ({Colored(cpuMs / budget * 100f)})   GPU {gpu}" +
                            (IsWeb ? "" : $"\nProcess CPU usage: {proc}");
            }

            // ---------- Memory ----------
            long sysMem = systemMemory.Valid ? systemMemory.LastValue : 0;
            long unityMem = Profiler.GetTotalAllocatedMemoryLong();
            long gfxMem = gfxMemory.Valid ? gfxMemory.LastValue : 0;
            string ram = sysMem > 0 ? $"{sysMem / 1048576} MB" : "n/a";
            string vram = gfxMem > 0 ? $"{gfxMem / 1048576} MB" : "n/a";

            // ---------- Resolution and effects ----------
            float pixPct = nativeW > 0 ? Screen.width * (float)Screen.height / ((float)nativeW * nativeH) * 100f : 0f;
            bool pp = ppLayer == null || ppLayer.enabled;
            string mb = motionBlurs.Count == 0 ? "none" : OnOff(motionBlurOn);

            var sb = new StringBuilder();
            sb.Append(reserveLine).Append("\n\n\n");   // room for the headroom bar
            sb.Append($"FPS: {avgFps:F0}   1% low: <color={fpsColor}>{low1Fps:F0}</color>   min: {minFps:F0}\n");
            sb.Append($"Frame: {avgMs:F1} ms (max {maxMs:F1})   jitter ±{jitter:F1}   budget {budget:F1}\n");
            sb.Append($"Missed in 5 s: <color={missedColor}>{missedLastWindow}</color>   " +
                      $"hitches: <color={hitchColor}>{hitchCount}</color>   F10 — report\n");
            sb.Append(loadLines).Append('\n');
            sb.Append($"Render {Screen.width}x{Screen.height} of {nativeW}x{nativeH} ({pixPct:F0}% px)  F9\n");
            sb.Append($"F6 Motion Blur {mb}  F7 post {OnOff(pp)}  F8 screen fx {OnOff(screenFxOn)}\n");
            sb.Append($"RAM {ram}  Unity {unityMem / 1048576} MB  VRAM {vram}  VSync {OnOff(vsync)} (F5)");
            foreach (var r in recentShort) sb.Append($"\n<color=#FFB060>  {(IsWeb ? "•" : "▸")} {r}</color>");
            if (Now < toastUntil && !string.IsNullOrEmpty(toast)) sb.Append($"\n<color=#8FD3FF>{toast}</color>");

            text = sb.ToString();
            textDirty = true;
        }

        static string Signed(float v) => (v >= 0f ? "+" : "−") + Mathf.Abs(v).ToString("F1");

        static string Colored(float percent)
        {
            string c = percent < 70f ? "#7CFC8A" : percent < 100f ? "#FFD84D" : "#FF6B5B";
            return $"<color={c}>{percent:F0}%</color>";
        }

        // =====================================================================
        void OnGUI()
        {
            if (!visible) return;
            if (style == null)
            {
                if (IsWeb) ApplyWebFont();
                style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 20,
                    alignment = TextAnchor.UpperLeft,
                    richText = true,
                    wordWrap = false,
                    padding = new RectOffset(12, 12, 10, 10)
                };
                style.normal.textColor = Color.white;
                white = Texture2D.whiteTexture;
            }
            if (textDirty || content == null)
            {
                content = new GUIContent(text);
                textHeight = style.CalcHeight(content, PanelWidth);
                textDirty = false;
            }

            const float margin = 10f, graphHeight = 110f;
            float budget = BudgetMs();
            var box = new Rect(Screen.width - PanelWidth - margin, margin, PanelWidth, textHeight + graphHeight + 10f);
            GUI.Box(box, content, style);

            var graph = new Rect(box.x + 40f, box.y + textHeight, PanelWidth - 52f, graphHeight);
            if (Event.current.type == EventType.Repaint) DrawFpsGraph(graph, budget);
            DrawGraphLabels(graph, budget);

            float line = style.lineHeight;
            var bar = new Rect(box.x + 12f, box.y + 10f + line + 6f, PanelWidth - 24f, line * 1.4f);
            DrawReserveBar(bar, budget);
        }

        // The browser has no system fonts: Unity's built-in font has no Cyrillic, so Russian text disappears.
        // Load the font from Assets/Resources/OverlayFont.ttf and set it for all OnGUI (this also fixes MovementDebugUI).
        static void ApplyWebFont()
        {
            var f = Resources.Load<Font>("OverlayFont");
            if (f != null) GUI.skin.font = f;
            else Debug.LogWarning("PerfOverlay: no font at Assets/Resources/OverlayFont.ttf — Cyrillic text won't show in the browser.");
        }

        void DrawReserveBar(Rect r, float budget)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(r, white);

            if (reserveMeasurable && frameAvgMs > 0f)
            {
                float scale = budget * 1.5f;
                float worst01 = Mathf.Clamp01(frameWorstMs / scale);
                float avg01 = Mathf.Clamp01(frameAvgMs / scale);
                float pct = reserveWorstMs / budget;
                Color c = pct >= 0.2f ? new Color(0.45f, 0.95f, 0.5f) : pct >= 0f ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.35f);

                GUI.color = new Color(c.r, c.g, c.b, 0.9f);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * worst01, r.height), white);
                GUI.color = new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 1f);
                GUI.DrawTexture(new Rect(r.x, r.y + r.height * 0.25f, r.width * avg01, r.height * 0.5f), white);

                float bx = r.x + r.width * (budget / scale);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(bx - 1f, r.y - 3f, 2f, r.height + 6f), white);
            }
            GUI.color = old;
        }

        // FPS "heartbeat" graph. The top of the scale adapts to the maximum; red vertical lines are hitches.
        void DrawFpsGraph(Rect r, float budget)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(r, white);
            GUI.color = old;

            if (lineMaterial == null)
            {
                lineMaterial = DebugMaterials.CreateColored(zWrite: false, zTest: UnityEngine.Rendering.CompareFunction.Always);
            }

            float target = 1000f / budget;
            int n = Mathf.Min(count, GraphFrames);

            // Auto scale: the top is a bit above the window maximum, in steps of 30 FPS
            float maxFps = target;
            for (int i = 0; i < n; i++)
            {
                int idx = (head - n + i + HistorySize) % HistorySize;
                if (frameMs[idx] > 0f) maxFps = Mathf.Max(maxFps, 1000f / frameMs[idx]);
            }
            float wantTop = Mathf.Max(target * 1.25f, Mathf.Ceil(maxFps * 1.1f / 30f) * 30f);
            graphTop = wantTop > graphTop ? wantTop : Mathf.Lerp(graphTop, wantTop, 0.05f);   // goes down smoothly
            float top = graphTop;

            lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
            GL.Begin(GL.LINES);

            GLLine(r.x, Y(r, target, top), r.xMax, Y(r, target, top), new Color(1f, 1f, 1f, 0.35f));
            GLLine(r.x, Y(r, target * 0.5f, top), r.xMax, Y(r, target * 0.5f, top), new Color(1f, 0.4f, 0.3f, 0.35f));

            float step = r.width / (GraphFrames - 1);
            float prevX = 0f, prevY = 0f;
            for (int i = 0; i < n; i++)
            {
                int idx = (head - n + i + HistorySize) % HistorySize;
                float fps = frameMs[idx] > 0f ? 1000f / frameMs[idx] : 0f;
                float x = r.x + (GraphFrames - n + i) * step;
                float y = Y(r, fps, top);
                if (frameHitch[idx]) GLLine(x, r.y, x, r.yMax, new Color(1f, 0.3f, 0.25f, 0.55f));
                if (i > 0)
                {
                    Color c = fps >= target * 0.9f ? new Color(0.45f, 0.95f, 0.5f)
                            : fps >= target * 0.6f ? new Color(1f, 0.85f, 0.3f)
                            : new Color(1f, 0.4f, 0.35f);
                    GLLine(prevX, prevY, x, y, c);
                    GLLine(prevX, prevY + 1f, x, y + 1f, c);
                }
                prevX = x; prevY = y;
            }

            GL.End();
            GL.PopMatrix();
        }

        void DrawGraphLabels(Rect r, float budget)
        {
            float target = 1000f / budget, top = graphTop;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleRight };
                labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            }
            GUI.Label(new Rect(r.x - 38f, r.y - 2f, 34f, 18f), top.ToString("F0"), labelStyle);
            GUI.Label(new Rect(r.x - 38f, Y(r, target, top) - 9f, 34f, 18f), target.ToString("F0"), labelStyle);
            GUI.Label(new Rect(r.x - 38f, Y(r, target * 0.5f, top) - 9f, 34f, 18f), (target * 0.5f).ToString("F0"), labelStyle);
            GUI.Label(new Rect(r.x - 38f, r.yMax - 12f, 34f, 18f), "0", labelStyle);
        }

        static float Y(Rect r, float fps, float top) => r.yMax - Mathf.Clamp01(fps / top) * r.height;

        static void GLLine(float x1, float y1, float x2, float y2, Color c)
        {
            GL.Color(c);
            GL.Vertex3(x1, y1, 0f);
            GL.Vertex3(x2, y2, 0f);
        }
    }
}
