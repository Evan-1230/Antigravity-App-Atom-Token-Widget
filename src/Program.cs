using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AntigravityAtomTokenWidget
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch
            {
                try
                {
                    NativeMethods.SetProcessDPIAware();
                }
                catch
                {
                }
            }

            if (args != null && args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                string reportPath = (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
                    ? args[1]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self_test_report.txt");
                return RunSelfTest(reportPath);
            }

            if (args != null && args.Length > 0 && string.Equals(args[0], "--render-preview", StringComparison.OrdinalIgnoreCase))
            {
                string outDir = (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
                    ? args[1]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "previews");
                return RunRenderPreview(outDir);
            }

            if (args != null && args.Length > 0 && string.Equals(args[0], "--render-readme-assets", StringComparison.OrdinalIgnoreCase))
            {
                string outDir = (args.Length > 1 && !string.IsNullOrEmpty(args[1]))
                    ? args[1]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "readme_assets_raw");
                return RunRenderReadmeAssets(outDir);
            }

            bool createdNew;
            using (Mutex singleInstanceMutex = new Mutex(true, @"Local\AntigravityAtomTokenWidget_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string exePath = Assembly.GetExecutingAssembly().Location;

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                WidgetSettings settings = WidgetSettings.Load(baseDir);
                ThemeWatcher themeWatcher = new ThemeWatcher();
                UiaModelProbe uiaProbe = new UiaModelProbe();
                AntigravityDataReader dataReader = new AntigravityDataReader(baseDir);

                WidgetWindow widget = new WidgetWindow(settings);
                widget.Show();
                if (widget.Handle != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(widget.Handle, NativeMethods.SW_HIDE);
                }

                WindowTracker tracker = new WindowTracker(widget, settings, uiaProbe);
                TrayController tray = new TrayController(settings, baseDir, exePath);
                SettingsWindow activeSettingsWindow = null;

                DataSnapshot latestSnap = dataReader.ReadSnapshot(settings, uiaProbe);
                widget.ApplyData(latestSnap, themeWatcher.IsLightTheme, true);
                tray.RefreshTextsAndChecks(latestSnap.ActiveModel.Element, latestSnap.Breakdown.TotalTokenCount);

                Action refreshAll = delegate
                {
                    themeWatcher.Refresh(false);
                    latestSnap = dataReader.ReadSnapshot(settings, uiaProbe);
                    widget.ApplyData(latestSnap, themeWatcher.IsLightTheme, false);
                    tray.RefreshTextsAndChecks(latestSnap.ActiveModel.Element, latestSnap.Breakdown.TotalTokenCount);
                    tracker.SyncToTarget(latestSnap.DetectedEmptyComposerDip);
                    if (activeSettingsWindow != null)
                    {
                        activeSettingsWindow.UpdateThemeAndModel(themeWatcher.IsLightTheme, latestSnap.ActiveModel);
                    }
                };

                widget.SettingsChangedByUser += delegate
                {
                    settings.Save(baseDir);
                    refreshAll();
                };

                widget.SettingsWindowRequested += delegate(Point screenPx)
                {
                    if (activeSettingsWindow != null)
                    {
                        activeSettingsWindow.SafeClose();
                        activeSettingsWindow = null;
                        return;
                    }
                    tracker.SyncToTarget(latestSnap != null ? latestSnap.DetectedEmptyComposerDip : null);
                    SettingsWindow swin = new SettingsWindow(settings, baseDir, exePath, themeWatcher.IsLightTheme, latestSnap.ActiveModel);
                    swin.Closed += delegate
                    {
                        if (activeSettingsWindow == swin)
                        {
                            activeSettingsWindow = null;
                        }
                    };
                    swin.SettingsChanged += delegate
                    {
                        settings.Save(baseDir);
                        refreshAll();
                    };
                    swin.ScaleLiveChanged += delegate
                    {
                        tracker.SyncToTarget(latestSnap != null ? latestSnap.DetectedEmptyComposerDip : null);
                        if (latestSnap != null && latestSnap.ActiveModel != null && latestSnap.Breakdown != null)
                        {
                            tray.RefreshTextsAndChecks(latestSnap.ActiveModel.Element, latestSnap.Breakdown.TotalTokenCount);
                        }
                    };
                    swin.ResetPositionRequested += delegate
                    {
                        widget.RepositionNow();
                        tracker.ForceRepositionFromWidget();
                    };
                    swin.ExitRequested += delegate
                    {
                        app.Shutdown();
                    };
                    activeSettingsWindow = swin;
                    swin.PositionWithCollision(
                        widget.GetVisibleWidgetScreenRectDip(),
                        tracker.CurrentAppBoundsDip,
                        tracker.CurrentWorkAreaDip,
                        tracker.IsAppFullscreenOrMaximized,
                        tracker.CurrentDpiScale);
                    swin.Show();
                    swin.Activate();
                };

                tracker.TargetBoundsUpdated += delegate
                {
                    if (activeSettingsWindow != null)
                    {
                        activeSettingsWindow.UpdateBoundsClamp(
                            tracker.CurrentAppBoundsDip,
                            tracker.CurrentWorkAreaDip,
                            tracker.IsAppFullscreenOrMaximized,
                            tracker.CurrentDpiScale);
                    }
                };

                widget.PositionNeedsSync += delegate
                {
                    tracker.ForceRepositionFromWidget();
                };

                tray.SettingsUpdated += delegate
                {
                    refreshAll();
                };

                tray.ToggleVisibilityRequested += delegate
                {
                    tracker.SetUserHidden(settings.IsWidgetHidden);
                    refreshAll();
                };

                Stopwatch sw = Stopwatch.StartNew();
                double prevSec = 0.0;

                DispatcherTimer renderTimer = new DispatcherTimer(DispatcherPriority.Render);
                renderTimer.Interval = TimeSpan.FromMilliseconds(16);
                renderTimer.Tick += delegate
                {
                    double nowSec = sw.Elapsed.TotalSeconds;
                    double dt = nowSec - prevSec;
                    prevSec = nowSec;

                    if (tracker.IsWidgetVisible && !settings.IsWidgetHidden)
                    {
                        widget.StepFrame(dt);
                    }
                };

                DispatcherTimer trackTimer = new DispatcherTimer(DispatcherPriority.Normal);
                trackTimer.Interval = TimeSpan.FromMilliseconds(33);
                trackTimer.Tick += delegate
                {
                    tracker.SyncToTarget(latestSnap != null ? latestSnap.DetectedEmptyComposerDip : null);
                };

                DispatcherTimer dataTimer = new DispatcherTimer(DispatcherPriority.Background);
                dataTimer.Interval = TimeSpan.FromMilliseconds(850);
                dataTimer.Tick += delegate
                {
                    themeWatcher.Refresh(false);
                    latestSnap = dataReader.ReadSnapshot(settings, uiaProbe);
                    widget.ApplyData(latestSnap, themeWatcher.IsLightTheme, false);
                    tray.RefreshTextsAndChecks(latestSnap.ActiveModel.Element, latestSnap.Breakdown.TotalTokenCount);
                    if (activeSettingsWindow != null)
                    {
                        activeSettingsWindow.UpdateThemeAndModel(themeWatcher.IsLightTheme, latestSnap.ActiveModel);
                    }
                };

                app.Exit += delegate
                {
                    renderTimer.Stop();
                    trackTimer.Stop();
                    dataTimer.Stop();
                    if (activeSettingsWindow != null)
                    {
                        activeSettingsWindow.SafeClose();
                    }
                    uiaProbe.Dispose();
                    tray.Dispose();
                };

                tracker.SyncToTarget(latestSnap.DetectedEmptyComposerDip);
                renderTimer.Start();
                trackTimer.Start();
                dataTimer.Start();

                app.Run();
                GC.KeepAlive(singleInstanceMutex);
                return 0;
            }
        }

        private static int RunSelfTest(string reportPath)
        {
            StringBuilder sb = new StringBuilder();
            bool passed = true;

            try
            {
                // 1. Model Classification Tests (all thinking levels + Orchestrator + Non-Gemini-4)
                sb.AppendLine("=== 1. Model Classification ===");
                passed &= AssertModel(sb, "Gemini 4 Hydrogen (Low)", "", "", 0, AtomElement.Hydrogen, false);
                passed &= AssertModel(sb, "Gemini 4 Hydrogen (High)", "", "", 0, AtomElement.Hydrogen, false);
                passed &= AssertModel(sb, "Gemini 4 Hydrogen (XHigh)", "gemini-4-hydrogen", "MODEL_PLACEHOLDER_M528", 1528, AtomElement.Hydrogen, false);
                passed &= AssertModel(sb, "Gemini 4 Carbon (Low)", "", "", 0, AtomElement.Carbon, false);
                passed &= AssertModel(sb, "Gemini 4 Carbon (Medium)", "", "", 0, AtomElement.Carbon, false);
                passed &= AssertModel(sb, "Gemini 4 Neon (Minimal)", "", "", 0, AtomElement.Neon, false);
                passed &= AssertModel(sb, "Gemini 4 Neon (Low)", "", "", 0, AtomElement.Neon, false);
                passed &= AssertModel(sb, "Gemini 4 Neon (XHigh)", "", "", 0, AtomElement.Neon, false);
                passed &= AssertModel(sb, "Gemini 4 Argon (Medium)", "", "", 0, AtomElement.Argon, false);
                passed &= AssertModel(sb, "Gemini 4 Argon (High)", "", "", 0, AtomElement.Argon, false);
                passed &= AssertModel(sb, "Orchestrator (Neon)", "gemini-pro-orchestrator-neon", "MODEL_PLACEHOLDER_M537", 1537, AtomElement.Neon, true);
                passed &= AssertModel(sb, "Orchestrator (Argon)", "gemini-pro-orchestrator-g1-pro", "MODEL_PLACEHOLDER_M668", 1668, AtomElement.Argon, true);
                passed &= AssertModel(sb, "Orchestrator (Carbon)", "", "", 0, AtomElement.Carbon, true);
                passed &= AssertModel(sb, "Gemini 4 Hydrogen (Orchestrator)", "gemini-flash-worker-agy", "MODEL_PLACEHOLDER_M669", 1669, AtomElement.Hydrogen, true);
                passed &= AssertModel(sb, "Orchestrator (Claude)", "", "", 0, AtomElement.Unsupported, true);
                passed &= AssertModel(sb, "Claude Opus 5.5 (High)", "claude-opus-5-5-high", "MODEL_PLACEHOLDER_M402", 1402, AtomElement.Unsupported, false);
                passed &= AssertModel(sb, "Claude Sonnet 4.6 (Low)", "", "", 0, AtomElement.Unsupported, false);
                passed &= AssertModel(sb, "Gemini 3.8 Flash (Low)", "", "", 0, AtomElement.Unsupported, false);
                passed &= AssertModel(sb, "Gemini 3.8 Flash (High)", "gemini-3.8-flash-n", "MODEL_PLACEHOLDER_M318", 1318, AtomElement.Unsupported, false);
                passed &= AssertModel(sb, "OpenAI GPT-OSS 120B (Medium)", "gpt-oss-120b-medium", "MODEL_OPENAI_GPT_OSS_120B_MEDIUM", 0, AtomElement.Unsupported, false);
                passed &= AssertModel(sb, "Gemini 4 Neon Low", "", "", 0, AtomElement.Neon, false);
                passed &= AssertModel(sb, "Gemini 4 Hydrogen Extra High", "", "", 0, AtomElement.Hydrogen, false);
                passed &= AssertModel(sb, "Gemini 4 Helium (Extra High)", "", "", 0, AtomElement.Unsupported, false);
                passed &= AssertModel(sb, "Orchestrator (Helium)", "", "", 0, AtomElement.Unsupported, true);

                string dedupClaude = ModelNameNormalizer.CleanAndDeduplicate("Claude Sonnet 5.5 Medium Claude Sonnet 5.");
                bool dedupClaudeOk = string.Equals(dedupClaude, "Claude Sonnet 5.5 (Medium)", StringComparison.Ordinal);

                ModelClassifyResult ehRes = ModelClassifier.Classify("Gemini 4 Hydrogen Extra High", "", "", 0);
                bool ehPreservedOk = (ehRes.Element == AtomElement.Hydrogen) && string.Equals(ehRes.NormalizedDisplayName, "Gemini 4 Hydrogen (Extra High)", StringComparison.Ordinal);

                ModelClassifyResult futRes = ModelClassifier.Classify("Gemini 4 Helium (Extra High)", "", "", 0);
                bool futPreservedOk = (futRes.Element == AtomElement.Unsupported) && string.Equals(futRes.NormalizedDisplayName, "Gemini 4 Helium (Extra High)", StringComparison.Ordinal);

                string multiSpanOrch = UiaModelProbe.SelectBestModelTextFromParts("Orchestrator", new string[] { "Orchestrator", "(Neon)" });
                ModelClassifyResult multiSpanRes = ModelClassifier.Classify(multiSpanOrch, "", "", 0);
                bool multiSpanOk = (multiSpanRes.Element == AtomElement.Neon) && multiSpanRes.IsOrchestrator;

                string multiSpanTier = UiaModelProbe.SelectBestModelTextFromParts("Gemini 4 Neon", new string[] { "Gemini 4 Neon", "Extra High" });
                bool tierSpanOk = string.Equals(multiSpanTier, "Gemini 4 Neon (Extra High)", StringComparison.Ordinal);

                string multiSpanClaudeDup = UiaModelProbe.SelectBestModelTextFromParts("Claude Sonnet 5.5 Medium", new string[] { "Claude Sonnet 5.5", "Medium" });
                bool claudeNoDupOk = string.Equals(multiSpanClaudeDup, "Claude Sonnet 5.5 (Medium)", StringComparison.Ordinal);

                string genericFallback = UiaModelProbe.SelectBestModelTextFromParts("Project Orion (Ultra)", new string[] { "Project Orion", "(Ultra)" }, "model-selector-btn", "Select model");
                bool genericFallbackOk = string.Equals(genericFallback, "Project Orion (Ultra)", StringComparison.Ordinal);

                bool poisonRejectOk = !AntigravityDataReader.IsPlausibleLearnedMapping("MODEL_OPENAI_GPT_OSS_120B_MEDIUM", "Gemini 4 Argon")
                    && AntigravityDataReader.IsPlausibleLearnedMapping("MODEL_PLACEHOLDER_M537", "Orchestrator (Neon)");

                bool extOk = dedupClaudeOk && ehPreservedOk && futPreservedOk && multiSpanOk && tierSpanOk && claudeNoDupOk && genericFallbackOk && poisonRejectOk;
                sb.AppendLine(string.Format("Dedup/ExtraHigh/Future/UIA: dedup={0} ('{1}'), eh={2}, fut={3}, orch={4} ('{5}'), tier={6} ('{7}'), claudeNoDup={8} ('{9}'), fallback={10} ('{11}'), poison={12} -> {13}",
                    dedupClaudeOk, dedupClaude, ehPreservedOk, futPreservedOk, multiSpanOk, multiSpanOrch, tierSpanOk, multiSpanTier, claudeNoDupOk, multiSpanClaudeDup, genericFallbackOk, genericFallback, poisonRejectOk, extOk ? "PASS" : "FAIL"));
                passed &= extOk;

                // 2. AI Studio Periodic Table Hex Palette Verification
                sb.AppendLine("\n=== 2. AI Studio Periodic Table Palette ===");
                ElementVisualInfo hInfo = ElementVisualCatalog.GetInfo(AtomElement.Hydrogen);
                ElementVisualInfo cInfo = ElementVisualCatalog.GetInfo(AtomElement.Carbon);
                ElementVisualInfo neInfo = ElementVisualCatalog.GetInfo(AtomElement.Neon);
                ElementVisualInfo arInfo = ElementVisualCatalog.GetInfo(AtomElement.Argon);
                bool paletteOk = (hInfo.LightTileBg == Color.FromRgb(220, 224, 236))
                    && (cInfo.LightTileBg == Color.FromRgb(238, 221, 253))
                    && (neInfo.LightTileBg == Color.FromRgb(226, 105, 75))
                    && (arInfo.LightTileBg == Color.FromRgb(214, 235, 220))
                    && (hInfo.DarkTileBg == Color.FromRgb(59, 61, 66))
                    && (cInfo.DarkTileBg == Color.FromRgb(59, 53, 68))
                    && (neInfo.DarkTileBg == Color.FromRgb(183, 82, 63))
                    && (arInfo.DarkTileBg == Color.FromRgb(41, 66, 52));
                sb.AppendLine("Periodic Table H/C/Ne/Ar Light & Dark colors exact match -> " + (paletteOk ? "PASS" : "FAIL"));
                passed &= paletteOk;

                // 3. Switch-Back & Static Animation Verification on WidgetWindow
                sb.AppendLine("\n=== 3. Switch-Back & Static Animation State ===");
                WidgetSettings wsTest = new WidgetSettings();
                wsTest.Language = UiLanguage.ZhTw;
                wsTest.EnableAnimation = true;
                WidgetWindow wTest = new WidgetWindow(wsTest);

                DataSnapshot s1 = new DataSnapshot();
                s1.ActiveModel.Element = AtomElement.Neon;
                s1.ActiveModel.DisplayName = "Gemini 4 Neon (Low)";
                s1.Breakdown.CachedContentTokens = 9164275;
                wTest.ApplyData(s1, false, false);
                bool step1Ok = (wTest.DebugElementPillText == "Ne") && (wTest.DebugCompactTotalText == "9.16M");

                DataSnapshot s2 = new DataSnapshot();
                s2.ActiveModel.Element = AtomElement.Unsupported;
                s2.ActiveModel.IsOrchestrator = true;
                s2.ActiveModel.DisplayName = "Orchestrator (Claude)";
                s2.Breakdown.CachedContentTokens = 9164275;
                wTest.ApplyData(s2, false, false);
                bool step2Ok = (wTest.DebugElementPillText == "動畫僅支援\u27264")
                    && wTest.DebugElementPillHasGeminiIcon
                    && wTest.DebugElementPillIconMatchesTextColor
                    && (wTest.DebugDetailElementMeta.IndexOf("非 Gemini 4 模型或尚未收錄至 3D 模型庫", StringComparison.Ordinal) >= 0)
                    && (wTest.DebugCompactTotalText == "9.16M")
                    && wTest.DebugOrchestratorVisible;

                DataSnapshot s3 = new DataSnapshot();
                s3.ActiveModel.Element = AtomElement.Neon;
                s3.ActiveModel.DisplayName = "Gemini 4 Neon (Low)";
                s3.Breakdown.CachedContentTokens = 9164275;
                wTest.ApplyData(s3, false, false);
                bool step3Ok = (wTest.DebugElementPillText == "Ne")
                    && !wTest.DebugElementPillHasGeminiIcon
                    && (wTest.DebugCompactTotalText == "9.16M")
                    && !wTest.DebugOrchestratorVisible;

                wsTest.Language = UiLanguage.En;
                wTest.ApplyData(s2, false, false);
                bool enPillOk = (wTest.DebugElementPillText == "\u27264 Animation Only")
                    && wTest.DebugElementPillHasGeminiIcon
                    && wTest.DebugElementPillIconMatchesTextColor;
                wsTest.Language = UiLanguage.ZhTw;
                wTest.ApplyData(s3, false, false);

                wsTest.EnableAnimation = false;
                wTest.ApplyData(s3, false, false);
                bool staticOk = !wTest.AtomControl.AnimationEnabled;

                bool switchOk = step1Ok && step2Ok && step3Ok && enPillOk && staticOk;
                sb.AppendLine(string.Format("G4->NonG4->G4 + Static: s1={0}, s2={1}, s3={2}, enPill={3}, static={4} -> {5}",
                    step1Ok, step2Ok, step3Ok, enPillOk, staticOk, switchOk ? "PASS" : "FAIL"));
                passed &= switchOk;

                // 4. Responsive Sizing + 5-Stop Scale (80%..160%) & Off-Preset Tray Checkmark Tests
                sb.AppendLine("\n=== 4. Responsive Sizing & 5-Stop Scale (80%..160%) ===");
                double fsSize = WindowTracker.ComputeResponsiveAtomDipSize(1080.0, 88.0, 1.0);
                double winSize = WindowTracker.ComputeResponsiveAtomDipSize(640.0, 88.0, 1.0);
                double sz80 = WindowTracker.ComputeResponsiveAtomDipSize(1080.0, 88.0, 0.80);
                double sz100 = WindowTracker.ComputeResponsiveAtomDipSize(1080.0, 88.0, 1.00);
                double sz120 = WindowTracker.ComputeResponsiveAtomDipSize(1080.0, 88.0, 1.20);
                double sz140 = WindowTracker.ComputeResponsiveAtomDipSize(1080.0, 88.0, 1.40);
                double sz160 = WindowTracker.ComputeResponsiveAtomDipSize(1080.0, 88.0, 1.60);
                bool fiveStopsMonotonic = (sz80 < sz100) && (sz100 < sz120) && (sz120 < sz140) && (sz140 < sz160) && (sz160 <= 148.0);
                bool clampScaleOk = (WidgetSettings.ClampScale(0.50) == 0.80)
                    && (WidgetSettings.ClampScale(2.00) == 1.60)
                    && (WidgetSettings.ClampScale(1.134) == 1.13);
                bool presetIdxOk = (WidgetSettings.TryGetExactScalePresetIndex(0.80) == 0)
                    && (WidgetSettings.TryGetExactScalePresetIndex(1.00) == 1)
                    && (WidgetSettings.TryGetExactScalePresetIndex(1.20) == 2)
                    && (WidgetSettings.TryGetExactScalePresetIndex(1.40) == 3)
                    && (WidgetSettings.TryGetExactScalePresetIndex(1.60) == 4)
                    && (WidgetSettings.TryGetExactScalePresetIndex(0.93) == -1)
                    && (WidgetSettings.TryGetExactScalePresetIndex(1.13) == -1)
                    && (WidgetSettings.TryGetExactScalePresetIndex(1.35) == -1)
                    && (WidgetSettings.TryGetExactScalePresetIndex(1.52) == -1);

                bool trayScaleCheckOk = true;
                WidgetSettings trayScaleWs = new WidgetSettings();
                using (TrayController tcTest = new TrayController(trayScaleWs, AppDomain.CurrentDomain.BaseDirectory, Assembly.GetExecutingAssembly().Location))
                {
                    for (int si = 0; si < WidgetSettings.ScalePresets.Length; si++)
                    {
                        trayScaleWs.ScaleMultiplier = WidgetSettings.ScalePresets[si];
                        tcTest.RefreshTextsAndChecks(AtomElement.Neon, 1000);
                        if (tcTest.DebugCheckedScalePresetIndex != si) { trayScaleCheckOk = false; }
                    }
                    trayScaleWs.ScaleMultiplier = 1.13;
                    tcTest.RefreshTextsAndChecks(AtomElement.Neon, 1000);
                    if (tcTest.DebugCheckedScalePresetIndex != -1) { trayScaleCheckOk = false; }
                }

                bool sizeOk = (fsSize < 88.0) && (winSize < fsSize) && (winSize >= 48.0) && fiveStopsMonotonic && clampScaleOk && presetIdxOk && trayScaleCheckOk;
                sb.AppendLine(string.Format("Fullscreen(1080, composer=88): 80%={0:F1}, 100%={1:F1}, 120%={2:F1}, 140%={3:F1}, 160%={4:F1} | Windowed={5:F1} | clamp={6} presetIdx={7} trayOffCheck={8} -> {9}",
                    sz80, sz100, sz120, sz140, sz160, winSize, clampScaleOk, presetIdxOk, trayScaleCheckOk, sizeOk ? "PASS" : "FAIL"));
                passed &= sizeOk;

                // 5. Live DB Parity Check
                sb.AppendLine("\n=== 5. Live SQLite + Protobuf Parity ===");
                AntigravityDataReader reader = new AntigravityDataReader(AppDomain.CurrentDomain.BaseDirectory);
                string[] testCids = new string[]
                {
                    "11b0e408-2cd3-4c60-8ccf-bd5fdfad8517",
                    "059ff45d-1def-4382-a3a5-e61a3698b4c2"
                };
                for (int i = 0; i < testCids.Length; i++)
                {
                    CachedConversationData conv = reader.GetOrLoadConversation(testCids[i]);
                    if (conv == null || conv.Steps.Count == 0)
                    {
                        sb.AppendLine(string.Format("Conv {0}: NOT FOUND OR EMPTY -> FAIL", testCids[i]));
                        passed = false;
                        continue;
                    }
                    TokenBreakdown tb = new TokenBreakdown();
                    for (int j = 0; j < conv.Steps.Count; j++)
                    {
                        StepUsageEntry st = conv.Steps[j];
                        tb.AddUsage(st.UncachedPromptTokens, st.CachedContentTokens, st.CandidatesTokens, st.ThoughtsTokens, st.CacheCreationTokens);
                    }
                    bool sumOk = (tb.PromptTokensUncached + tb.CachedContentTokens + tb.CandidatesTokens + tb.ThoughtsTokens + tb.CacheCreationTokens) == tb.TotalTokenCount;
                    sb.AppendLine(string.Format(
                        "Conv {0}: Calls={1}, Total={2}, LatestModel={3} -> {4}",
                        testCids[i], tb.CallCount, tb.TotalTokenCount, conv.LatestDisplayName, sumOk ? "PASS" : "FAIL"));
                    passed &= sumOk && tb.CallCount > 0 && tb.TotalTokenCount > 0;
                }

                // 6. 3D Scene Build Smoke Test (including Dark & Light Gold/Silver/Copper Orchestrator + Blue Current)
                sb.AppendLine("\n=== 6. 3D Scene Build ===");
                Atom3DControl ctrl = new Atom3DControl();
                AtomElement[] elems = new AtomElement[] { AtomElement.Hydrogen, AtomElement.Carbon, AtomElement.Neon, AtomElement.Argon, AtomElement.Unsupported };
                for (int i = 0; i < elems.Length; i++)
                {
                    ctrl.SetState(elems[i], i == 2, false, true, true);
                    ctrl.StepAnimation(0.016);
                    ctrl.SetState(elems[i], i == 3, true, false, false);
                }
                sb.AppendLine("All 4 Bohr models + Dark/Light Gold/Silver/Copper Orchestrator + Static pose -> PASS");

                // 7. Settings Collision Avoidance, Boundary Clamping & Relative Resize Tracking
                sb.AppendLine("\n=== 7. Collision Box & Boundary Clamping & Resize Tracking ===");
                Rect workArea = new Rect(0, 0, 1920, 1032); // 1080p minus 48px taskbar
                Rect fullApp = new Rect(0, 0, 1920, 1080);  // borderless fullscreen covering monitor
                Rect hitBR = new Rect(1780, 910, 120, 110);
                Point pFsBR = SettingsPlacementSolver.ComputePosition(360, 480, hitBR, fullApp, workArea, true);
                Rect winFsBR = new Rect(pFsBR.X, pFsBR.Y, 360, 480);
                Rect ovFsBR = Rect.Intersect(winFsBR, hitBR);
                bool fsBrNoOverlap = (ovFsBR.IsEmpty || ovFsBR.Width <= 0.1 || ovFsBR.Height <= 0.1);
                bool fsBrInBounds = (winFsBR.Left >= fullApp.Left && winFsBR.Right <= fullApp.Right && winFsBR.Top >= fullApp.Top && winFsBR.Bottom <= workArea.Bottom);

                Rect hitTL = new Rect(16, 16, 120, 110);
                Point pFsTL = SettingsPlacementSolver.ComputePosition(360, 480, hitTL, fullApp, workArea, true);
                Rect winFsTL = new Rect(pFsTL.X, pFsTL.Y, 360, 480);
                Rect ovFsTL = Rect.Intersect(winFsTL, hitTL);
                bool fsTlNoOverlap = (ovFsTL.IsEmpty || ovFsTL.Width <= 0.1 || ovFsTL.Height <= 0.1);
                bool fsTlInBounds = (winFsTL.Left >= fullApp.Left && winFsTL.Right <= fullApp.Right && winFsTL.Top >= fullApp.Top && winFsTL.Bottom <= workArea.Bottom);

                Point dragClampFs = SettingsPlacementSolver.ClampManualMove(1800, 900, 360, 480, new Rect(100, 80, 1400, 900), workArea, true);
                bool dragFsOk = (dragClampFs.X <= 1500 - 360 + 0.1) && (dragClampFs.Y <= 980 - 480 + 0.1) && (dragClampFs.X >= 100 - 0.1) && (dragClampFs.Y >= 80 - 0.1);

                Rect smallApp = new Rect(120, 100, 820, 560);
                Point dragClampWin = SettingsPlacementSolver.ClampManualMove(1850, 990, 360, 480, smallApp, workArea, false);
                bool dragWinOk = (dragClampWin.X <= workArea.Right - 360 + 0.1) && (dragClampWin.Y <= workArea.Bottom - 480 + 0.1);

                WidgetSettings resizeWs = new WidgetSettings();
                resizeWs.AnchorNormX = 0.955;
                resizeWs.AnchorNormY = 0.965;
                WidgetWindow resizeWin = new WidgetWindow(resizeWs);
                resizeWin.SetTargetGeometry(fullApp, workArea, 72.0, 1.0, true);
                Rect rFull = resizeWin.GetVisibleWidgetScreenRectDip();
                bool fsWidgetAboveTaskbar = (rFull.Bottom <= workArea.Bottom + 5.0) && (rFull.Right <= workArea.Right + 5.0);

                resizeWin.SetTargetGeometry(smallApp, workArea, 56.0, 1.0, false);
                Rect rSmall = resizeWin.GetVisibleWidgetScreenRectDip();
                bool resizeFollowed = (rSmall.Left >= smallApp.Left - 5.0) && (rSmall.Right <= smallApp.Right + 5.0)
                    && (rSmall.Top >= smallApp.Top - 5.0) && (rSmall.Bottom <= smallApp.Bottom + 5.0)
                    && (rSmall.Left < rFull.Left - 400.0) && (rSmall.Top < rFull.Top - 200.0);

                bool layoutSuiteOk = fsBrNoOverlap && fsBrInBounds && fsTlNoOverlap && fsTlInBounds && dragFsOk && dragWinOk && fsWidgetAboveTaskbar && resizeFollowed;
                sb.AppendLine(string.Format("fsBR=({0},{1}) fsTL=({2},{3}) dragFs={4} dragWin={5} widgetTaskbarSafe={6} resizeTrack={7} -> {8}",
                    fsBrNoOverlap, fsBrInBounds, fsTlNoOverlap, fsTlInBounds, dragFsOk, dragWinOk, fsWidgetAboveTaskbar, resizeFollowed, layoutSuiteOk ? "PASS" : "FAIL"));
                passed &= layoutSuiteOk;

                // 8. No Double Orchestrator, [Element Preview] Badge, & Long Name Wrapping Vertical Stretch
                sb.AppendLine("\n=== 8. Wrapping, Vertical Stretch, No Double Orchestrator & Preview Badge ===");
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string exePath = Assembly.GetExecutingAssembly().Location;

                ActiveModelState orchState = new ActiveModelState();
                orchState.Element = AtomElement.Neon;
                orchState.DetectedElement = AtomElement.Neon;
                orchState.IsOrchestrator = true;
                orchState.DisplayName = "Orchestrator (Neon)";
                WidgetSettings noOvWs = new WidgetSettings();
                noOvWs.Language = UiLanguage.ZhTw;
                WidgetWindow wOrch = new WidgetWindow(noOvWs);
                DataSnapshot orchSnap = new DataSnapshot();
                orchSnap.ActiveModel = orchState.Clone();
                wOrch.ApplyData(orchSnap, false, true);
                wOrch.SetHoverExpandedImmediate(true);
                SettingsWindow sOrch = new SettingsWindow(noOvWs, baseDir, exePath, false, orchState);
                Border sOrchCard = sOrch.BuildPreviewCard();
                sOrchCard.Width = 344;
                sOrchCard.Measure(new Size(344, 2000));
                bool noDoubleOrchOk = (CountOccurrences(sOrch.DebugHeaderSubtitleText, "Orchestrator") == 1)
                    && (CountOccurrences(wOrch.DebugDetailModelTitle + " | " + wOrch.DebugDetailElementMeta, "Orchestrator") == 1);

                // Preview mode with long model name
                WidgetSettings prevWs = new WidgetSettings();
                prevWs.Language = UiLanguage.ZhTw;
                prevWs.ManualElementOverride = AtomElement.Carbon;

                ActiveModelState shortPreviewState = new ActiveModelState();
                shortPreviewState.Element = AtomElement.Carbon;
                shortPreviewState.DetectedElement = AtomElement.Neon;
                shortPreviewState.PreviewOverrideElement = AtomElement.Carbon;
                shortPreviewState.DisplayName = "Gemini 4 Neon (Extra High)";

                ActiveModelState longPreviewState = new ActiveModelState();
                longPreviewState.Element = AtomElement.Carbon;
                longPreviewState.DetectedElement = AtomElement.Neon;
                longPreviewState.PreviewOverrideElement = AtomElement.Carbon;
                longPreviewState.DisplayName = "Gemini 4 Neon Experimental Preview Build 2026.11 Ultra-Long Variant (Extra High)";

                WidgetWindow wShort = new WidgetWindow(prevWs);
                DataSnapshot shortSnap = new DataSnapshot();
                shortSnap.ActiveModel = shortPreviewState.Clone();
                wShort.ApplyData(shortSnap, false, true);
                wShort.SetHoverExpandedImmediate(true);

                WidgetWindow wLong = new WidgetWindow(prevWs);
                DataSnapshot longSnap = new DataSnapshot();
                longSnap.ActiveModel = longPreviewState.Clone();
                wLong.ApplyData(longSnap, false, true);
                wLong.SetHoverExpandedImmediate(true);

                bool widgetPreviewOk = string.Equals(wShort.DebugDetailModelTitle, "Gemini 4 Neon (Extra High)", StringComparison.Ordinal)
                    && string.Equals(wShort.DebugDetailPreviewBadge, "[Carbon Preview]", StringComparison.Ordinal)
                    && wLong.DebugExpandedBubbleHeight > wShort.DebugExpandedBubbleHeight + 8.0;

                SettingsWindow sShort = new SettingsWindow(prevWs, baseDir, exePath, false, shortPreviewState);
                Border sShortCard = sShort.BuildPreviewCard();
                sShortCard.Width = 344;
                sShortCard.Measure(new Size(344, 2000));

                SettingsWindow sLong = new SettingsWindow(prevWs, baseDir, exePath, false, longPreviewState);
                Border sLongCard = sLong.BuildPreviewCard();
                sLongCard.Width = 344;
                sLongCard.Measure(new Size(344, 2000));

                bool settingsPreviewOk = (sShort.DebugHeaderSubtitleText.IndexOf("Gemini 4 Neon (Extra High)", StringComparison.Ordinal) >= 0)
                    && string.Equals(sShort.DebugPreviewBadgeText, "[Carbon Preview]", StringComparison.Ordinal)
                    && sLongCard.DesiredSize.Height > sShortCard.DesiredSize.Height + 6.0;

                bool wrapSuiteOk = noDoubleOrchOk && widgetPreviewOk && settingsPreviewOk;
                sb.AppendLine(string.Format("noDoubleOrch={0} widgetPrev={1} (hShort={2:F1}, hLong={3:F1}) settingsPrev={4} (sShort={5:F1}, sLong={6:F1}) -> {7}",
                    noDoubleOrchOk, widgetPreviewOk, wShort.DebugExpandedBubbleHeight, wLong.DebugExpandedBubbleHeight,
                    settingsPreviewOk, sShortCard.DesiredSize.Height, sLongCard.DesiredSize.Height, wrapSuiteOk ? "PASS" : "FAIL"));
                passed &= wrapSuiteOk;

                // 9. Horizontal Model Carousel Metrics (4 models flush vs 5+ models overflow scrollbar)
                sb.AppendLine("\n=== 9. Model Strip Horizontal Scrollbar Metrics ===");
                double e4, s4, tw4, tl4;
                SettingsWindow.ComputeTileStripMetrics(4, SettingsWindow.DefaultStripViewportWidth, 0.0, out e4, out s4, out tw4, out tl4);
                bool strip4Ok = (Math.Abs(e4 - SettingsWindow.DefaultStripViewportWidth) < 0.2)
                    && (s4 <= 0.1)
                    && (Math.Abs(tw4 - SettingsWindow.DefaultStripViewportWidth) < 0.2)
                    && (Math.Abs(tl4) < 0.1);

                double e5, s5, tw5Start, tl5Start;
                SettingsWindow.ComputeTileStripMetrics(5, SettingsWindow.DefaultStripViewportWidth, 0.0, out e5, out s5, out tw5Start, out tl5Start);
                double e5End, s5End, tw5End, tl5End;
                SettingsWindow.ComputeTileStripMetrics(5, SettingsWindow.DefaultStripViewportWidth, s5, out e5End, out s5End, out tw5End, out tl5End);
                bool strip5Ok = (Math.Abs(e5 - 391.0) < 0.5)
                    && (s5 > 70.0)
                    && (tw5Start < SettingsWindow.DefaultStripViewportWidth - 20.0)
                    && (Math.Abs(tl5Start) < 0.1)
                    && (Math.Abs((tl5End + tw5End) - SettingsWindow.DefaultStripViewportWidth) < 0.5);

                bool stripSuiteOk = strip4Ok && strip5Ok;
                sb.AppendLine(string.Format("strip4Ok={0} (s4={1:F1}, tw4={2:F1}) strip5Ok={3} (e5={4:F1}, s5={5:F1}, tw5={6:F1}, endRight={7:F1}) -> {8}",
                    strip4Ok, s4, tw4, strip5Ok, e5, s5, tw5Start, tl5End + tw5End, stripSuiteOk ? "PASS" : "FAIL"));
                passed &= stripSuiteOk;

                // 10. Clean Settings Labels, Typing-Dropout Sticky Cache & Hollow Dashed Non-G4 3D Mesh
                sb.AppendLine("\n=== 10. Clean Labels, Sticky Model Cache & Hollow Dashed Non-G4 3D ===");

                // 10a. Verify removed parenthetical suffixes in both ZhTw and En Settings cards
                WidgetSettings s10Zh = new WidgetSettings();
                s10Zh.Language = UiLanguage.ZhTw;
                SettingsWindow sw10Zh = new SettingsWindow(s10Zh, baseDir, exePath, false, orchState);
                Border c10Zh = sw10Zh.BuildPreviewCard();
                List<string> zhTexts = new List<string>();
                CollectVisualText(c10Zh, zhTexts);
                string zhJoined = string.Join(" | ", zhTexts.ToArray());

                WidgetSettings s10En = new WidgetSettings();
                s10En.Language = UiLanguage.En;
                SettingsWindow sw10En = new SettingsWindow(s10En, baseDir, exePath, false, orchState);
                Border c10En = sw10En.BuildPreviewCard();
                List<string> enTexts = new List<string>();
                CollectVisualText(c10En, enTexts);
                string enJoined = string.Join(" | ", enTexts.ToArray());

                bool labelsOk = zhJoined.Contains("3D 原子模型")
                    && zhJoined.Contains("自動跟隨 AI 模型")
                    && zhJoined.Contains("3D 動態旋轉特效")
                    && !zhJoined.Contains("元素週期表配色")
                    && !zhJoined.Contains("Orchestrator 自動適應")
                    && !zhJoined.Contains("關閉即靜態")
                    && enJoined.Contains("3D Bohr Model")
                    && enJoined.Contains("Auto-Follow AI Model")
                    && enJoined.Contains("3D Orbital Animation")
                    && !enJoined.Contains("AI Studio Palette")
                    && !enJoined.Contains("Orchestrator Auto")
                    && !enJoined.Contains("OFF = Static");

                // 10b. Regex boundary safety & Sticky cache across transient UIA dropouts while typing
                bool regexBoundaryOk = string.IsNullOrEmpty(UiaModelProbe.SelectBestModelTextFromParts("Auto1", new string[0]))
                    && string.IsNullOrEmpty(UiaModelProbe.SelectBestModelTextFromParts("Studio4", new string[0]))
                    && string.IsNullOrEmpty(UiaModelProbe.SelectBestModelTextFromParts("Info2", new string[0]))
                    && string.IsNullOrEmpty(UiaModelProbe.SelectBestModelTextFromParts("Video3", new string[0]))
                    && string.IsNullOrEmpty(UiaModelProbe.SelectBestModelTextFromParts("v1.0.o4", new string[0]))
                    && !string.IsNullOrEmpty(UiaModelProbe.SelectBestModelTextFromParts("OpenAI o3-mini", new string[0]))
                    && !string.IsNullOrEmpty(UiaModelProbe.SelectBestModelTextFromParts("Gemini 4 Carbon Extra High", new string[0]))
                    && !string.IsNullOrEmpty(UiaModelProbe.SelectBestModelTextFromParts("Orchestrator (Neon)", new string[0]));

                AntigravityDataReader stickyReader = new AntigravityDataReader(baseDir);
                DateTime t0 = new DateTime(2026, 11, 22, 3, 0, 0, DateTimeKind.Utc);
                bool outInv, outRescan;
                string outLearnE, outLearnD;

                ModelClassifyResult unknownRes999 = ModelClassifier.Classify("", "", "MODEL_PLACEHOLDER_M999", 0);
                ActiveModelState step1 = stickyReader.ResolveAutoDetectedModelCore(
                    "cid-typing", false, "MODEL_PLACEHOLDER_M999", t0, false, unknownRes999,
                    true, "Gemini 4 Carbon (Extra High)", t0.AddSeconds(1),
                    null, "", DateTime.MinValue,
                    out outInv, out outRescan, out outLearnE, out outLearnD);

                // Simulate typing: UIA temporarily drops out + non-model write bumps state.pbtxt mtime by 2s
                ActiveModelState step2 = stickyReader.ResolveAutoDetectedModelCore(
                    "cid-typing", false, "MODEL_PLACEHOLDER_M999", t0.AddSeconds(2), false, unknownRes999,
                    false, "", DateTime.MinValue,
                    null, "", DateTime.MinValue,
                    out outInv, out outRescan, out outLearnE, out outLearnD);

                // Simulate genuine switch to known Claude Opus 5.5 (MODEL_PLACEHOLDER_M401)
                ModelClassifyResult claudeRes = ModelClassifier.Classify("Claude Opus 5.5 (Medium)", "claude-opus-5-5-medium", "MODEL_PLACEHOLDER_M401", 1401);
                ActiveModelState step3 = stickyReader.ResolveAutoDetectedModelCore(
                    "cid-typing", false, "MODEL_PLACEHOLDER_M401", t0.AddSeconds(5), true, claudeRes,
                    false, "", DateTime.MinValue,
                    null, "", DateTime.MinValue,
                    out outInv, out outRescan, out outLearnE, out outLearnD);

                // Simulate genuine switch to known Orchestrator Neon (MODEL_PLACEHOLDER_M537)
                ModelClassifyResult orchRes = ModelClassifier.Classify("Orchestrator (Neon)", "gemini-pro-orchestrator-neon", "MODEL_PLACEHOLDER_M537", 1537);
                ActiveModelState step4 = stickyReader.ResolveAutoDetectedModelCore(
                    "cid-typing", false, "MODEL_PLACEHOLDER_M537", t0.AddSeconds(8), true, orchRes,
                    false, "", DateTime.MinValue,
                    null, "", DateTime.MinValue,
                    out outInv, out outRescan, out outLearnE, out outLearnD);

                bool stickyOk = (step1.Element == AtomElement.Carbon)
                    && (step2.Element == AtomElement.Carbon) && step2.DetectionSource.StartsWith("Sticky(", StringComparison.Ordinal)
                    && (step3.Element == AtomElement.Unsupported) && !step3.IsOrchestrator
                    && (step4.Element == AtomElement.Neon) && step4.IsOrchestrator;

                // 10c. Hollow Dashed Non-G4 3D mesh check in Dark & Light themes
                Atom3DControl dashedAtom = new Atom3DControl();
                dashedAtom.SetState(AtomElement.Unsupported, false, false, false, true);
                bool darkDashOk = (dashedAtom.DebugChildModelCount == 3) && (dashedAtom.DebugTotalVertexCount > 500) && (dashedAtom.Opacity >= 0.80);
                dashedAtom.SetState(AtomElement.Unsupported, false, true, false, false);
                bool lightDashOk = (dashedAtom.DebugChildModelCount == 3) && (dashedAtom.DebugTotalVertexCount > 500) && (dashedAtom.Opacity >= 0.80);
                bool dashMeshOk = darkDashOk && lightDashOk;

                bool sec10Ok = labelsOk && regexBoundaryOk && stickyOk && dashMeshOk;
                sb.AppendLine(string.Format("labels={0} regexBoundary={1} sticky={2} (s1={3}, s2={4}/{5}, s3={6}, s4={7}/Orch={8}) dashMesh={9} (cnt={10}, verts={11}, op={12:F2}) -> {13}",
                    labelsOk, regexBoundaryOk, stickyOk, step1.Element, step2.Element, step2.DetectionSource, step3.Element, step4.Element, step4.IsOrchestrator,
                    dashMeshOk, dashedAtom.DebugChildModelCount, dashedAtom.DebugTotalVertexCount, dashedAtom.Opacity,
                    sec10Ok ? "PASS" : "FAIL"));
                passed &= sec10Ok;

                // 11. Equivalent API Value Pricing, Multi-Model Switch/Orchestrator, Live DB & Toggle UI
                sb.AppendLine("\n=== 11. Equivalent API Value (USD Standard List Price) & Toggle ===");

                // 11a. Per-model standard USD list price checks
                bool priceUnitOk = true;
                priceUnitOk &= AssertModelPrice(sb, "Gemini 4 Hydrogen (1528)", "", "", 1528, 100000L, 1000000L, 50000L, 50000L, 0L, true, 0.975);
                priceUnitOk &= AssertModelPrice(sb, "Gemini 4 Hydrogen Worker (1669)", "gemini-flash-worker-agy", "", 1669, 100000L, 1000000L, 50000L, 50000L, 0L, true, 0.975);
                priceUnitOk &= AssertModelPrice(sb, "Gemini 4 Neon (1535)", "", "", 1535, 100000L, 1000000L, 50000L, 50000L, 0L, true, 1.95);
                priceUnitOk &= AssertModelPrice(sb, "Orchestrator Neon (1537)", "gemini-pro-orchestrator-neon", "", 1537, 100000L, 1000000L, 50000L, 50000L, 0L, true, 1.95);
                priceUnitOk &= AssertModelPrice(sb, "Gemini 4 Carbon", "gemini-4-carbon", "", 0, 100000L, 1000000L, 50000L, 50000L, 0L, true, 2.60);
                priceUnitOk &= AssertModelPrice(sb, "Gemini 4 Argon (1417)", "", "", 1417, 100000L, 1000000L, 50000L, 50000L, 0L, true, 2.60);
                priceUnitOk &= AssertModelPrice(sb, "Orchestrator Argon (1668)", "gemini-pro-orchestrator-g1-pro", "", 1668, 100000L, 1000000L, 50000L, 50000L, 0L, true, 2.60);
                priceUnitOk &= AssertModelPrice(sb, "Gemini 3.8 Flash (1318)", "", "", 1318, 100000L, 1000000L, 50000L, 50000L, 0L, true, 1.05);
                priceUnitOk &= AssertModelPrice(sb, "Gemini 2.5 Flash", "gemini-2.5-flash", "", 0, 100000L, 1000000L, 50000L, 50000L, 0L, true, 0.31);
                priceUnitOk &= AssertModelPrice(sb, "Gemini 3.1 Pro <=200K", "gemini-3.1-pro", "", 0, 100000L, 50000L, 50000L, 50000L, 0L, true, 1.41);
                priceUnitOk &= AssertModelPrice(sb, "Gemini 3.1 Pro >200K", "gemini-3.1-pro", "", 0, 100000L, 200000L, 50000L, 50000L, 0L, true, 2.28);
                priceUnitOk &= AssertModelPrice(sb, "Gemini 2.5 Pro <=200K", "gemini-2.5-pro", "", 0, 100000L, 50000L, 50000L, 50000L, 0L, true, 1.13125);
                priceUnitOk &= AssertModelPrice(sb, "Gemini 2.5 Pro >200K", "gemini-2.5-pro", "", 0, 100000L, 200000L, 50000L, 50000L, 0L, true, 1.80);
                priceUnitOk &= AssertModelPrice(sb, "Claude Opus 5.5 (1401)", "", "", 1401, 100000L, 1000000L, 50000L, 0L, 20000L, true, 1.70);
                priceUnitOk &= AssertModelPrice(sb, "Claude Sonnet 5.5", "claude-sonnet-5-5", "", 0, 100000L, 1000000L, 50000L, 0L, 20000L, true, 0.85);
                priceUnitOk &= AssertModelPrice(sb, "Claude Opus 4.6", "claude-opus-4-6", "", 0, 100000L, 1000000L, 50000L, 0L, 20000L, true, 2.375);
                priceUnitOk &= AssertModelPrice(sb, "Claude Sonnet 4.6", "claude-sonnet-4-6", "", 0, 100000L, 1000000L, 50000L, 0L, 20000L, true, 1.425);
                priceUnitOk &= AssertModelPrice(sb, "GPT-OSS 120B", "gpt-oss-120b-medium", "", 0, 100000L, 1000000L, 50000L, 50000L, 0L, true, 0.15);
                priceUnitOk &= AssertModelPrice(sb, "Unknown Future Model", "unknown-xyz-model", "", 99999, 100000L, 100000L, 10000L, 0L, 0L, false, 0.0);

                // 11b. Multi-model Orchestrator + mid-conversation switch summation ($0.5250 + $0.2400 + $0.8925 = $1.6575)
                TokenBreakdown mixTb = new TokenBreakdown();
                bool p1, p2, p3;
                double c1 = ApiPricingCatalog.CalculateStepCostUsd(50000L, 500000L, 10000L, 10000L, 0L, "", "", "", 1537, out p1);
                double c2 = ApiPricingCatalog.CalculateStepCostUsd(40000L, 400000L, 10000L, 10000L, 0L, "", "", "", 1669, out p2);
                double c3 = ApiPricingCatalog.CalculateStepCostUsd(20000L, 150000L, 30000L, 4125L, 20000L, "", "", "", 1401, out p3);
                mixTb.AddUsage(50000L, 500000L, 10000L, 10000L, 0L, c1, p1);
                mixTb.AddUsage(40000L, 400000L, 10000L, 10000L, 0L, c2, p2);
                mixTb.AddUsage(20000L, 150000L, 30000L, 4125L, 20000L, c3, p3);
                bool multiModelOk = p1 && p2 && p3
                    && Math.Abs(mixTb.EquivalentApiValueUsd - 1.6575) < 1e-6
                    && string.Equals(WidgetWindow.FormatUsdValue(mixTb.EquivalentApiValueUsd), "$1.66", StringComparison.Ordinal)
                    && mixTb.UnpricedCallCount == 0;

                // 11c. Live Antigravity DB parity across all local conversations
                AntigravityDataReader liveCostReader = new AntigravityDataReader(baseDir);
                WidgetSettings liveAllWs = new WidgetSettings();
                liveAllWs.Scope = TokenScope.AllTime;
                liveAllWs.IncludeSubagents = true;
                DataSnapshot liveAllSnap = liveCostReader.ReadSnapshot(liveAllWs, null);
                bool liveAllOk = (liveAllSnap.Breakdown.TotalTokenCount == 0)
                    || (liveAllSnap.Breakdown.EquivalentApiValueUsd > 0.0 && liveAllSnap.Breakdown.UnpricedCallCount == 0);

                string sampleCid = "059ff45d-1def-4382-a3a5-e61a3698b4c2";
                string sampleDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "conversations", sampleCid + ".db");
                bool liveConvOk = true;
                double liveConvUsd = 0.0;
                int liveConvCalls = 0;
                int liveConvUnpriced = 0;
                if (File.Exists(sampleDbPath))
                {
                    TokenBreakdown sampleTb = liveCostReader.ComputeConversationBreakdownForTest(sampleCid);
                    liveConvUsd = sampleTb.EquivalentApiValueUsd;
                    liveConvCalls = sampleTb.CallCount;
                    liveConvUnpriced = sampleTb.UnpricedCallCount;
                    liveConvOk = (sampleTb.CallCount == 0) || (sampleTb.EquivalentApiValueUsd > 0.0 && sampleTb.UnpricedCallCount == 0);
                }

                // 11d. WidgetSettings persistence round-trip for ShowEquivalentApiValue
                bool settingsDefaultOff = !(new WidgetSettings()).ShowEquivalentApiValue;
                bool settingsPersistOk = false;
                string tmpSettingsDir = Path.Combine(Path.GetTempPath(), "AtomWidgetApiTest_" + Guid.NewGuid().ToString("N"));
                try
                {
                    WidgetSettings tmpWs = new WidgetSettings();
                    tmpWs.ShowEquivalentApiValue = true;
                    tmpWs.Save(tmpSettingsDir);
                    WidgetSettings reloadedOn = WidgetSettings.Load(tmpSettingsDir);
                    tmpWs.ShowEquivalentApiValue = false;
                    tmpWs.Save(tmpSettingsDir);
                    WidgetSettings reloadedOff = WidgetSettings.Load(tmpSettingsDir);
                    settingsPersistOk = reloadedOn.ShowEquivalentApiValue && !reloadedOff.ShowEquivalentApiValue;
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tmpSettingsDir))
                        {
                            Directory.Delete(tmpSettingsDir, true);
                        }
                    }
                    catch
                    {
                    }
                }

                // 11e. WidgetWindow, SettingsWindow & TrayController UI toggle checks
                bool formatOk = string.Equals(WidgetWindow.FormatUsdValue(0.0), "$0.00", StringComparison.Ordinal)
                    && string.Equals(WidgetWindow.FormatUsdValue(0.43), "$0.43", StringComparison.Ordinal)
                    && string.Equals(WidgetWindow.FormatUsdValue(1234.564), "$1,234.56", StringComparison.Ordinal);

                WidgetSettings uiApiWs = new WidgetSettings();
                uiApiWs.Language = UiLanguage.ZhTw;
                uiApiWs.ShowEquivalentApiValue = false;
                DataSnapshot uiApiSnap = new DataSnapshot();
                uiApiSnap.HasTargetData = true;
                uiApiSnap.ActiveModel.Element = AtomElement.Argon;
                uiApiSnap.ActiveModel.IsOrchestrator = true;
                uiApiSnap.ActiveModel.DisplayName = "Orchestrator (Argon)";
                uiApiSnap.Breakdown = MakeSampleBreakdown(1250000L, 73900000L, 180000L, 450000L, 148200L, 62, 2);
                uiApiSnap.Breakdown.EquivalentApiValueUsd = 0.43;

                WidgetWindow wApi = new WidgetWindow(uiApiWs);
                wApi.ApplyData(uiApiSnap, false, true);
                wApi.SetHoverExpandedImmediate(true);
                bool uiOffOk = string.Equals(wApi.DebugCompactTotalText, "75.78M", StringComparison.Ordinal)
                    && !wApi.DebugCompactApiValueVisible
                    && string.Equals(wApi.DebugCompactApiValueText, "", StringComparison.Ordinal)
                    && string.Equals(wApi.DebugDetailTotalText, "75,780,000", StringComparison.Ordinal);

                uiApiWs.ShowEquivalentApiValue = true;
                wApi.ApplyData(uiApiSnap, false, true);
                bool uiOnOk = string.Equals(wApi.DebugCompactTotalText, "75.78M", StringComparison.Ordinal)
                    && wApi.DebugCompactApiValueVisible
                    && string.Equals(wApi.DebugCompactApiValueText, "$0.43", StringComparison.Ordinal)
                    && wApi.DebugCompactApiSepBold
                    && wApi.DebugCompactApiSepMatchesAccent
                    && wApi.DebugCompactApiValueMatchesPrimaryText
                    && string.Equals(wApi.DebugDetailTotalText, "75,780,000", StringComparison.Ordinal);

                bool trayToggleOk = false;
                using (TrayController tc = new TrayController(uiApiWs, baseDir, exePath))
                {
                    bool checkedOn = tc.DebugShowApiValueChecked;
                    uiApiWs.ShowEquivalentApiValue = false;
                    tc.RefreshTextsAndChecks(AtomElement.Argon, 75780000L);
                    bool checkedOff = !tc.DebugShowApiValueChecked;
                    trayToggleOk = checkedOn && checkedOff;
                }

                bool settingsLabelApiOk = zhJoined.Contains("等效 API 價值") && enJoined.Contains("Equivalent API Value");

                bool sec11Ok = priceUnitOk && multiModelOk && liveAllOk && liveConvOk && settingsDefaultOff && settingsPersistOk
                    && formatOk && uiOffOk && uiOnOk && trayToggleOk && settingsLabelApiOk;
                sb.AppendLine(string.Format(
                    "priceUnits={0} multiModel={1} (${2:F4}) liveAll={3} (calls={4}, unpriced={5}, ${6:F2}) liveConv={7} (calls={8}, unpriced={9}, ${10:F2}) persist={11}/{12} fmt={13} ui={14}/{15} tray={16} settingsLbl={17} -> {18}",
                    priceUnitOk, multiModelOk, mixTb.EquivalentApiValueUsd,
                    liveAllOk, liveAllSnap.Breakdown.CallCount, liveAllSnap.Breakdown.UnpricedCallCount, liveAllSnap.Breakdown.EquivalentApiValueUsd,
                    liveConvOk, liveConvCalls, liveConvUnpriced, liveConvUsd,
                    settingsDefaultOff, settingsPersistOk, formatOk, uiOffOk, uiOnOk, trayToggleOk, settingsLabelApiOk,
                    sec11Ok ? "PASS" : "FAIL"));
                passed &= sec11Ok;
            }
            catch (Exception ex)
            {
                passed = false;
                sb.AppendLine("EXCEPTION: " + ex.ToString());
            }

            sb.AppendLine("\nOVERALL: " + (passed ? "PASS" : "FAIL"));
            try
            {
                string parent = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    Directory.CreateDirectory(parent);
                }
                File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
            return passed ? 0 : 1;
        }

        private static void CollectVisualText(DependencyObject obj, List<string> acc)
        {
            if (obj == null)
            {
                return;
            }
            TextBlock tb = obj as TextBlock;
            if (tb != null && !string.IsNullOrEmpty(tb.Text))
            {
                acc.Add(tb.Text);
            }
            Panel p = obj as Panel;
            if (p != null)
            {
                for (int i = 0; i < p.Children.Count; i++)
                {
                    CollectVisualText(p.Children[i], acc);
                }
                return;
            }
            Decorator dec = obj as Decorator;
            if (dec != null)
            {
                CollectVisualText(dec.Child, acc);
                return;
            }
            ContentControl cc = obj as ContentControl;
            if (cc != null)
            {
                DependencyObject d = cc.Content as DependencyObject;
                if (d != null)
                {
                    CollectVisualText(d, acc);
                }
                else if (cc.Content is string)
                {
                    acc.Add((string)cc.Content);
                }
                return;
            }
            ItemsControl ic = obj as ItemsControl;
            if (ic != null)
            {
                for (int i = 0; i < ic.Items.Count; i++)
                {
                    DependencyObject d = ic.Items[i] as DependencyObject;
                    if (d != null)
                    {
                        CollectVisualText(d, acc);
                    }
                }
            }
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
            {
                return 0;
            }
            int count = 0;
            int idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }

        private static bool AssertModel(StringBuilder sb, string disp, string id, string enumStr, int enumInt, AtomElement expectedElem, bool expectedOrch)
        {
            ModelClassifyResult r = ModelClassifier.Classify(disp, id, enumStr, enumInt);
            bool ok = (r.Element == expectedElem) && (r.IsOrchestrator == expectedOrch);
            sb.AppendLine(string.Format("Classify('{0}' / '{1}' / {2}) => {3} (Orch={4}) -> {5}",
                disp, id, enumInt, r.Element, r.IsOrchestrator, ok ? "PASS" : "FAIL"));
            return ok;
        }

        private static bool AssertModelPrice(
            StringBuilder sb,
            string label,
            string modelId,
            string enumStr,
            int enumInt,
            long uncachedPrompt,
            long cachedContent,
            long candidates,
            long thoughts,
            long cacheCreation,
            bool expectedPriced,
            double expectedUsd)
        {
            bool actualPriced;
            double actualUsd = ApiPricingCatalog.CalculateStepCostUsd(
                uncachedPrompt, cachedContent, candidates, thoughts, cacheCreation,
                label, modelId, enumStr, enumInt, out actualPriced);
            bool ok = (actualPriced == expectedPriced) && (Math.Abs(actualUsd - expectedUsd) < 1e-6);
            sb.AppendLine(string.Format("Price '{0}' (enum={1}): priced={2} (exp {3}), usd=${4:F6} (exp ${5:F6}) -> {6}",
                label, enumInt, actualPriced, expectedPriced, actualUsd, expectedUsd, ok ? "PASS" : "FAIL"));
            return ok;
        }

        private static int RunRenderPreview(string outDir)
        {
            try
            {
                if (!Directory.Exists(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // Individual 3D Bohr models
                RenderAtomCard(outDir, "preview_H.png", AtomElement.Hydrogen, false, false);
                RenderAtomCard(outDir, "preview_C.png", AtomElement.Carbon, false, false);
                RenderAtomCard(outDir, "preview_Ne.png", AtomElement.Neon, false, false);
                RenderAtomCard(outDir, "preview_Ar.png", AtomElement.Argon, false, false);
                RenderAtomCard(outDir, "preview_Ne_Orchestrator.png", AtomElement.Neon, true, false);
                RenderAtomCard(outDir, "preview_Ne_Orchestrator_dark.png", AtomElement.Neon, true, false);
                RenderAtomCard(outDir, "preview_Ne_Orchestrator_light.png", AtomElement.Neon, true, true);
                RenderAtomCard(outDir, "preview_Ar_Orchestrator.png", AtomElement.Argon, true, false);
                RenderAtomCard(outDir, "preview_Unsupported_3d_dark.png", AtomElement.Unsupported, false, false);
                RenderAtomCard(outDir, "preview_Unsupported_3d_light.png", AtomElement.Unsupported, false, true);

                AntigravityDataReader reader = new AntigravityDataReader(AppDomain.CurrentDomain.BaseDirectory);
                WidgetSettings ws = new WidgetSettings();
                ws.Scope = TokenScope.CurrentConversation;
                ws.IncludeSubagents = true;
                ws.Language = UiLanguage.ZhTw;
                DataSnapshot snap = reader.ReadSnapshot(ws, null);
                if (snap.Breakdown.TotalTokenCount == 0)
                {
                    snap.Breakdown.PromptTokensUncached = 135420;
                    snap.Breakdown.CachedContentTokens = 980400;
                    snap.Breakdown.CandidatesTokens = 14820;
                    snap.Breakdown.ThoughtsTokens = 28410;
                    snap.Breakdown.LatestContextTokens = 128953;
                    snap.Breakdown.CallCount = 14;
                    snap.Breakdown.SubagentCount = 1;
                }

                // Gemini 4 Neon Compact & Hover previews (Dark & Light)
                DataSnapshot g4Snap = new DataSnapshot();
                g4Snap.ActiveModel.Element = AtomElement.Neon;
                g4Snap.ActiveModel.DisplayName = "Gemini 4 Neon (Low)";
                g4Snap.ActiveModel.IsRunning = true;
                g4Snap.Breakdown = snap.Breakdown.Clone();
                RenderFullWidgetPreview(outDir, "preview_compact_dark.png", ws, g4Snap, false, false);
                RenderFullWidgetPreview(outDir, "preview_hover_dark.png", ws, g4Snap, false, true);
                RenderFullWidgetPreview(outDir, "preview_compact_light.png", ws, g4Snap, true, false);
                RenderFullWidgetPreview(outDir, "preview_hover_light.png", ws, g4Snap, true, true);

                // Unsupported (Non-Gemini-4 + Orchestrator) compact previews (Dark & Light)
                DataSnapshot unsupSnap = new DataSnapshot();
                unsupSnap.ActiveModel.Element = AtomElement.Unsupported;
                unsupSnap.ActiveModel.IsOrchestrator = true;
                unsupSnap.ActiveModel.DisplayName = "Gemini 3.8 Flash (Low)";
                unsupSnap.Breakdown = snap.Breakdown.Clone();
                RenderFullWidgetPreview(outDir, "preview_Unsupported_compact_dark.png", ws, unsupSnap, false, false);
                RenderFullWidgetPreview(outDir, "preview_Unsupported_compact_light.png", ws, unsupSnap, true, false);

                WidgetSettings enUnsupWs = new WidgetSettings();
                enUnsupWs.Scope = TokenScope.CurrentConversation;
                enUnsupWs.IncludeSubagents = true;
                enUnsupWs.Language = UiLanguage.En;
                RenderFullWidgetPreview(outDir, "preview_Unsupported_compact_en_dark.png", enUnsupWs, unsupSnap, false, false);

                // Custom Settings Window previews (Dark & Light zh, Dark en, plus Dark off-preset 133% slider)
                RenderSettingsPreview(outDir, "preview_settings_dark.png", ws, g4Snap.ActiveModel, false);
                RenderSettingsPreview(outDir, "preview_settings_light.png", ws, g4Snap.ActiveModel, true);

                WidgetSettings enSettingsWs = new WidgetSettings();
                enSettingsWs.Scope = TokenScope.CurrentConversation;
                enSettingsWs.IncludeSubagents = true;
                enSettingsWs.Language = UiLanguage.En;
                RenderSettingsPreview(outDir, "preview_settings_en_dark.png", enSettingsWs, g4Snap.ActiveModel, false);

                WidgetSettings customScaleWs = new WidgetSettings();
                customScaleWs.Scope = TokenScope.CurrentConversation;
                customScaleWs.IncludeSubagents = true;
                customScaleWs.Language = UiLanguage.ZhTw;
                customScaleWs.ScaleMultiplier = 1.33;
                RenderSettingsPreview(outDir, "preview_settings_scale_custom_dark.png", customScaleWs, g4Snap.ActiveModel, false);

                // 5-model horizontal carousel + proportional scrollbar preview (simulating future 5th element release)
                RenderSettingsCarouselPreview(outDir, "preview_settings_5models_scroll_dark.png", ws, g4Snap.ActiveModel, false,
                    new AtomElement[] { AtomElement.Hydrogen, AtomElement.Carbon, AtomElement.Neon, AtomElement.Argon, AtomElement.Hydrogen });

                // Long model name + [Carbon Preview] wrapping & vertical stretch previews
                WidgetSettings carbonPrevWs = new WidgetSettings();
                carbonPrevWs.Scope = TokenScope.CurrentConversation;
                carbonPrevWs.IncludeSubagents = true;
                carbonPrevWs.Language = UiLanguage.ZhTw;
                carbonPrevWs.ManualElementOverride = AtomElement.Carbon;

                DataSnapshot longPrevSnap = new DataSnapshot();
                longPrevSnap.ActiveModel.Element = AtomElement.Carbon;
                longPrevSnap.ActiveModel.DetectedElement = AtomElement.Neon;
                longPrevSnap.ActiveModel.PreviewOverrideElement = AtomElement.Carbon;
                longPrevSnap.ActiveModel.IsOrchestrator = true;
                longPrevSnap.ActiveModel.DisplayName = "Orchestrator Gemini 4 Neon Experimental Long Model Name (Extra High)";
                longPrevSnap.ActiveModel.IsRunning = true;
                longPrevSnap.Breakdown = snap.Breakdown.Clone();
                RenderFullWidgetPreview(outDir, "preview_hover_preview_long_dark.png", carbonPrevWs, longPrevSnap, false, true);
                RenderSettingsPreview(outDir, "preview_settings_preview_long_dark.png", carbonPrevWs, longPrevSnap.ActiveModel, false);

                // Future uncatalogued Gemini 4 element fallback preview
                DataSnapshot futureElemSnap = new DataSnapshot();
                futureElemSnap.ActiveModel.Element = AtomElement.Unsupported;
                futureElemSnap.ActiveModel.DetectedElement = AtomElement.Unsupported;
                futureElemSnap.ActiveModel.DisplayName = "Gemini 4 Helium (Extra High)";
                futureElemSnap.Breakdown = snap.Breakdown.Clone();
                RenderFullWidgetPreview(outDir, "preview_future_element_compact_dark.png", ws, futureElemSnap, false, false);

                // Equivalent API Value enabled previews (75.78M · $0.43)
                WidgetSettings apiWs = new WidgetSettings();
                apiWs.Scope = TokenScope.CurrentConversation;
                apiWs.IncludeSubagents = true;
                apiWs.ShowEquivalentApiValue = true;
                apiWs.Language = UiLanguage.ZhTw;

                DataSnapshot apiSnap = new DataSnapshot();
                apiSnap.HasTargetData = true;
                apiSnap.ActiveModel.Element = AtomElement.Argon;
                apiSnap.ActiveModel.DetectedElement = AtomElement.Argon;
                apiSnap.ActiveModel.IsOrchestrator = true;
                apiSnap.ActiveModel.IsRunning = true;
                apiSnap.ActiveModel.DisplayName = "Orchestrator (Argon)";
                apiSnap.Breakdown = MakeSampleBreakdown(1250000L, 73900000L, 180000L, 450000L, 148200L, 62, 2);
                apiSnap.Breakdown.EquivalentApiValueUsd = 0.43;

                RenderFullWidgetPreview(outDir, "preview_api_value_compact_dark.png", apiWs, apiSnap, false, false);
                RenderFullWidgetPreview(outDir, "preview_api_value_hover_dark.png", apiWs, apiSnap, false, true);
                RenderFullWidgetPreview(outDir, "preview_api_value_hover_light.png", apiWs, apiSnap, true, true);
                RenderSettingsPreview(outDir, "preview_settings_api_value_dark.png", apiWs, apiSnap.ActiveModel, false);

                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(outDir, "render_error.txt"), ex.ToString(), new UTF8Encoding(false));
                }
                catch
                {
                }
                return 1;
            }
        }

        private static void RenderAtomCard(string outDir, string fileName, AtomElement elem, bool orch, bool light)
        {
            Border card = new Border();
            card.Width = 260;
            card.Height = 260;
            card.Background = new SolidColorBrush(light ? Color.FromRgb(255, 246, 248) : Color.FromRgb(28, 20, 20));
            card.CornerRadius = new CornerRadius(16);

            Grid g = new Grid();
            Atom3DControl atom = new Atom3DControl();
            atom.Width = 220;
            atom.Height = 220;
            atom.SetState(elem, orch, light, false, true);
            atom.StepAnimation(0.5);
            g.Children.Add(atom);

            ElementVisualInfo info = ElementVisualCatalog.GetInfo(elem);
            TextBlock label = new TextBlock();
            label.Text = string.Format("{0} ({1}) Z={2}{3}", info.Symbol, info.EnglishName, info.AtomicNumber, orch ? " \u26A1" : "");
            label.Foreground = new SolidColorBrush(light ? info.LightAccent : info.DarkAccent);
            label.FontSize = 13;
            label.FontWeight = FontWeights.Bold;
            label.FontFamily = new FontFamily("Segoe UI");
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Bottom;
            label.Margin = new Thickness(0, 0, 0, 8);
            g.Children.Add(label);

            card.Child = g;
            RenderElementToPng(card, 260, 260, Path.Combine(outDir, fileName));
        }

        private static void RenderFullWidgetPreview(string outDir, string fileName, WidgetSettings ws, DataSnapshot snap, bool light, bool hoverExpanded)
        {
            WidgetWindow win = new WidgetWindow(ws);
            win.SetTargetGeometry(0, 0, 1280, 800, 72.0, 1.0);
            win.ApplyData(snap, light, true);
            win.SetHoverExpandedImmediate(hoverExpanded);
            win.AtomControl.StepAnimation(0.4);

            UIElement content = win.Content as UIElement;
            win.Content = null;

            Border bg = new Border();
            bg.Width = WidgetWindow.CanvasWidth;
            bg.Height = WidgetWindow.CanvasHeight;
            bg.Background = new SolidColorBrush(light ? Color.FromRgb(255, 246, 248) : Color.FromRgb(28, 20, 20));
            bg.Child = content;

            RenderElementToPng(bg, (int)WidgetWindow.CanvasWidth, (int)WidgetWindow.CanvasHeight, Path.Combine(outDir, fileName));
        }

        private static void RenderSettingsPreview(string outDir, string fileName, WidgetSettings ws, ActiveModelState modelState, bool light)
        {
            RenderSettingsCarouselPreview(outDir, fileName, ws, modelState, light, null);
        }

        private static void RenderSettingsCarouselPreview(string outDir, string fileName, WidgetSettings ws, ActiveModelState modelState, bool light, AtomElement[] customElements)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = Assembly.GetExecutingAssembly().Location;
            SettingsWindow swin = new SettingsWindow(ws, baseDir, exePath, light, modelState);
            Border card = (customElements != null) ? swin.BuildPreviewCardWithElements(customElements) : swin.BuildPreviewCard();
            card.Width = 344;

            Border backdrop = new Border();
            backdrop.Width = 384;
            backdrop.Background = new SolidColorBrush(light ? Color.FromRgb(245, 236, 240) : Color.FromRgb(20, 15, 16));
            backdrop.Padding = new Thickness(8);
            backdrop.Child = card;

            backdrop.Measure(new Size(384, 1200));
            int h = Math.Max(420, (int)Math.Ceiling(backdrop.DesiredSize.Height));
            RenderElementToPng(backdrop, 384, h, Path.Combine(outDir, fileName));
        }

        private static void RenderElementToPng(FrameworkElement el, int w, int h, string filePath)
        {
            RenderElementToPngAtScale(el, w, h, 2.0, filePath);
        }

        private static void RenderElementToPngAtScale(FrameworkElement el, int w, int h, double scale, string filePath)
        {
            el.Measure(new Size(w, h));
            el.Arrange(new Rect(0, 0, w, h));
            el.UpdateLayout();

            int pxW = (int)Math.Round(w * scale);
            int pxH = (int)Math.Round(h * scale);
            double dpi = 96.0 * scale;
            RenderTargetBitmap rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(el);

            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                enc.Save(fs);
            }
        }

        private static TokenBreakdown MakeSampleBreakdown(long uncached, long cached, long candidates, long thoughts, long latestCtx, int calls, int subs)
        {
            TokenBreakdown tb = new TokenBreakdown();
            tb.PromptTokensUncached = uncached;
            tb.CachedContentTokens = cached;
            tb.CandidatesTokens = candidates;
            tb.ThoughtsTokens = thoughts;
            tb.LatestContextTokens = latestCtx;
            tb.CallCount = calls;
            tb.SubagentCount = subs;
            return tb;
        }

        private static int RunRenderReadmeAssets(string outDir)
        {
            try
            {
                if (!Directory.Exists(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                WidgetSettings ws = new WidgetSettings();
                ws.Scope = TokenScope.CurrentConversation;
                ws.IncludeSubagents = true;
                ws.ShowEquivalentApiValue = false;
                ws.Language = UiLanguage.ZhTw;
                ws.EnableAnimation = true;

                WidgetSettings wsOrchApi = new WidgetSettings();
                wsOrchApi.Scope = TokenScope.CurrentConversation;
                wsOrchApi.IncludeSubagents = true;
                wsOrchApi.ShowEquivalentApiValue = true;
                wsOrchApi.Language = UiLanguage.ZhTw;
                wsOrchApi.EnableAnimation = true;

                // 1. Argon
                DataSnapshot argonSnap = new DataSnapshot();
                argonSnap.HasTargetData = true;
                argonSnap.ActiveModel.Element = AtomElement.Argon;
                argonSnap.ActiveModel.DetectedElement = AtomElement.Argon;
                argonSnap.ActiveModel.IsOrchestrator = false;
                argonSnap.ActiveModel.IsRunning = true;
                argonSnap.ActiveModel.DisplayName = "Gemini 4 Argon (High)";
                argonSnap.Breakdown = MakeSampleBreakdown(486210, 11420800, 198430, 374560, 142680, 32, 1);

                // 2. Orchestrator Argon (showcases Equivalent API Value toggle ON)
                DataSnapshot orchArgonSnap = new DataSnapshot();
                orchArgonSnap.HasTargetData = true;
                orchArgonSnap.ActiveModel.Element = AtomElement.Argon;
                orchArgonSnap.ActiveModel.DetectedElement = AtomElement.Argon;
                orchArgonSnap.ActiveModel.IsOrchestrator = true;
                orchArgonSnap.ActiveModel.IsRunning = true;
                orchArgonSnap.ActiveModel.DisplayName = "Orchestrator (Argon)";
                orchArgonSnap.Breakdown = MakeSampleBreakdown(712400, 16890500, 318200, 698900, 168420, 48, 3);
                bool orchPriced;
                orchArgonSnap.Breakdown.EquivalentApiValueUsd = ApiPricingCatalog.CalculateStepCostUsd(
                    712400L, 16890500L, 318200L, 698900L, 0L,
                    "Orchestrator (Argon)", "gemini-pro-orchestrator-g1-pro", "MODEL_PLACEHOLDER_M668", 1668,
                    out orchPriced);

                // 3. Non-G4 Model
                DataSnapshot nonG4Snap = new DataSnapshot();
                nonG4Snap.HasTargetData = true;
                nonG4Snap.ActiveModel.Element = AtomElement.Unsupported;
                nonG4Snap.ActiveModel.DetectedElement = AtomElement.Unsupported;
                nonG4Snap.ActiveModel.IsOrchestrator = false;
                nonG4Snap.ActiveModel.IsRunning = false;
                nonG4Snap.ActiveModel.DisplayName = "Gemini 3.8 Flash (Low)";
                nonG4Snap.Breakdown = MakeSampleBreakdown(284120, 8564000, 142880, 169000, 96420, 24, 0);

                // Render 48-frame compact animation sequences for GIF encoding
                RenderWidgetAnimFrames(Path.Combine(outDir, "frames_argon"), ws, argonSnap, false, 128.0, 48, 1, 0.024);
                RenderWidgetAnimFrames(Path.Combine(outDir, "frames_orch_argon"), wsOrchApi, orchArgonSnap, false, 128.0, 48, 1, 0.024);
                RenderWidgetAnimFrames(Path.Combine(outDir, "frames_non_g4"), ws, nonG4Snap, false, 128.0, 48, 2, 0.08);

                // Render expanded Token detail cards
                RenderFullWidgetPreviewAtSize(outDir, "raw_token_argon.png", ws, argonSnap, false, true, 92.0, 2.0);
                RenderFullWidgetPreviewAtSize(outDir, "raw_token_orchestrator_argon.png", wsOrchApi, orchArgonSnap, false, true, 92.0, 2.0);
                RenderFullWidgetPreviewAtSize(outDir, "raw_token_non_g4.png", ws, nonG4Snap, false, true, 92.0, 2.0);

                // Render custom right-click Settings Window previews (Dark Argon + Light Orchestrator Argon)
                RenderSettingsPreview(outDir, "preview_settings_argon_dark.png", ws, argonSnap.ActiveModel, false);
                RenderSettingsPreview(outDir, "preview_settings_orchestrator_argon_light.png", wsOrchApi, orchArgonSnap.ActiveModel, true);

                // English UI renders for README.md (Live Previews)
                WidgetSettings wsEn = new WidgetSettings();
                wsEn.Scope = TokenScope.CurrentConversation;
                wsEn.IncludeSubagents = true;
                wsEn.ShowEquivalentApiValue = false;
                wsEn.Language = UiLanguage.En;
                wsEn.EnableAnimation = true;

                WidgetSettings wsOrchApiEn = new WidgetSettings();
                wsOrchApiEn.Scope = TokenScope.CurrentConversation;
                wsOrchApiEn.IncludeSubagents = true;
                wsOrchApiEn.ShowEquivalentApiValue = true;
                wsOrchApiEn.Language = UiLanguage.En;
                wsOrchApiEn.EnableAnimation = true;

                RenderWidgetAnimFrames(Path.Combine(outDir, "frames_non_g4_en"), wsEn, nonG4Snap, false, 128.0, 48, 2, 0.08);
                RenderFullWidgetPreviewAtSize(outDir, "raw_token_argon_en.png", wsEn, argonSnap, false, true, 92.0, 2.0);
                RenderFullWidgetPreviewAtSize(outDir, "raw_token_orchestrator_argon_en.png", wsOrchApiEn, orchArgonSnap, false, true, 92.0, 2.0);
                RenderFullWidgetPreviewAtSize(outDir, "raw_token_non_g4_en.png", wsEn, nonG4Snap, false, true, 92.0, 2.0);
                RenderSettingsPreview(outDir, "preview_settings_argon_dark_en.png", wsEn, argonSnap.ActiveModel, false);
                RenderSettingsPreview(outDir, "preview_settings_orchestrator_argon_light_en.png", wsOrchApiEn, orchArgonSnap.ActiveModel, true);

                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(outDir, "render_readme_error.txt"), ex.ToString(), new UTF8Encoding(false));
                }
                catch
                {
                }
                return 1;
            }
        }

        private static void RenderFullWidgetPreviewAtSize(string outDir, string fileName, WidgetSettings ws, DataSnapshot snap, bool light, bool hoverExpanded, double atomSizeDip, double scale)
        {
            WidgetWindow win = new WidgetWindow(ws);
            win.SetTargetGeometry(0, 0, 1280, 800, atomSizeDip, 1.0);
            win.ApplyData(snap, light, true);
            win.SetHoverExpandedImmediate(hoverExpanded);
            for (int w = 0; w < 20; w++)
            {
                win.AtomControl.StepAnimation(0.03);
            }

            UIElement content = win.Content as UIElement;
            win.Content = null;

            Border bg = new Border();
            bg.Width = WidgetWindow.CanvasWidth;
            bg.Height = WidgetWindow.CanvasHeight;
            bg.Background = new SolidColorBrush(light ? Color.FromRgb(255, 246, 248) : Color.FromRgb(28, 20, 20));
            bg.Child = content;

            RenderElementToPngAtScale(bg, (int)WidgetWindow.CanvasWidth, (int)WidgetWindow.CanvasHeight, scale, Path.Combine(outDir, fileName));
        }

        private static void RenderWidgetAnimFrames(string frameDir, WidgetSettings ws, DataSnapshot snap, bool light, double atomSizeDip, int frameCount, int subStepsPerFrame, double subStepDt)
        {
            if (!Directory.Exists(frameDir))
            {
                Directory.CreateDirectory(frameDir);
            }

            WidgetWindow win = new WidgetWindow(ws);
            win.SetTargetGeometry(0, 0, 1280, 800, atomSizeDip, 1.0);
            win.ApplyData(snap, light, true);
            win.SetHoverExpandedImmediate(false);
            for (int w = 0; w < 20; w++)
            {
                win.AtomControl.StepAnimation(0.03);
            }

            UIElement content = win.Content as UIElement;
            win.Content = null;

            Border bg = new Border();
            bg.Width = WidgetWindow.CanvasWidth;
            bg.Height = WidgetWindow.CanvasHeight;
            bg.Background = new SolidColorBrush(light ? Color.FromRgb(255, 246, 248) : Color.FromRgb(28, 20, 20));
            bg.Child = content;

            int wDip = (int)WidgetWindow.CanvasWidth;
            int hDip = (int)WidgetWindow.CanvasHeight;
            bg.Measure(new Size(wDip, hDip));
            bg.Arrange(new Rect(0, 0, wDip, hDip));
            bg.UpdateLayout();

            const double scale = 1.5;
            int pxW = (int)Math.Round(wDip * scale);
            int pxH = (int)Math.Round(hDip * scale);
            double dpi = 96.0 * scale;

            for (int f = 0; f < frameCount; f++)
            {
                if (f > 0)
                {
                    for (int s = 0; s < subStepsPerFrame; s++)
                    {
                        win.AtomControl.StepAnimation(subStepDt);
                    }
                    bg.UpdateLayout();
                }

                RenderTargetBitmap rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);
                rtb.Render(bg);

                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                string path = Path.Combine(frameDir, string.Format(CultureInfo.InvariantCulture, "f_{0:000}.png", f));
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    enc.Save(fs);
                }
            }
        }
    }
}
