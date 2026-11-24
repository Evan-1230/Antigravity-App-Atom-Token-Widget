using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace AntigravityAtomTokenWidget
{
    public sealed class WidgetWindow : Window
    {
        public const double CanvasWidth = 660.0;
        public const double CanvasHeight = 520.0;

        private static readonly Geometry GeminiSparkleGeometry = CreateGeminiSparkleGeometry();

        private static Geometry CreateGeminiSparkleGeometry()
        {
            Geometry g = Geometry.Parse(
                "M 11.35,1.25 Q 12,0.2 12.65,1.25 "
                + "C 14.6,6.4 17.6,9.4 22.75,11.35 Q 23.8,12 22.75,12.65 "
                + "C 17.6,14.6 14.6,17.6 12.65,22.75 Q 12,23.8 11.35,22.75 "
                + "C 9.4,17.6 6.4,14.6 1.25,12.65 Q 0.2,12 1.25,11.35 "
                + "C 6.4,9.4 9.4,6.4 11.35,1.25 Z");
            g.Freeze();
            return g;
        }

        private readonly Canvas rootCanvas;
        private readonly Border atomHost;
        private readonly Atom3DControl atomControl;

        private readonly Border bubbleBorder;
        private readonly StackPanel bubbleStack;
        private readonly Polygon pointerDown;
        private readonly Polygon pointerUp;

        // Compact header elements
        private readonly Grid compactRow;
        private readonly Border elementPill;
        private readonly StackPanel elementPillPanel;
        private readonly TextBlock elementPillText;
        private readonly System.Windows.Shapes.Path elementPillGeminiIcon;
        private readonly TextBlock elementPillSuffixText;
        private readonly TextBlock orchestratorBadge;
        private readonly StackPanel compactTotalPanel;
        private readonly TextBlock compactTotalText;
        private readonly TextBlock compactApiSepText;
        private readonly TextBlock compactApiValueText;
        private readonly Ellipse statusDot;

        // Hover-expanded detail elements
        private readonly StackPanel detailPanel;
        private readonly Border detailDivider1;
        private readonly TextBlock detailModelTitle;
        private readonly TextBlock detailPreviewBadge;
        private readonly TextBlock detailElementMeta;
        private readonly TextBlock detailStatusLabel;

        private readonly Border scopeChipSession;
        private readonly TextBlock scopeChipSessionText;
        private readonly Border scopeChipToday;
        private readonly TextBlock scopeChipTodayText;
        private readonly Border scopeChipAll;
        private readonly TextBlock scopeChipAllText;
        private readonly Border subChip;
        private readonly TextBlock subChipText;

        private readonly TextBlock lblPromptUncached;
        private readonly TextBlock valPromptUncached;
        private readonly TextBlock lblCachedContent;
        private readonly TextBlock valCachedContent;
        private readonly TextBlock lblCandidates;
        private readonly TextBlock valCandidates;
        private readonly TextBlock lblThoughts;
        private readonly TextBlock valThoughts;
        private readonly Grid rowCacheCreation;
        private readonly TextBlock lblCacheCreation;
        private readonly TextBlock valCacheCreation;
        private readonly Border detailDivider2;
        private readonly TextBlock lblTotal;
        private readonly TextBlock valTotal;
        private readonly TextBlock detailFooterText;
        private readonly TextBlock resetAutoLink;

        private readonly DispatcherTimer hoverCollapseTimer;
        private IntPtr hwnd = IntPtr.Zero;

        private WidgetSettings settings;
        private DataSnapshot currentSnapshot = new DataSnapshot();
        private bool isLightTheme = false;

        // Layout geometry (DIPs relative to target window)
        private double atomDipSize = 68.0;
        private Rect rawAppBoundsDip = new Rect(0, 0, 1280, 800);
        private Rect workAreaBoundsDip = new Rect(0, 0, 1920, 1040);
        private bool isAppFullscreenOrMax = false;
        private double targetWinWidthDip = 1280.0;
        private double targetWinHeightDip = 800.0;
        private double targetWinLeftDip = 0.0;
        private double targetWinTopDip = 0.0;
        private bool flipBubbleBelow = false;
        private double atomCenterXInCanvas = CanvasWidth * 0.5;
        private double atomTopInCanvas = CanvasHeight - 80.0;

        // Hover & rolling counter animations
        private bool isPointerInsideBubble = false;
        private double expandProgress = 0.0;
        private double targetTotalAnimated = 0.0;
        private double displayedTotalAnimated = 0.0;
        private long lastRenderedCompactVal = -1;
        private double targetApiValueUsd = 0.0;
        private double displayedApiValueUsd = 0.0;
        private string lastRenderedCompactApiText = "";
        private bool hasAppliedRealData = false;

        // Drag state
        private bool isDragging = false;
        private Point dragStartScreenPx;
        private double dragStartAnchorX;
        private double dragStartAnchorY;
        private double currentDpiScale = 1.0;

        public event Action SettingsChangedByUser;
        public event Action<Point> SettingsWindowRequested;
        public event Action PositionNeedsSync;

        public string DebugElementPillText
        {
            get
            {
                string res = "";
                if (elementPillText.Visibility == Visibility.Visible && !string.IsNullOrEmpty(elementPillText.Text))
                {
                    res += elementPillText.Text;
                }
                if (elementPillGeminiIcon.Visibility == Visibility.Visible)
                {
                    res += "\u2726";
                }
                if (elementPillSuffixText.Visibility == Visibility.Visible && !string.IsNullOrEmpty(elementPillSuffixText.Text))
                {
                    res += elementPillSuffixText.Text;
                }
                return res;
            }
        }

        public bool DebugElementPillHasGeminiIcon
        {
            get { return elementPillGeminiIcon.Visibility == Visibility.Visible; }
        }

        public bool DebugElementPillIconMatchesTextColor
        {
            get
            {
                SolidColorBrush iconBrush = elementPillGeminiIcon.Fill as SolidColorBrush;
                SolidColorBrush textBrush = elementPillSuffixText.Foreground as SolidColorBrush;
                if (iconBrush == null || textBrush == null)
                {
                    return false;
                }
                return iconBrush.Color == textBrush.Color;
            }
        }

        public string DebugCompactTotalText
        {
            get { return compactTotalText.Text; }
        }

        public string DebugCompactApiValueText
        {
            get { return compactApiValueText.Text; }
        }

        public bool DebugCompactApiValueVisible
        {
            get { return compactApiValueText.Visibility == Visibility.Visible; }
        }

        public bool DebugCompactApiSepBold
        {
            get { return compactApiSepText.FontWeight == FontWeights.Bold; }
        }

        public bool DebugCompactApiSepMatchesAccent
        {
            get
            {
                SolidColorBrush sepBrush = compactApiSepText.Foreground as SolidColorBrush;
                SolidColorBrush accentBr = valTotal.Foreground as SolidColorBrush;
                if (sepBrush == null || accentBr == null)
                {
                    return false;
                }
                return sepBrush.Color == accentBr.Color;
            }
        }

        public bool DebugCompactApiValueMatchesPrimaryText
        {
            get
            {
                SolidColorBrush apiBrush = compactApiValueText.Foreground as SolidColorBrush;
                SolidColorBrush primaryBr = compactTotalText.Foreground as SolidColorBrush;
                if (apiBrush == null || primaryBr == null)
                {
                    return false;
                }
                return apiBrush.Color == primaryBr.Color;
            }
        }

        public string DebugDetailTotalText
        {
            get { return valTotal.Text; }
        }

        public bool DebugOrchestratorVisible
        {
            get { return orchestratorBadge.Visibility == Visibility.Visible; }
        }

        public string DebugDetailModelTitle
        {
            get { return detailModelTitle.Text; }
        }

        public string DebugDetailPreviewBadge
        {
            get { return detailPreviewBadge.Text; }
        }

        public string DebugDetailElementMeta
        {
            get { return detailElementMeta.Text; }
        }

        public double DebugExpandedBubbleHeight
        {
            get
            {
                InvalidateSubtreeMeasure(bubbleBorder);
                bubbleBorder.Measure(new Size(CanvasWidth - 8.0, CanvasHeight - 16.0));
                return bubbleBorder.DesiredSize.Height;
            }
        }

        public WidgetWindow(WidgetSettings initialSettings)
        {
            this.settings = initialSettings;
            this.Title = "Antigravity Atom Token Widget";
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.ResizeMode = ResizeMode.NoResize;
            this.ShowInTaskbar = false;
            this.ShowActivated = false;
            this.Topmost = false;
            this.Width = CanvasWidth;
            this.Height = CanvasHeight;
            this.Left = -32000;
            this.Top = -32000;

            rootCanvas = new Canvas();
            rootCanvas.Width = CanvasWidth;
            rootCanvas.Height = CanvasHeight;
            rootCanvas.Background = Brushes.Transparent;
            rootCanvas.ClipToBounds = false;
            this.Content = rootCanvas;

            // 1. 3D Atom Host (subtle 1/255 alpha hit layer so drag & right-click work reliably)
            atomControl = new Atom3DControl();
            atomHost = new Border();
            atomHost.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            atomHost.CornerRadius = new CornerRadius(999);
            atomHost.Cursor = Cursors.Hand;
            atomHost.Child = atomControl;
            atomHost.MouseLeftButtonDown += OnAtomMouseLeftButtonDown;
            atomHost.MouseMove += OnAtomMouseMove;
            atomHost.MouseLeftButtonUp += OnAtomMouseLeftButtonUp;
            atomHost.MouseRightButtonUp += OnWidgetMouseRightButtonUp;
            rootCanvas.Children.Add(atomHost);

            // 2. Speech Bubble Pointers
            pointerDown = new Polygon();
            pointerDown.Points = new PointCollection(new Point[] { new Point(0, 0), new Point(12, 0), new Point(6, 7) });
            pointerDown.StrokeThickness = 1.0;
            pointerDown.IsHitTestVisible = false;
            rootCanvas.Children.Add(pointerDown);

            pointerUp = new Polygon();
            pointerUp.Points = new PointCollection(new Point[] { new Point(6, 0), new Point(12, 7), new Point(0, 7) });
            pointerUp.StrokeThickness = 1.0;
            pointerUp.IsHitTestVisible = false;
            pointerUp.Visibility = Visibility.Collapsed;
            rootCanvas.Children.Add(pointerUp);

            // 3. Speech Bubble + Hover Detail Card
            bubbleBorder = new Border();
            bubbleBorder.CornerRadius = new CornerRadius(12);
            bubbleBorder.BorderThickness = new Thickness(1.2);
            bubbleBorder.Padding = new Thickness(10, 6, 10, 6);
            bubbleBorder.SnapsToDevicePixels = true;
            bubbleBorder.MouseEnter += OnBubbleMouseEnter;
            bubbleBorder.MouseLeave += OnBubbleMouseLeave;
            bubbleBorder.MouseRightButtonUp += OnWidgetMouseRightButtonUp;
            bubbleBorder.SizeChanged += delegate
            {
                AlignBubbleAroundAtom();
            };

            System.Windows.Media.Effects.DropShadowEffect shadow = new System.Windows.Media.Effects.DropShadowEffect();
            shadow.BlurRadius = 14.0;
            shadow.ShadowDepth = 2.0;
            shadow.Opacity = 0.28;
            shadow.Color = Colors.Black;
            bubbleBorder.Effect = shadow;

            bubbleStack = new StackPanel();
            bubbleStack.Orientation = Orientation.Vertical;
            bubbleBorder.Child = bubbleStack;

            // Compact Row
            compactRow = new Grid();
            compactRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            compactRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            compactRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            compactRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            elementPill = new Border();
            elementPill.CornerRadius = new CornerRadius(6);
            elementPill.Padding = new Thickness(7, 1.5, 7, 1.5);
            elementPill.Margin = new Thickness(0, 0, 6, 0);
            elementPill.VerticalAlignment = VerticalAlignment.Center;
            elementPill.HorizontalAlignment = HorizontalAlignment.Left;

            elementPillPanel = new StackPanel();
            elementPillPanel.Orientation = Orientation.Horizontal;
            elementPillPanel.HorizontalAlignment = HorizontalAlignment.Center;
            elementPillPanel.VerticalAlignment = VerticalAlignment.Center;

            elementPillText = new TextBlock();
            elementPillText.FontSize = 11.0;
            elementPillText.FontWeight = FontWeights.Bold;
            elementPillText.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            elementPillText.TextAlignment = TextAlignment.Center;
            elementPillText.VerticalAlignment = VerticalAlignment.Center;

            elementPillGeminiIcon = new System.Windows.Shapes.Path();
            elementPillGeminiIcon.Data = GeminiSparkleGeometry;
            elementPillGeminiIcon.Width = 11.5;
            elementPillGeminiIcon.Height = 11.5;
            elementPillGeminiIcon.Stretch = Stretch.Uniform;
            elementPillGeminiIcon.VerticalAlignment = VerticalAlignment.Center;
            elementPillGeminiIcon.SnapsToDevicePixels = false;
            elementPillGeminiIcon.Visibility = Visibility.Collapsed;

            elementPillSuffixText = new TextBlock();
            elementPillSuffixText.FontSize = 11.0;
            elementPillSuffixText.FontWeight = FontWeights.Bold;
            elementPillSuffixText.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            elementPillSuffixText.VerticalAlignment = VerticalAlignment.Center;
            elementPillSuffixText.Visibility = Visibility.Collapsed;

            elementPillPanel.Children.Add(elementPillText);
            elementPillPanel.Children.Add(elementPillGeminiIcon);
            elementPillPanel.Children.Add(elementPillSuffixText);
            elementPill.Child = elementPillPanel;
            Grid.SetColumn(elementPill, 0);
            compactRow.Children.Add(elementPill);

            orchestratorBadge = new TextBlock();
            orchestratorBadge.Text = "\u26A1";
            orchestratorBadge.FontSize = 11.0;
            orchestratorBadge.Margin = new Thickness(0, 0, 5, 0);
            orchestratorBadge.VerticalAlignment = VerticalAlignment.Center;
            orchestratorBadge.Visibility = Visibility.Collapsed;
            Grid.SetColumn(orchestratorBadge, 1);
            compactRow.Children.Add(orchestratorBadge);

            compactTotalPanel = new StackPanel();
            compactTotalPanel.Orientation = Orientation.Horizontal;
            compactTotalPanel.VerticalAlignment = VerticalAlignment.Center;

            compactTotalText = new TextBlock();
            compactTotalText.FontSize = 12.5;
            compactTotalText.FontWeight = FontWeights.SemiBold;
            compactTotalText.FontFamily = new FontFamily("Consolas, Segoe UI, Microsoft JhengHei UI");
            compactTotalText.VerticalAlignment = VerticalAlignment.Center;

            compactApiSepText = new TextBlock();
            compactApiSepText.Text = " \u00B7 ";
            compactApiSepText.FontSize = 12.5;
            compactApiSepText.FontWeight = FontWeights.Bold;
            compactApiSepText.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            compactApiSepText.VerticalAlignment = VerticalAlignment.Center;
            compactApiSepText.Visibility = Visibility.Collapsed;

            compactApiValueText = new TextBlock();
            compactApiValueText.FontSize = 12.5;
            compactApiValueText.FontWeight = FontWeights.SemiBold;
            compactApiValueText.FontFamily = new FontFamily("Consolas, Segoe UI, Microsoft JhengHei UI");
            compactApiValueText.VerticalAlignment = VerticalAlignment.Center;
            compactApiValueText.Visibility = Visibility.Collapsed;

            compactTotalPanel.Children.Add(compactTotalText);
            compactTotalPanel.Children.Add(compactApiSepText);
            compactTotalPanel.Children.Add(compactApiValueText);
            Grid.SetColumn(compactTotalPanel, 2);
            compactRow.Children.Add(compactTotalPanel);

            statusDot = new Ellipse();
            statusDot.Width = 6.5;
            statusDot.Height = 6.5;
            statusDot.Margin = new Thickness(7, 0, 1, 0);
            statusDot.VerticalAlignment = VerticalAlignment.Center;
            statusDot.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(statusDot, 3);
            compactRow.Children.Add(statusDot);

            bubbleStack.Children.Add(compactRow);

            // Detail Panel (collapsed by default, revealed on cursor hover into bubble)
            detailPanel = new StackPanel();
            detailPanel.Orientation = Orientation.Vertical;
            detailPanel.Width = 298.0;
            detailPanel.Visibility = Visibility.Collapsed;
            detailPanel.Opacity = 0.0;
            detailPanel.MaxHeight = 0.0;
            detailPanel.ClipToBounds = true;

            detailDivider1 = new Border();
            detailDivider1.Height = 1.0;
            detailDivider1.Margin = new Thickness(0, 7, 0, 7);
            detailPanel.Children.Add(detailDivider1);

            // Header: Model name + element Z + live status text
            Grid hdrGrid = new Grid();
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel hdrLeft = new StackPanel();
            detailModelTitle = new TextBlock();
            detailModelTitle.FontSize = 12.5;
            detailModelTitle.FontWeight = FontWeights.SemiBold;
            detailModelTitle.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            detailModelTitle.TextWrapping = TextWrapping.Wrap;
            hdrLeft.Children.Add(detailModelTitle);

            detailPreviewBadge = new TextBlock();
            detailPreviewBadge.FontSize = 10.6;
            detailPreviewBadge.FontWeight = FontWeights.SemiBold;
            detailPreviewBadge.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            detailPreviewBadge.TextWrapping = TextWrapping.Wrap;
            detailPreviewBadge.Margin = new Thickness(0, 2, 0, 0);
            detailPreviewBadge.Visibility = Visibility.Collapsed;
            hdrLeft.Children.Add(detailPreviewBadge);

            detailElementMeta = new TextBlock();
            detailElementMeta.FontSize = 10.8;
            detailElementMeta.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            detailElementMeta.TextWrapping = TextWrapping.Wrap;
            detailElementMeta.Margin = new Thickness(0, 2, 0, 0);
            hdrLeft.Children.Add(detailElementMeta);
            Grid.SetColumn(hdrLeft, 0);
            hdrGrid.Children.Add(hdrLeft);

            detailStatusLabel = new TextBlock();
            detailStatusLabel.FontSize = 10.5;
            detailStatusLabel.FontWeight = FontWeights.SemiBold;
            detailStatusLabel.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            detailStatusLabel.VerticalAlignment = VerticalAlignment.Top;
            detailStatusLabel.Margin = new Thickness(6, 1, 0, 0);
            Grid.SetColumn(detailStatusLabel, 1);
            hdrGrid.Children.Add(detailStatusLabel);
            detailPanel.Children.Add(hdrGrid);

            // Scope Bar: [Session] [Today] [All] + [+Sub]
            StackPanel scopeBar = new StackPanel();
            scopeBar.Orientation = Orientation.Horizontal;
            scopeBar.Margin = new Thickness(0, 8, 0, 8);

            scopeChipSession = MakeChip(out scopeChipSessionText, delegate { SetScope(TokenScope.CurrentConversation); });
            scopeChipToday = MakeChip(out scopeChipTodayText, delegate { SetScope(TokenScope.Today); });
            scopeChipAll = MakeChip(out scopeChipAllText, delegate { SetScope(TokenScope.AllTime); });
            subChip = MakeChip(out subChipText, delegate { ToggleSubagents(); });
            subChip.Margin = new Thickness(6, 0, 0, 0);

            scopeBar.Children.Add(scopeChipSession);
            scopeBar.Children.Add(scopeChipToday);
            scopeBar.Children.Add(scopeChipAll);
            scopeBar.Children.Add(subChip);
            detailPanel.Children.Add(scopeBar);

            // Formal Token Breakdown Rows
            detailPanel.Children.Add(MakeTokenRow(out lblPromptUncached, out valPromptUncached, false));
            detailPanel.Children.Add(MakeTokenRow(out lblCachedContent, out valCachedContent, false));
            detailPanel.Children.Add(MakeTokenRow(out lblCandidates, out valCandidates, false));
            detailPanel.Children.Add(MakeTokenRow(out lblThoughts, out valThoughts, false));
            rowCacheCreation = MakeTokenRow(out lblCacheCreation, out valCacheCreation, false);
            rowCacheCreation.Visibility = Visibility.Collapsed;
            detailPanel.Children.Add(rowCacheCreation);

            detailDivider2 = new Border();
            detailDivider2.Height = 1.0;
            detailDivider2.Margin = new Thickness(0, 5, 0, 5);
            detailPanel.Children.Add(detailDivider2);

            detailPanel.Children.Add(MakeTokenRow(out lblTotal, out valTotal, true));

            // Footer meta
            detailFooterText = new TextBlock();
            detailFooterText.FontSize = 9.8;
            detailFooterText.FontFamily = new FontFamily("Consolas, Segoe UI, Microsoft JhengHei UI");
            detailFooterText.Margin = new Thickness(0, 6, 0, 0);
            detailFooterText.TextWrapping = TextWrapping.Wrap;
            detailPanel.Children.Add(detailFooterText);

            resetAutoLink = new TextBlock();
            resetAutoLink.FontSize = 10.5;
            resetAutoLink.FontWeight = FontWeights.SemiBold;
            resetAutoLink.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            resetAutoLink.Margin = new Thickness(0, 5, 0, 0);
            resetAutoLink.Cursor = Cursors.Hand;
            resetAutoLink.TextDecorations = TextDecorations.Underline;
            resetAutoLink.Visibility = Visibility.Collapsed;
            resetAutoLink.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                settings.ManualElementOverride = null;
                if (SettingsChangedByUser != null)
                {
                    SettingsChangedByUser();
                }
            };
            detailPanel.Children.Add(resetAutoLink);

            bubbleStack.Children.Add(detailPanel);
            rootCanvas.Children.Add(bubbleBorder);

            hoverCollapseTimer = new DispatcherTimer();
            hoverCollapseTimer.Interval = TimeSpan.FromMilliseconds(220);
            hoverCollapseTimer.Tick += delegate
            {
                hoverCollapseTimer.Stop();
                if (!isPointerInsideBubble)
                {
                    SetHoverExpandedTarget(false);
                }
            };

            ApplyData(currentSnapshot, false, true);
            RepositionInternalLayout();
        }

        public IntPtr Handle
        {
            get { return hwnd; }
        }

        public Atom3DControl AtomControl
        {
            get { return atomControl; }
        }

        public double AtomDipSize
        {
            get { return atomDipSize; }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

            HwndSource src = HwndSource.FromHwnd(hwnd);
            if (src != null)
            {
                src.AddHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);
            }
            return IntPtr.Zero;
        }

        private static Border MakeChip(out TextBlock textOut, Action onClick)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(6);
            b.BorderThickness = new Thickness(1.0);
            b.Padding = new Thickness(7, 2, 7, 2);
            b.Margin = new Thickness(0, 0, 4, 0);
            b.Cursor = Cursors.Hand;

            TextBlock tb = new TextBlock();
            tb.FontSize = 10.6;
            tb.FontWeight = FontWeights.Medium;
            tb.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            b.Child = tb;

            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                onClick();
            };

            textOut = tb;
            return b;
        }

        private static Grid MakeTokenRow(out TextBlock labelOut, out TextBlock valueOut, bool bold)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 1.5, 0, 1.5);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock lbl = new TextBlock();
            lbl.FontSize = bold ? 12.0 : 11.0;
            lbl.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
            lbl.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
            Grid.SetColumn(lbl, 0);
            g.Children.Add(lbl);

            TextBlock val = new TextBlock();
            val.FontSize = bold ? 12.5 : 11.2;
            val.FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold;
            val.FontFamily = new FontFamily("Consolas, Segoe UI, Microsoft JhengHei UI");
            val.HorizontalAlignment = HorizontalAlignment.Right;
            val.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(val, 1);
            g.Children.Add(val);

            labelOut = lbl;
            valueOut = val;
            return g;
        }

        public void SetTargetGeometry(double leftDip, double topDip, double widthDip, double heightDip, double atomSizeDip, double dpiScale)
        {
            double w = Math.Max(200.0, widthDip);
            double h = Math.Max(160.0, heightDip);
            Rect bounds = new Rect(leftDip, topDip, w, h);
            SetTargetGeometry(bounds, bounds, atomSizeDip, dpiScale, false);
        }

        public void SetTargetGeometry(Rect appBoundsDip, Rect workAreaDip, double atomSizeDip, double dpiScale, bool isAppFullscreenOrMaximized)
        {
            this.rawAppBoundsDip = appBoundsDip;
            this.workAreaBoundsDip = workAreaDip;
            this.isAppFullscreenOrMax = isAppFullscreenOrMaximized;

            Rect visible = Rect.Intersect(appBoundsDip, workAreaDip);
            if (visible.IsEmpty || visible.Width < 200.0 || visible.Height < 160.0)
            {
                visible = (workAreaDip.Width >= 200.0 && workAreaDip.Height >= 160.0) ? workAreaDip : appBoundsDip;
            }

            this.targetWinLeftDip = visible.Left;
            this.targetWinTopDip = visible.Top;
            this.targetWinWidthDip = Math.Max(200.0, visible.Width);
            this.targetWinHeightDip = Math.Max(160.0, visible.Height);
            this.atomDipSize = atomSizeDip;
            this.currentDpiScale = dpiScale > 0.2 ? dpiScale : 1.0;
            RepositionInternalLayout();
        }

        public void RepositionNow()
        {
            RepositionInternalLayout();
        }

        public Rect GetVisibleWidgetScreenRectDip()
        {
            double cx = GetAtomCenterScreenDipX();
            double aTop = GetAtomTopScreenDipY();
            Rect rect = new Rect(cx - atomDipSize * 0.5, aTop, atomDipSize, atomDipSize);

            double winLeftDip = cx - atomCenterXInCanvas;
            double winTopDip = aTop - atomTopInCanvas;
            if (bubbleBorder != null && bubbleBorder.Visibility == Visibility.Visible)
            {
                double bLeft = Canvas.GetLeft(bubbleBorder);
                double bTop = Canvas.GetTop(bubbleBorder);
                double bw = bubbleBorder.ActualWidth > 1.0 ? bubbleBorder.ActualWidth : bubbleBorder.DesiredSize.Width;
                double bh = bubbleBorder.ActualHeight > 1.0 ? bubbleBorder.ActualHeight : bubbleBorder.DesiredSize.Height;
                if (!double.IsNaN(bLeft) && !double.IsNaN(bTop) && bw > 1.0 && bh > 1.0)
                {
                    rect.Union(new Rect(winLeftDip + bLeft, winTopDip + bTop, bw, bh));
                }
            }

            rect.Inflate(4.0, 4.0);
            return rect;
        }

        public void GetDesiredScreenWindowOriginPx(out int xPx, out int yPx, out int wPx, out int hPx)
        {
            double atomCenterScreenDipX = GetAtomCenterScreenDipX();
            double atomTopScreenDipY = GetAtomTopScreenDipY();

            double winLeftDip = atomCenterScreenDipX - atomCenterXInCanvas;
            double winTopDip = atomTopScreenDipY - atomTopInCanvas;

            xPx = (int)Math.Round(winLeftDip * currentDpiScale);
            yPx = (int)Math.Round(winTopDip * currentDpiScale);
            wPx = (int)Math.Round(CanvasWidth * currentDpiScale);
            hPx = (int)Math.Round(CanvasHeight * currentDpiScale);
        }

        private double GetAtomCenterScreenDipX()
        {
            double minCx = targetWinLeftDip + atomDipSize * 0.5 + 8.0;
            double maxCx = Math.Max(minCx, targetWinLeftDip + targetWinWidthDip - atomDipSize * 0.5 - 8.0);
            double cx = targetWinLeftDip + settings.AnchorNormX * targetWinWidthDip;
            return Math.Max(minCx, Math.Min(maxCx, cx));
        }

        private double GetAtomTopScreenDipY()
        {
            double minTop = targetWinTopDip + 8.0;
            double maxTop = Math.Max(minTop, targetWinTopDip + targetWinHeightDip - atomDipSize - 8.0);
            double bottomY = targetWinTopDip + settings.AnchorNormY * targetWinHeightDip;
            double topY = bottomY - atomDipSize;
            return Math.Max(minTop, Math.Min(maxTop, topY));
        }

        private static void InvalidateSubtreeMeasure(DependencyObject root)
        {
            if (root == null)
            {
                return;
            }
            UIElement ui = root as UIElement;
            if (ui != null)
            {
                ui.InvalidateMeasure();
            }
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                InvalidateSubtreeMeasure(VisualTreeHelper.GetChild(root, i));
            }
        }

        private double MeasureDetailFullHeight()
        {
            if (detailPanel == null)
            {
                return 210.0;
            }
            double prevMax = detailPanel.MaxHeight;
            Visibility prevVis = detailPanel.Visibility;
            detailPanel.MaxHeight = double.PositiveInfinity;
            if (prevVis == Visibility.Collapsed)
            {
                detailPanel.Visibility = Visibility.Visible;
            }
            InvalidateSubtreeMeasure(detailPanel);
            detailPanel.Measure(new Size(detailPanel.Width, double.PositiveInfinity));
            double h = detailPanel.DesiredSize.Height;
            detailPanel.Visibility = prevVis;
            detailPanel.MaxHeight = prevMax;
            return Math.Max(210.0, h + 10.0);
        }

        private void RepositionInternalLayout()
        {
            atomHost.Width = atomDipSize;
            atomHost.Height = atomDipSize;

            double atomTopInTarget = GetAtomTopScreenDipY() - targetWinTopDip;
            double flipThreshold = Math.Max(265.0, MeasureDetailFullHeight() + 62.0);
            flipBubbleBelow = (atomTopInTarget < flipThreshold);

            atomCenterXInCanvas = CanvasWidth * 0.5;
            if (flipBubbleBelow)
            {
                atomTopInCanvas = 8.0;
            }
            else
            {
                atomTopInCanvas = CanvasHeight - atomDipSize - 8.0;
            }

            Canvas.SetLeft(atomHost, atomCenterXInCanvas - atomDipSize * 0.5);
            Canvas.SetTop(atomHost, atomTopInCanvas);

            AlignBubbleAroundAtom();
        }

        private bool isAligningBubble = false;

        private void AlignBubbleAroundAtom()
        {
            if (isAligningBubble)
            {
                return;
            }
            isAligningBubble = true;
            try
            {
                InvalidateSubtreeMeasure(bubbleBorder);
                bubbleBorder.Measure(new Size(CanvasWidth - 8.0, CanvasHeight - 16.0));
                double bw = Math.Max(84.0, bubbleBorder.DesiredSize.Width);
                double bh = Math.Max(26.0, bubbleBorder.DesiredSize.Height);

                double atomCenterScreenX = GetAtomCenterScreenDipX();
                double canvasLeftScreen = atomCenterScreenX - atomCenterXInCanvas;

                // Default center over atom, clamped inside Antigravity horizontal bounds
                double desiredLeftInCanvas = atomCenterXInCanvas - bw * 0.5;
                double minLeftInCanvas = Math.Max(4.0, (targetWinLeftDip + 8.0) - canvasLeftScreen);
                double maxLeftInCanvas = Math.Min(CanvasWidth - bw - 4.0, (targetWinLeftDip + targetWinWidthDip - 8.0) - canvasLeftScreen - bw);
                if (maxLeftInCanvas < minLeftInCanvas)
                {
                    maxLeftInCanvas = minLeftInCanvas;
                }
                double bLeft = Math.Max(minLeftInCanvas, Math.Min(maxLeftInCanvas, desiredLeftInCanvas));

                double bTop;
                if (flipBubbleBelow)
                {
                    bTop = Clamp(atomTopInCanvas + atomDipSize + 9.0, 4.0, Math.Max(4.0, CanvasHeight - bh - 4.0));
                    pointerDown.Visibility = Visibility.Collapsed;
                    pointerUp.Visibility = Visibility.Visible;
                    double tipX = Clamp(atomCenterXInCanvas - 6.0, bLeft + 12.0, bLeft + bw - 24.0);
                    Canvas.SetLeft(pointerUp, tipX);
                    Canvas.SetTop(pointerUp, bTop - 6.5);
                }
                else
                {
                    bTop = Clamp(atomTopInCanvas - bh - 9.0, 4.0, Math.Max(4.0, CanvasHeight - bh - 4.0));
                    pointerUp.Visibility = Visibility.Collapsed;
                    pointerDown.Visibility = Visibility.Visible;
                    double tipX = Clamp(atomCenterXInCanvas - 6.0, bLeft + 12.0, bLeft + bw - 24.0);
                    Canvas.SetLeft(pointerDown, tipX);
                    Canvas.SetTop(pointerDown, bTop + bh - 0.8);
                }

                Canvas.SetLeft(bubbleBorder, bLeft);
                Canvas.SetTop(bubbleBorder, bTop);
            }
            finally
            {
                isAligningBubble = false;
            }
        }

        public void ApplyData(DataSnapshot snap, bool lightTheme, bool immediateNumbers)
        {
            this.currentSnapshot = snap ?? new DataSnapshot();
            this.isLightTheme = lightTheme;

            ActiveModelState m = currentSnapshot.ActiveModel ?? new ActiveModelState();
            TokenBreakdown tb = currentSnapshot.Breakdown ?? new TokenBreakdown();
            ElementVisualInfo ev = ElementVisualCatalog.GetInfo(m.Element);
            bool unsupported = (m.Element == AtomElement.Unsupported);
            bool zh = (settings.Language == UiLanguage.ZhTw);

            atomControl.SetState(m.Element, m.IsOrchestrator, lightTheme, m.IsRunning, settings.EnableAnimation);
            atomHost.Background = new SolidColorBrush(lightTheme ? Color.FromArgb(1, 255, 255, 255) : Color.FromArgb(1, 0, 0, 0));

            // Colors aligned to Google AI Studio Periodic Table & Antigravity Light/Dark theme
            Color accent = lightTheme ? ev.LightAccent : ev.DarkAccent;
            Color bgCol = lightTheme ? Color.FromArgb(245, 255, 248, 250) : Color.FromArgb(242, 28, 22, 24);
            Color borderCol = unsupported
                ? (lightTheme ? Color.FromArgb(180, 148, 163, 184) : Color.FromArgb(165, 110, 120, 136))
                : Color.FromArgb(215, accent.R, accent.G, accent.B);
            Color fgPrimary = lightTheme ? Color.FromRgb(42, 24, 30) : Color.FromRgb(240, 232, 228);
            Color fgMuted = lightTheme ? Color.FromRgb(118, 92, 102) : Color.FromRgb(168, 154, 148);
            Color divCol = lightTheme ? Color.FromArgb(48, 140, 80, 100) : Color.FromArgb(48, 255, 220, 200);

            SolidColorBrush bgBrush = new SolidColorBrush(bgCol);
            SolidColorBrush borderBrush = new SolidColorBrush(borderCol);
            SolidColorBrush fgBrush = new SolidColorBrush(fgPrimary);
            SolidColorBrush mutedBrush = new SolidColorBrush(fgMuted);
            SolidColorBrush accentBrush = new SolidColorBrush(accent);

            bubbleBorder.Background = bgBrush;
            bubbleBorder.BorderBrush = borderBrush;
            pointerDown.Fill = bgBrush;
            pointerDown.Stroke = borderBrush;
            pointerUp.Fill = bgBrush;
            pointerUp.Stroke = borderBrush;

            // Compact Pill styled after Google AI Studio Periodic Table element tile
            elementPill.Background = new SolidColorBrush(lightTheme ? ev.LightTileBg : ev.DarkTileBg);
            elementPill.BorderThickness = new Thickness(0, 0, 0, 2);
            elementPill.BorderBrush = new SolidColorBrush(lightTheme ? ev.LightEdgeLip : ev.DarkEdgeLip);
            SolidColorBrush symbolInkBrush = new SolidColorBrush(lightTheme ? ev.LightSymbolInk : ev.DarkSymbolInk);
            elementPillText.Foreground = symbolInkBrush;
            elementPillSuffixText.Foreground = symbolInkBrush;
            if (unsupported)
            {
                if (zh)
                {
                    elementPillText.Text = "動畫僅支援";
                    elementPillText.Visibility = Visibility.Visible;
                    elementPillGeminiIcon.Visibility = Visibility.Visible;
                    elementPillGeminiIcon.Fill = symbolInkBrush;
                    elementPillGeminiIcon.Margin = new Thickness(1.5, 0.4, 0.5, 0);
                    elementPillSuffixText.Text = "4";
                    elementPillSuffixText.Visibility = Visibility.Visible;
                }
                else
                {
                    elementPillText.Text = "";
                    elementPillText.Visibility = Visibility.Collapsed;
                    elementPillGeminiIcon.Visibility = Visibility.Visible;
                    elementPillGeminiIcon.Fill = symbolInkBrush;
                    elementPillGeminiIcon.Margin = new Thickness(0, 0.4, 0.8, 0);
                    elementPillSuffixText.Text = "4 Animation Only";
                    elementPillSuffixText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                elementPillText.Text = ev.Symbol;
                elementPillText.Visibility = Visibility.Visible;
                elementPillGeminiIcon.Visibility = Visibility.Collapsed;
                elementPillSuffixText.Text = "";
                elementPillSuffixText.Visibility = Visibility.Collapsed;
            }

            orchestratorBadge.Visibility = m.IsOrchestrator ? Visibility.Visible : Visibility.Collapsed;
            orchestratorBadge.Foreground = new SolidColorBrush(Color.FromRgb(245, 182, 66));

            // Always show Total Tokens regardless of active model!
            compactTotalText.Foreground = fgBrush;
            targetTotalAnimated = tb.TotalTokenCount;
            bool firstRealSnap = !hasAppliedRealData && (currentSnapshot.HasTargetData || tb.TotalTokenCount > 0);
            if (firstRealSnap)
            {
                hasAppliedRealData = true;
            }
            if (immediateNumbers || firstRealSnap || !settings.EnableAnimation || lastRenderedCompactVal < 0 || Math.Abs(targetTotalAnimated - displayedTotalAnimated) < 0.8)
            {
                displayedTotalAnimated = targetTotalAnimated;
                lastRenderedCompactVal = tb.TotalTokenCount;
                compactTotalText.Text = FormatCompactTokens(tb.TotalTokenCount);
            }

            bool showApi = settings.ShowEquivalentApiValue;
            Visibility apiVis = showApi ? Visibility.Visible : Visibility.Collapsed;
            compactApiSepText.Visibility = apiVis;
            compactApiValueText.Visibility = apiVis;
            compactApiSepText.Foreground = accentBrush;
            compactApiValueText.Foreground = fgBrush;
            targetApiValueUsd = Math.Max(0.0, tb.EquivalentApiValueUsd);
            if (!showApi)
            {
                displayedApiValueUsd = targetApiValueUsd;
                lastRenderedCompactApiText = "";
                compactApiValueText.Text = "";
            }
            else if (immediateNumbers || firstRealSnap || !settings.EnableAnimation || string.IsNullOrEmpty(lastRenderedCompactApiText) || Math.Abs(targetApiValueUsd - displayedApiValueUsd) < 0.005)
            {
                displayedApiValueUsd = targetApiValueUsd;
                lastRenderedCompactApiText = FormatUsdValue(targetApiValueUsd);
                compactApiValueText.Text = lastRenderedCompactApiText;
            }

            if (m.IsRunning)
            {
                statusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94));
            }
            else if (unsupported)
            {
                statusDot.Fill = mutedBrush;
            }
            else
            {
                statusDot.Fill = accentBrush;
            }

            // Detail Card Content
            detailDivider1.Background = new SolidColorBrush(divCol);
            detailDivider2.Background = new SolidColorBrush(divCol);

            string modelLabel = string.IsNullOrEmpty(m.DisplayName)
                ? (unsupported ? (zh ? "非 Gemini 4 模型 (待機)" : "Non-Gemini 4 (Standby)") : ("Gemini 4 " + ev.EnglishName))
                : m.DisplayName;
            detailModelTitle.Text = modelLabel;
            detailModelTitle.Foreground = fgBrush;

            if (m.PreviewOverrideElement.HasValue)
            {
                ElementVisualInfo ovInfo = ElementVisualCatalog.GetInfo(m.PreviewOverrideElement.Value);
                detailPreviewBadge.Text = "[" + ovInfo.EnglishName + " Preview]";
                detailPreviewBadge.Foreground = accentBrush;
                detailPreviewBadge.Visibility = Visibility.Visible;
            }
            else
            {
                detailPreviewBadge.Text = "";
                detailPreviewBadge.Visibility = Visibility.Collapsed;
            }

            bool alreadyHasOrch = ModelNameNormalizer.ContainsOrchestratorWord(modelLabel);
            string orchNote = (m.IsOrchestrator && !alreadyHasOrch) ? " · Orchestrator" : "";
            if (unsupported)
            {
                detailElementMeta.Text = (zh
                    ? "非 Gemini 4 模型或尚未收錄至 3D 模型庫"
                    : "Non-Gemini 4 model or not yet in 3D library") + orchNote;
            }
            else
            {
                detailElementMeta.Text = string.Format("{0} ({1}) · Z={2} · {3}{4}",
                    ev.Symbol,
                    zh ? ev.ChineseName : ev.EnglishName,
                    ev.AtomicNumber,
                    ev.ShellConfig,
                    orchNote);
            }
            detailElementMeta.Foreground = mutedBrush;

            if (m.IsRunning)
            {
                detailStatusLabel.Text = zh ? "● 運行中" : "● Running";
                detailStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94));
            }
            else if (unsupported)
            {
                detailStatusLabel.Text = zh ? "○ 待機" : "○ Standby";
                detailStatusLabel.Foreground = mutedBrush;
            }
            else
            {
                detailStatusLabel.Text = zh ? "● 就緒" : "● Ready";
                detailStatusLabel.Foreground = accentBrush;
            }

            // Scope Chips
            scopeChipSessionText.Text = zh ? "當前對話" : "Session";
            scopeChipTodayText.Text = zh ? "今日累計" : "Today";
            scopeChipAllText.Text = zh ? "全部紀錄" : "All-Time";
            subChipText.Text = zh ? "+子代理" : "+Sub";

            StyleChip(scopeChipSession, scopeChipSessionText, settings.Scope == TokenScope.CurrentConversation, accent, lightTheme, fgMuted);
            StyleChip(scopeChipToday, scopeChipTodayText, settings.Scope == TokenScope.Today, accent, lightTheme, fgMuted);
            StyleChip(scopeChipAll, scopeChipAllText, settings.Scope == TokenScope.AllTime, accent, lightTheme, fgMuted);
            StyleChip(subChip, subChipText, settings.IncludeSubagents, Color.FromRgb(245, 158, 11), lightTheme, fgMuted);

            // Formal Token Breakdown Names (official Gemini UsageMetadata terminology)
            lblPromptUncached.Text = "Prompt Tokens (Uncached)";
            lblCachedContent.Text = "Cached Content Tokens";
            lblCandidates.Text = "Candidates Tokens";
            lblThoughts.Text = "Thoughts Tokens";
            lblCacheCreation.Text = "Cache Creation Tokens";
            lblTotal.Text = "Total Tokens";

            lblPromptUncached.Foreground = mutedBrush;
            lblCachedContent.Foreground = mutedBrush;
            lblCandidates.Foreground = mutedBrush;
            lblThoughts.Foreground = mutedBrush;
            lblCacheCreation.Foreground = mutedBrush;
            lblTotal.Foreground = fgBrush;

            valPromptUncached.Text = tb.PromptTokensUncached.ToString("N0", CultureInfo.InvariantCulture);
            valCachedContent.Text = tb.CachedContentTokens.ToString("N0", CultureInfo.InvariantCulture);
            valCandidates.Text = tb.CandidatesTokens.ToString("N0", CultureInfo.InvariantCulture);
            valThoughts.Text = tb.ThoughtsTokens.ToString("N0", CultureInfo.InvariantCulture);
            valCacheCreation.Text = tb.CacheCreationTokens.ToString("N0", CultureInfo.InvariantCulture);
            valTotal.Text = tb.TotalTokenCount.ToString("N0", CultureInfo.InvariantCulture);

            valPromptUncached.Foreground = fgBrush;
            valCachedContent.Foreground = fgBrush;
            valCandidates.Foreground = fgBrush;
            valThoughts.Foreground = fgBrush;
            valCacheCreation.Foreground = fgBrush;
            valTotal.Foreground = accentBrush;

            rowCacheCreation.Visibility = (tb.CacheCreationTokens > 0) ? Visibility.Visible : Visibility.Collapsed;

            string subNote = (tb.SubagentCount > 0) ? string.Format(" · +{0} Sub", tb.SubagentCount) : "";
            detailFooterText.Text = string.Format(
                "Prompt Total: {0} · Context: {1} · Calls: {2}{3}",
                tb.PromptTokenCount.ToString("N0", CultureInfo.InvariantCulture),
                tb.LatestContextTokens.ToString("N0", CultureInfo.InvariantCulture),
                tb.CallCount,
                subNote);
            detailFooterText.Foreground = mutedBrush;

            if (settings.ManualElementOverride.HasValue)
            {
                resetAutoLink.Text = zh ? "目前為手動預覽模式 — 點此恢復自動偵測" : "Manual preview active — Click to restore Auto";
                resetAutoLink.Foreground = accentBrush;
                resetAutoLink.Visibility = Visibility.Visible;
            }
            else
            {
                resetAutoLink.Visibility = Visibility.Collapsed;
            }

            if (isDragging)
            {
                AlignBubbleAroundAtom();
            }
            else
            {
                RepositionInternalLayout();
            }
        }

        private static void StyleChip(Border chip, TextBlock txt, bool active, Color accent, bool lightTheme, Color mutedColor)
        {
            if (active)
            {
                chip.Background = new SolidColorBrush(Color.FromArgb(lightTheme ? (byte)36 : (byte)55, accent.R, accent.G, accent.B));
                chip.BorderBrush = new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B));
                txt.Foreground = new SolidColorBrush(accent);
            }
            else
            {
                chip.Background = new SolidColorBrush(lightTheme ? Color.FromArgb(14, 0, 0, 0) : Color.FromArgb(18, 255, 255, 255));
                chip.BorderBrush = new SolidColorBrush(lightTheme ? Color.FromArgb(36, 0, 0, 0) : Color.FromArgb(36, 255, 255, 255));
                txt.Foreground = new SolidColorBrush(mutedColor);
            }
        }

        public void SetHoverExpandedImmediate(bool expanded)
        {
            isPointerInsideBubble = expanded;
            expandProgress = expanded ? 1.0 : 0.0;
            detailPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            detailPanel.Opacity = expanded ? 1.0 : 0.0;
            detailPanel.MaxHeight = expanded ? double.PositiveInfinity : 0.0;
            RepositionInternalLayout();
        }

        private void SetHoverExpandedTarget(bool expanded)
        {
            if (expanded)
            {
                if (detailPanel.Visibility != Visibility.Visible)
                {
                    detailPanel.Visibility = Visibility.Visible;
                }
            }
        }

        public void StepFrame(double dt)
        {
            if (settings.EnableAnimation)
            {
                atomControl.StepAnimation(dt);
            }

            // Smooth hover expand/collapse transition
            double targetExp = isPointerInsideBubble ? 1.0 : 0.0;
            if (Math.Abs(expandProgress - targetExp) > 0.005)
            {
                double speed = isPointerInsideBubble ? 10.5 : 8.5;
                expandProgress += (targetExp - expandProgress) * Math.Min(1.0, dt * speed);
                if (Math.Abs(expandProgress - targetExp) <= 0.01)
                {
                    expandProgress = targetExp;
                }
                double ease = 1.0 - Math.Pow(1.0 - expandProgress, 2.5);
                detailPanel.Opacity = ease;
                if (expandProgress <= 0.01 && !isPointerInsideBubble)
                {
                    detailPanel.MaxHeight = 0.0;
                    detailPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (detailPanel.Visibility != Visibility.Visible)
                    {
                        detailPanel.Visibility = Visibility.Visible;
                    }
                    if (expandProgress >= 0.995 && isPointerInsideBubble)
                    {
                        detailPanel.MaxHeight = double.PositiveInfinity;
                    }
                    else
                    {
                        detailPanel.MaxHeight = MeasureDetailFullHeight() * ease;
                    }
                }
                AlignBubbleAroundAtom();
            }

            // Smooth rolling total tokens counter in compact bubble (across all models)
            if (settings.EnableAnimation)
            {
                double diff = targetTotalAnimated - displayedTotalAnimated;
                if (Math.Abs(diff) >= 0.8)
                {
                    displayedTotalAnimated += diff * Math.Min(1.0, dt * 8.0);
                    if (Math.Abs(targetTotalAnimated - displayedTotalAnimated) < 1.0)
                    {
                        displayedTotalAnimated = targetTotalAnimated;
                    }
                    long rounded = (long)Math.Round(displayedTotalAnimated);
                    if (rounded != lastRenderedCompactVal)
                    {
                        lastRenderedCompactVal = rounded;
                        compactTotalText.Text = FormatCompactTokens(rounded);
                        AlignBubbleAroundAtom();
                    }
                }
                if (settings.ShowEquivalentApiValue)
                {
                    double apiDiff = targetApiValueUsd - displayedApiValueUsd;
                    if (Math.Abs(apiDiff) >= 0.004)
                    {
                        displayedApiValueUsd += apiDiff * Math.Min(1.0, dt * 8.0);
                        if (Math.Abs(targetApiValueUsd - displayedApiValueUsd) < 0.004)
                        {
                            displayedApiValueUsd = targetApiValueUsd;
                        }
                    }
                    else
                    {
                        displayedApiValueUsd = targetApiValueUsd;
                    }
                    string renderedApi = FormatUsdValue(displayedApiValueUsd);
                    if (!string.Equals(renderedApi, lastRenderedCompactApiText, StringComparison.Ordinal))
                    {
                        lastRenderedCompactApiText = renderedApi;
                        compactApiValueText.Text = renderedApi;
                        AlignBubbleAroundAtom();
                    }
                }
            }
        }

        public static string FormatUsdValue(double usd)
        {
            if (double.IsNaN(usd) || double.IsInfinity(usd) || usd < 0.0)
            {
                usd = 0.0;
            }
            return "$" + usd.ToString("N2", CultureInfo.InvariantCulture);
        }

        public static string FormatCompactTokens(long tokens)
        {
            if (tokens < 100000L)
            {
                return tokens.ToString("N0", CultureInfo.InvariantCulture);
            }
            if (tokens < 1000000L)
            {
                return (tokens / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "K";
            }
            if (tokens < 1000000000L)
            {
                return (tokens / 1000000.0).ToString("0.00", CultureInfo.InvariantCulture) + "M";
            }
            return (tokens / 1000000000.0).ToString("0.00", CultureInfo.InvariantCulture) + "B";
        }

        private void SetScope(TokenScope scope)
        {
            if (settings.Scope != scope)
            {
                settings.Scope = scope;
                if (SettingsChangedByUser != null)
                {
                    SettingsChangedByUser();
                }
            }
        }

        private void ToggleSubagents()
        {
            settings.IncludeSubagents = !settings.IncludeSubagents;
            if (SettingsChangedByUser != null)
            {
                SettingsChangedByUser();
            }
        }

        private void OnBubbleMouseEnter(object sender, MouseEventArgs e)
        {
            hoverCollapseTimer.Stop();
            isPointerInsideBubble = true;
            SetHoverExpandedTarget(true);
        }

        private void OnBubbleMouseLeave(object sender, MouseEventArgs e)
        {
            isPointerInsideBubble = false;
            hoverCollapseTimer.Stop();
            hoverCollapseTimer.Start();
        }

        private void OnAtomMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            isDragging = true;
            System.Drawing.Point cp = WinForms.Control.MousePosition;
            dragStartScreenPx = new Point(cp.X, cp.Y);
            dragStartAnchorX = settings.AnchorNormX;
            dragStartAnchorY = settings.AnchorNormY;
            atomHost.CaptureMouse();
        }

        private void OnAtomMouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging)
            {
                return;
            }
            System.Drawing.Point cp = WinForms.Control.MousePosition;
            double dxDip = (cp.X - dragStartScreenPx.X) / currentDpiScale;
            double dyDip = (cp.Y - dragStartScreenPx.Y) / currentDpiScale;

            double newNormX = dragStartAnchorX + (dxDip / Math.Max(300.0, targetWinWidthDip));
            double newNormY = dragStartAnchorY + (dyDip / Math.Max(200.0, targetWinHeightDip));

            settings.AnchorNormX = Clamp(newNormX, 0.03, 0.985);
            settings.AnchorNormY = Clamp(newNormY, 0.06, 0.99);
            RepositionInternalLayout();
            if (PositionNeedsSync != null)
            {
                PositionNeedsSync();
            }
        }

        private void OnAtomMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDragging)
            {
                return;
            }
            e.Handled = true;
            isDragging = false;
            atomHost.ReleaseMouseCapture();

            // Magnetic edge/corner snap within 26 DIPs
            double cxInTarget = settings.AnchorNormX * targetWinWidthDip;
            double bottomInTarget = settings.AnchorNormY * targetWinHeightDip;
            double snapDist = 26.0;

            double rightTargetCx = targetWinWidthDip - (atomDipSize * 0.5 + 12.0);
            double leftTargetCx = atomDipSize * 0.5 + 12.0;
            double bottomTargetY = targetWinHeightDip - 10.0;
            double topTargetY = atomDipSize + 10.0;

            if (Math.Abs(cxInTarget - rightTargetCx) <= snapDist)
            {
                cxInTarget = rightTargetCx;
            }
            else if (Math.Abs(cxInTarget - leftTargetCx) <= snapDist)
            {
                cxInTarget = leftTargetCx;
            }

            if (Math.Abs(bottomInTarget - bottomTargetY) <= snapDist)
            {
                bottomInTarget = bottomTargetY;
            }
            else if (Math.Abs(bottomInTarget - topTargetY) <= snapDist)
            {
                bottomInTarget = topTargetY;
            }

            settings.AnchorNormX = Clamp(cxInTarget / targetWinWidthDip, 0.03, 0.985);
            settings.AnchorNormY = Clamp(bottomInTarget / targetWinHeightDip, 0.06, 0.99);
            RepositionInternalLayout();

            if (PositionNeedsSync != null)
            {
                PositionNeedsSync();
            }
            if (SettingsChangedByUser != null)
            {
                SettingsChangedByUser();
            }
        }

        private void OnWidgetMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (SettingsWindowRequested != null)
            {
                System.Drawing.Point cp = WinForms.Control.MousePosition;
                SettingsWindowRequested(new Point(cp.X, cp.Y));
            }
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) { return min; }
            if (v > max) { return max; }
            return v;
        }
    }
}
