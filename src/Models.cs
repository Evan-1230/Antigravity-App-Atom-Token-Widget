using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace AntigravityAtomTokenWidget
{
    public enum AtomElement
    {
        Unsupported = 0,
        Hydrogen = 1,
        Carbon = 6,
        Neon = 10,
        Argon = 18
    }

    public enum TokenScope
    {
        CurrentConversation = 0,
        Today = 1,
        AllTime = 2
    }

    public enum UiLanguage
    {
        ZhTw = 0,
        En = 1
    }

    public sealed class TokenBreakdown
    {
        public long PromptTokensUncached;
        public long CachedContentTokens;
        public long CandidatesTokens;
        public long ThoughtsTokens;
        public long CacheCreationTokens;
        public long LatestContextTokens;
        public int CallCount;
        public int SubagentCount;
        public double EquivalentApiValueUsd;
        public int UnpricedCallCount;

        public long PromptTokenCount
        {
            get { return PromptTokensUncached + CachedContentTokens; }
        }

        public long OutputTotalTokens
        {
            get { return CandidatesTokens + ThoughtsTokens; }
        }

        public long TotalTokenCount
        {
            get
            {
                return PromptTokensUncached
                    + CachedContentTokens
                    + CandidatesTokens
                    + ThoughtsTokens
                    + CacheCreationTokens;
            }
        }

        public void AddUsage(long uncachedPrompt, long cachedContent, long candidates, long thoughts, long cacheCreation)
        {
            AddUsage(uncachedPrompt, cachedContent, candidates, thoughts, cacheCreation, 0.0, false);
        }

        public void AddUsage(long uncachedPrompt, long cachedContent, long candidates, long thoughts, long cacheCreation, double stepCostUsd, bool isPriced)
        {
            PromptTokensUncached += uncachedPrompt;
            CachedContentTokens += cachedContent;
            CandidatesTokens += candidates;
            ThoughtsTokens += thoughts;
            CacheCreationTokens += cacheCreation;
            CallCount++;
            if (isPriced && stepCostUsd >= 0.0 && !double.IsNaN(stepCostUsd) && !double.IsInfinity(stepCostUsd))
            {
                EquivalentApiValueUsd += stepCostUsd;
            }
            else if (!isPriced)
            {
                UnpricedCallCount++;
            }
        }

        public TokenBreakdown Clone()
        {
            TokenBreakdown copy = new TokenBreakdown();
            copy.PromptTokensUncached = this.PromptTokensUncached;
            copy.CachedContentTokens = this.CachedContentTokens;
            copy.CandidatesTokens = this.CandidatesTokens;
            copy.ThoughtsTokens = this.ThoughtsTokens;
            copy.CacheCreationTokens = this.CacheCreationTokens;
            copy.LatestContextTokens = this.LatestContextTokens;
            copy.CallCount = this.CallCount;
            copy.SubagentCount = this.SubagentCount;
            copy.EquivalentApiValueUsd = this.EquivalentApiValueUsd;
            copy.UnpricedCallCount = this.UnpricedCallCount;
            return copy;
        }
    }

    public sealed class ActiveModelState
    {
        public AtomElement Element = AtomElement.Unsupported;
        public AtomElement DetectedElement = AtomElement.Unsupported;
        public AtomElement? PreviewOverrideElement = null;
        public bool IsOrchestrator = false;
        public string DisplayName = "";
        public string ModelId = "";
        public string DetectionSource = "";
        public bool IsRunning = false;

        public ActiveModelState Clone()
        {
            ActiveModelState copy = new ActiveModelState();
            copy.Element = this.Element;
            copy.DetectedElement = this.DetectedElement;
            copy.PreviewOverrideElement = this.PreviewOverrideElement;
            copy.IsOrchestrator = this.IsOrchestrator;
            copy.DisplayName = this.DisplayName;
            copy.ModelId = this.ModelId;
            copy.DetectionSource = this.DetectionSource;
            copy.IsRunning = this.IsRunning;
            return copy;
        }
    }

    public sealed class WidgetSettings
    {
        private const string SettingsFileName = "widget_settings.ini";

        public const double MinScale = 0.80;
        public const double MaxScale = 1.60;
        public static readonly double[] ScalePresets = new double[] { 0.80, 1.00, 1.20, 1.40, 1.60 };

        public static double ClampScale(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 1.00;
            }
            double clamped = Math.Max(MinScale, Math.Min(MaxScale, value));
            return Math.Round(clamped * 100.0) / 100.0;
        }

        public static int TryGetExactScalePresetIndex(double scale)
        {
            for (int i = 0; i < ScalePresets.Length; i++)
            {
                if (Math.Abs(scale - ScalePresets[i]) <= 0.005)
                {
                    return i;
                }
            }
            return -1;
        }

        public TokenScope Scope = TokenScope.CurrentConversation;
        public bool IncludeSubagents = true;
        public bool ShowEquivalentApiValue = false;
        public AtomElement? ManualElementOverride = null;
        public bool EnableAnimation = true;
        public bool IsWidgetHidden = false;
        public double ScaleMultiplier = 1.0;
        public double AnchorNormX = 0.955;
        public double AnchorNormY = 0.965;
        public UiLanguage Language = DetectDefaultLanguage();

        private static UiLanguage DetectDefaultLanguage()
        {
            try
            {
                if (CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return UiLanguage.ZhTw;
                }
            }
            catch
            {
            }
            return UiLanguage.En;
        }

        public static WidgetSettings Load(string baseDir)
        {
            WidgetSettings s = new WidgetSettings();
            try
            {
                string path = Path.Combine(baseDir, SettingsFileName);
                if (!File.Exists(path))
                {
                    return s;
                }
                string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));
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
                        continue;
                    }
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (string.Equals(k, "Scope", StringComparison.OrdinalIgnoreCase))
                    {
                        int iv;
                        if (int.TryParse(v, out iv) && iv >= 0 && iv <= 2)
                        {
                            s.Scope = (TokenScope)iv;
                        }
                    }
                    else if (string.Equals(k, "IncludeSubagents", StringComparison.OrdinalIgnoreCase))
                    {
                        bool bv;
                        if (bool.TryParse(v, out bv))
                        {
                            s.IncludeSubagents = bv;
                        }
                    }
                    else if (string.Equals(k, "ShowEquivalentApiValue", StringComparison.OrdinalIgnoreCase))
                    {
                        bool bv;
                        if (bool.TryParse(v, out bv))
                        {
                            s.ShowEquivalentApiValue = bv;
                        }
                    }
                    else if (string.Equals(k, "ManualElementOverride", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(v) || string.Equals(v, "Auto", StringComparison.OrdinalIgnoreCase))
                        {
                            s.ManualElementOverride = null;
                        }
                        else
                        {
                            int ev;
                            if (int.TryParse(v, out ev))
                            {
                                if (ev == 1 || ev == 6 || ev == 10 || ev == 18)
                                {
                                    s.ManualElementOverride = (AtomElement)ev;
                                }
                            }
                        }
                    }
                    else if (string.Equals(k, "ManualOrchestratorPreview", StringComparison.OrdinalIgnoreCase))
                    {
                        // Ignored: Orchestrator is now 100% auto-adaptive based on the live model state.
                    }
                    else if (string.Equals(k, "EnableAnimation", StringComparison.OrdinalIgnoreCase))
                    {
                        bool bv;
                        if (bool.TryParse(v, out bv))
                        {
                            s.EnableAnimation = bv;
                        }
                    }
                    else if (string.Equals(k, "ScaleMultiplier", StringComparison.OrdinalIgnoreCase))
                    {
                        double dv;
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                        {
                            s.ScaleMultiplier = ClampScale(dv);
                        }
                    }
                    else if (string.Equals(k, "AnchorNormX", StringComparison.OrdinalIgnoreCase))
                    {
                        double dx;
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dx))
                        {
                            s.AnchorNormX = Math.Max(0.02, Math.Min(0.99, dx));
                        }
                    }
                    else if (string.Equals(k, "AnchorNormY", StringComparison.OrdinalIgnoreCase))
                    {
                        double dy;
                        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dy))
                        {
                            s.AnchorNormY = Math.Max(0.04, Math.Min(0.99, dy));
                        }
                    }
                    else if (string.Equals(k, "Language", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(v, "en", StringComparison.OrdinalIgnoreCase))
                        {
                            s.Language = UiLanguage.En;
                        }
                        else if (string.Equals(v, "zh", StringComparison.OrdinalIgnoreCase))
                        {
                            s.Language = UiLanguage.ZhTw;
                        }
                    }
                }
            }
            catch
            {
            }
            return s;
        }

        public void Save(string baseDir)
        {
            try
            {
                if (!Directory.Exists(baseDir))
                {
                    Directory.CreateDirectory(baseDir);
                }
                string path = Path.Combine(baseDir, SettingsFileName);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Scope=" + ((int)Scope).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("IncludeSubagents=" + (IncludeSubagents ? "true" : "false"));
                sb.AppendLine("ShowEquivalentApiValue=" + (ShowEquivalentApiValue ? "true" : "false"));
                sb.AppendLine("ManualElementOverride=" + (ManualElementOverride.HasValue ? ((int)ManualElementOverride.Value).ToString(CultureInfo.InvariantCulture) : "Auto"));
                sb.AppendLine("EnableAnimation=" + (EnableAnimation ? "true" : "false"));
                sb.AppendLine("ScaleMultiplier=" + ScaleMultiplier.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("AnchorNormX=" + AnchorNormX.ToString("0.#####", CultureInfo.InvariantCulture));
                sb.AppendLine("AnchorNormY=" + AnchorNormY.ToString("0.#####", CultureInfo.InvariantCulture));
                sb.AppendLine("Language=" + (Language == UiLanguage.ZhTw ? "zh" : "en"));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }

    public static class ModelNameNormalizer
    {
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex TrailingChromeRegex = new Regex(@"\s+(?:model|selector|dropdown|button|picker|menu)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StandaloneTierRegex = new Regex(@"^(?:Extra\s+High|Very\s+High|Extended\s+Thinking|XHigh|High|Medium|Low|Minimal|Thinking|Max|Fast)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TrailingTierRegex = new Regex(@"^(?<base>.+?)\s+(?<tier>Extra\s+High|Very\s+High|Extended\s+Thinking|XHigh|High|Medium|Low|Minimal|Thinking|Max|Fast)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool ContainsOrchestratorWord(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            return text.IndexOf("orchestrator", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string CleanAndDeduplicate(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return "";
            }

            string s = raw
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Replace("\u25BE", " ")
                .Replace("\u25BC", " ")
                .Replace("\u2304", " ")
                .Replace("\u25B8", " ")
                .Replace("\u203A", " ")
                .Replace("\u26A1", " ");

            s = WhitespaceRegex.Replace(s, " ").Trim();
            if (s.StartsWith("model:", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(6).Trim();
            }
            if (s.Length >= 5)
            {
                string stripped = TrailingChromeRegex.Replace(s, "").Trim();
                if (stripped.Length >= 3)
                {
                    s = stripped;
                }
            }

            // Tokenize & remove duplicated repeated phrases (including truncated second occurrences)
            string[] rawTokens = s.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> dedup = new List<string>();
            for (int i = 0; i < rawTokens.Length; i++)
            {
                string tok = rawTokens[i].Trim();
                if (tok.Length == 0)
                {
                    continue;
                }
                if (dedup.Count > 0 && string.Equals(dedup[dedup.Count - 1], tok, StringComparison.OrdinalIgnoreCase))
                {
                    bool hasLetter = false;
                    for (int c = 0; c < tok.Length; c++)
                    {
                        if (char.IsLetter(tok[c])) { hasLetter = true; break; }
                    }
                    if (hasLetter)
                    {
                        continue;
                    }
                }
                dedup.Add(tok);
            }

            // Detect adjacent repeated blocks of length k >= 2 anywhere, or trailing prefix repetition at index j >= 2
            int n = dedup.Count;
            if (n >= 4)
            {
                for (int j = 2; j <= n - 2; j++)
                {
                    // Case A: Exact block of length k repeating [start..start+k) == [start+k..start+2k)
                    int maxK = Math.Min(j, n - j);
                    if (maxK >= 2)
                    {
                        bool exactMatch = true;
                        for (int m = 0; m < maxK; m++)
                        {
                            if (!string.Equals(dedup[j - maxK + m], dedup[j + m], StringComparison.OrdinalIgnoreCase))
                            {
                                exactMatch = false;
                                break;
                            }
                        }
                        if (exactMatch && (j - maxK) == 0)
                        {
                            // Phrase starting at 0 repeats completely at j
                            if (j + maxK == n)
                            {
                                dedup.RemoveRange(j, maxK);
                                n = dedup.Count;
                                break;
                            }
                        }
                    }

                    // Case B: Trailing slice [j..n) echoes prefix [0..n-j), even if the last token is truncated (e.g. "5." of "5.5")
                    int rem = n - j;
                    if (rem >= 2 && j >= rem)
                    {
                        bool prefixRepeat = true;
                        for (int m = 0; m < rem; m++)
                        {
                            string a = dedup[m].TrimEnd('.', ',', ':', ';');
                            string b = dedup[j + m].TrimEnd('.', ',', ':', ';');
                            if (m < rem - 1)
                            {
                                if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                                {
                                    prefixRepeat = false;
                                    break;
                                }
                            }
                            else
                            {
                                bool isTruncatedMatch = string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
                                    || (b.Length >= 1 && a.StartsWith(b, StringComparison.OrdinalIgnoreCase));
                                if (!isTruncatedMatch)
                                {
                                    prefixRepeat = false;
                                    break;
                                }
                            }
                        }
                        if (prefixRepeat)
                        {
                            dedup.RemoveRange(j, rem);
                            n = dedup.Count;
                            break;
                        }
                    }
                }
            }

            s = string.Join(" ", dedup.ToArray()).Trim();
            s = s.TrimEnd(',', ';', '|', '\u00B7', ':', '-').Trim();

            if (StandaloneTierRegex.IsMatch(s))
            {
                return CanonicalizeTier(WhitespaceRegex.Replace(s, " "));
            }

            // Normalize unparenthesized thinking level suffix into clean "Base (Tier)" format
            Match tm = TrailingTierRegex.Match(s);
            if (tm.Success)
            {
                string baseName = tm.Groups["base"].Value.Trim();
                string rawTier = WhitespaceRegex.Replace(tm.Groups["tier"].Value.Trim(), " ");
                if (baseName.Length >= 3 && !baseName.EndsWith("(") && !baseName.EndsWith("[") && !baseName.EndsWith("-"))
                {
                    s = baseName + " (" + CanonicalizeTier(rawTier) + ")";
                }
            }

            return s;
        }

        private static string CanonicalizeTier(string rawTier)
        {
            string low = rawTier.Trim().ToLowerInvariant();
            if (low == "extra high") { return "Extra High"; }
            if (low == "very high") { return "Very High"; }
            if (low == "extended thinking") { return "Extended Thinking"; }
            if (low == "xhigh") { return "XHigh"; }
            if (low == "high") { return "High"; }
            if (low == "medium") { return "Medium"; }
            if (low == "low") { return "Low"; }
            if (low == "minimal") { return "Minimal"; }
            if (low == "thinking") { return "Thinking"; }
            if (low == "max") { return "Max"; }
            if (low == "fast") { return "Fast"; }
            return rawTier;
        }
    }

    public sealed class ModelClassifyResult
    {
        public AtomElement Element = AtomElement.Unsupported;
        public bool IsOrchestrator = false;
        public string NormalizedDisplayName = "";
    }

    public static class ModelClassifier
    {
        private sealed class SupportedElementRule
        {
            public readonly AtomElement Element;
            public readonly Regex[] KeywordRegexes;
            public readonly string[] ModelIdMarkers;
            public readonly int[] EnumInts;
            public readonly string[] EnumStrs;
            public readonly string FallbackBaseName;

            public SupportedElementRule(AtomElement element, string fallbackBaseName, string[] wholeWords, string[] modelIdMarkers, int[] enumInts, string[] enumStrs)
            {
                this.Element = element;
                this.FallbackBaseName = fallbackBaseName;
                this.ModelIdMarkers = modelIdMarkers ?? new string[0];
                this.EnumInts = enumInts ?? new int[0];
                this.EnumStrs = enumStrs ?? new string[0];

                if (wholeWords != null && wholeWords.Length > 0)
                {
                    this.KeywordRegexes = new Regex[wholeWords.Length];
                    for (int i = 0; i < wholeWords.Length; i++)
                    {
                        this.KeywordRegexes[i] = new Regex(@"\b" + Regex.Escape(wholeWords[i]) + @"\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    }
                }
                else
                {
                    this.KeywordRegexes = new Regex[0];
                }
            }
        }

        // Extensible 3D Model Library: add new Gemini 4 elements here alongside Atom3DRenderer & ElementVisualCatalog
        private static readonly SupportedElementRule[] SupportedLibrary = new SupportedElementRule[]
        {
            new SupportedElementRule(
                AtomElement.Neon,
                "Gemini 4 Neon",
                new string[] { "neon" },
                new string[0],
                new int[] { 1535, 1537 },
                new string[] { "MODEL_PLACEHOLDER_M535", "MODEL_PLACEHOLDER_M537" }),
            new SupportedElementRule(
                AtomElement.Argon,
                "Gemini 4 Argon",
                new string[] { "argon" },
                new string[] { "orchestrator-g1-pro" },
                new int[] { 1417, 1418, 1668 },
                new string[] { "MODEL_PLACEHOLDER_M417", "MODEL_PLACEHOLDER_M418", "MODEL_PLACEHOLDER_M668" }),
            new SupportedElementRule(
                AtomElement.Carbon,
                "Gemini 4 Carbon",
                new string[] { "carbon" },
                new string[0],
                new int[0],
                new string[0]),
            new SupportedElementRule(
                AtomElement.Hydrogen,
                "Gemini 4 Hydrogen",
                new string[] { "hydrogen" },
                new string[] { "flash-worker-agy" },
                new int[] { 1528, 1530, 1669 },
                new string[] { "MODEL_PLACEHOLDER_M528", "MODEL_PLACEHOLDER_M530", "MODEL_PLACEHOLDER_M669" })
        };

        public static ModelClassifyResult Classify(string displayName, string modelId, string modelEnumStr, int modelEnumInt)
        {
            ModelClassifyResult res = new ModelClassifyResult();
            string disp = ModelNameNormalizer.CleanAndDeduplicate(displayName);
            string id = (modelId ?? "").Trim();
            string enumStr = (modelEnumStr ?? "").Trim();
            string combined = (disp + " " + id + " " + enumStr).ToLowerInvariant();

            if (combined.Trim().Length == 0 && modelEnumInt <= 0)
            {
                res.Element = AtomElement.Unsupported;
                res.NormalizedDisplayName = "";
                return res;
            }

            // Detect Orchestrator flag before element/exclusion check
            res.IsOrchestrator = combined.IndexOf("orchestrator", StringComparison.Ordinal) >= 0
                || combined.IndexOf("flash-worker-agy", StringComparison.Ordinal) >= 0
                || combined.IndexOf("orchestrator-g1-pro", StringComparison.Ordinal) >= 0;

            string rawFallback = disp.Length > 0 ? disp : (id.Length > 0 ? id : enumStr);

            // Explicit non-Gemini-4 exclusions (all thinking levels of Claude, Gemini 2/3/5, GPT, DeepSeek, Llama, Qwen, Grok, etc.)
            if (combined.IndexOf("claude", StringComparison.Ordinal) >= 0
                || combined.IndexOf("opus", StringComparison.Ordinal) >= 0
                || combined.IndexOf("sonnet", StringComparison.Ordinal) >= 0
                || combined.IndexOf("haiku", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gemini-3", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gemini 3", StringComparison.Ordinal) >= 0
                || combined.IndexOf("3.8 flash", StringComparison.Ordinal) >= 0
                || combined.IndexOf("3.5 flash", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gemini-2", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gemini 2", StringComparison.Ordinal) >= 0
                || combined.IndexOf("2.5 pro", StringComparison.Ordinal) >= 0
                || combined.IndexOf("2.5 flash", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gemini-5", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gemini 5", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gpt-", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gpt_oss", StringComparison.Ordinal) >= 0
                || combined.IndexOf("openai", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gpt ", StringComparison.Ordinal) >= 0
                || combined.IndexOf("llama", StringComparison.Ordinal) >= 0
                || combined.IndexOf("qwen", StringComparison.Ordinal) >= 0
                || combined.IndexOf("mistral", StringComparison.Ordinal) >= 0
                || combined.IndexOf("deepseek", StringComparison.Ordinal) >= 0
                || combined.IndexOf("grok", StringComparison.Ordinal) >= 0
                || combined.IndexOf("o1", StringComparison.Ordinal) >= 0
                || combined.IndexOf("o3", StringComparison.Ordinal) >= 0
                || combined.IndexOf("o4", StringComparison.Ordinal) >= 0
                || modelEnumInt == 1318
                || modelEnumInt == 1322
                || modelEnumInt == 1401
                || modelEnumInt == 1402)
            {
                res.Element = AtomElement.Unsupported;
                res.NormalizedDisplayName = rawFallback;
                return res;
            }

            // Match against extensible 3D Model Library
            for (int r = 0; r < SupportedLibrary.Length; r++)
            {
                SupportedElementRule rule = SupportedLibrary[r];
                bool hit = false;

                for (int i = 0; i < rule.KeywordRegexes.Length; i++)
                {
                    if (rule.KeywordRegexes[i].IsMatch(combined))
                    {
                        hit = true;
                        break;
                    }
                }
                if (!hit)
                {
                    for (int i = 0; i < rule.ModelIdMarkers.Length; i++)
                    {
                        if (combined.IndexOf(rule.ModelIdMarkers[i], StringComparison.Ordinal) >= 0)
                        {
                            hit = true;
                            break;
                        }
                    }
                }
                if (!hit && modelEnumInt > 0)
                {
                    for (int i = 0; i < rule.EnumInts.Length; i++)
                    {
                        if (rule.EnumInts[i] == modelEnumInt)
                        {
                            hit = true;
                            break;
                        }
                    }
                }
                if (!hit && enumStr.Length > 0)
                {
                    for (int i = 0; i < rule.EnumStrs.Length; i++)
                    {
                        if (string.Equals(rule.EnumStrs[i], enumStr, StringComparison.OrdinalIgnoreCase))
                        {
                            hit = true;
                            break;
                        }
                    }
                }

                if (hit)
                {
                    res.Element = rule.Element;
                    if (disp.Length > 0)
                    {
                        res.NormalizedDisplayName = disp;
                    }
                    else if (rule.Element == AtomElement.Argon && (modelEnumInt == 1417 || string.Equals(enumStr, "MODEL_PLACEHOLDER_M417", StringComparison.OrdinalIgnoreCase)))
                    {
                        res.NormalizedDisplayName = "Gemini 4 Argon (High)";
                    }
                    else if (rule.Element == AtomElement.Argon && (modelEnumInt == 1418 || string.Equals(enumStr, "MODEL_PLACEHOLDER_M418", StringComparison.OrdinalIgnoreCase)))
                    {
                        res.NormalizedDisplayName = "Gemini 4 Argon (Medium)";
                    }
                    else if (rule.Element == AtomElement.Hydrogen && (modelEnumInt == 1528 || string.Equals(enumStr, "MODEL_PLACEHOLDER_M528", StringComparison.OrdinalIgnoreCase)))
                    {
                        res.NormalizedDisplayName = "Gemini 4 Hydrogen (XHigh)";
                    }
                    else if (res.IsOrchestrator)
                    {
                        ElementVisualInfo ev = ElementVisualCatalog.GetInfo(rule.Element);
                        res.NormalizedDisplayName = "Orchestrator (" + ev.EnglishName + ")";
                    }
                    else
                    {
                        res.NormalizedDisplayName = rule.FallbackBaseName;
                    }
                    return res;
                }
            }

            // Future Gemini element or unknown model: default to Unsupported animation while preserving full detected name
            res.Element = AtomElement.Unsupported;
            res.NormalizedDisplayName = rawFallback;
            return res;
        }
    }

    public sealed class ElementVisualInfo
    {
        public string Symbol;
        public string EnglishName;
        public string ChineseName;
        public int AtomicNumber;
        public int Protons;
        public int Neutrons;
        public int Electrons;
        public string ShellConfig;

        // Exact Google AI Studio Periodic Table CSS colors (Light & Dark)
        public Color LightTileBg;
        public Color LightSymbolInk;
        public Color LightEdgeLip;
        public Color LightAccent;

        public Color DarkTileBg;
        public Color DarkSymbolInk;
        public Color DarkEdgeLip;
        public Color DarkAccent;
    }

    public static class ElementVisualCatalog
    {
        // Central list of modeled Gemini 4 elements; adding a 5th+ element here automatically populates
        // the horizontally scrollable model strip in SettingsWindow.
        public static readonly AtomElement[] SupportedElements = new AtomElement[]
        {
            AtomElement.Hydrogen,
            AtomElement.Carbon,
            AtomElement.Neon,
            AtomElement.Argon
        };

        public static ElementVisualInfo GetInfo(AtomElement element)
        {
            ElementVisualInfo info = new ElementVisualInfo();
            switch (element)
            {
                case AtomElement.Hydrogen:
                    info.Symbol = "H";
                    info.EnglishName = "Hydrogen";
                    info.ChineseName = "氫";
                    info.AtomicNumber = 1;
                    info.Protons = 1;
                    info.Neutrons = 0;
                    info.Electrons = 1;
                    info.ShellConfig = "K:1";
                    info.LightTileBg = Color.FromRgb(220, 224, 236);    // #DCE0EC
                    info.LightSymbolInk = Color.FromRgb(30, 55, 119);   // #1E3777
                    info.LightEdgeLip = Color.FromRgb(198, 205, 224);   // #C6CDE0
                    info.LightAccent = Color.FromRgb(36, 131, 226);     // #2483E2
                    info.DarkTileBg = Color.FromRgb(59, 61, 66);        // #3B3D42
                    info.DarkSymbolInk = Color.FromRgb(167, 187, 237);  // #A7BBED
                    info.DarkEdgeLip = Color.FromRgb(38, 39, 43);       // #26272B
                    info.DarkAccent = Color.FromRgb(135, 169, 255);     // #87A9FF
                    return info;

                case AtomElement.Carbon:
                    info.Symbol = "C";
                    info.EnglishName = "Carbon";
                    info.ChineseName = "碳";
                    info.AtomicNumber = 6;
                    info.Protons = 6;
                    info.Neutrons = 6;
                    info.Electrons = 6;
                    info.ShellConfig = "K:2 · L:4";
                    info.LightTileBg = Color.FromRgb(238, 221, 253);    // #EEDDFD
                    info.LightSymbolInk = Color.FromRgb(100, 57, 143);  // #64398F
                    info.LightEdgeLip = Color.FromRgb(229, 204, 253);   // #E5CCFD
                    info.LightAccent = Color.FromRgb(168, 85, 247);     // #A855F7
                    info.DarkTileBg = Color.FromRgb(59, 53, 68);        // #3B3544
                    info.DarkSymbolInk = Color.FromRgb(203, 175, 238);  // #CBAFEE
                    info.DarkEdgeLip = Color.FromRgb(39, 35, 43);       // #27232B
                    info.DarkAccent = Color.FromRgb(197, 151, 255);     // #C597FF
                    return info;

                case AtomElement.Neon:
                    info.Symbol = "Ne";
                    info.EnglishName = "Neon";
                    info.ChineseName = "氖";
                    info.AtomicNumber = 10;
                    info.Protons = 10;
                    info.Neutrons = 10;
                    info.Electrons = 10;
                    info.ShellConfig = "K:2 · L:8";
                    info.LightTileBg = Color.FromRgb(226, 105, 75);     // #E2694B
                    info.LightSymbolInk = Color.FromRgb(255, 255, 255); // #FFFFFF
                    info.LightEdgeLip = Color.FromRgb(178, 86, 64);     // #B25640
                    info.LightAccent = Color.FromRgb(226, 105, 75);     // #E2694B
                    info.DarkTileBg = Color.FromRgb(183, 82, 63);       // #B7523F
                    info.DarkSymbolInk = Color.FromRgb(255, 255, 255);  // #FFFFFF
                    info.DarkEdgeLip = Color.FromRgb(123, 60, 49);      // #7B3C31
                    info.DarkAccent = Color.FromRgb(226, 105, 75);      // #E2694B
                    return info;

                case AtomElement.Argon:
                    info.Symbol = "Ar";
                    info.EnglishName = "Argon";
                    info.ChineseName = "氬";
                    info.AtomicNumber = 18;
                    info.Protons = 18;
                    info.Neutrons = 22;
                    info.Electrons = 18;
                    info.ShellConfig = "K:2 · L:8 · M:8";
                    info.LightTileBg = Color.FromRgb(214, 235, 220);    // #D6EBDC
                    info.LightSymbolInk = Color.FromRgb(47, 107, 65);   // #2F6B41
                    info.LightEdgeLip = Color.FromRgb(189, 223, 199);   // #BDDFC7
                    info.LightAccent = Color.FromRgb(60, 160, 89);      // #3CA059
                    info.DarkTileBg = Color.FromRgb(41, 66, 52);        // #294234
                    info.DarkSymbolInk = Color.FromRgb(140, 215, 174);  // #8CD7AE
                    info.DarkEdgeLip = Color.FromRgb(29, 44, 36);       // #1D2C24
                    info.DarkAccent = Color.FromRgb(61, 219, 133);      // #3DDB85
                    return info;

                default:
                    info.Symbol = "--";
                    info.EnglishName = "Standby";
                    info.ChineseName = "待機";
                    info.AtomicNumber = 0;
                    info.Protons = 0;
                    info.Neutrons = 0;
                    info.Electrons = 0;
                    info.ShellConfig = "Gemini 4 Only";
                    info.LightTileBg = Color.FromRgb(236, 238, 242);    // #ECEEF2
                    info.LightSymbolInk = Color.FromRgb(100, 116, 139); // #64748B
                    info.LightEdgeLip = Color.FromRgb(215, 216, 218);   // #D7D8DA
                    info.LightAccent = Color.FromRgb(148, 163, 184);    // #94A3B8
                    info.DarkTileBg = Color.FromRgb(46, 49, 56);        // #2E3138
                    info.DarkSymbolInk = Color.FromRgb(160, 170, 184);  // #A0AAB8
                    info.DarkEdgeLip = Color.FromRgb(31, 34, 39);       // #1F2227
                    info.DarkAccent = Color.FromRgb(148, 163, 184);     // #94A3B8
                    return info;
            }
        }
    }

    public sealed class ApiModelPricing
    {
        public readonly string PricingKey;
        public readonly double InputPerMillionUsd;
        public readonly double CachedInputPerMillionUsd;
        public readonly double CacheWritePerMillionUsd;
        public readonly double OutputPerMillionUsd;
        public readonly long HighTierPromptThreshold;
        public readonly double HighTierInputPerMillionUsd;
        public readonly double HighTierCachedInputPerMillionUsd;
        public readonly double HighTierCacheWritePerMillionUsd;
        public readonly double HighTierOutputPerMillionUsd;

        public ApiModelPricing(
            string pricingKey,
            double inputPerMillionUsd,
            double cachedInputPerMillionUsd,
            double cacheWritePerMillionUsd,
            double outputPerMillionUsd)
            : this(pricingKey, inputPerMillionUsd, cachedInputPerMillionUsd, cacheWritePerMillionUsd, outputPerMillionUsd, 0L, 0.0, 0.0, 0.0, 0.0)
        {
        }

        public ApiModelPricing(
            string pricingKey,
            double inputPerMillionUsd,
            double cachedInputPerMillionUsd,
            double cacheWritePerMillionUsd,
            double outputPerMillionUsd,
            long highTierPromptThreshold,
            double highTierInputPerMillionUsd,
            double highTierCachedInputPerMillionUsd,
            double highTierCacheWritePerMillionUsd,
            double highTierOutputPerMillionUsd)
        {
            this.PricingKey = pricingKey;
            this.InputPerMillionUsd = inputPerMillionUsd;
            this.CachedInputPerMillionUsd = cachedInputPerMillionUsd;
            this.CacheWritePerMillionUsd = cacheWritePerMillionUsd;
            this.OutputPerMillionUsd = outputPerMillionUsd;
            this.HighTierPromptThreshold = highTierPromptThreshold;
            this.HighTierInputPerMillionUsd = highTierInputPerMillionUsd;
            this.HighTierCachedInputPerMillionUsd = highTierCachedInputPerMillionUsd;
            this.HighTierCacheWritePerMillionUsd = highTierCacheWritePerMillionUsd;
            this.HighTierOutputPerMillionUsd = highTierOutputPerMillionUsd;
        }
    }

    public static class ApiPricingCatalog
    {
        // Standard 2026 USD list prices (per 1M tokens); Thoughts billed at Output rate.
        public static readonly ApiModelPricing Gemini4Hydrogen = new ApiModelPricing("gemini-4-hydrogen", 1.50, 0.075, 1.875, 7.50);
        public static readonly ApiModelPricing Gemini4Neon = new ApiModelPricing("gemini-4-neon", 3.00, 0.15, 3.75, 15.00);
        public static readonly ApiModelPricing Gemini4Carbon = new ApiModelPricing("gemini-4-carbon", 4.00, 0.20, 5.00, 20.00);
        public static readonly ApiModelPricing Gemini4Argon = new ApiModelPricing("gemini-4-argon", 4.00, 0.20, 5.00, 20.00);
        public static readonly ApiModelPricing Gemini38Flash = new ApiModelPricing("gemini-3.8-flash", 1.50, 0.15, 1.50, 7.50);
        public static readonly ApiModelPricing Gemini35Or25Flash = new ApiModelPricing("gemini-3.5-or-2.5-flash", 0.30, 0.03, 0.30, 2.50);
        public static readonly ApiModelPricing Gemini31Or3Pro = new ApiModelPricing("gemini-3.1-pro", 2.00, 0.20, 2.00, 12.00, 200000L, 4.00, 0.40, 4.00, 18.00);
        public static readonly ApiModelPricing Gemini25Pro = new ApiModelPricing("gemini-2.5-pro", 1.25, 0.125, 1.25, 10.00, 200000L, 2.50, 0.25, 2.50, 15.00);
        public static readonly ApiModelPricing ClaudeOpus55 = new ApiModelPricing("claude-opus-5.5", 4.00, 0.20, 5.00, 20.00);
        public static readonly ApiModelPricing ClaudeSonnet55 = new ApiModelPricing("claude-sonnet-5.5", 2.00, 0.10, 2.50, 10.00);
        public static readonly ApiModelPricing ClaudeOpus4x = new ApiModelPricing("claude-opus-4", 5.00, 0.50, 6.25, 25.00);
        public static readonly ApiModelPricing ClaudeSonnet4x = new ApiModelPricing("claude-sonnet-4", 3.00, 0.30, 3.75, 15.00);
        public static readonly ApiModelPricing ClaudeHaiku45 = new ApiModelPricing("claude-haiku-4.5", 1.00, 0.10, 1.25, 5.00);
        public static readonly ApiModelPricing OpenAiGptOss120B = new ApiModelPricing("gpt-oss-120b", 0.15, 0.075, 0.15, 0.60);

        public static bool TryResolvePricing(string displayName, string modelId, string modelEnumStr, int modelEnumInt, out ApiModelPricing pricing)
        {
            switch (modelEnumInt)
            {
                case 1528:
                case 1530:
                case 1669:
                    pricing = Gemini4Hydrogen;
                    return true;
                case 1535:
                case 1537:
                    pricing = Gemini4Neon;
                    return true;
                case 1417:
                case 1418:
                case 1668:
                    pricing = Gemini4Argon;
                    return true;
                case 1318:
                case 1322:
                    pricing = Gemini38Flash;
                    return true;
                case 1401:
                case 1402:
                    pricing = ClaudeOpus55;
                    return true;
            }

            string combined = ((modelId ?? "") + " " + (displayName ?? "") + " " + (modelEnumStr ?? "")).ToLowerInvariant();
            if (combined.IndexOf("hydrogen", StringComparison.Ordinal) >= 0 || combined.IndexOf("flash-worker-agy", StringComparison.Ordinal) >= 0)
            {
                pricing = Gemini4Hydrogen;
                return true;
            }
            if (combined.IndexOf("neon", StringComparison.Ordinal) >= 0 || combined.IndexOf("orchestrator-neon", StringComparison.Ordinal) >= 0)
            {
                pricing = Gemini4Neon;
                return true;
            }
            if (combined.IndexOf("carbon", StringComparison.Ordinal) >= 0)
            {
                pricing = Gemini4Carbon;
                return true;
            }
            if (combined.IndexOf("argon", StringComparison.Ordinal) >= 0 || combined.IndexOf("orchestrator-g1-pro", StringComparison.Ordinal) >= 0)
            {
                pricing = Gemini4Argon;
                return true;
            }
            if (combined.IndexOf("3.8-flash", StringComparison.Ordinal) >= 0
                || combined.IndexOf("3.8 flash", StringComparison.Ordinal) >= 0
                || combined.IndexOf("3_8_flash", StringComparison.Ordinal) >= 0)
            {
                pricing = Gemini38Flash;
                return true;
            }
            if (combined.IndexOf("3.5-flash", StringComparison.Ordinal) >= 0
                || combined.IndexOf("3.5 flash", StringComparison.Ordinal) >= 0
                || combined.IndexOf("2.5-flash", StringComparison.Ordinal) >= 0
                || combined.IndexOf("2.5 flash", StringComparison.Ordinal) >= 0
                || combined.IndexOf("flash-lite", StringComparison.Ordinal) >= 0)
            {
                pricing = Gemini35Or25Flash;
                return true;
            }
            if (combined.IndexOf("3.1-pro", StringComparison.Ordinal) >= 0
                || combined.IndexOf("3.1 pro", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gemini-3-pro", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gemini 3 pro", StringComparison.Ordinal) >= 0)
            {
                pricing = Gemini31Or3Pro;
                return true;
            }
            if (combined.IndexOf("2.5-pro", StringComparison.Ordinal) >= 0
                || combined.IndexOf("2.5 pro", StringComparison.Ordinal) >= 0)
            {
                pricing = Gemini25Pro;
                return true;
            }
            if (combined.IndexOf("opus-5-5", StringComparison.Ordinal) >= 0
                || combined.IndexOf("opus-5.5", StringComparison.Ordinal) >= 0
                || combined.IndexOf("opus 5.5", StringComparison.Ordinal) >= 0
                || combined.IndexOf("opus_5_5", StringComparison.Ordinal) >= 0)
            {
                pricing = ClaudeOpus55;
                return true;
            }
            if (combined.IndexOf("sonnet-5-5", StringComparison.Ordinal) >= 0
                || combined.IndexOf("sonnet-5.5", StringComparison.Ordinal) >= 0
                || combined.IndexOf("sonnet 5.5", StringComparison.Ordinal) >= 0
                || combined.IndexOf("sonnet_5_5", StringComparison.Ordinal) >= 0)
            {
                pricing = ClaudeSonnet55;
                return true;
            }
            if (combined.IndexOf("opus-4", StringComparison.Ordinal) >= 0
                || combined.IndexOf("opus 4", StringComparison.Ordinal) >= 0
                || (combined.IndexOf("claude", StringComparison.Ordinal) >= 0 && combined.IndexOf("opus", StringComparison.Ordinal) >= 0))
            {
                pricing = ClaudeOpus4x;
                return true;
            }
            if (combined.IndexOf("sonnet-4", StringComparison.Ordinal) >= 0
                || combined.IndexOf("sonnet 4", StringComparison.Ordinal) >= 0
                || (combined.IndexOf("claude", StringComparison.Ordinal) >= 0 && combined.IndexOf("sonnet", StringComparison.Ordinal) >= 0))
            {
                pricing = ClaudeSonnet4x;
                return true;
            }
            if (combined.IndexOf("haiku", StringComparison.Ordinal) >= 0)
            {
                pricing = ClaudeHaiku45;
                return true;
            }
            if (combined.IndexOf("gpt-oss-120b", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gpt_oss_120b", StringComparison.Ordinal) >= 0
                || combined.IndexOf("gpt-oss", StringComparison.Ordinal) >= 0)
            {
                pricing = OpenAiGptOss120B;
                return true;
            }

            pricing = null;
            return false;
        }

        public static double CalculateStepCostUsd(
            long uncachedPrompt,
            long cachedContent,
            long candidates,
            long thoughts,
            long cacheCreation,
            string displayName,
            string modelId,
            string modelEnumStr,
            int modelEnumInt,
            out bool isPriced)
        {
            ApiModelPricing p;
            if (!TryResolvePricing(displayName, modelId, modelEnumStr, modelEnumInt, out p))
            {
                isPriced = false;
                return 0.0;
            }

            long u = Math.Max(0L, uncachedPrompt);
            long c = Math.Max(0L, cachedContent);
            long cw = Math.Max(0L, cacheCreation);
            long cand = Math.Max(0L, candidates);
            long th = Math.Max(0L, thoughts);

            long totalPrompt = u + c + cw;
            bool highTier = (p.HighTierPromptThreshold > 0L && totalPrompt > p.HighTierPromptThreshold);

            double inRate = highTier ? p.HighTierInputPerMillionUsd : p.InputPerMillionUsd;
            double cachedRate = highTier ? p.HighTierCachedInputPerMillionUsd : p.CachedInputPerMillionUsd;
            double writeRate = highTier ? p.HighTierCacheWritePerMillionUsd : p.CacheWritePerMillionUsd;
            double outRate = highTier ? p.HighTierOutputPerMillionUsd : p.OutputPerMillionUsd;

            isPriced = true;
            return (u * inRate + c * cachedRate + cw * writeRate + (cand + th) * outRate) / 1000000.0;
        }
    }
}
