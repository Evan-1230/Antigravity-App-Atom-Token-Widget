using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace AntigravityAtomTokenWidget
{
    public static class SettingsPlacementSolver
    {
        public static Rect ComputeDragClampRect(Rect appBoundsDip, Rect workAreaDip, bool isAppFullscreenOrMaximized)
        {
            Rect safeWorkArea = (workAreaDip.Width >= 200.0 && workAreaDip.Height >= 160.0)
                ? workAreaDip
                : new Rect(0, 0, 1920, 1040);

            if (isAppFullscreenOrMaximized)
            {
                Rect inter = Rect.Intersect(appBoundsDip, safeWorkArea);
                if (!inter.IsEmpty && inter.Width >= 240.0 && inter.Height >= 200.0)
                {
                    return inter;
                }
            }
            return safeWorkArea;
        }

        public static Point ClampManualMove(double desiredLeft, double desiredTop, double winW, double winH, Rect appBoundsDip, Rect workAreaDip, bool isAppFullscreenOrMaximized)
        {
            Rect clampRect = ComputeDragClampRect(appBoundsDip, workAreaDip, isAppFullscreenOrMaximized);
            double minX = clampRect.Left;
            double maxX = Math.Max(minX, clampRect.Right - winW);
            double minY = clampRect.Top;
            double maxY = Math.Max(minY, clampRect.Bottom - winH);

            double x = Math.Max(minX, Math.Min(maxX, desiredLeft));
            double y = Math.Max(minY, Math.Min(maxY, desiredTop));
            return new Point(x, y);
        }

        public static Point ComputePosition(double winW, double winH, Rect widgetHitBoxDip, Rect appBoundsDip, Rect workAreaDip, bool isAppFullscreenOrMaximized)
        {
            Rect clampRect = ComputeDragClampRect(appBoundsDip, workAreaDip, isAppFullscreenOrMaximized);
            double gap = 10.0;

            double bottomAlignedTop = widgetHitBoxDip.Bottom - winH;
            double centerAlignedLeft = widgetHitBoxDip.Left + (widgetHitBoxDip.Width - winW) * 0.5;
            double centerAlignedTop = widgetHitBoxDip.Top + (widgetHitBoxDip.Height - winH) * 0.5;

            List<Point> candidates = new List<Point>();
            // 1. Left of widget (bottom-aligned / centered / top-aligned)
            candidates.Add(new Point(widgetHitBoxDip.Left - winW - gap, bottomAlignedTop));
            candidates.Add(new Point(widgetHitBoxDip.Left - winW - gap, centerAlignedTop));
            candidates.Add(new Point(widgetHitBoxDip.Left - winW - gap, widgetHitBoxDip.Top));
            // 2. Above widget (right-aligned / centered / left-aligned)
            candidates.Add(new Point(widgetHitBoxDip.Right - winW, widgetHitBoxDip.Top - winH - gap));
            candidates.Add(new Point(centerAlignedLeft, widgetHitBoxDip.Top - winH - gap));
            candidates.Add(new Point(widgetHitBoxDip.Left, widgetHitBoxDip.Top - winH - gap));
            // 3. Right of widget (bottom-aligned / top-aligned)
            candidates.Add(new Point(widgetHitBoxDip.Right + gap, bottomAlignedTop));
            candidates.Add(new Point(widgetHitBoxDip.Right + gap, widgetHitBoxDip.Top));
            // 4. Below widget (right-aligned / centered / left-aligned)
            candidates.Add(new Point(widgetHitBoxDip.Right - winW, widgetHitBoxDip.Bottom + gap));
            candidates.Add(new Point(centerAlignedLeft, widgetHitBoxDip.Bottom + gap));
            candidates.Add(new Point(widgetHitBoxDip.Left, widgetHitBoxDip.Bottom + gap));
            // 5. Opposite-corner / edge fallbacks
            candidates.Add(new Point(clampRect.Left + 8.0, clampRect.Top + 8.0));
            candidates.Add(new Point(clampRect.Right - winW - 8.0, clampRect.Top + 8.0));
            candidates.Add(new Point(clampRect.Left + 8.0, clampRect.Bottom - winH - 8.0));

            Point bestPos = ClampManualMove(candidates[0].X, candidates[0].Y, winW, winH, appBoundsDip, workAreaDip, isAppFullscreenOrMaximized);
            double bestOverlap = double.MaxValue;
            double bestShift = double.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                Point raw = candidates[i];
                Point clamped = ClampManualMove(raw.X, raw.Y, winW, winH, appBoundsDip, workAreaDip, isAppFullscreenOrMaximized);
                Rect winRect = new Rect(clamped.X, clamped.Y, winW, winH);
                Rect overlap = Rect.Intersect(winRect, widgetHitBoxDip);
                double area = (overlap.IsEmpty || overlap.Width <= 0.0 || overlap.Height <= 0.0)
                    ? 0.0
                    : (overlap.Width * overlap.Height);
                double shift = Math.Abs(clamped.X - raw.X) + Math.Abs(clamped.Y - raw.Y);

                if (area < bestOverlap - 0.5 || (Math.Abs(area - bestOverlap) <= 0.5 && shift < bestShift))
                {
                    bestOverlap = area;
                    bestShift = shift;
                    bestPos = clamped;
                }
                if (bestOverlap <= 0.1 && shift <= 1.0)
                {
                    break;
                }
            }

            return bestPos;
        }
    }

    public sealed class SettingsWindow : Window
    {
        public const double ModelTileWidth = 73.4;
        public const double ModelTileGap = 6.0;
        public const double DefaultStripViewportWidth = 311.6; // 4 * 73.4 + 3 * 6.0

        public static void ComputeTileStripMetrics(
            int elementCount,
            double viewportWidth,
            double scrollOffset,
            out double extentWidth,
            out double scrollableWidth,
            out double thumbWidth,
            out double thumbLeft)
        {
            double viewW = (viewportWidth > 60.0) ? viewportWidth : DefaultStripViewportWidth;
            extentWidth = (elementCount <= 0)
                ? 0.0
                : (elementCount * ModelTileWidth + Math.Max(0, elementCount - 1) * ModelTileGap);
            scrollableWidth = Math.Max(0.0, extentWidth - viewW);
            if (scrollableWidth <= 0.5)
            {
                thumbWidth = viewW;
                thumbLeft = 0.0;
                return;
            }

            thumbWidth = Math.Max(36.0, viewW * (viewW / extentWidth));
            if (thumbWidth > viewW - 8.0)
            {
                thumbWidth = Math.Max(24.0, viewW - 8.0);
            }
            double maxThumbX = Math.Max(0.0, viewW - thumbWidth);
            double clampedOff = Math.Max(0.0, Math.Min(scrollableWidth, scrollOffset));
            thumbLeft = (clampedOff / scrollableWidth) * maxThumbX;
        }

        private readonly WidgetSettings settings;
        private readonly string baseDir;
        private readonly string exePath;

        private bool isLightTheme;
        private ActiveModelState latestModelState;
        private bool isClosingFlag = false;
        private double savedTileScrollOffset = 0.0;

        private Rect lastAppBoundsDip = new Rect(0, 0, 1280, 800);
        private Rect lastWorkAreaDip = new Rect(0, 0, 1920, 1040);
        private bool lastFullscreenOrMax = false;
        private double currentDpiScale = 1.0;
        private bool hasPositionedOnce = false;

        private bool isDraggingWindow = false;
        private Point dragStartScreenPx;
        private double dragStartLeftDip;
        private double dragStartTopDip;
        private string lastBuiltSubtitleText = "";
        private string lastBuiltPreviewBadgeText = "";

        public string DebugHeaderSubtitleText
        {
            get { return lastBuiltSubtitleText; }
        }

        public string DebugPreviewBadgeText
        {
            get { return lastBuiltPreviewBadgeText; }
        }

        public event Action SettingsChanged;
        public event Action ScaleLiveChanged;
        public event Action ResetPositionRequested;
        public event Action ExitRequested;

        public SettingsWindow(WidgetSettings widgetSettings, string appBaseDir, string executablePath, bool lightTheme, ActiveModelState modelState)
        {
            this.settings = widgetSettings;
            this.baseDir = appBaseDir;
            this.exePath = executablePath;
            this.isLightTheme = lightTheme;
            this.latestModelState = modelState ?? new ActiveModelState();

            this.Title = "Antigravity Atom Token Settings";
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.ResizeMode = ResizeMode.NoResize;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.Width = 360.0;
            this.SizeToContent = SizeToContent.Height;

            this.Deactivated += delegate
            {
                SafeClose();
            };
            this.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    SafeClose();
                }
            };
            this.SizeChanged += delegate
            {
                if (hasPositionedOnce && !isClosingFlag)
                {
                    ClampCurrentPosition();
                }
            };

            RebuildContent();
        }

        public void PositionWithCollision(Rect widgetHitBoxDip, Rect appBoundsDip, Rect workAreaDip, bool isAppFullscreenOrMaximized, double dpiScale)
        {
            this.lastAppBoundsDip = appBoundsDip;
            this.lastWorkAreaDip = workAreaDip;
            this.lastFullscreenOrMax = isAppFullscreenOrMaximized;
            this.currentDpiScale = dpiScale > 0.2 ? dpiScale : 1.0;

            Rect clampRect = SettingsPlacementSolver.ComputeDragClampRect(appBoundsDip, workAreaDip, isAppFullscreenOrMaximized);
            this.MaxWidth = Math.Max(280.0, clampRect.Width);
            this.MaxHeight = Math.Max(240.0, clampRect.Height);

            double targetW = Math.Min(this.Width, this.MaxWidth);
            this.Measure(new Size(targetW, this.MaxHeight));
            double winW = Math.Max(280.0, targetW);
            double winH = Math.Min(this.MaxHeight, Math.Max(340.0, this.DesiredSize.Height));

            Point pos = SettingsPlacementSolver.ComputePosition(winW, winH, widgetHitBoxDip, appBoundsDip, workAreaDip, isAppFullscreenOrMaximized);
            this.Left = pos.X;
            this.Top = pos.Y;
            this.hasPositionedOnce = true;

            this.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                if (!isClosingFlag)
                {
                    ClampCurrentPosition();
                }
            }));
        }

        public void UpdateBoundsClamp(Rect appBoundsDip, Rect workAreaDip, bool isAppFullscreenOrMaximized, double dpiScale)
        {
            this.lastAppBoundsDip = appBoundsDip;
            this.lastWorkAreaDip = workAreaDip;
            this.lastFullscreenOrMax = isAppFullscreenOrMaximized;
            this.currentDpiScale = dpiScale > 0.2 ? dpiScale : 1.0;

            Rect clampRect = SettingsPlacementSolver.ComputeDragClampRect(appBoundsDip, workAreaDip, isAppFullscreenOrMaximized);
            this.MaxWidth = Math.Max(280.0, clampRect.Width);
            this.MaxHeight = Math.Max(240.0, clampRect.Height);

            if (hasPositionedOnce && !isClosingFlag)
            {
                ClampCurrentPosition();
            }
        }

        private void ClampCurrentPosition()
        {
            double winW = this.ActualWidth > 10.0 ? this.ActualWidth : this.Width;
            double winH = this.ActualHeight > 10.0 ? this.ActualHeight : Math.Min(this.MaxHeight > 0 ? this.MaxHeight : 900.0, Math.Max(340.0, this.DesiredSize.Height));
            Point clamped = SettingsPlacementSolver.ClampManualMove(
                this.Left, this.Top, winW, winH,
                lastAppBoundsDip, lastWorkAreaDip, lastFullscreenOrMax);
            if (Math.Abs(this.Left - clamped.X) > 0.5)
            {
                this.Left = clamped.X;
            }
            if (Math.Abs(this.Top - clamped.Y) > 0.5)
            {
                this.Top = clamped.Y;
            }
        }

        public void UpdateThemeAndModel(bool lightTheme, ActiveModelState modelState)
        {
            ActiveModelState next = (modelState != null) ? modelState.Clone() : new ActiveModelState();
            ActiveModelState prev = this.latestModelState ?? new ActiveModelState();

            bool changed = (this.isLightTheme != lightTheme)
                || !string.Equals(prev.DisplayName, next.DisplayName, StringComparison.Ordinal)
                || (prev.Element != next.Element)
                || (prev.IsOrchestrator != next.IsOrchestrator)
                || (prev.PreviewOverrideElement != next.PreviewOverrideElement);

            this.isLightTheme = lightTheme;
            this.latestModelState = next;
            if (changed)
            {
                RebuildContent();
            }
        }

        public void SafeClose()
        {
            if (isClosingFlag)
            {
                return;
            }
            isClosingFlag = true;
            try
            {
                this.Close();
            }
            catch
            {
            }
        }

        public Border BuildPreviewCard()
        {
            return CreateCardVisual(false, null);
        }

        public Border BuildPreviewCardWithElements(AtomElement[] customElements)
        {
            return CreateCardVisual(false, customElements);
        }

        private void RebuildContent()
        {
            this.Content = CreateCardVisual(true, null);
            if (hasPositionedOnce && !isClosingFlag)
            {
                this.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    if (!isClosingFlag)
                    {
                        ClampCurrentPosition();
                    }
                }));
            }
        }

        private Border CreateCardVisual(bool interactive, AtomElement[] overrideElements)
        {
            bool zh = (settings.Language == UiLanguage.ZhTw);
            bool light = isLightTheme;

            ElementVisualInfo activeInfo = ElementVisualCatalog.GetInfo(
                settings.ManualElementOverride.HasValue ? settings.ManualElementOverride.Value : latestModelState.Element);
            Color accent = light ? activeInfo.LightAccent : activeInfo.DarkAccent;
            if (activeInfo.AtomicNumber == 0)
            {
                accent = light ? Color.FromRgb(36, 131, 226) : Color.FromRgb(135, 169, 255);
            }

            Color cardBg = light ? Color.FromRgb(255, 249, 251) : Color.FromRgb(29, 22, 24);
            Color cardBorder = light ? Color.FromArgb(80, 180, 120, 140) : Color.FromArgb(75, 255, 210, 195);
            Color sectionBg = light ? Color.FromArgb(14, 100, 40, 60) : Color.FromArgb(16, 255, 235, 225);
            Color fgPrimary = light ? Color.FromRgb(38, 22, 28) : Color.FromRgb(242, 234, 230);
            Color fgMuted = light ? Color.FromRgb(118, 92, 102) : Color.FromRgb(166, 152, 146);

            Border outer = new Border();
            outer.Margin = new Thickness(8);
            outer.CornerRadius = new CornerRadius(15);
            outer.Background = new SolidColorBrush(cardBg);
            outer.BorderBrush = new SolidColorBrush(cardBorder);
            outer.BorderThickness = new Thickness(1.2);
            outer.Padding = new Thickness(15, 13, 15, 13);

            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 14.0;
            shadow.ShadowDepth = 2.0;
            shadow.Opacity = light ? 0.20 : 0.42;
            shadow.Color = Colors.Black;
            outer.Effect = shadow;

            DockPanel frameDock = new DockPanel();
            frameDock.LastChildFill = true;
            outer.Child = frameDock;

            // Create closeBtn upfront so header drag ignores clicks originating inside [X]
            Border closeBtn = new Border();
            closeBtn.Width = 26;
            closeBtn.Height = 26;
            closeBtn.CornerRadius = new CornerRadius(13);
            closeBtn.Background = new SolidColorBrush(sectionBg);
            closeBtn.BorderThickness = new Thickness(1.0);
            closeBtn.BorderBrush = new SolidColorBrush(Colors.Transparent);
            closeBtn.VerticalAlignment = VerticalAlignment.Top;
            closeBtn.Cursor = Cursors.Hand;
            TextBlock closeIcon = new TextBlock();
            closeIcon.Text = "\u2715";
            closeIcon.FontSize = 11.5;
            closeIcon.Foreground = new SolidColorBrush(fgMuted);
            closeIcon.HorizontalAlignment = HorizontalAlignment.Center;
            closeIcon.VerticalAlignment = VerticalAlignment.Center;
            closeBtn.Child = closeIcon;
            if (interactive)
            {
                closeBtn.MouseEnter += delegate
                {
                    closeBtn.Background = new SolidColorBrush(Color.FromArgb(light ? (byte)32 : (byte)44, 220, 68, 68));
                    closeIcon.Foreground = new SolidColorBrush(Color.FromRgb(220, 68, 68));
                };
                closeBtn.MouseLeave += delegate
                {
                    closeBtn.Background = new SolidColorBrush(sectionBg);
                    closeIcon.Foreground = new SolidColorBrush(fgMuted);
                };
                closeBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                };
                closeBtn.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    SafeClose();
                };
            }

            // 1. Header Bar (custom clamped manual drag — never escapes screen, taskbar, or fullscreen App bounds)
            Grid header = new Grid();
            header.Background = Brushes.Transparent;
            header.Cursor = Cursors.SizeAll;
            header.Margin = new Thickness(0, 0, 0, 2);
            if (interactive)
            {
                header.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    if (IsDescendantOf(e.OriginalSource as DependencyObject, closeBtn))
                    {
                        return;
                    }
                    if (e.ButtonState == MouseButtonState.Pressed)
                    {
                        isDraggingWindow = true;
                        System.Drawing.Point cp = WinForms.Control.MousePosition;
                        dragStartScreenPx = new Point(cp.X, cp.Y);
                        dragStartLeftDip = this.Left;
                        dragStartTopDip = this.Top;
                        header.CaptureMouse();
                        e.Handled = true;
                    }
                };
                header.MouseMove += delegate(object s, MouseEventArgs e)
                {
                    if (!isDraggingWindow)
                    {
                        return;
                    }
                    System.Drawing.Point cp = WinForms.Control.MousePosition;
                    double dxDip = (cp.X - dragStartScreenPx.X) / currentDpiScale;
                    double dyDip = (cp.Y - dragStartScreenPx.Y) / currentDpiScale;
                    double winW = this.ActualWidth > 10.0 ? this.ActualWidth : this.Width;
                    double winH = this.ActualHeight > 10.0 ? this.ActualHeight : Math.Max(340.0, this.DesiredSize.Height);
                    Point clamped = SettingsPlacementSolver.ClampManualMove(
                        dragStartLeftDip + dxDip,
                        dragStartTopDip + dyDip,
                        winW,
                        winH,
                        lastAppBoundsDip,
                        lastWorkAreaDip,
                        lastFullscreenOrMax);
                    this.Left = clamped.X;
                    this.Top = clamped.Y;
                };
                header.MouseLeftButtonUp += delegate
                {
                    if (isDraggingWindow)
                    {
                        isDraggingWindow = false;
                        if (header.IsMouseCaptured) { header.ReleaseMouseCapture(); }
                    }
                };
                header.LostMouseCapture += delegate
                {
                    isDraggingWindow = false;
                };
            }
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titleStack = new StackPanel();
            TextBlock titleTxt = new TextBlock();
            titleTxt.Text = zh ? "\u269B 原子 Token 設定" : "\u269B Atom Token Settings";
            titleTxt.FontSize = 14.5;
            titleTxt.FontWeight = FontWeights.Bold;
            titleTxt.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            titleTxt.Foreground = new SolidColorBrush(fgPrimary);
            titleStack.Children.Add(titleTxt);

            string detectedLabel = string.IsNullOrEmpty(latestModelState.DisplayName)
                ? (zh ? "待機中" : "Standby")
                : latestModelState.DisplayName;
            string orchTag = (latestModelState.IsOrchestrator && !ModelNameNormalizer.ContainsOrchestratorWord(detectedLabel))
                ? " · \u26A1 Orchestrator"
                : "";
            string fullSubtitle = (zh ? "目前模型：" : "Detected: ") + detectedLabel + orchTag;
            lastBuiltSubtitleText = fullSubtitle;

            TextBlock subTxt = new TextBlock();
            subTxt.Text = fullSubtitle;
            subTxt.FontSize = 10.6;
            subTxt.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            subTxt.Foreground = new SolidColorBrush(fgMuted);
            subTxt.TextWrapping = TextWrapping.Wrap;
            subTxt.Margin = new Thickness(0, 2, 6, 0);
            titleStack.Children.Add(subTxt);

            if (settings.ManualElementOverride.HasValue)
            {
                ElementVisualInfo ovInfo = ElementVisualCatalog.GetInfo(settings.ManualElementOverride.Value);
                string previewLine = "[" + ovInfo.EnglishName + " Preview]";
                lastBuiltPreviewBadgeText = previewLine;

                TextBlock previewTxt = new TextBlock();
                previewTxt.Text = previewLine;
                previewTxt.FontSize = 10.6;
                previewTxt.FontWeight = FontWeights.SemiBold;
                previewTxt.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
                previewTxt.Foreground = new SolidColorBrush(accent);
                previewTxt.TextWrapping = TextWrapping.Wrap;
                previewTxt.Margin = new Thickness(0, 2, 6, 0);
                titleStack.Children.Add(previewTxt);
            }
            else
            {
                lastBuiltPreviewBadgeText = "";
            }

            Grid.SetColumn(titleStack, 0);
            header.Children.Add(titleStack);

            Grid.SetColumn(closeBtn, 1);
            header.Children.Add(closeBtn);
            DockPanel.SetDock(header, Dock.Top);
            frameDock.Children.Add(header);

            StackPanel root = new StackPanel();
            root.Orientation = Orientation.Vertical;

            // 2. Section: 3D Atom Model
            root.Children.Add(MakeSectionLabel(zh ? "3D 原子模型" : "3D Bohr Model", fgMuted));

            bool isAuto = !settings.ManualElementOverride.HasValue;
            Border autoPill = MakeActionPill(
                zh ? "自動跟隨 AI 模型" : "Auto-Follow AI Model",
                isAuto, accent, light, fgPrimary, fgMuted,
                delegate
                {
                    settings.ManualElementOverride = null;
                    NotifyAndRebuild();
                });
            autoPill.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(autoPill);

            AtomElement[] elems = (overrideElements != null && overrideElements.Length > 0)
                ? overrideElements
                : ElementVisualCatalog.SupportedElements;
            root.Children.Add(BuildModelTileCarousel(elems, interactive, accent, light, zh));

            // 3. Section: Token Scope
            root.Children.Add(MakeSectionLabel(zh ? "Token 統計範圍" : "Token Scope", fgMuted));

            UniformGrid scopeGrid = new UniformGrid();
            scopeGrid.Columns = 3;
            scopeGrid.Margin = new Thickness(-2, 0, -2, 6);
            scopeGrid.Children.Add(MakeSegmentButton(zh ? "當前對話" : "Session", settings.Scope == TokenScope.CurrentConversation, accent, light, fgPrimary, fgMuted, delegate { settings.Scope = TokenScope.CurrentConversation; NotifyAndRebuild(); }));
            scopeGrid.Children.Add(MakeSegmentButton(zh ? "今日累計" : "Today", settings.Scope == TokenScope.Today, accent, light, fgPrimary, fgMuted, delegate { settings.Scope = TokenScope.Today; NotifyAndRebuild(); }));
            scopeGrid.Children.Add(MakeSegmentButton(zh ? "全部紀錄" : "All-Time", settings.Scope == TokenScope.AllTime, accent, light, fgPrimary, fgMuted, delegate { settings.Scope = TokenScope.AllTime; NotifyAndRebuild(); }));
            root.Children.Add(scopeGrid);

            root.Children.Add(MakeToggleRow(
                zh ? "納入子代理 Token (+Subagents)" : "Include Subagents (+Sub)",
                settings.IncludeSubagents, accent, light, fgPrimary, sectionBg,
                delegate
                {
                    settings.IncludeSubagents = !settings.IncludeSubagents;
                    NotifyAndRebuild();
                }));

            // 4. Section: Visuals & Animation
            root.Children.Add(MakeSectionLabel(zh ? "動態與大小比例" : "Animation & Size Scale", fgMuted));

            root.Children.Add(MakeToggleRow(
                zh ? "3D 動態旋轉特效" : "3D Orbital Animation",
                settings.EnableAnimation, accent, light, fgPrimary, sectionBg,
                delegate
                {
                    settings.EnableAnimation = !settings.EnableAnimation;
                    NotifyAndRebuild();
                }));

            root.Children.Add(BuildScaleSliderCard(interactive, accent, light, cardBg, fgPrimary, fgMuted, sectionBg, zh));

            Border resetBtn = MakeActionPill(
                zh ? "\u21BA 重置懸浮位置至右下角" : "\u21BA Reset Position to Bottom-Right",
                false, accent, light, fgPrimary, fgMuted,
                delegate
                {
                    settings.AnchorNormX = 0.955;
                    settings.AnchorNormY = 0.965;
                    if (ResetPositionRequested != null) { ResetPositionRequested(); }
                    NotifyAndRebuild();
                });
            resetBtn.Margin = new Thickness(0, 0, 0, 4);
            root.Children.Add(resetBtn);

            // 5. Section: System
            root.Children.Add(MakeSectionLabel(zh ? "系統與語言" : "System & Language", fgMuted));

            UniformGrid langGrid = new UniformGrid();
            langGrid.Columns = 2;
            langGrid.Margin = new Thickness(-2, 0, -2, 6);
            langGrid.Children.Add(MakeSegmentButton("繁體中文", settings.Language == UiLanguage.ZhTw, accent, light, fgPrimary, fgMuted, delegate { settings.Language = UiLanguage.ZhTw; NotifyAndRebuild(); }));
            langGrid.Children.Add(MakeSegmentButton("English", settings.Language == UiLanguage.En, accent, light, fgPrimary, fgMuted, delegate { settings.Language = UiLanguage.En; NotifyAndRebuild(); }));
            root.Children.Add(langGrid);

            bool autoStartEnabled = StartupRegistry.IsEnabled();
            root.Children.Add(MakeToggleRow(
                zh ? "Windows 開機時自動啟動" : "Run at Windows Startup",
                autoStartEnabled, accent, light, fgPrimary, sectionBg,
                delegate
                {
                    try { StartupRegistry.SetEnabled(!StartupRegistry.IsEnabled(), exePath); } catch { }
                    NotifyAndRebuild();
                }));

            // Footer Actions
            Grid footer = new Grid();
            footer.Margin = new Thickness(0, 8, 0, 0);
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border folderBtn = MakeActionPill(
                zh ? "開啟資料夾" : "Open Folder",
                false, accent, light, fgPrimary, fgMuted,
                delegate
                {
                    try { Process.Start("explorer.exe", baseDir); } catch { }
                });
            Grid.SetColumn(folderBtn, 0);
            footer.Children.Add(folderBtn);

            Color exitTint = Color.FromRgb(220, 68, 68);
            Border exitBtn = MakeActionPill(
                zh ? "結束程式" : "Exit Widget",
                false, exitTint, light, exitTint, exitTint,
                delegate
                {
                    SafeClose();
                    if (ExitRequested != null) { ExitRequested(); }
                });
            exitBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(110, exitTint.R, exitTint.G, exitTint.B));
            exitBtn.Background = new SolidColorBrush(Color.FromArgb(light ? (byte)18 : (byte)28, exitTint.R, exitTint.G, exitTint.B));
            Grid.SetColumn(exitBtn, 2);
            footer.Children.Add(exitBtn);

            root.Children.Add(footer);

            if (interactive)
            {
                ScrollViewer scroll = new ScrollViewer();
                scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                scroll.Content = root;
                frameDock.Children.Add(scroll);
            }
            else
            {
                frameDock.Children.Add(root);
            }
            return outer;
        }

        private StackPanel BuildModelTileCarousel(AtomElement[] elems, bool interactive, Color accent, bool light, bool zh)
        {
            StackPanel container = new StackPanel();
            container.Orientation = Orientation.Vertical;
            container.Margin = new Thickness(0, 0, 0, 6);

            ScrollViewer tileScroller = new ScrollViewer();
            tileScroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            tileScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            tileScroller.CanContentScroll = false;
            tileScroller.PanningMode = PanningMode.HorizontalOnly;

            StackPanel tileRow = new StackPanel();
            tileRow.Orientation = Orientation.Horizontal;
            for (int i = 0; i < elems.Length; i++)
            {
                AtomElement el = elems[i];
                bool selected = settings.ManualElementOverride == el || (!settings.ManualElementOverride.HasValue && latestModelState.Element == el);
                double rightMargin = (i == elems.Length - 1) ? 0.0 : ModelTileGap;
                tileRow.Children.Add(MakePeriodicTile(el, selected, light, zh, ModelTileWidth, rightMargin));
            }
            tileScroller.Content = tileRow;
            container.Children.Add(tileScroller);

            // Sleek horizontal scrollbar track + thumb directly below the model tiles (active & proportional at 5+ models)
            Canvas hScrollCanvas = new Canvas();
            hScrollCanvas.Height = 14.0;
            hScrollCanvas.Margin = new Thickness(0, 4, 0, 0);
            hScrollCanvas.Background = Brushes.Transparent;
            hScrollCanvas.ClipToBounds = false;

            Border hTrack = new Border();
            hTrack.Height = 5.0;
            hTrack.CornerRadius = new CornerRadius(2.5);
            hTrack.Background = new SolidColorBrush(light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(36, 255, 255, 255));
            hTrack.IsHitTestVisible = false;
            Canvas.SetTop(hTrack, 4.5);
            hScrollCanvas.Children.Add(hTrack);

            Border hThumb = new Border();
            hThumb.Height = 5.0;
            hThumb.CornerRadius = new CornerRadius(2.5);
            hThumb.IsHitTestVisible = false;
            Canvas.SetTop(hThumb, 4.5);
            Panel.SetZIndex(hThumb, 2);
            hScrollCanvas.Children.Add(hThumb);

            container.Children.Add(hScrollCanvas);

            Action<double, double> updateHScrollVisuals = delegate(double viewW, double offset)
            {
                double effViewW = (viewW > 60.0) ? viewW : DefaultStripViewportWidth;
                double extentW, scrollableW, thumbW, thumbX;
                ComputeTileStripMetrics(elems.Length, effViewW, offset, out extentW, out scrollableW, out thumbW, out thumbX);

                hTrack.Width = effViewW;
                Canvas.SetLeft(hTrack, 0.0);
                hThumb.Width = thumbW;
                Canvas.SetLeft(hThumb, thumbX);

                if (scrollableW > 0.5)
                {
                    hThumb.Background = new SolidColorBrush(Color.FromArgb(light ? (byte)195 : (byte)220, accent.R, accent.G, accent.B));
                    hScrollCanvas.Cursor = Cursors.Hand;
                }
                else
                {
                    hThumb.Background = new SolidColorBrush(Color.FromArgb(light ? (byte)65 : (byte)80, accent.R, accent.G, accent.B));
                    hScrollCanvas.Cursor = Cursors.Arrow;
                }
            };

            updateHScrollVisuals(DefaultStripViewportWidth, savedTileScrollOffset);

            if (interactive)
            {
                tileScroller.Loaded += delegate
                {
                    double clampedInit = Math.Max(0.0, Math.Min(tileScroller.ScrollableWidth, savedTileScrollOffset));
                    if (Math.Abs(clampedInit - tileScroller.HorizontalOffset) > 0.5)
                    {
                        tileScroller.ScrollToHorizontalOffset(clampedInit);
                    }
                    double vw = tileScroller.ViewportWidth > 60.0 ? tileScroller.ViewportWidth : tileScroller.ActualWidth;
                    updateHScrollVisuals(vw, tileScroller.HorizontalOffset);
                };

                tileScroller.SizeChanged += delegate
                {
                    double vw = tileScroller.ViewportWidth > 60.0 ? tileScroller.ViewportWidth : tileScroller.ActualWidth;
                    updateHScrollVisuals(vw, tileScroller.HorizontalOffset);
                };

                tileScroller.ScrollChanged += delegate
                {
                    savedTileScrollOffset = tileScroller.HorizontalOffset;
                    double vw = tileScroller.ViewportWidth > 60.0 ? tileScroller.ViewportWidth : tileScroller.ActualWidth;
                    updateHScrollVisuals(vw, tileScroller.HorizontalOffset);
                };

                tileScroller.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
                {
                    if (tileScroller.ScrollableWidth > 0.5)
                    {
                        double nextOff = Math.Max(0.0, Math.Min(tileScroller.ScrollableWidth, tileScroller.HorizontalOffset - e.Delta * 0.45));
                        tileScroller.ScrollToHorizontalOffset(nextOff);
                        savedTileScrollOffset = nextOff;
                        e.Handled = true;
                    }
                };

                bool isDraggingHScroll = false;
                double dragStartX = 0.0;
                double dragStartOffset = 0.0;

                hScrollCanvas.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    double vw = tileScroller.ViewportWidth > 60.0 ? tileScroller.ViewportWidth : hScrollCanvas.ActualWidth;
                    double extentW, scrollableW, thumbW, thumbX;
                    ComputeTileStripMetrics(elems.Length, vw, tileScroller.HorizontalOffset, out extentW, out scrollableW, out thumbW, out thumbX);
                    if (scrollableW <= 0.5)
                    {
                        return;
                    }

                    e.Handled = true;
                    double x = e.GetPosition(hScrollCanvas).X;
                    double maxThumbX = Math.Max(1.0, vw - thumbW);

                    if (x >= thumbX - 4.0 && x <= thumbX + thumbW + 4.0)
                    {
                        dragStartX = x;
                        dragStartOffset = tileScroller.HorizontalOffset;
                    }
                    else
                    {
                        double desiredThumbX = Math.Max(0.0, Math.Min(maxThumbX, x - thumbW * 0.5));
                        double jumpOff = (desiredThumbX / maxThumbX) * scrollableW;
                        tileScroller.ScrollToHorizontalOffset(jumpOff);
                        savedTileScrollOffset = jumpOff;
                        dragStartX = x;
                        dragStartOffset = jumpOff;
                    }

                    isDraggingHScroll = true;
                    hScrollCanvas.CaptureMouse();
                };

                hScrollCanvas.MouseMove += delegate(object s, MouseEventArgs e)
                {
                    if (!isDraggingHScroll)
                    {
                        return;
                    }
                    double vw = tileScroller.ViewportWidth > 60.0 ? tileScroller.ViewportWidth : hScrollCanvas.ActualWidth;
                    double extentW, scrollableW, thumbW, thumbX;
                    ComputeTileStripMetrics(elems.Length, vw, dragStartOffset, out extentW, out scrollableW, out thumbW, out thumbX);
                    if (scrollableW <= 0.5)
                    {
                        return;
                    }

                    double dx = e.GetPosition(hScrollCanvas).X - dragStartX;
                    double maxThumbX = Math.Max(1.0, vw - thumbW);
                    double deltaOff = (dx / maxThumbX) * scrollableW;
                    double nextOff = Math.Max(0.0, Math.Min(scrollableW, dragStartOffset + deltaOff));
                    tileScroller.ScrollToHorizontalOffset(nextOff);
                    savedTileScrollOffset = nextOff;
                };

                hScrollCanvas.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    if (!isDraggingHScroll)
                    {
                        return;
                    }
                    e.Handled = true;
                    isDraggingHScroll = false;
                    if (hScrollCanvas.IsMouseCaptured)
                    {
                        hScrollCanvas.ReleaseMouseCapture();
                    }
                };

                hScrollCanvas.LostMouseCapture += delegate
                {
                    isDraggingHScroll = false;
                };
            }

            return container;
        }

        private Border MakePeriodicTile(AtomElement el, bool selected, bool light, bool zh, double tileWidth, double rightMargin)
        {
            ElementVisualInfo info = ElementVisualCatalog.GetInfo(el);
            Color bg = light ? info.LightTileBg : info.DarkTileBg;
            Color ink = light ? info.LightSymbolInk : info.DarkSymbolInk;
            Color lip = light ? info.LightEdgeLip : info.DarkEdgeLip;
            Color ring = light ? info.LightAccent : info.DarkAccent;

            Border outerRing = new Border();
            outerRing.Width = tileWidth;
            outerRing.Margin = new Thickness(0, 0, rightMargin, 0);
            outerRing.CornerRadius = new CornerRadius(9);
            outerRing.Padding = new Thickness(1.5);
            outerRing.BorderThickness = new Thickness(selected ? 2.0 : 1.0);
            outerRing.BorderBrush = new SolidColorBrush(selected
                ? ring
                : (light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(30, 255, 255, 255)));
            outerRing.Cursor = Cursors.Hand;

            Border tile = new Border();
            tile.CornerRadius = new CornerRadius(7);
            tile.Background = new SolidColorBrush(bg);
            tile.BorderThickness = new Thickness(0, 0, 0, 2.5);
            tile.BorderBrush = new SolidColorBrush(lip);
            tile.Padding = new Thickness(6, 5, 6, 5);

            StackPanel sp = new StackPanel();
            TextBlock num = new TextBlock();
            num.Text = info.AtomicNumber.ToString();
            num.FontSize = 9.5;
            num.FontWeight = FontWeights.SemiBold;
            num.FontFamily = new FontFamily("Segoe UI");
            num.Foreground = new SolidColorBrush(Color.FromArgb(195, ink.R, ink.G, ink.B));
            sp.Children.Add(num);

            TextBlock sym = new TextBlock();
            sym.Text = info.Symbol;
            sym.FontSize = 16.5;
            sym.FontWeight = FontWeights.Bold;
            sym.FontFamily = new FontFamily("Segoe UI");
            sym.Foreground = new SolidColorBrush(ink);
            sym.HorizontalAlignment = HorizontalAlignment.Center;
            sym.Margin = new Thickness(0, -1, 0, 0);
            sp.Children.Add(sym);

            TextBlock nm = new TextBlock();
            nm.Text = zh ? info.ChineseName : info.EnglishName;
            nm.FontSize = 9.5;
            nm.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            nm.Foreground = new SolidColorBrush(Color.FromArgb(205, ink.R, ink.G, ink.B));
            nm.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(nm);

            tile.Child = sp;
            outerRing.Child = tile;

            outerRing.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                settings.ManualElementOverride = el;
                NotifyAndRebuild();
            };

            return outerRing;
        }

        private static TextBlock MakeSectionLabel(string text, Color fgMuted)
        {
            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.FontSize = 11.0;
            tb.FontWeight = FontWeights.SemiBold;
            tb.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            tb.Foreground = new SolidColorBrush(fgMuted);
            tb.Margin = new Thickness(0, 10, 0, 5);
            return tb;
        }

        private static Border MakeActionPill(string text, bool active, Color accent, bool light, Color fgPrimary, Color fgMuted, Action onClick)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(9, 5, 9, 5);
            b.BorderThickness = new Thickness(1.0);
            b.Cursor = Cursors.Hand;

            if (active)
            {
                b.Background = new SolidColorBrush(Color.FromArgb(light ? (byte)32 : (byte)48, accent.R, accent.G, accent.B));
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(215, accent.R, accent.G, accent.B));
            }
            else
            {
                b.Background = new SolidColorBrush(light ? Color.FromArgb(14, 0, 0, 0) : Color.FromArgb(18, 255, 255, 255));
                b.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(35, 0, 0, 0) : Color.FromArgb(38, 255, 255, 255));
            }

            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.FontSize = 11.2;
            tb.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            tb.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            tb.Foreground = new SolidColorBrush(active ? accent : fgPrimary);
            tb.TextWrapping = TextWrapping.Wrap;
            tb.TextAlignment = TextAlignment.Center;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            tb.VerticalAlignment = VerticalAlignment.Center;
            b.Child = tb;

            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (onClick != null) { onClick(); }
            };
            return b;
        }

        private static Border MakeSegmentButton(string text, bool active, Color accent, bool light, Color fgPrimary, Color fgMuted, Action onClick)
        {
            Border b = MakeActionPill(text, active, accent, light, fgPrimary, fgMuted, onClick);
            b.Margin = new Thickness(2, 0, 2, 0);
            b.Padding = new Thickness(4, 5, 4, 5);
            return b;
        }

        private static Border MakeToggleRow(string label, bool isOn, Color accent, bool light, Color fgPrimary, Color rowBg, Action onToggle)
        {
            Border row = new Border();
            row.CornerRadius = new CornerRadius(9);
            row.Background = new SolidColorBrush(rowBg);
            row.Padding = new Thickness(10, 6, 10, 6);
            row.Margin = new Thickness(0, 2, 0, 2);
            row.Cursor = Cursors.Hand;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock tb = new TextBlock();
            tb.Text = label;
            tb.FontSize = 11.5;
            tb.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            tb.Foreground = new SolidColorBrush(fgPrimary);
            tb.TextWrapping = TextWrapping.Wrap;
            tb.Margin = new Thickness(0, 0, 8, 0);
            tb.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tb, 0);
            g.Children.Add(tb);

            Border track = new Border();
            track.Width = 36;
            track.Height = 20;
            track.CornerRadius = new CornerRadius(10);
            track.Padding = new Thickness(2);
            track.Background = new SolidColorBrush(isOn
                ? accent
                : (light ? Color.FromRgb(198, 204, 214) : Color.FromRgb(74, 78, 88)));

            Ellipse thumb = new Ellipse();
            thumb.Width = 16;
            thumb.Height = 16;
            thumb.Fill = Brushes.White;
            thumb.HorizontalAlignment = isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            thumb.VerticalAlignment = VerticalAlignment.Center;
            track.Child = thumb;

            Grid.SetColumn(track, 1);
            g.Children.Add(track);
            row.Child = g;

            row.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (onToggle != null) { onToggle(); }
            };
            return row;
        }

        private Border BuildScaleSliderCard(bool interactive, Color accent, bool light, Color cardBg, Color fgPrimary, Color fgMuted, Color sectionBg, bool zh)
        {
            Border card = new Border();
            card.CornerRadius = new CornerRadius(10);
            card.Background = new SolidColorBrush(sectionBg);
            card.Padding = new Thickness(12, 8, 12, 8);
            card.Margin = new Thickness(0, 6, 0, 6);

            StackPanel col = new StackPanel();
            col.Orientation = Orientation.Vertical;
            card.Child = col;

            // Top Row: Title + live percentage pill
            Grid topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock titleTb = new TextBlock();
            titleTb.Text = zh ? "大小比例" : "Size Scale";
            titleTb.FontSize = 11.5;
            titleTb.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            titleTb.Foreground = new SolidColorBrush(fgPrimary);
            titleTb.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(titleTb, 0);
            topRow.Children.Add(titleTb);

            Border badgeBorder = new Border();
            badgeBorder.CornerRadius = new CornerRadius(6);
            badgeBorder.Padding = new Thickness(7, 2, 7, 2);
            badgeBorder.BorderThickness = new Thickness(1.0);
            badgeBorder.Background = new SolidColorBrush(Color.FromArgb(light ? (byte)28 : (byte)42, accent.R, accent.G, accent.B));
            badgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B));

            TextBlock valueBadgeTxt = new TextBlock();
            valueBadgeTxt.FontSize = 10.8;
            valueBadgeTxt.FontWeight = FontWeights.SemiBold;
            valueBadgeTxt.FontFamily = new FontFamily("Consolas, Segoe UI, Microsoft JhengHei UI");
            valueBadgeTxt.Foreground = new SolidColorBrush(accent);
            badgeBorder.Child = valueBadgeTxt;
            Grid.SetColumn(badgeBorder, 1);
            topRow.Children.Add(badgeBorder);
            col.Children.Add(topRow);

            // Middle Row: Interactive Canvas track + 5 circular dot stops + draggable thumb
            Canvas trackCanvas = new Canvas();
            trackCanvas.Height = 28.0;
            trackCanvas.Margin = new Thickness(0, 5, 0, 2);
            trackCanvas.Background = Brushes.Transparent;
            trackCanvas.Cursor = Cursors.Hand;
            trackCanvas.ClipToBounds = false;

            Border bgRail = new Border();
            bgRail.Height = 4.0;
            bgRail.CornerRadius = new CornerRadius(2.0);
            bgRail.Background = new SolidColorBrush(light ? Color.FromArgb(46, 0, 0, 0) : Color.FromArgb(56, 255, 255, 255));
            bgRail.IsHitTestVisible = false;
            Canvas.SetTop(bgRail, 12.0);
            trackCanvas.Children.Add(bgRail);

            Border activeRail = new Border();
            activeRail.Height = 4.0;
            activeRail.CornerRadius = new CornerRadius(2.0);
            activeRail.Background = new SolidColorBrush(accent);
            activeRail.IsHitTestVisible = false;
            Canvas.SetTop(activeRail, 12.0);
            trackCanvas.Children.Add(activeRail);

            Ellipse[] dots = new Ellipse[WidgetSettings.ScalePresets.Length];
            for (int i = 0; i < dots.Length; i++)
            {
                Ellipse dot = new Ellipse();
                dot.Width = 10.0;
                dot.Height = 10.0;
                dot.StrokeThickness = 1.8;
                dot.IsHitTestVisible = false;
                Canvas.SetTop(dot, 9.0);
                Panel.SetZIndex(dot, 4);
                trackCanvas.Children.Add(dot);
                dots[i] = dot;
            }

            Border thumb = new Border();
            thumb.Width = 18.0;
            thumb.Height = 18.0;
            thumb.CornerRadius = new CornerRadius(9.0);
            thumb.Background = Brushes.White;
            thumb.BorderThickness = new Thickness(2.5);
            thumb.BorderBrush = new SolidColorBrush(accent);
            thumb.IsHitTestVisible = false;
            DropShadowEffect thumbShadow = new DropShadowEffect();
            thumbShadow.BlurRadius = 5.0;
            thumbShadow.ShadowDepth = 1.0;
            thumbShadow.Opacity = 0.28;
            thumbShadow.Color = Colors.Black;
            thumb.Effect = thumbShadow;
            Canvas.SetTop(thumb, 5.0);
            Panel.SetZIndex(thumb, 10);
            trackCanvas.Children.Add(thumb);

            col.Children.Add(trackCanvas);

            // Bottom Row: 5 clickable percentage labels aligned under dots
            Canvas labelCanvas = new Canvas();
            labelCanvas.Height = 17.0;
            labelCanvas.ClipToBounds = false;
            Border[] labelPills = new Border[WidgetSettings.ScalePresets.Length];
            TextBlock[] labelTbs = new TextBlock[WidgetSettings.ScalePresets.Length];

            for (int i = 0; i < WidgetSettings.ScalePresets.Length; i++)
            {
                Border lp = new Border();
                lp.Background = Brushes.Transparent;
                lp.Padding = new Thickness(2, 0, 2, 0);
                lp.Cursor = Cursors.Hand;

                TextBlock ltb = new TextBlock();
                int pct = (int)Math.Round(WidgetSettings.ScalePresets[i] * 100.0);
                ltb.Text = pct.ToString() + "%";
                ltb.FontSize = 9.8;
                ltb.FontFamily = new FontFamily("Consolas, Segoe UI");
                lp.Child = ltb;

                labelCanvas.Children.Add(lp);
                labelPills[i] = lp;
                labelTbs[i] = ltb;
            }
            col.Children.Add(labelCanvas);

            const double padX = 10.0;
            Action<double> updateSliderVisuals = delegate(double widthVal)
            {
                double w = (widthVal > 60.0) ? widthVal : 286.0;
                double usable = Math.Max(40.0, w - 2.0 * padX);
                double cur = WidgetSettings.ClampScale(settings.ScaleMultiplier);
                double curT = (cur - WidgetSettings.MinScale) / (WidgetSettings.MaxScale - WidgetSettings.MinScale);
                curT = Math.Max(0.0, Math.Min(1.0, curT));
                int exactIdx = WidgetSettings.TryGetExactScalePresetIndex(cur);
                int pctInt = (int)Math.Round(cur * 100.0);

                valueBadgeTxt.Text = (exactIdx == 1)
                    ? string.Format("{0}% · {1}", pctInt, zh ? "標準" : "Std")
                    : string.Format("{0}%", pctInt);

                bgRail.Width = usable;
                Canvas.SetLeft(bgRail, padX);

                double activeW = usable * curT;
                activeRail.Width = Math.Max(0.0, activeW);
                Canvas.SetLeft(activeRail, padX);

                double thumbCenterX = padX + activeW;
                Canvas.SetLeft(thumb, thumbCenterX - 9.0);

                for (int i = 0; i < WidgetSettings.ScalePresets.Length; i++)
                {
                    double st = i / (double)(WidgetSettings.ScalePresets.Length - 1);
                    double dotCenterX = padX + usable * st;
                    Canvas.SetLeft(dots[i], dotCenterX - 5.0);

                    bool covered = (cur + 0.005) >= WidgetSettings.ScalePresets[i];
                    if (covered)
                    {
                        dots[i].Fill = new SolidColorBrush(accent);
                        dots[i].Stroke = new SolidColorBrush(cardBg);
                    }
                    else
                    {
                        dots[i].Fill = new SolidColorBrush(cardBg);
                        dots[i].Stroke = new SolidColorBrush(light ? Color.FromArgb(135, 110, 90, 100) : Color.FromArgb(145, 165, 152, 146));
                    }

                    bool isExact = (exactIdx == i);
                    labelTbs[i].Foreground = new SolidColorBrush(isExact ? accent : fgMuted);
                    labelTbs[i].FontWeight = isExact ? FontWeights.SemiBold : FontWeights.Normal;

                    labelPills[i].Measure(new Size(100.0, 30.0));
                    double lw = labelPills[i].DesiredSize.Width;
                    double lx = dotCenterX - lw * 0.5;
                    lx = Math.Max(0.0, Math.Min(w - lw, lx));
                    Canvas.SetLeft(labelPills[i], lx);
                    Canvas.SetTop(labelPills[i], 0.0);
                }
            };

            updateSliderVisuals(286.0);
            trackCanvas.SizeChanged += delegate(object s, SizeChangedEventArgs e)
            {
                if (e.NewSize.Width > 40.0)
                {
                    updateSliderVisuals(e.NewSize.Width);
                }
            };

            if (interactive)
            {
                bool isDraggingScale = false;
                bool scaleDragMoved = false;
                double scaleDownX = 0.0;
                int scaleDownStopIdx = -1;

                trackCanvas.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    double w = trackCanvas.ActualWidth > 40.0 ? trackCanvas.ActualWidth : 286.0;
                    double usable = Math.Max(40.0, w - 2.0 * padX);
                    double x = e.GetPosition(trackCanvas).X;

                    int hitStop = -1;
                    double bestDist = 9.5;
                    for (int i = 0; i < WidgetSettings.ScalePresets.Length; i++)
                    {
                        double dotX = padX + usable * (i / (double)(WidgetSettings.ScalePresets.Length - 1));
                        double dist = Math.Abs(x - dotX);
                        if (dist <= bestDist)
                        {
                            bestDist = dist;
                            hitStop = i;
                        }
                    }

                    scaleDownStopIdx = hitStop;
                    scaleDownX = x;
                    scaleDragMoved = false;
                    isDraggingScale = true;

                    double nextVal;
                    if (hitStop >= 0)
                    {
                        nextVal = WidgetSettings.ScalePresets[hitStop];
                    }
                    else
                    {
                        double t = Math.Max(0.0, Math.Min(1.0, (x - padX) / usable));
                        nextVal = WidgetSettings.ClampScale(WidgetSettings.MinScale + t * (WidgetSettings.MaxScale - WidgetSettings.MinScale));
                    }

                    settings.ScaleMultiplier = nextVal;
                    updateSliderVisuals(w);
                    if (ScaleLiveChanged != null)
                    {
                        ScaleLiveChanged();
                    }
                    trackCanvas.CaptureMouse();
                };

                trackCanvas.MouseMove += delegate(object s, MouseEventArgs e)
                {
                    if (!isDraggingScale)
                    {
                        return;
                    }
                    double w = trackCanvas.ActualWidth > 40.0 ? trackCanvas.ActualWidth : 286.0;
                    double usable = Math.Max(40.0, w - 2.0 * padX);
                    double x = e.GetPosition(trackCanvas).X;

                    if (!scaleDragMoved && Math.Abs(x - scaleDownX) <= 3.0 && scaleDownStopIdx >= 0)
                    {
                        return;
                    }
                    scaleDragMoved = true;

                    double t = Math.Max(0.0, Math.Min(1.0, (x - padX) / usable));
                    double nextVal = WidgetSettings.ClampScale(WidgetSettings.MinScale + t * (WidgetSettings.MaxScale - WidgetSettings.MinScale));
                    if (Math.Abs(settings.ScaleMultiplier - nextVal) >= 0.005)
                    {
                        settings.ScaleMultiplier = nextVal;
                        updateSliderVisuals(w);
                        if (ScaleLiveChanged != null)
                        {
                            ScaleLiveChanged();
                        }
                    }
                };

                trackCanvas.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    if (!isDraggingScale)
                    {
                        return;
                    }
                    e.Handled = true;
                    isDraggingScale = false;
                    if (trackCanvas.IsMouseCaptured)
                    {
                        trackCanvas.ReleaseMouseCapture();
                    }
                    if (SettingsChanged != null)
                    {
                        SettingsChanged();
                    }
                };

                trackCanvas.LostMouseCapture += delegate
                {
                    if (isDraggingScale)
                    {
                        isDraggingScale = false;
                        if (SettingsChanged != null)
                        {
                            SettingsChanged();
                        }
                    }
                };

                for (int i = 0; i < labelPills.Length; i++)
                {
                    int stopIdx = i;
                    labelPills[stopIdx].MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                    {
                        e.Handled = true;
                    };
                    labelPills[stopIdx].MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                    {
                        e.Handled = true;
                        settings.ScaleMultiplier = WidgetSettings.ScalePresets[stopIdx];
                        double w = trackCanvas.ActualWidth > 40.0 ? trackCanvas.ActualWidth : 286.0;
                        updateSliderVisuals(w);
                        if (ScaleLiveChanged != null)
                        {
                            ScaleLiveChanged();
                        }
                        if (SettingsChanged != null)
                        {
                            SettingsChanged();
                        }
                    };
                }
            }

            return card;
        }

        private static bool IsDescendantOf(DependencyObject source, DependencyObject ancestor)
        {
            DependencyObject cur = source;
            while (cur != null)
            {
                if (object.ReferenceEquals(cur, ancestor))
                {
                    return true;
                }
                DependencyObject parent = null;
                try
                {
                    parent = VisualTreeHelper.GetParent(cur);
                }
                catch
                {
                }
                if (parent == null)
                {
                    try
                    {
                        parent = LogicalTreeHelper.GetParent(cur);
                    }
                    catch
                    {
                    }
                }
                cur = parent;
            }
            return false;
        }

        private void NotifyAndRebuild()
        {
            if (SettingsChanged != null)
            {
                SettingsChanged();
            }
            RebuildContent();
        }
    }
}
