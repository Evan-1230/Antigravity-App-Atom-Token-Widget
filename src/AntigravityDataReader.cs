using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32;

namespace AntigravityAtomTokenWidget
{
    internal static class ProtoWire
    {
        public static bool TryReadVarint(byte[] buf, ref int pos, int end, out ulong val)
        {
            val = 0;
            int shift = 0;
            while (pos < end)
            {
                byte b = buf[pos++];
                if (shift < 64)
                {
                    val |= ((ulong)(b & 0x7F)) << shift;
                }
                if ((b & 0x80) == 0)
                {
                    return true;
                }
                shift += 7;
                if (shift > 70)
                {
                    return false;
                }
            }
            return false;
        }

        public static bool TryReadField(byte[] buf, ref int pos, int end, out int fieldNum, out int wireType, out ulong varintVal, out int sliceOffset, out int sliceLen)
        {
            fieldNum = 0;
            wireType = 0;
            varintVal = 0;
            sliceOffset = 0;
            sliceLen = 0;

            ulong tag;
            if (!TryReadVarint(buf, ref pos, end, out tag))
            {
                return false;
            }
            fieldNum = (int)(tag >> 3);
            wireType = (int)(tag & 7UL);
            if (fieldNum <= 0)
            {
                return false;
            }

            if (wireType == 0)
            {
                return TryReadVarint(buf, ref pos, end, out varintVal);
            }
            if (wireType == 1)
            {
                if (pos + 8 > end)
                {
                    return false;
                }
                pos += 8;
                return true;
            }
            if (wireType == 2)
            {
                ulong lenVal;
                if (!TryReadVarint(buf, ref pos, end, out lenVal))
                {
                    return false;
                }
                if (lenVal > (ulong)(end - pos))
                {
                    return false;
                }
                sliceOffset = pos;
                sliceLen = (int)lenVal;
                pos += sliceLen;
                return true;
            }
            if (wireType == 5)
            {
                if (pos + 4 > end)
                {
                    return false;
                }
                pos += 4;
                return true;
            }
            return false;
        }

        public static string ReadUtf8(byte[] buf, int offset, int len)
        {
            if (buf == null || len <= 0 || offset < 0 || offset + len > buf.Length)
            {
                return "";
            }
            try
            {
                return Encoding.UTF8.GetString(buf, offset, len);
            }
            catch
            {
                return "";
            }
        }
    }

    internal sealed class LearnedModelInfo
    {
        public int EnumInt;
        public string EnumStr = "";
        public string ModelId = "";
        public string DisplayName = "";
    }

    internal sealed class StepUsageEntry
    {
        public DateTime LocalTime;
        public int ModelEnumInt;
        public long UncachedPromptTokens;
        public long CachedContentTokens;
        public long CandidatesTokens;
        public long ThoughtsTokens;
        public long CacheCreationTokens;
    }

    internal sealed class CachedConversationData
    {
        public string ConversationId = "";
        public long DbLength;
        public long DbMtimeTicks;
        public long WalLength;
        public long WalMtimeTicks;
        public List<StepUsageEntry> Steps = new List<StepUsageEntry>();
        public List<LearnedModelInfo> LearnedModels = new List<LearnedModelInfo>();
        public int LatestModelEnumInt = 0;
        public string LatestModelEnumStr = "";
        public string LatestModelId = "";
        public string LatestDisplayName = "";
        public DateTime LatestTimestampUtc = DateTime.MinValue;
    }

    internal sealed class ConversationSummaryRow
    {
        public string ConversationId = "";
        public string Title = "";
        public DateTime LastModifiedUtc = DateTime.MinValue;
        public string Status = "";
        public string ParentConversationId = "";
        public bool NotFullyIdle = false;
    }

    public sealed class DataSnapshot
    {
        public string ActiveConversationId = "";
        public ActiveModelState ActiveModel = new ActiveModelState();
        public TokenBreakdown Breakdown = new TokenBreakdown();
        public double? DetectedEmptyComposerDip = null;
        public bool HasTargetData = false;
    }

    internal sealed class ThemeWatcher
    {
        private static readonly Regex ThemeModeRegex = new Regex("\"themeMode\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        private readonly string configPath;
        private DateTime lastWriteUtc = DateTime.MinValue;
        private bool isLightTheme = false;

        public ThemeWatcher()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            this.configPath = Path.Combine(userProfile, ".gemini", "config", "config.json");
            Refresh(true);
        }

        public bool IsLightTheme
        {
            get { return isLightTheme; }
        }

