using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using WinForms = System.Windows.Forms;

namespace AntigravityAtomTokenWidget
{
    internal static class StartupRegistry
    {
        private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AntigravityAtomTokenWidget";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunSubKey, false))
                {
                    if (key != null)
                    {
                        object v = key.GetValue(ValueName);
                        return v != null && !string.IsNullOrEmpty(v.ToString());
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static void SetEnabled(bool enable, string exePath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunSubKey, true))
            {
                if (key == null)
                {
                    return;
                }
                if (enable)
                {
                    key.SetValue(ValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                }
                else
                {
                    if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
        }
    }

    internal sealed class TrayController : IDisposable
    {
        private readonly WinForms.NotifyIcon notifyIcon;
        private readonly WinForms.ContextMenuStrip menu;
        private readonly WidgetSettings settings;
        private readonly string baseDir;
        private readonly string exePath;

        private AtomElement lastActiveElement = AtomElement.Unsupported;
        private long lastTotalTokens = 0;

        private WinForms.ToolStripMenuItem titleItem;

        private WinForms.ToolStripMenuItem modelSubmenu;
        private WinForms.ToolStripMenuItem itemModelAuto;
        private WinForms.ToolStripMenuItem itemModelH;
        private WinForms.ToolStripMenuItem itemModelC;
        private WinForms.ToolStripMenuItem itemModelNe;
        private WinForms.ToolStripMenuItem itemModelAr;

        private WinForms.ToolStripMenuItem scopeSubmenu;
        private WinForms.ToolStripMenuItem itemScopeSession;
        private WinForms.ToolStripMenuItem itemScopeToday;
        private WinForms.ToolStripMenuItem itemScopeAll;
        private WinForms.ToolStripMenuItem itemIncludeSubagents;
        private WinForms.ToolStripMenuItem itemShowApiValue;

        private WinForms.ToolStripMenuItem scaleSubmenu;
        private WinForms.ToolStripMenuItem itemScale80;
        private WinForms.ToolStripMenuItem itemScale100;
        private WinForms.ToolStripMenuItem itemScale120;
        private WinForms.ToolStripMenuItem itemScale140;
        private WinForms.ToolStripMenuItem itemScale160;

        private WinForms.ToolStripMenuItem itemEnableAnim;
        private WinForms.ToolStripMenuItem itemHideWidget;
        private WinForms.ToolStripMenuItem itemResetPos;
        private WinForms.ToolStripMenuItem langSubmenu;
        private WinForms.ToolStripMenuItem itemLangZh;
        private WinForms.ToolStripMenuItem itemLangEn;
        private WinForms.ToolStripMenuItem itemAutoStart;
        private WinForms.ToolStripMenuItem itemOpenFolder;
        private WinForms.ToolStripMenuItem itemExit;

        public event Action SettingsUpdated;
        public event Action ToggleVisibilityRequested;

        public TrayController(WidgetSettings widgetSettings, string appBaseDir, string executablePath)
        {
            this.settings = widgetSettings;
            this.baseDir = appBaseDir;
            this.exePath = executablePath;

            menu = new WinForms.ContextMenuStrip();

            titleItem = new WinForms.ToolStripMenuItem();
            titleItem.Enabled = false;
            menu.Items.Add(titleItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            modelSubmenu = new WinForms.ToolStripMenuItem();
            itemModelAuto = new WinForms.ToolStripMenuItem();
            itemModelAuto.Click += delegate { SetModelOverride(null); };
            itemModelH = new WinForms.ToolStripMenuItem();
            itemModelH.Click += delegate { SetModelOverride(AtomElement.Hydrogen); };
            itemModelC = new WinForms.ToolStripMenuItem();
            itemModelC.Click += delegate { SetModelOverride(AtomElement.Carbon); };
            itemModelNe = new WinForms.ToolStripMenuItem();
            itemModelNe.Click += delegate { SetModelOverride(AtomElement.Neon); };
            itemModelAr = new WinForms.ToolStripMenuItem();
            itemModelAr.Click += delegate { SetModelOverride(AtomElement.Argon); };
            modelSubmenu.DropDownItems.Add(itemModelAuto);
            modelSubmenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
            modelSubmenu.DropDownItems.Add(itemModelH);
            modelSubmenu.DropDownItems.Add(itemModelC);
            modelSubmenu.DropDownItems.Add(itemModelNe);
            modelSubmenu.DropDownItems.Add(itemModelAr);
            menu.Items.Add(modelSubmenu);

            scopeSubmenu = new WinForms.ToolStripMenuItem();
            itemScopeSession = new WinForms.ToolStripMenuItem();
            itemScopeSession.Click += delegate { SetScope(TokenScope.CurrentConversation); };
            itemScopeToday = new WinForms.ToolStripMenuItem();
            itemScopeToday.Click += delegate { SetScope(TokenScope.Today); };
            itemScopeAll = new WinForms.ToolStripMenuItem();
            itemScopeAll.Click += delegate { SetScope(TokenScope.AllTime); };
            itemIncludeSubagents = new WinForms.ToolStripMenuItem();
            itemIncludeSubagents.Click += delegate
            {
                settings.IncludeSubagents = !settings.IncludeSubagents;
                SaveAndNotify();
            };
            itemShowApiValue = new WinForms.ToolStripMenuItem();
            itemShowApiValue.Click += delegate
            {
                settings.ShowEquivalentApiValue = !settings.ShowEquivalentApiValue;
                SaveAndNotify();
            };
            scopeSubmenu.DropDownItems.Add(itemScopeSession);
            scopeSubmenu.DropDownItems.Add(itemScopeToday);
            scopeSubmenu.DropDownItems.Add(itemScopeAll);
            scopeSubmenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
            scopeSubmenu.DropDownItems.Add(itemIncludeSubagents);
            scopeSubmenu.DropDownItems.Add(itemShowApiValue);
            menu.Items.Add(scopeSubmenu);

            scaleSubmenu = new WinForms.ToolStripMenuItem();
            itemScale80 = new WinForms.ToolStripMenuItem();
            itemScale80.Click += delegate { SetScale(0.80); };
            itemScale100 = new WinForms.ToolStripMenuItem();
            itemScale100.Click += delegate { SetScale(1.00); };
            itemScale120 = new WinForms.ToolStripMenuItem();
            itemScale120.Click += delegate { SetScale(1.20); };
            itemScale140 = new WinForms.ToolStripMenuItem();
            itemScale140.Click += delegate { SetScale(1.40); };
            itemScale160 = new WinForms.ToolStripMenuItem();
            itemScale160.Click += delegate { SetScale(1.60); };
            scaleSubmenu.DropDownItems.Add(itemScale80);
            scaleSubmenu.DropDownItems.Add(itemScale100);
            scaleSubmenu.DropDownItems.Add(itemScale120);
            scaleSubmenu.DropDownItems.Add(itemScale140);
            scaleSubmenu.DropDownItems.Add(itemScale160);
            menu.Items.Add(scaleSubmenu);

            itemEnableAnim = new WinForms.ToolStripMenuItem();
            itemEnableAnim.Click += delegate
            {
                settings.EnableAnimation = !settings.EnableAnimation;
                SaveAndNotify();
            };
            menu.Items.Add(itemEnableAnim);

            itemHideWidget = new WinForms.ToolStripMenuItem();
            itemHideWidget.Click += delegate { ToggleVisibility(); };
            menu.Items.Add(itemHideWidget);

            itemResetPos = new WinForms.ToolStripMenuItem();
            itemResetPos.Click += delegate
            {
                settings.AnchorNormX = 0.955;
                settings.AnchorNormY = 0.965;
                SaveAndNotify();
            };
            menu.Items.Add(itemResetPos);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            langSubmenu = new WinForms.ToolStripMenuItem();
            itemLangZh = new WinForms.ToolStripMenuItem("繁體中文");
            itemLangZh.Click += delegate { SetLanguage(UiLanguage.ZhTw); };
            itemLangEn = new WinForms.ToolStripMenuItem("English");
            itemLangEn.Click += delegate { SetLanguage(UiLanguage.En); };
            langSubmenu.DropDownItems.Add(itemLangZh);
            langSubmenu.DropDownItems.Add(itemLangEn);
            menu.Items.Add(langSubmenu);

            itemAutoStart = new WinForms.ToolStripMenuItem();
            itemAutoStart.Click += delegate { ToggleAutoStart(); };
            menu.Items.Add(itemAutoStart);

            itemOpenFolder = new WinForms.ToolStripMenuItem();
            itemOpenFolder.Click += delegate
            {
                try
                {
                    Process.Start("explorer.exe", baseDir);
                }
                catch
                {
                }
            };
            menu.Items.Add(itemOpenFolder);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            itemExit = new WinForms.ToolStripMenuItem();
            itemExit.Click += delegate
            {
                if (Application.Current != null)
                {
                    Application.Current.Shutdown();
                }
            };
            menu.Items.Add(itemExit);

            notifyIcon = new WinForms.NotifyIcon();
            notifyIcon.Icon = LoadOrCreateTrayIcon(baseDir, executablePath);
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.DoubleClick += delegate { ToggleVisibility(); };

            RefreshTextsAndChecks(AtomElement.Unsupported, 0);
            notifyIcon.Visible = true;
        }

        public void RefreshTextsAndChecks(AtomElement activeElement, long totalTokens)
        {
            this.lastActiveElement = activeElement;
            this.lastTotalTokens = totalTokens;

            bool zh = (settings.Language == UiLanguage.ZhTw);
            if (zh)
            {
                titleItem.Text = "\u269B Antigravity 原子 Token 計算器";
                modelSubmenu.Text = "3D 原子模型顯示";
                itemModelAuto.Text = "自動跟隨 AI 模型 (Auto)";
                itemModelH.Text = "H — Hydrogen 氫 (Z=1)";
                itemModelC.Text = "C — Carbon 碳 (Z=6)";
                itemModelNe.Text = "Ne — Neon 氖 (Z=10)";
                itemModelAr.Text = "Ar — Argon 氬 (Z=18)";

                scopeSubmenu.Text = "Token 統計範圍";
                itemScopeSession.Text = "當前對話 (Session)";
                itemScopeToday.Text = "今日累計 (Today)";
                itemScopeAll.Text = "全部紀錄 (All-Time)";
                itemIncludeSubagents.Text = "納入子代理 (+Subagents)";
                itemShowApiValue.Text = "顯示等效 API 價值 (USD)";

                scaleSubmenu.Text = "模型尺寸比例";
                itemScale80.Text = "80%";
                itemScale100.Text = "標準 (100%)";
                itemScale120.Text = "120%";
                itemScale140.Text = "140%";
                itemScale160.Text = "160%";

                itemEnableAnim.Text = "開啟動態效果";
                itemHideWidget.Text = "隱藏懸浮小工具（雙擊圖示）";
                itemResetPos.Text = "重置位置至右下角";
                langSubmenu.Text = "語言 / Language";
                itemAutoStart.Text = "開機時自動啟動";
                itemOpenFolder.Text = "開啟程式資料夾";
                itemExit.Text = "結束";
            }
            else
            {
                titleItem.Text = "\u269B Antigravity Atom Token Widget";
                modelSubmenu.Text = "3D Bohr Atom Model";
                itemModelAuto.Text = "Auto-Follow AI Model";
                itemModelH.Text = "H — Hydrogen (Z=1)";
                itemModelC.Text = "C — Carbon (Z=6)";
                itemModelNe.Text = "Ne — Neon (Z=10)";
                itemModelAr.Text = "Ar — Argon (Z=18)";

                scopeSubmenu.Text = "Token Scope";
                itemScopeSession.Text = "Current Conversation";
                itemScopeToday.Text = "Today";
                itemScopeAll.Text = "All-Time";
                itemIncludeSubagents.Text = "Include Subagents";
                itemShowApiValue.Text = "Show Equivalent API Value (USD)";

                scaleSubmenu.Text = "Widget Size";
                itemScale80.Text = "80%";
                itemScale100.Text = "Standard (100%)";
                itemScale120.Text = "120%";
                itemScale140.Text = "140%";
                itemScale160.Text = "160%";

                itemEnableAnim.Text = "Enable 3D Animation";
                itemHideWidget.Text = "Hide Floating Widget (Double-Click)";
                itemResetPos.Text = "Reset to Bottom-Right";
                langSubmenu.Text = "Language / \u8A9E\u8A00";
                itemAutoStart.Text = "Run at Windows Startup";
                itemOpenFolder.Text = "Open Widget Folder";
                itemExit.Text = "Exit";
            }

            itemModelAuto.Checked = !settings.ManualElementOverride.HasValue;
            itemModelH.Checked = settings.ManualElementOverride == AtomElement.Hydrogen;
            itemModelC.Checked = settings.ManualElementOverride == AtomElement.Carbon;
            itemModelNe.Checked = settings.ManualElementOverride == AtomElement.Neon;
            itemModelAr.Checked = settings.ManualElementOverride == AtomElement.Argon;

            itemScopeSession.Checked = settings.Scope == TokenScope.CurrentConversation;
            itemScopeToday.Checked = settings.Scope == TokenScope.Today;
            itemScopeAll.Checked = settings.Scope == TokenScope.AllTime;
            itemIncludeSubagents.Checked = settings.IncludeSubagents;
            itemShowApiValue.Checked = settings.ShowEquivalentApiValue;

            int scaleIdx = WidgetSettings.TryGetExactScalePresetIndex(settings.ScaleMultiplier);
            itemScale80.Checked = (scaleIdx == 0);
            itemScale100.Checked = (scaleIdx == 1);
            itemScale120.Checked = (scaleIdx == 2);
            itemScale140.Checked = (scaleIdx == 3);
            itemScale160.Checked = (scaleIdx == 4);

            itemEnableAnim.Checked = settings.EnableAnimation;
            itemHideWidget.Checked = settings.IsWidgetHidden;

            itemLangZh.Checked = zh;
            itemLangEn.Checked = !zh;
            itemAutoStart.Checked = StartupRegistry.IsEnabled();

            ElementVisualInfo ev = ElementVisualCatalog.GetInfo(activeElement);
            string suffix = "";
            if (settings.IsWidgetHidden)
            {
                suffix = zh ? " (已隱藏)" : " (Hidden)";
            }
            else if (!settings.EnableAnimation)
            {
                suffix = zh ? " (靜態)" : " (Static)";
            }

            string tip;
            if (activeElement == AtomElement.Unsupported)
            {
                tip = string.Format(
                    zh ? "Antigravity 原子 Token — 僅支援G4 ({0}){1}" : "Antigravity Atom Token — G4 Anim Only ({0}){1}",
                    WidgetWindow.FormatCompactTokens(totalTokens),
                    suffix);
            }
            else
            {
                tip = string.Format(
                    "Antigravity Atom ({0}) — {1} tokens{2}",
                    ev.Symbol,
                    WidgetWindow.FormatCompactTokens(totalTokens),
                    suffix);
            }
            if (tip.Length > 63)
            {
                tip = tip.Substring(0, 63);
            }
            notifyIcon.Text = tip;
        }

        internal bool DebugShowApiValueChecked
        {
            get { return itemShowApiValue.Checked; }
        }

        internal int DebugCheckedScalePresetIndex
        {
            get
            {
                bool[] flags = new bool[] { itemScale80.Checked, itemScale100.Checked, itemScale120.Checked, itemScale140.Checked, itemScale160.Checked };
                int found = -1;
                int count = 0;
                for (int i = 0; i < flags.Length; i++)
                {
                    if (flags[i])
                    {
                        found = i;
                        count++;
                    }
                }
                if (count == 0) { return -1; }
                if (count == 1) { return found; }
                return -2;
            }
        }

        private void ToggleVisibility()
        {
            settings.IsWidgetHidden = !settings.IsWidgetHidden;
            RefreshTextsAndChecks(lastActiveElement, lastTotalTokens);
            if (ToggleVisibilityRequested != null)
            {
                ToggleVisibilityRequested();
            }
        }

        private void SetModelOverride(AtomElement? element)
        {
            settings.ManualElementOverride = element;
            SaveAndNotify();
        }

        private void SetScope(TokenScope scope)
        {
            settings.Scope = scope;
            SaveAndNotify();
        }

        private void SetScale(double scale)
        {
            settings.ScaleMultiplier = WidgetSettings.ClampScale(scale);
            SaveAndNotify();
        }

        private void SetLanguage(UiLanguage lang)
        {
            settings.Language = lang;
            SaveAndNotify();
        }

        private void ToggleAutoStart()
        {
            bool target = !StartupRegistry.IsEnabled();
            try
            {
                StartupRegistry.SetEnabled(target, exePath);
            }
            catch
            {
            }
            RefreshTextsAndChecks(lastActiveElement, lastTotalTokens);
        }

        private void SaveAndNotify()
        {
            settings.Save(baseDir);
            if (SettingsUpdated != null)
            {
                SettingsUpdated();
            }
        }

        private static Drawing.Icon LoadOrCreateTrayIcon(string baseDir, string executablePath)
        {
            string icoPath = Path.Combine(baseDir, "assets", "atom_widget.ico");
            try
            {
                if (File.Exists(icoPath))
                {
                    return new Drawing.Icon(icoPath, new Drawing.Size(32, 32));
                }
            }
            catch
            {
            }
            try
            {
                if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                {
                    return Drawing.Icon.ExtractAssociatedIcon(executablePath);
                }
            }
            catch
            {
            }

            using (Drawing.Bitmap bmp = new Drawing.Bitmap(32, 32))
            using (Drawing.Graphics g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);
                using (Drawing.Brush bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(24, 24, 34)))
                {
                    g.FillEllipse(bg, 1, 1, 30, 30);
                }
                using (Drawing.Pen p1 = new Drawing.Pen(Drawing.Color.FromArgb(148, 163, 184), 1.6f))
                {
                    Drawing2D.GraphicsState st = g.Save();
                    g.TranslateTransform(16, 16);
                    g.RotateTransform(30);
                    g.DrawEllipse(p1, -12, -5.5f, 24, 11);
                    g.RotateTransform(60);
                    g.DrawEllipse(p1, -12, -5.5f, 24, 11);
                    g.RotateTransform(60);
                    g.DrawEllipse(p1, -12, -5.5f, 24, 11);
                    g.Restore(st);
                }
                using (Drawing.Brush p = new Drawing.SolidBrush(Drawing.Color.FromArgb(230, 52, 46)))
                {
                    g.FillEllipse(p, 12.5f, 12.5f, 7, 7);
                }
                return Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }

        public void Dispose()
        {
            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
            if (menu != null)
            {
                menu.Dispose();
            }
        }
    }
}