        public bool Refresh(bool force)
        {
            bool previous = isLightTheme;
            try
            {
                if (File.Exists(configPath))
                {
                    DateTime wt = File.GetLastWriteTimeUtc(configPath);
                    if (force || wt != lastWriteUtc)
                    {
                        lastWriteUtc = wt;
                        string text;
                        using (FileStream fs = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                        {
                            text = sr.ReadToEnd();
                        }
                        Match m = ThemeModeRegex.Match(text);
                        if (m.Success)
                        {
                            string mode = m.Groups[1].Value;
                            if (string.Equals(mode, "THEME_MODE_LIGHT", StringComparison.OrdinalIgnoreCase))
                            {
                                isLightTheme = true;
                            }
                            else if (string.Equals(mode, "THEME_MODE_DARK", StringComparison.OrdinalIgnoreCase))
                            {
                                isLightTheme = false;
                            }
                            else if (string.Equals(mode, "THEME_MODE_INHERIT", StringComparison.OrdinalIgnoreCase))
                            {
                                isLightTheme = IsSystemLightTheme();
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return isLightTheme != previous;
        }

        private static bool IsSystemLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AppsUseLightTheme");
                        if (val is int)
                        {
                            return ((int)val) == 1;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }
    }

    internal sealed class UiaModelProbe : IDisposable
    {
        private static readonly Regex FutureProofModelRegex = new Regex(
            @"("
            + @"\bOrchestrator(?:\s*(?:\([^\)]+\)|(?:Gemini\s*(?:\d+(?:\.\d+)?\s+)?)?[A-Za-z0-9\.\-]{2,20}(?:\s+[A-Za-z0-9\.\-]{1,18}){0,3}(?:\s*(?:\([^\)]+\)|Extra\s+High|Very\s+High|Extended\s+Thinking|XHigh|High|Medium|Low|Minimal|Thinking|Max|Fast))?))?"
            + @"|\bGemini\s*\d+(?:\.\d+)?(?:\s+[A-Za-z0-9\.\-]{1,22}){1,4}(?:\s*(?:\([^\)]+\)|Extra\s+High|Very\s+High|Extended\s+Thinking|XHigh|High|Medium|Low|Minimal|Thinking|Max|Fast))?"
            + @"|\b(?:Hydrogen|Helium|Lithium|Beryllium|Boron|Carbon|Nitrogen|Oxygen|Fluorine|Neon|Sodium|Magnesium|Aluminum|Aluminium|Silicon|Phosphorus|Sulfur|Chlorine|Argon|Potassium|Calcium|Krypton|Xenon|Radon)(?:\s*(?:\([^\)]+\)|Extra\s+High|Very\s+High|Extended\s+Thinking|XHigh|High|Medium|Low|Minimal|Thinking|Max|Fast))?"
            + @"|\bClaude(?:\s+[A-Za-z0-9\.\-]{1,18}){1,4}(?:\s*(?:\([^\)]+\)|Extra\s+High|Very\s+High|Extended\s+Thinking|XHigh|High|Medium|Low|Minimal|Thinking|Max|Fast))?"
            + @"|(?<!\.)\b(?:OpenAI\s+)?(?:GPT[A-Za-z0-9\.\-]*|o[1-9](?:-[A-Za-z0-9\.\-]+)?\b)(?:\s+[A-Za-z0-9\.\-]{1,16}){0,3}(?:\s*(?:\([^\)]+\)|Extra\s+High|Very\s+High|Extended\s+Thinking|XHigh|High|Medium|Low|Minimal|Thinking|Max|Fast))?"
            + @"|\b(?:DeepSeek|Llama|Qwen|Mistral|Grok|Phi|Kimi|GLM)(?:\s+[A-Za-z0-9\.\-]{1,18}){1,4}(?:\s*(?:\([^\)]+\)|Extra\s+High|Very\s+High|Extended\s+Thinking|XHigh|High|Medium|Low|Minimal|Thinking|Max|Fast))?"
            + @")",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> UiChromeStopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "send", "stop", "attach", "planning", "fast", "settings", "close", "new",
            "cancel", "retry", "copy", "edit", "approve", "deny", "allow", "submit",
            "clear", "more", "history", "search", "files", "terminal", "browser",
            "mcp", "rules", "skills", "workflows", "auto", "all", "conversation",
            "workspace", "customize"
        };

        private readonly object lockObj = new object();
        private readonly AutoResetEvent rescanSignal = new AutoResetEvent(false);
        private IntPtr targetHwnd = IntPtr.Zero;
        private double targetBottomPx = 0.0;
        private double targetHeightPx = 0.0;
        private double targetDpiScale = 1.0;

        private string lastModelText = "";
        private DateTime lastModelSeenUtc = DateTime.MinValue;
        private int consecutiveEmptyScans = 0;
        private double? lastComposerHeightDip = null;
        private bool stopRequested = false;
        private Thread worker;

        public UiaModelProbe()
        {
            worker = new Thread(WorkerLoop);
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }

        public void RequestImmediateRescan()
        {
            try
            {
                if (!stopRequested)
                {
                    rescanSignal.Set();
                }
            }
            catch
            {
            }
        }

        public void InvalidateIfOlderThan(DateTime utcThreshold)
        {
            lock (lockObj)
            {
                if (lastModelSeenUtc != DateTime.MinValue && lastModelSeenUtc < utcThreshold)
                {
                    lastModelText = "";
                    lastModelSeenUtc = DateTime.MinValue;
                }
            }
        }

        public void SetTargetWindow(IntPtr hwnd, double topPx, double bottomPx, double dpiScale)
        {
            bool switchedWindow = false;
            lock (lockObj)
            {
                if (targetHwnd != IntPtr.Zero && hwnd != IntPtr.Zero && targetHwnd != hwnd)
                {
                    lastModelText = "";
                    lastModelSeenUtc = DateTime.MinValue;
                    consecutiveEmptyScans = 0;
                    switchedWindow = true;
                }
                targetHwnd = hwnd;
                targetBottomPx = bottomPx;
                targetHeightPx = Math.Max(0.0, bottomPx - topPx);
                targetDpiScale = dpiScale > 0.2 ? dpiScale : 1.0;
            }
            if (switchedWindow)
            {
                RequestImmediateRescan();
            }
        }

        public bool TryGetRecentModelText(out string modelText, out DateTime seenUtc)
        {
            lock (lockObj)
            {
                modelText = lastModelText;
                seenUtc = lastModelSeenUtc;
                if (!string.IsNullOrEmpty(lastModelText) && (DateTime.UtcNow - lastModelSeenUtc).TotalSeconds <= 30.0)
                {
                    return true;
                }
                return false;
            }
        }

        public double? GetDetectedEmptyComposerDip()
        {
            lock (lockObj)
            {
                return lastComposerHeightDip;
            }
        }

        private void WorkerLoop()
        {
            int sleepMs = 700;
            int cycleCount = 0;
            PropertyCondition btnCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            PropertyCondition txtCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
            PropertyCondition editCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
            PropertyCondition docCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
            OrCondition composerCond = new OrCondition(editCond, docCond);

            while (!stopRequested)
            {
                rescanSignal.WaitOne(sleepMs);
                if (stopRequested)
                {
                    break;
                }
                cycleCount++;

                IntPtr hwnd;
                double bottomPx;
                double heightPx;
                double dpiScale;
                lock (lockObj)
                {
                    hwnd = targetHwnd;
                    bottomPx = targetBottomPx;
                    heightPx = targetHeightPx;
                    dpiScale = targetDpiScale;
                }

                if (hwnd == IntPtr.Zero || heightPx < 200.0)
                {
                    sleepMs = 1200;
                    continue;
                }

                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    AutomationElement root = AutomationElement.FromHandle(hwnd);
                    if (root != null)
                    {
                        // 1. Scan ONLY ControlType.Button without cap, widened vertically for tall multi-line prompts
                        AutomationElementCollection buttons = root.FindAll(TreeScope.Descendants, btnCond);
                        string bestModel = "";
                        if (buttons != null)
                        {
                            double minBottomStripPx = bottomPx - Math.Max(340.0 * dpiScale, heightPx * 0.62);
                            double minTopStripPx = bottomPx - Math.Max(400.0 * dpiScale, heightPx * 0.68);
                            double minBtnH = 14.0 * dpiScale;
                            double maxBtnH = 68.0 * dpiScale;
                            double minBtnW = 36.0 * dpiScale;
                            double maxBtnW = 540.0 * dpiScale;

                            int bestBtnScore = -1;
                            double bestBottom = double.MinValue;
                            bool bestHasModelSignal = false;

                            for (int i = 0; i < buttons.Count; i++)
                            {
                                try
                                {
                                    AutomationElement btn = buttons[i];
                                    System.Windows.Rect rc = btn.Current.BoundingRectangle;
                                    if (rc.IsEmpty || rc.Width < minBtnW || rc.Width > maxBtnW || rc.Height < minBtnH || rc.Height > maxBtnH)
                                    {
                                        continue;
                                    }
                                    if (rc.Bottom < minBottomStripPx || rc.Top < minTopStripPx)
                                    {
                                        continue;
                                    }

                                    string aid = btn.Current.AutomationId ?? "";
                                    string help = btn.Current.HelpText ?? "";
                                    bool hasExpandCollapse = false;
                                    try
                                    {
                                        object ecObj = btn.GetCurrentPropertyValue(AutomationElement.IsExpandCollapsePatternAvailableProperty, true);
                                        if (ecObj is bool && (bool)ecObj)
                                        {
                                            hasExpandCollapse = true;
                                        }
                                    }
                                    catch
                                    {
                                    }

                                    string candidate = ExtractModelFromButton(btn, txtCond, aid, help);
                                    if (!string.IsNullOrEmpty(candidate))
                                    {
                                        bool hasModelSignal = hasExpandCollapse
                                            || aid.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0
                                            || help.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0;

                                        int btnScore = candidate.Length;
                                        if (hasExpandCollapse) { btnScore += 80; }
                                        if (aid.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0 || help.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            btnScore += 90;
                                        }

                                        bool beats = false;
                                        if (hasModelSignal && !bestHasModelSignal)
                                        {
                                            beats = true;
                                        }
                                        else if (hasModelSignal == bestHasModelSignal)
                                        {
                                            beats = (rc.Bottom > bestBottom + 6.0) || (Math.Abs(rc.Bottom - bestBottom) <= 6.0 && btnScore > bestBtnScore);
                                        }

                                        if (beats)
                                        {
                                            bestHasModelSignal = hasModelSignal;
                                            bestBottom = rc.Bottom;
                                            bestBtnScore = btnScore;
                                            bestModel = candidate;
                                        }
                                    }
                                }
                                catch
                                {
                                }
                            }

                            if (!string.IsNullOrEmpty(bestModel))
                            {
                                lock (lockObj)
                                {
                                    consecutiveEmptyScans = 0;
                                    lastModelText = bestModel;
                                    lastModelSeenUtc = DateTime.UtcNow;
                                }
                            }
                            else if (buttons.Count > 0)
                            {
                                // Do NOT wipe lastModelText during transient Chromium UIA tree mutations while typing;
                                // AntigravityDataReader invalidates lastModelText only on confirmed model switches.
                                lock (lockObj)
                                {
                                    consecutiveEmptyScans++;
                                }
                            }
                        }

                        // 2. Periodically check empty composer height (every 4th cycle)
                        if ((cycleCount % 4) == 1)
                        {
                            AutomationElementCollection inputs = root.FindAll(TreeScope.Descendants, composerCond);
                            if (inputs != null)
                            {
                                double minComposerTop = bottomPx - heightPx * 0.28;
                                double? detectedComposer = null;
                                for (int i = 0; i < inputs.Count; i++)
                                {
                                    try
                                    {
                                        AutomationElement el = inputs[i];
                                        System.Windows.Rect rc = el.Current.BoundingRectangle;
                                        if (rc.IsEmpty || rc.Top < minComposerTop || rc.Width < 220.0 * dpiScale)
                                        {
                                            continue;
                                        }
                                        double hDip = rc.Height / dpiScale;
                                        if (hDip >= 58.0 && hDip <= 135.0)
                                        {
                                            string nm = el.Current.Name ?? "";
                                            bool looksEmpty = nm.Length == 0 || nm.IndexOf("Ask anything", StringComparison.OrdinalIgnoreCase) >= 0;
                                            if (looksEmpty && (!detectedComposer.HasValue || hDip > detectedComposer.Value))
                                            {
                                                detectedComposer = hDip;
                                            }
                                        }
                                    }
                                    catch
                                    {
                                    }
                                }
                                if (detectedComposer.HasValue)
                                {
                                    lock (lockObj)
                                    {
                                        lastComposerHeightDip = detectedComposer.Value;
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
                bool emptyCache;
                lock (lockObj)
                {
                    emptyCache = string.IsNullOrEmpty(lastModelText);
                }
                sw.Stop();
                if (emptyCache)
                {
                    sleepMs = 450;
                }
                else
                {
                    sleepMs = (sw.ElapsedMilliseconds > 400) ? 1400 : 700;
                }
            }
        }

        public static string SelectBestModelTextFromParts(string buttonName, string[] childTexts)
        {
            return SelectBestModelTextFromParts(buttonName, childTexts, "", "");
        }

        public static string SelectBestModelTextFromParts(string buttonName, string[] childTexts, string automationId, string helpText)
        {
            List<string> rawClean = new List<string>();
            if (childTexts != null)
            {
                for (int j = 0; j < childTexts.Length; j++)
                {
                    string ct = ModelNameNormalizer.CleanAndDeduplicate(childTexts[j]);
                    if (ct.Length == 0)
                    {
                        continue;
                    }
                    bool dup = false;
                    for (int k = 0; k < rawClean.Count; k++)
                    {
                        if (string.Equals(rawClean[k], ct, StringComparison.OrdinalIgnoreCase))
                        {
                            dup = true;
                            break;
                        }
                    }
                    if (!dup)
                    {
                        rawClean.Add(ct);
                    }
                }
            }

            // Remove child spans that are already substrings of another longer child span (fixes duplicate multi-span concatenation)
            List<string> uniqueParts = new List<string>();
            for (int i = 0; i < rawClean.Count; i++)
            {
                string cand = rawClean[i];
                bool coveredByLonger = false;
                for (int k = 0; k < rawClean.Count; k++)
                {
                    if (k != i && rawClean[k].Length > cand.Length && rawClean[k].IndexOf(cand, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        coveredByLonger = true;
                        break;
                    }
                }
                if (!coveredByLonger)
                {
                    uniqueParts.Add(cand);
                }
            }

            List<string> candidates = new List<string>();
            string nm = ModelNameNormalizer.CleanAndDeduplicate(buttonName);
            string fullChildren = ModelNameNormalizer.CleanAndDeduplicate(string.Join(" ", uniqueParts.ToArray()));

            if (!string.IsNullOrEmpty(fullChildren))
            {
                candidates.Add(fullChildren);
            }
            if (!string.IsNullOrEmpty(nm) && !string.Equals(nm, fullChildren, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(nm);
                if (!string.IsNullOrEmpty(fullChildren) && !SharesAnySignificantWord(nm, fullChildren))
                {
                    string combined = ModelNameNormalizer.CleanAndDeduplicate(nm + " " + fullChildren);
                    if (!string.IsNullOrEmpty(combined))
                    {
                        candidates.Add(combined);
                    }
                }
            }
            for (int j = 0; j < uniqueParts.Count; j++)
            {
                candidates.Add(uniqueParts[j]);
            }

            string bestMatch = "";
            int bestScore = -1;

            for (int i = 0; i < candidates.Count; i++)
            {
                string c = candidates[i];
                if (c.Length < 3 || c.Length > 160)
                {
                    continue;
                }
                Match m = FutureProofModelRegex.Match(c);
                if (!m.Success)
                {
                    continue;
                }
                string val = ModelNameNormalizer.CleanAndDeduplicate(m.Groups[1].Value);
                if (string.IsNullOrEmpty(val))
                {
                    continue;
                }
                int score = val.Length;
                string lower = val.ToLowerInvariant();
                if (lower.Contains("orchestrator"))
                {
                    score += 120;
                }
                if (lower.Contains("gemini") || lower.Contains("argon") || lower.Contains("neon") || lower.Contains("carbon") || lower.Contains("hydrogen")
                    || lower.Contains("helium") || lower.Contains("lithium") || lower.Contains("claude") || lower.Contains("flash") || lower.Contains("pro")
                    || lower.Contains("gpt") || lower.Contains("openai") || lower.Contains("deepseek") || lower.Contains("llama") || lower.Contains("qwen"))
                {
                    score += 50;
                }
                if (lower.Contains("extra high") || lower.Contains("very high") || lower.Contains("extended thinking")
                    || lower.Contains("minimal") || lower.Contains("low") || lower.Contains("medium") || lower.Contains("high") || lower.Contains("thinking"))
                {
                    score += 25;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = val;
                }
            }

            if (!string.IsNullOrEmpty(bestMatch))
            {
                return bestMatch;
            }

            // Tier 2 Future-Proof Metadata Fallback: if AutomationId or HelpText indicates a model selector,
            // trust non-stopword labels even for completely unprecedented future vendor/element codenames.
            string meta = ((automationId ?? "") + " " + (helpText ?? "")).ToLowerInvariant();
            if (meta.Contains("model"))
            {
                string fb = !string.IsNullOrEmpty(fullChildren) ? fullChildren : nm;
                if (fb.Length >= 3 && fb.Length <= 64 && !UiChromeStopwords.Contains(fb))
                {
                    for (int i = 0; i < fb.Length; i++)
                    {
                        if (char.IsLetter(fb[i]))
                        {
                            return fb;
                        }
                    }
                }
            }

            return "";
        }

        private static bool SharesAnySignificantWord(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }
            string[] pa = a.Split(new char[] { ' ', '(', ')', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            string[] pb = b.Split(new char[] { ' ', '(', ')', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pa.Length; i++)
            {
                if (pa[i].Length < 2) { continue; }
                for (int j = 0; j < pb.Length; j++)
                {
                    if (string.Equals(pa[i], pb[j], StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string ExtractModelFromButton(AutomationElement btn, PropertyCondition txtCond, string automationId, string helpText)
        {
            string nm = "";
            try
            {
                nm = btn.Current.Name ?? "";
            }
            catch
            {
            }

            List<string> childList = new List<string>();
            try
            {
                AutomationElementCollection texts = btn.FindAll(TreeScope.Descendants, txtCond);
                if (texts != null && texts.Count > 0)
                {
                    for (int j = 0; j < texts.Count; j++)
                    {
                        try
                        {
                            string t = (texts[j].Current.Name ?? "").Trim();
                            if (t.Length > 0)
                            {
                                childList.Add(t);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return SelectBestModelTextFromParts(nm, childList.ToArray(), automationId, helpText);
        }

        public void Dispose()
        {
            stopRequested = true;
            try
            {
                rescanSignal.Set();
            }
            catch
            {
            }
        }
    }

    internal sealed class AntigravityDataReader
    {
        private const string LearnedModelsFileName = "learned_models.ini";
        private static readonly Regex ViewTimeRegex = new Regex(@"last_user_view_time:\s*\{\s*seconds:\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex StateModelRegex = new Regex(@"last_selected_agent_model:\s*(\S+)", RegexOptions.Compiled);
        private static readonly Regex TranscriptModelChangeRegex = new Regex(@"changed setting `Model Selection` from [^\r\n]+? to ([^.\r\n]+)\.", RegexOptions.Compiled);
        private static readonly Regex TranscriptCreatedAtRegex = new Regex(@"""created_at""\s*:\s*""([^""]+)""", RegexOptions.Compiled);

        private readonly string baseDir;
        private readonly string agyRootDir;
        private readonly string annotationsDir;
        private readonly string conversationsDir;
        private readonly string summariesDbPath;
        private readonly string statePbtxtPath;
        private readonly string brainDir;

        private readonly Dictionary<string, CachedConversationData> dbCache = new Dictionary<string, CachedConversationData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, LearnedModelInfo> enumIntCatalog = new Dictionary<int, LearnedModelInfo>();
        private readonly Dictionary<string, LearnedModelInfo> enumStrCatalog = new Dictionary<string, LearnedModelInfo>(StringComparer.OrdinalIgnoreCase);

        private string pendingLearnEnumStr = "";
        private string pendingLearnUiaText = "";
        private DateTime pendingLearnFirstUtc = DateTime.MinValue;
        private int pendingLearnCount = 0;
        private string lastObservedPbtxtSig = "";
        private string lastObservedEnumStr = "";
        private DateTime lastEnumChangeUtc = DateTime.MinValue;
        private ActiveModelState lastConfirmedAutoModel = null;
        private string lastConfirmedEnumStr = "";
        private string lastConfirmedActiveCid = "";

        public AntigravityDataReader()
            : this(AppDomain.CurrentDomain.BaseDirectory)
        {
        }

        public AntigravityDataReader(string appBaseDir)
        {
            this.baseDir = string.IsNullOrEmpty(appBaseDir) ? AppDomain.CurrentDomain.BaseDirectory : appBaseDir;
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            this.agyRootDir = Path.Combine(userProfile, ".gemini", "antigravity");
            this.annotationsDir = Path.Combine(agyRootDir, "annotations");
            this.conversationsDir = Path.Combine(agyRootDir, "conversations");
            this.summariesDbPath = Path.Combine(agyRootDir, "conversation_summaries.db");
            this.statePbtxtPath = Path.Combine(agyRootDir, "antigravity_state.pbtxt");
            this.brainDir = Path.Combine(agyRootDir, "brain");
            SeedKnownModels();
            LoadLearnedModelsIni();
        }

        private void SeedKnownModels()
        {
            AddSeed(1528, "MODEL_PLACEHOLDER_M528", "gemini-4-hydrogen", "Gemini 4 Hydrogen (XHigh)");
            AddSeed(1530, "MODEL_PLACEHOLDER_M530", "gemini-4-hydrogen-tiered-high", "Gemini 4 Hydrogen (High)");
            AddSeed(1669, "MODEL_PLACEHOLDER_M669", "gemini-flash-worker-agy", "Gemini 4 Hydrogen (Orchestrator)");
            AddSeed(1535, "MODEL_PLACEHOLDER_M535", "gemini-4-neon", "Gemini 4 Neon (XHigh)");
            AddSeed(1537, "MODEL_PLACEHOLDER_M537", "gemini-pro-orchestrator-neon", "Orchestrator (Neon)");
            AddSeed(1417, "MODEL_PLACEHOLDER_M417", "gemini-4-argon-trial", "Gemini 4 Argon (High)");
            AddSeed(1418, "MODEL_PLACEHOLDER_M418", "gemini-4-argon-trial", "Gemini 4 Argon (Medium)");
            AddSeed(1668, "MODEL_PLACEHOLDER_M668", "gemini-pro-orchestrator-g1-pro", "Orchestrator (Argon)");
            AddSeed(1318, "MODEL_PLACEHOLDER_M318", "gemini-3.8-flash-n", "Gemini 3.8 Flash (High)");
            AddSeed(1322, "MODEL_PLACEHOLDER_M322", "gemini-3.8-flash-tiered", "Gemini 3.8 Flash");
            AddSeed(1401, "MODEL_PLACEHOLDER_M401", "claude-opus-5-5-medium", "Claude Opus 5.5 (Medium)");
            AddSeed(1402, "MODEL_PLACEHOLDER_M402", "claude-opus-5-5-high", "Claude Opus 5.5 (High)");
            AddSeed(0, "MODEL_OPENAI_GPT_OSS_120B_MEDIUM", "gpt-oss-120b-medium", "OpenAI GPT-OSS 120B (Medium)");
        }

        private void AddSeed(int enumInt, string enumStr, string modelId, string displayName)
        {
            LearnedModelInfo info = new LearnedModelInfo();
            info.EnumInt = enumInt;
            info.EnumStr = enumStr;
            info.ModelId = modelId;
            info.DisplayName = displayName;
            if (enumInt > 0)
            {
                enumIntCatalog[enumInt] = info;
            }
            if (!string.IsNullOrEmpty(enumStr))
            {
                enumStrCatalog[enumStr] = info;
            }
        }

        public static bool IsPlausibleLearnedMapping(string enumStr, string displayName)
        {
            if (string.IsNullOrEmpty(enumStr) || string.IsNullOrEmpty(displayName))
            {
                return false;
            }
            string e = enumStr.Trim();
            string rawTrim = displayName.Trim();
            if (rawTrim.IndexOf('|') >= 0 || rawTrim.IndexOf('\r') >= 0 || rawTrim.IndexOf('\n') >= 0)
            {
                return false;
            }
            string d = ModelNameNormalizer.CleanAndDeduplicate(rawTrim);
            if (d.Length < 3 || d.Length > 80)
            {
                return false;
            }

            string eLower = e.ToLowerInvariant();
            string dLower = d.ToLowerInvariant();
            ModelClassifyResult dRes = ModelClassifier.Classify(d, "", "", 0);
            ModelClassifyResult eRes = ModelClassifier.Classify("", "", e, 0);

            // Never allow explicit non-Gemini-4 enum names to be mapped to a Gemini 4 element
            bool enumClearlyNonG4 = eLower.Contains("openai") || eLower.Contains("gpt") || eLower.Contains("claude")
                || eLower.Contains("deepseek") || eLower.Contains("llama") || eLower.Contains("qwen")
                || eLower.Contains("mistral") || eLower.Contains("grok") || eLower.Contains("gemini_2") || eLower.Contains("gemini_3") || eLower.Contains("gemini_5");
            if (enumClearlyNonG4 && dRes.Element != AtomElement.Unsupported)
            {
                return false;
            }

            // If enum itself resolves to a Gemini 4 element, display name must agree
            if (eRes.Element != AtomElement.Unsupported && dRes.Element != eRes.Element)
            {
                return false;
            }

            // Reject UI chrome words or unrecognised noise
            if (dLower == "send" || dLower == "stop" || dLower == "attach" || dLower == "planning" || dLower == "settings" || dLower == "close")
            {
                return false;
            }
            bool hasAlpha = false;
            for (int i = 0; i < d.Length; i++)
            {
                if (char.IsLetter(d[i])) { hasAlpha = true; break; }
            }
            if (!hasAlpha)
            {
                return false;
            }

            return true;
        }

        private void LoadLearnedModelsIni()
        {
            try
            {
                string path = Path.Combine(baseDir, LearnedModelsFileName);
                if (!File.Exists(path))
                {
                    return;
                }
                string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));
                List<KeyValuePair<string, string>> validEntries = new List<KeyValuePair<string, string>>();
                bool dirty = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        dirty = true;
                        continue;
                    }
                    string k = line.Substring(0, eq).Trim();
                    string rawV = line.Substring(eq + 1).Trim();
                    string v = ModelNameNormalizer.CleanAndDeduplicate(rawV);
                    if (!string.Equals(v, rawV, StringComparison.Ordinal))
                    {
                        dirty = true;
                    }
                    if (string.IsNullOrEmpty(k) || string.IsNullOrEmpty(v) || enumStrCatalog.ContainsKey(k) || !IsPlausibleLearnedMapping(k, v))
                    {
                        dirty = true;
                        continue;
                    }
                    AddSeed(0, k, "", v);
                    validEntries.Add(new KeyValuePair<string, string>(k, v));
                }

                if (dirty)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < validEntries.Count; i++)
                    {
                        sb.AppendLine(validEntries[i].Key + "=" + validEntries[i].Value);
                    }
                    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        private void SaveLearnedModelToIni(string enumStr, string displayName)
        {
            string cleanDisp = ModelNameNormalizer.CleanAndDeduplicate(displayName);
            if (!IsPlausibleLearnedMapping(enumStr, cleanDisp))
            {
                return;
            }
            try
            {
                AddSeed(0, enumStr, "", cleanDisp);
                if (!Directory.Exists(baseDir))
                {
                    Directory.CreateDirectory(baseDir);
                }
                string path = Path.Combine(baseDir, LearnedModelsFileName);
                Dictionary<string, string> all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));
                    for (int i = 0; i < lines.Length; i++)
                    {
                        int eq = lines[i].IndexOf('=');
                        if (eq > 0)
                        {
                            string ek = lines[i].Substring(0, eq).Trim();
                            string ev = ModelNameNormalizer.CleanAndDeduplicate(lines[i].Substring(eq + 1).Trim());
                            if (!string.IsNullOrEmpty(ek) && !string.IsNullOrEmpty(ev) && !enumStrCatalog.ContainsKey(ek) && IsPlausibleLearnedMapping(ek, ev))
                            {
                                all[ek] = ev;
                            }
                        }
                    }
                }
                all[enumStr] = cleanDisp;
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in all)
                {
                    if (IsPlausibleLearnedMapping(kv.Key, kv.Value))
                    {
                        sb.AppendLine(kv.Key + "=" + kv.Value);
                    }
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public DataSnapshot ReadSnapshot(WidgetSettings settings, UiaModelProbe uiaProbe)
        {
            DataSnapshot snap = new DataSnapshot();
            if (uiaProbe != null)
            {
                snap.DetectedEmptyComposerDip = uiaProbe.GetDetectedEmptyComposerDip();
            }

            Dictionary<string, ConversationSummaryRow> summaries = LoadSummaries();
            Dictionary<string, List<string>> parentToChildren = BuildChildrenMap(summaries);
            string activeCid = ResolveActiveConversationId(summaries);
            snap.ActiveConversationId = activeCid;

            List<string> allDbCids = ListAllConversationDbIds();
            for (int i = 0; i < allDbCids.Count; i++)
            {
                GetOrLoadConversation(allDbCids[i]);
            }

            // Build active family set (active + recursive child subagents)
            HashSet<string> activeFamily = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> childSubagents = new List<string>();
            if (!string.IsNullOrEmpty(activeCid))
            {
                activeFamily.Add(activeCid);
                if (settings.IncludeSubagents)
                {
                    CollectDescendants(activeCid, parentToChildren, activeFamily, childSubagents);
                }
            }

            // Compute running status across active family
            bool isRunning = false;
            foreach (string cid in activeFamily)
            {
                ConversationSummaryRow row;
                if (summaries.TryGetValue(cid, out row))
                {
                    if (string.Equals(row.Status, "CASCADE_RUN_STATUS_RUNNING", StringComparison.OrdinalIgnoreCase) || row.NotFullyIdle)
                    {
                        isRunning = true;
                        break;
                    }
                }
            }

            // Compute TokenBreakdown across ALL models (so Token counts always work even on non-Gemini-4 models!)
            TokenBreakdown breakdown = new TokenBreakdown();
            DateTime todayLocal = DateTime.Now.Date;
            DateTime latestActiveStepTime = DateTime.MinValue;

            if (settings.Scope == TokenScope.CurrentConversation)
            {
                int subWithUsage = 0;
                foreach (string cid in activeFamily)
                {
                    CachedConversationData conv = GetOrLoadConversation(cid);
                    if (conv == null)
                    {
                        continue;
                    }
                    bool isSub = !string.Equals(cid, activeCid, StringComparison.OrdinalIgnoreCase);
                    if (isSub && conv.Steps.Count > 0)
                    {
                        subWithUsage++;
                    }
                    for (int i = 0; i < conv.Steps.Count; i++)
                    {
                        StepUsageEntry st = conv.Steps[i];
                        bool stepPriced;
                        double stepCostUsd = ComputeStepCostUsd(st, conv, out stepPriced);
                        breakdown.AddUsage(st.UncachedPromptTokens, st.CachedContentTokens, st.CandidatesTokens, st.ThoughtsTokens, st.CacheCreationTokens, stepCostUsd, stepPriced);
                        if (!isSub && st.LocalTime >= latestActiveStepTime)
                        {
                            latestActiveStepTime = st.LocalTime;
                            breakdown.LatestContextTokens = st.UncachedPromptTokens + st.CachedContentTokens;
                        }
                    }
                }
                breakdown.SubagentCount = subWithUsage;
                snap.HasTargetData = breakdown.CallCount > 0 || activeFamily.Count > 0;
            }
            else
            {
                HashSet<string> allSubagentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, ConversationSummaryRow> kv in summaries)
                {
                    if (!string.IsNullOrEmpty(kv.Value.ParentConversationId))
                    {
                        allSubagentIds.Add(kv.Key);
                    }
                }

                int subCount = 0;
                foreach (KeyValuePair<string, CachedConversationData> kv in dbCache)
                {
                    string cid = kv.Key;
                    bool isSub = allSubagentIds.Contains(cid);
                    if (!settings.IncludeSubagents && isSub)
                    {
                        continue;
                    }
                    CachedConversationData conv = kv.Value;
                    bool convCountedSub = false;
                    for (int i = 0; i < conv.Steps.Count; i++)
                    {
                        StepUsageEntry st = conv.Steps[i];
                        if (settings.Scope == TokenScope.Today && st.LocalTime.Date != todayLocal)
                        {
                            continue;
                        }
                        bool stepPriced;
                        double stepCostUsd = ComputeStepCostUsd(st, conv, out stepPriced);
                        breakdown.AddUsage(st.UncachedPromptTokens, st.CachedContentTokens, st.CandidatesTokens, st.ThoughtsTokens, st.CacheCreationTokens, stepCostUsd, stepPriced);
                        if (isSub && !convCountedSub)
                        {
                            convCountedSub = true;
                            subCount++;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(activeCid))
                {
                    CachedConversationData activeConv = GetOrLoadConversation(activeCid);
                    if (activeConv != null && activeConv.Steps.Count > 0)
                    {
                        StepUsageEntry last = activeConv.Steps[activeConv.Steps.Count - 1];
                        breakdown.LatestContextTokens = last.UncachedPromptTokens + last.CachedContentTokens;
                    }
                }
                breakdown.SubagentCount = subCount;
                snap.HasTargetData = dbCache.Count > 0;
            }

            snap.Breakdown = breakdown;
            snap.ActiveModel = ResolveActiveModel(activeCid, isRunning, uiaProbe, settings);
            return snap;
        }

        private double ComputeStepCostUsd(StepUsageEntry st, CachedConversationData conv, out bool isPriced)
        {
            int enumInt = st.ModelEnumInt;
            string disp = "";
            string mid = "";
            string estr = "";

            LearnedModelInfo info;
            if (enumInt > 0 && enumIntCatalog.TryGetValue(enumInt, out info) && info != null)
            {
                disp = info.DisplayName ?? "";
                mid = info.ModelId ?? "";
                estr = info.EnumStr ?? "";
            }
            else if (enumInt <= 0 && conv != null)
            {
                enumInt = conv.LatestModelEnumInt;
                disp = conv.LatestDisplayName ?? "";
                mid = conv.LatestModelId ?? "";
                estr = conv.LatestModelEnumStr ?? "";
                if (enumInt > 0 && (string.IsNullOrEmpty(disp) || string.IsNullOrEmpty(mid)) && enumIntCatalog.TryGetValue(enumInt, out info) && info != null)
                {
                    if (string.IsNullOrEmpty(disp)) { disp = info.DisplayName ?? ""; }
                    if (string.IsNullOrEmpty(mid)) { mid = info.ModelId ?? ""; }
                    if (string.IsNullOrEmpty(estr)) { estr = info.EnumStr ?? ""; }
                }
            }

            if (conv != null && st.ModelEnumInt > 0 && conv.LatestModelEnumInt == st.ModelEnumInt)
            {
                if (string.IsNullOrEmpty(disp)) { disp = conv.LatestDisplayName ?? ""; }
                if (string.IsNullOrEmpty(mid)) { mid = conv.LatestModelId ?? ""; }
                if (string.IsNullOrEmpty(estr)) { estr = conv.LatestModelEnumStr ?? ""; }
            }

            return ApiPricingCatalog.CalculateStepCostUsd(
                st.UncachedPromptTokens,
                st.CachedContentTokens,
                st.CandidatesTokens,
                st.ThoughtsTokens,
                st.CacheCreationTokens,
                disp,
                mid,
                estr,
                enumInt,
                out isPriced);
        }

        public TokenBreakdown ComputeConversationBreakdownForTest(string cid)
        {
            List<string> allDbCids = ListAllConversationDbIds();
            for (int i = 0; i < allDbCids.Count; i++)
            {
                GetOrLoadConversation(allDbCids[i]);
            }

            TokenBreakdown breakdown = new TokenBreakdown();
            CachedConversationData conv = GetOrLoadConversation(cid);
            if (conv == null)
            {
                return breakdown;
            }
            DateTime latestTime = DateTime.MinValue;
            for (int i = 0; i < conv.Steps.Count; i++)
            {
                StepUsageEntry st = conv.Steps[i];
                bool stepPriced;
                double stepCostUsd = ComputeStepCostUsd(st, conv, out stepPriced);
                breakdown.AddUsage(st.UncachedPromptTokens, st.CachedContentTokens, st.CandidatesTokens, st.ThoughtsTokens, st.CacheCreationTokens, stepCostUsd, stepPriced);
                if (st.LocalTime >= latestTime)
                {
                    latestTime = st.LocalTime;
                    breakdown.LatestContextTokens = st.UncachedPromptTokens + st.CachedContentTokens;
                }
            }
            return breakdown;
        }

        private ActiveModelState ResolveActiveModel(string activeCid, bool isRunning, UiaModelProbe uiaProbe, WidgetSettings settings)
        {
            ActiveModelState detected = ResolveAutoDetectedModel(activeCid, isRunning, uiaProbe);
            detected.DetectedElement = detected.Element;

            // Manual element preview: override rendered 3D element + mark PreviewOverrideElement,
            // while preserving the real detected DisplayName and auto-adaptive IsOrchestrator!
            if (settings.ManualElementOverride.HasValue)
            {
                AtomElement ov = settings.ManualElementOverride.Value;
                detected.PreviewOverrideElement = ov;
                detected.Element = ov;
                detected.DetectionSource = "Preview+" + (string.IsNullOrEmpty(detected.DetectionSource) ? "Auto" : detected.DetectionSource);
            }
            return detected;
        }

        private ActiveModelState ResolveAutoDetectedModel(string activeCid, bool isRunning, UiaModelProbe uiaProbe)
        {
            DateTime stateTimeUtc;
            string stateEnumStr;
            bool stateKnown;
            ModelClassifyResult stateResult = ReadStatePbtxtModel(out stateTimeUtc, out stateEnumStr, out stateKnown);

            bool hasUia = false;
            string uiaText = "";
            DateTime uiaTime = DateTime.MinValue;
            if (uiaProbe != null)
            {
                hasUia = uiaProbe.TryGetRecentModelText(out uiaText, out uiaTime);
            }

            DateTime convTimeUtc = DateTime.MinValue;
            ModelClassifyResult convResult = null;
            string convModelId = "";
            if (!string.IsNullOrEmpty(activeCid))
            {
                CachedConversationData conv = GetOrLoadConversation(activeCid);
                if (conv != null && (conv.LatestModelEnumInt > 0 || !string.IsNullOrEmpty(conv.LatestDisplayName) || !string.IsNullOrEmpty(conv.LatestModelId)))
                {
                    LearnedModelInfo info = null;
                    if (conv.LatestModelEnumInt > 0)
                    {
                        enumIntCatalog.TryGetValue(conv.LatestModelEnumInt, out info);
                    }
                    string disp = !string.IsNullOrEmpty(conv.LatestDisplayName) ? conv.LatestDisplayName : (info != null ? info.DisplayName : "");
                    string mid = !string.IsNullOrEmpty(conv.LatestModelId) ? conv.LatestModelId : (info != null ? info.ModelId : "");
                    string estr = !string.IsNullOrEmpty(conv.LatestModelEnumStr) ? conv.LatestModelEnumStr : (info != null ? info.EnumStr : "");
                    convResult = ModelClassifier.Classify(disp, mid, estr, conv.LatestModelEnumInt);
                    convModelId = mid;
                    convTimeUtc = conv.LatestTimestampUtc;
                }

                DateTime trTimeUtc;
                string trModelDisp = ReadLatestTranscriptModelChange(activeCid, out trTimeUtc);
                if (!string.IsNullOrEmpty(trModelDisp) && trTimeUtc >= convTimeUtc)
                {
                    convResult = ModelClassifier.Classify(trModelDisp, "", "", 0);
                    convTimeUtc = trTimeUtc;
                }
            }

            bool shouldInvalidateUia;
            bool shouldRequestRescan;
            string learnEnumStr;
            string learnDisplayName;
            ActiveModelState resolved = ResolveAutoDetectedModelCore(
                activeCid,
                isRunning,
                stateEnumStr,
                stateTimeUtc,
                stateKnown,
                stateResult,
                hasUia,
                uiaText,
                uiaTime,
                convResult,
                convModelId,
                convTimeUtc,
                out shouldInvalidateUia,
                out shouldRequestRescan,
                out learnEnumStr,
                out learnDisplayName);

            if (uiaProbe != null)
            {
                if (shouldInvalidateUia)
                {
                    uiaProbe.InvalidateIfOlderThan(lastEnumChangeUtc);
                }
                if (shouldRequestRescan)
                {
                    uiaProbe.RequestImmediateRescan();
                }
            }
            if (!string.IsNullOrEmpty(learnEnumStr) && !string.IsNullOrEmpty(learnDisplayName))
            {
                SaveLearnedModelToIni(learnEnumStr, learnDisplayName);
            }
            return resolved;
        }

        internal ActiveModelState ResolveAutoDetectedModelCore(
            string activeCid,
            bool isRunning,
            string stateEnumStr,
            DateTime stateTimeUtc,
            bool stateKnown,
            ModelClassifyResult stateResult,
            bool hasUia,
            string uiaText,
            DateTime uiaTime,
            ModelClassifyResult convResult,
            string convModelId,
            DateTime convTimeUtc,
            out bool shouldInvalidateUia,
            out bool shouldRequestRescan,
            out string learnEnumStr,
            out string learnDisplayName)
        {
            shouldInvalidateUia = false;
            shouldRequestRescan = false;
            learnEnumStr = "";
            learnDisplayName = "";

            ActiveModelState state = new ActiveModelState();
            state.IsRunning = isRunning;

            string curEnum = stateEnumStr ?? "";
            string curCid = activeCid ?? "";
            string pbtxtSig = curEnum + "|" + stateTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);

            if (string.IsNullOrEmpty(lastObservedPbtxtSig))
            {
                lastObservedEnumStr = curEnum;
                lastEnumChangeUtc = stateTimeUtc;
            }
            else if (!string.Equals(pbtxtSig, lastObservedPbtxtSig, StringComparison.Ordinal))
            {
                if (!string.Equals(curEnum, lastObservedEnumStr, StringComparison.OrdinalIgnoreCase))
                {
                    // Actual model selection changed in antigravity_state.pbtxt
                    lastObservedEnumStr = curEnum;
                    lastEnumChangeUtc = stateTimeUtc;
                    lastConfirmedAutoModel = null;
                    lastConfirmedEnumStr = "";
                    lastConfirmedActiveCid = "";
                    shouldInvalidateUia = true;
                    shouldRequestRescan = true;
                    pendingLearnEnumStr = "";
                    pendingLearnUiaText = "";
                    pendingLearnCount = 0;
                }
                else
                {
                    // Non-model write to antigravity_state.pbtxt while typing: keep confirmed cache & UIA valid
                    shouldRequestRescan = true;
                }
            }
            lastObservedPbtxtSig = pbtxtSig;

            if (lastConfirmedAutoModel != null
                && string.Equals(lastConfirmedAutoModel.DetectionSource, "ConversationDB", StringComparison.Ordinal)
                && !string.Equals(curCid, lastConfirmedActiveCid, StringComparison.Ordinal))
            {
                lastConfirmedAutoModel = null;
            }

            // Priority 1 (auto): Live UI Automation bottom-toolbar model button text
            if (hasUia && !string.IsNullOrEmpty(uiaText))
            {
                bool uiaIsPreSwitch = (lastEnumChangeUtc > DateTime.MinValue) && (uiaTime < lastEnumChangeUtc.AddMilliseconds(-50));
                if (uiaIsPreSwitch)
                {
                    shouldRequestRescan = true;
                }
                else
                {
                    string cleanedUia = ModelNameNormalizer.CleanAndDeduplicate(uiaText);
                    bool pbtxtNewerAndKnown = stateKnown
                        && stateResult != null
                        && lastEnumChangeUtc > DateTime.MinValue
                        && lastEnumChangeUtc > uiaTime.AddMilliseconds(350);

                    if (!pbtxtNewerAndKnown && !string.IsNullOrEmpty(cleanedUia))
                    {
                        if (!string.IsNullOrEmpty(curEnum) && !stateKnown && !enumStrCatalog.ContainsKey(curEnum) && IsPlausibleLearnedMapping(curEnum, cleanedUia))
                        {
                            bool pbtxtSettled = (lastEnumChangeUtc == DateTime.MinValue) || (uiaTime >= lastEnumChangeUtc.AddMilliseconds(200));
                            if (pbtxtSettled)
                            {
                                if (string.Equals(pendingLearnEnumStr, curEnum, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(pendingLearnUiaText, cleanedUia, StringComparison.Ordinal))
                                {
                                    pendingLearnCount++;
                                    if (pendingLearnCount >= 2 && (DateTime.UtcNow - pendingLearnFirstUtc).TotalSeconds >= 1.2)
                                    {
                                        learnEnumStr = curEnum;
                                        learnDisplayName = cleanedUia;
                                    }
                                }
                                else
                                {
                                    pendingLearnEnumStr = curEnum;
                                    pendingLearnUiaText = cleanedUia;
                                    pendingLearnFirstUtc = DateTime.UtcNow;
                                    pendingLearnCount = 1;
                                }
                            }
                        }

                        ModelClassifyResult rUia = ModelClassifier.Classify(cleanedUia, "", "", 0);
                        state.Element = rUia.Element;
                        state.IsOrchestrator = rUia.IsOrchestrator;
                        state.DisplayName = rUia.NormalizedDisplayName;
                        state.DetectionSource = "UIA";
                        lastConfirmedAutoModel = state.Clone();
                        lastConfirmedEnumStr = curEnum;
                        lastConfirmedActiveCid = curCid;
                        return state;
                    }
                }
            }

            // Priority 2: Sticky confirmed model cache across transient Chromium UIA dropouts while typing
            if (lastConfirmedAutoModel != null && string.Equals(curEnum, lastConfirmedEnumStr, StringComparison.OrdinalIgnoreCase))
            {
                ActiveModelState sticky = lastConfirmedAutoModel.Clone();
                sticky.IsRunning = isRunning;
                sticky.DetectionSource = "Sticky(" + lastConfirmedAutoModel.DetectionSource + ")";
                return sticky;
            }

            // Priority 3: antigravity_state.pbtxt vs Conversation DB / Transcript
            if (stateResult != null && (convResult == null || stateTimeUtc > convTimeUtc))
            {
                state.Element = stateResult.Element;
                state.IsOrchestrator = stateResult.IsOrchestrator;
                state.DisplayName = stateResult.NormalizedDisplayName;
                state.DetectionSource = stateKnown ? "StatePbtxt" : "StatePbtxt(Unknown)";
                if (stateKnown)
                {
                    lastConfirmedAutoModel = state.Clone();
                    lastConfirmedEnumStr = curEnum;
                    lastConfirmedActiveCid = curCid;
                }
                return state;
            }

            if (convResult != null)
            {
                state.Element = convResult.Element;
                state.IsOrchestrator = convResult.IsOrchestrator;
                state.DisplayName = convResult.NormalizedDisplayName;
                state.ModelId = convModelId;
                state.DetectionSource = "ConversationDB";
                lastConfirmedAutoModel = state.Clone();
                lastConfirmedEnumStr = curEnum;
                lastConfirmedActiveCid = curCid;
                return state;
            }

            if (stateResult != null)
            {
                state.Element = stateResult.Element;
                state.IsOrchestrator = stateResult.IsOrchestrator;
                state.DisplayName = stateResult.NormalizedDisplayName;
                state.DetectionSource = stateKnown ? "StatePbtxt" : "StatePbtxt(Unknown)";
                if (stateKnown)
                {
                    lastConfirmedAutoModel = state.Clone();
                    lastConfirmedEnumStr = curEnum;
                    lastConfirmedActiveCid = curCid;
                }
                return state;
            }

            state.Element = AtomElement.Unsupported;
            state.DisplayName = "No Active Model";
            state.DetectionSource = "None";
            return state;
        }

        private ModelClassifyResult ReadStatePbtxtModel(out DateTime mtimeUtc, out string rawEnumStr, out bool isKnown)
        {
            mtimeUtc = DateTime.MinValue;
            rawEnumStr = "";
            isKnown = false;
            try
            {
                if (!File.Exists(statePbtxtPath))
                {
                    return null;
                }
                mtimeUtc = File.GetLastWriteTimeUtc(statePbtxtPath);
                string text = File.ReadAllText(statePbtxtPath, Encoding.UTF8);
                Match m = StateModelRegex.Match(text);
                if (!m.Success)
                {
                    return null;
                }
                rawEnumStr = m.Groups[1].Value.Trim();
                LearnedModelInfo known;
                if (enumStrCatalog.TryGetValue(rawEnumStr, out known))
                {
                    isKnown = true;
                    return ModelClassifier.Classify(known.DisplayName, known.ModelId, rawEnumStr, known.EnumInt);
                }

                // Return an explicit Unsupported result with rawEnumStr so stale convResult does not win when state.pbtxt is newer!
                ModelClassifyResult r = ModelClassifier.Classify("", "", rawEnumStr, 0);
                isKnown = (r.Element != AtomElement.Unsupported);
                return r;
            }
            catch
            {
            }
            return null;
        }

        private string ReadLatestTranscriptModelChange(string cid, out DateTime createdAtUtc)
        {
            createdAtUtc = DateTime.MinValue;
            try
            {
                string path = Path.Combine(brainDir, cid, ".system_generated", "logs", "transcript.jsonl");
                if (!File.Exists(path))
                {
                    return "";
                }
                string tail;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    long start = Math.Max(0, len - 65536);
                    fs.Seek(start, SeekOrigin.Begin);
                    byte[] buf = new byte[len - start];
                    int read = fs.Read(buf, 0, buf.Length);
                    tail = Encoding.UTF8.GetString(buf, 0, read);
                }
                string[] lines = tail.Split('\n');
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string line = lines[i];
                    if (line.IndexOf("Model Selection", StringComparison.Ordinal) >= 0)
                    {
                        Match mm = TranscriptModelChangeRegex.Match(line);
                        if (mm.Success)
                        {
                            Match tm = TranscriptCreatedAtRegex.Match(line);
                            if (tm.Success)
                            {
                                DateTime dt;
                                if (DateTime.TryParse(tm.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out dt))
                                {
                                    createdAtUtc = dt;
                                }
                            }
                            return mm.Groups[1].Value.Trim();
                        }
                    }
                }
            }
            catch
            {
            }
            return "";
        }

        private Dictionary<string, ConversationSummaryRow> LoadSummaries()
        {
            Dictionary<string, ConversationSummaryRow> map = new Dictionary<string, ConversationSummaryRow>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(summariesDbPath))
            {
                return map;
            }
            try
            {
                using (ReadOnlySqlite db = ReadOnlySqlite.Open(summariesDbPath))
                {
                    if (db == null)
                    {
                        return map;
                    }
                    using (SqliteQuery q = db.Prepare("SELECT conversation_id, title, last_modified_time, status, parent_conversation_id, not_fully_idle FROM conversation_summaries"))
                    {
                        if (q == null)
                        {
                            return map;
                        }
                        while (q.StepRow())
                        {
                            ConversationSummaryRow row = new ConversationSummaryRow();
                            row.ConversationId = q.GetString(0);
                            if (string.IsNullOrEmpty(row.ConversationId))
                            {
                                continue;
                            }
                            row.Title = q.GetString(1);
                            string lm = q.GetString(2);
                            DateTime dt;
                            if (!string.IsNullOrEmpty(lm) && DateTime.TryParse(lm, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out dt))
                            {
                                row.LastModifiedUtc = dt;
                            }
                            row.Status = q.GetString(3);
                            row.ParentConversationId = q.GetString(4);
                            row.NotFullyIdle = q.GetInt32(5) != 0;
                            map[row.ConversationId] = row;
                        }
                    }
                }
            }
            catch
            {
            }
            return map;
        }

        private static Dictionary<string, List<string>> BuildChildrenMap(Dictionary<string, ConversationSummaryRow> summaries)
        {
            Dictionary<string, List<string>> map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ConversationSummaryRow> kv in summaries)
            {
                string parent = kv.Value.ParentConversationId;
                if (string.IsNullOrEmpty(parent))
                {
                    continue;
                }
                List<string> list;
                if (!map.TryGetValue(parent, out list))
                {
                    list = new List<string>();
                    map[parent] = list;
                }
                list.Add(kv.Key);
            }
            return map;
        }

        private static void CollectDescendants(string parentCid, Dictionary<string, List<string>> map, HashSet<string> visited, List<string> outList)
        {
            List<string> children;
            if (!map.TryGetValue(parentCid, out children))
            {
                return;
            }
            for (int i = 0; i < children.Count; i++)
            {
                string c = children[i];
                if (visited.Add(c))
                {
                    outList.Add(c);
                    CollectDescendants(c, map, visited, outList);
                }
            }
        }

        private string ResolveActiveConversationId(Dictionary<string, ConversationSummaryRow> summaries)
        {
            string bestCid = "";
            long bestSec = -1;
            DateTime bestFileUtc = DateTime.MinValue;

            try
            {
                if (Directory.Exists(annotationsDir))
                {
                    string[] files = Directory.GetFiles(annotationsDir, "*.pbtxt");
                    for (int i = 0; i < files.Length; i++)
                    {
                        string fp = files[i];
                        string cid = Path.GetFileNameWithoutExtension(fp);
                        if (string.IsNullOrEmpty(cid))
                        {
                            continue;
                        }
                        string dbCheck = Path.Combine(conversationsDir, cid + ".db");
                        if (!File.Exists(dbCheck))
                        {
                            continue;
                        }
                        long sec = 0;
                        try
                        {
                            string content = File.ReadAllText(fp, Encoding.UTF8);
                            Match m = ViewTimeRegex.Match(content);
                            if (m.Success)
                            {
                                long.TryParse(m.Groups[1].Value, out sec);
                            }
                        }
                        catch
                        {
                        }
                        DateTime mt = File.GetLastWriteTimeUtc(fp);
                        if (sec > bestSec || (sec == bestSec && mt > bestFileUtc))
                        {
                            bestSec = sec;
                            bestFileUtc = mt;
                            bestCid = cid;
                        }
                    }
                }
            }
            catch
            {
            }

            if (!string.IsNullOrEmpty(bestCid))
            {
                return bestCid;
            }

            DateTime newestMod = DateTime.MinValue;
            foreach (KeyValuePair<string, ConversationSummaryRow> kv in summaries)
            {
                if (!string.IsNullOrEmpty(kv.Value.ParentConversationId))
                {
                    continue;
                }
                if (kv.Value.LastModifiedUtc > newestMod)
                {
                    newestMod = kv.Value.LastModifiedUtc;
                    bestCid = kv.Key;
                }
            }
            return bestCid;
        }

        private List<string> ListAllConversationDbIds()
        {
            List<string> list = new List<string>();
            try
            {
                if (!Directory.Exists(conversationsDir))
                {
                    return list;
                }
                string[] files = Directory.GetFiles(conversationsDir, "*.db");
                for (int i = 0; i < files.Length; i++)
                {
                    string cid = Path.GetFileNameWithoutExtension(files[i]);
                    if (!string.IsNullOrEmpty(cid))
                    {
                        list.Add(cid);
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        public CachedConversationData GetOrLoadConversation(string cid)
        {
            if (string.IsNullOrEmpty(cid))
            {
                return null;
            }
            string dbPath = Path.Combine(conversationsDir, cid + ".db");
            if (!File.Exists(dbPath))
            {
                return null;
            }
            string walPath = dbPath + "-wal";

            long dbLen = 0;
            long dbTicks = 0;
            long walLen = 0;
            long walTicks = 0;
            try
            {
                FileInfo fiDb = new FileInfo(dbPath);
                dbLen = fiDb.Length;
                dbTicks = fiDb.LastWriteTimeUtc.Ticks;
                if (File.Exists(walPath))
                {
                    FileInfo fiWal = new FileInfo(walPath);
                    walLen = fiWal.Length;
                    walTicks = fiWal.LastWriteTimeUtc.Ticks;
                }
            }
            catch
            {
            }

            CachedConversationData cached;
            if (dbCache.TryGetValue(cid, out cached))
            {
                if (cached.DbLength == dbLen && cached.DbMtimeTicks == dbTicks && cached.WalLength == walLen && cached.WalMtimeTicks == walTicks)
                {
                    return cached;
                }
            }

            CachedConversationData parsed = ParseConversationDb(cid, dbPath);
            if (parsed != null)
            {
                parsed.DbLength = dbLen;
                parsed.DbMtimeTicks = dbTicks;
                parsed.WalLength = walLen;
                parsed.WalMtimeTicks = walTicks;
                dbCache[cid] = parsed;

                for (int i = 0; i < parsed.LearnedModels.Count; i++)
                {
                    MergeLearnedModel(parsed.LearnedModels[i]);
                }
            }
            return parsed;
        }

        private void MergeLearnedModel(LearnedModelInfo info)
        {
            if (info == null)
            {
                return;
            }
            if (info.EnumInt > 0)
            {
                LearnedModelInfo existing;
                if (enumIntCatalog.TryGetValue(info.EnumInt, out existing))
                {
                    if (string.IsNullOrEmpty(existing.DisplayName) && !string.IsNullOrEmpty(info.DisplayName))
                    {
                        existing.DisplayName = info.DisplayName;
                    }
                    if (string.IsNullOrEmpty(existing.ModelId) && !string.IsNullOrEmpty(info.ModelId))
                    {
                        existing.ModelId = info.ModelId;
                    }
                    if (string.IsNullOrEmpty(existing.EnumStr) && !string.IsNullOrEmpty(info.EnumStr))
                    {
                        existing.EnumStr = info.EnumStr;
                    }
                }
                else
                {
                    enumIntCatalog[info.EnumInt] = info;
                }
            }
            if (!string.IsNullOrEmpty(info.EnumStr))
            {
                LearnedModelInfo existingStr;
                if (!enumStrCatalog.TryGetValue(info.EnumStr, out existingStr) || (string.IsNullOrEmpty(existingStr.DisplayName) && !string.IsNullOrEmpty(info.DisplayName)))
                {
                    enumStrCatalog[info.EnumStr] = info;
                }
            }
        }

        private CachedConversationData ParseConversationDb(string cid, string dbPath)
        {
            CachedConversationData data = new CachedConversationData();
            data.ConversationId = cid;

            try
            {
                using (ReadOnlySqlite db = ReadOnlySqlite.Open(dbPath))
                {
                    if (db == null)
                    {
                        return null;
                    }

                    using (SqliteQuery q = db.Prepare("SELECT idx, data FROM gen_metadata ORDER BY idx"))
                    {
                        if (q != null)
                        {
                            while (q.StepRow())
                            {
                                byte[] blob = q.GetBlob(1);
                                if (blob == null || blob.Length == 0)
                                {
                                    continue;
                                }
                                LearnedModelInfo gmModel = DecodeGenMetadataModel(blob);
                                if (gmModel != null)
                                {
                                    data.LearnedModels.Add(gmModel);
                                    data.LatestModelEnumInt = gmModel.EnumInt;
                                    data.LatestModelEnumStr = gmModel.EnumStr;
                                    data.LatestModelId = gmModel.ModelId;
                                    data.LatestDisplayName = gmModel.DisplayName;
                                }
                            }
                        }
                    }

                    using (SqliteQuery q = db.Prepare("SELECT idx, metadata FROM steps WHERE metadata IS NOT NULL ORDER BY idx"))
                    {
                        if (q != null)
                        {
                            while (q.StepRow())
                            {
                                byte[] blob = q.GetBlob(1);
                                if (blob == null || blob.Length == 0)
                                {
                                    continue;
                                }
                                StepUsageEntry entry;
                                DateTime utcTime;
                                if (DecodeStepMetadataUsage(blob, out entry, out utcTime))
                                {
                                    data.Steps.Add(entry);
                                    if (utcTime > data.LatestTimestampUtc)
                                    {
                                        data.LatestTimestampUtc = utcTime;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                return null;
            }

            return data;
        }

        private static LearnedModelInfo DecodeGenMetadataModel(byte[] blob)
        {
            int pos = 0;
            int end = blob.Length;
            while (pos < end)
            {
                int fn, wt, sOff, sLen;
                ulong v;
                if (!ProtoWire.TryReadField(blob, ref pos, end, out fn, out wt, out v, out sOff, out sLen))
                {
                    break;
                }
                if (fn == 1 && wt == 2)
                {
                    LearnedModelInfo info = new LearnedModelInfo();
                    int p2 = sOff;
                    int e2 = sOff + sLen;
                    while (p2 < e2)
                    {
                        int fn2, wt2, off2, len2;
                        ulong v2;
                        if (!ProtoWire.TryReadField(blob, ref p2, e2, out fn2, out wt2, out v2, out off2, out len2))
                        {
                            break;
                        }
                        if (fn2 == 3 && wt2 == 0)
                        {
                            info.EnumInt = (int)v2;
                        }
                        else if (fn2 == 19 && wt2 == 2)
                        {
                            info.ModelId = ProtoWire.ReadUtf8(blob, off2, len2);
                        }
                        else if (fn2 == 21 && wt2 == 2)
                        {
                            info.DisplayName = ProtoWire.ReadUtf8(blob, off2, len2);
                        }
                        else if (fn2 == 20 && wt2 == 2)
                        {
                            string mk, mv;
                            DecodeMapEntry(blob, off2, len2, out mk, out mv);
                            if (string.Equals(mk, "model_enum", StringComparison.Ordinal))
                            {
                                info.EnumStr = mv;
                            }
                        }
                    }
                    if (info.EnumInt > 0 || !string.IsNullOrEmpty(info.ModelId) || !string.IsNullOrEmpty(info.DisplayName) || !string.IsNullOrEmpty(info.EnumStr))
                    {
                        return info;
                    }
                }
            }
            return null;
        }

        private static void DecodeMapEntry(byte[] blob, int offset, int len, out string key, out string val)
        {
            key = "";
            val = "";
            int p = offset;
            int e = offset + len;
            while (p < e)
            {
                int fn, wt, off, slen;
                ulong v;
                if (!ProtoWire.TryReadField(blob, ref p, e, out fn, out wt, out v, out off, out slen))
                {
                    break;
                }
                if (fn == 1 && wt == 2)
                {
                    key = ProtoWire.ReadUtf8(blob, off, slen);
                }
                else if (fn == 2 && wt == 2)
                {
                    val = ProtoWire.ReadUtf8(blob, off, slen);
                }
            }
        }

        private static bool DecodeStepMetadataUsage(byte[] blob, out StepUsageEntry entry, out DateTime utcTime)
        {
            entry = null;
            utcTime = DateTime.MinValue;
            long epochSec = 0;
            int stepModelEnum = 0;
            bool hasUsage = false;
            StepUsageEntry tmp = new StepUsageEntry();

            int pos = 0;
            int end = blob.Length;
            while (pos < end)
            {
                int fn, wt, sOff, sLen;
                ulong v;
                if (!ProtoWire.TryReadField(blob, ref pos, end, out fn, out wt, out v, out sOff, out sLen))
                {
                    break;
                }
                if (fn == 1 && wt == 2)
                {
                    int tp = sOff;
                    int te = sOff + sLen;
                    while (tp < te)
                    {
                        int tfn, twt, toff, tlen;
                        ulong tv;
                        if (!ProtoWire.TryReadField(blob, ref tp, te, out tfn, out twt, out tv, out toff, out tlen))
                        {
                            break;
                        }
                        if (tfn == 1 && twt == 0)
                        {
                            epochSec = (long)tv;
                        }
                    }
                }
                else if (fn == 9 && wt == 2)
                {
                    hasUsage = DecodeUsageMessage(blob, sOff, sLen, tmp);
                }
                else if (fn == 11 && wt == 0)
                {
                    stepModelEnum = (int)v;
                }
            }

            if (!hasUsage)
            {
                return false;
            }
            if (tmp.ModelEnumInt <= 0 && stepModelEnum > 0)
            {
                tmp.ModelEnumInt = stepModelEnum;
            }
            if (epochSec > 0)
            {
                utcTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(epochSec);
                tmp.LocalTime = utcTime.ToLocalTime();
            }
            else
            {
                tmp.LocalTime = DateTime.MinValue;
            }
            entry = tmp;
            return true;
        }

        private static bool DecodeUsageMessage(byte[] blob, int offset, int len, StepUsageEntry dst)
        {
            int p = offset;
            int e = offset + len;
            long totalOutput = 0;
            bool anyField = false;

            while (p < e)
            {
                int fn, wt, off, slen;
                ulong v;
                if (!ProtoWire.TryReadField(blob, ref p, e, out fn, out wt, out v, out off, out slen))
                {
                    break;
                }
                if (wt != 0)
                {
                    continue;
                }
                anyField = true;
                long iv = (long)v;
                if (fn == 1) { dst.ModelEnumInt = (int)iv; }
                else if (fn == 2) { dst.UncachedPromptTokens = iv; }
                else if (fn == 3) { totalOutput = iv; }
                else if (fn == 4) { dst.CacheCreationTokens = iv; }
                else if (fn == 5) { dst.CachedContentTokens = iv; }
                else if (fn == 9) { dst.ThoughtsTokens = iv; }
                else if (fn == 10) { dst.CandidatesTokens = iv; }
            }

            if (dst.ThoughtsTokens == 0 && dst.CandidatesTokens == 0 && totalOutput > 0)
            {
                dst.CandidatesTokens = totalOutput;
            }

            return anyField && (dst.UncachedPromptTokens > 0 || dst.CachedContentTokens > 0 || dst.CandidatesTokens > 0 || dst.ThoughtsTokens > 0 || totalOutput > 0);
        }
    }
}
