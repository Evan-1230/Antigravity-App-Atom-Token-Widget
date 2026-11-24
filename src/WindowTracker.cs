using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace AntigravityAtomTokenWidget
{
    internal sealed class WindowTracker
    {
        private readonly WidgetWindow widget;
        private readonly WidgetSettings settings;
        private readonly UiaModelProbe uiaProbe;
        private readonly int selfPid;

        public event Action TargetBoundsUpdated;

        private double lastWorkLeftDip = double.NaN;
        private double lastWorkTopDip = double.NaN;
        private double lastWorkWidthDip = double.NaN;
        private double lastWorkHeightDip = double.NaN;
        private double lastAnchorX = double.NaN;
        private double lastAnchorY = double.NaN;
        private bool lastFullscreenOrMax = false;
        private Rect currentAppBoundsDip = new Rect(0, 0, 1280, 800);
        private Rect currentWorkAreaDip = new Rect(0, 0, 1920, 1040);
        private bool isAppFullscreenOrMaximized = false;

        public Rect CurrentAppBoundsDip { get { return currentAppBoundsDip; } }
        public Rect CurrentWorkAreaDip { get { return currentWorkAreaDip; } }
        public bool IsAppFullscreenOrMaximized { get { return isAppFullscreenOrMaximized; } }
        private readonly Dictionary<int, int> pidKindCache = new Dictionary<int, int>(); // 0=other, 1=AntigravityDesktop, 2=SakuraMapleOverlay
        private DateTime lastCacheFlush = DateTime.UtcNow;

        private bool isVisible = false;
        private int lastWinX = int.MinValue;
        private int lastWinY = int.MinValue;
        private int lastWinW = int.MinValue;
        private int lastWinH = int.MinValue;
        private double lastTargetLeftDip = double.NaN;
        private double lastTargetTopDip = double.NaN;
        private double lastTargetWidthDip = double.NaN;
        private double lastTargetHeightDip = double.NaN;
        private double lastAtomSizeDip = double.NaN;
        private double lastDpiScale = 1.0;

        public WindowTracker(WidgetWindow widgetWindow, WidgetSettings widgetSettings, UiaModelProbe probe)
        {
            this.widget = widgetWindow;
            this.settings = widgetSettings;
            this.uiaProbe = probe;
            this.selfPid = Process.GetCurrentProcess().Id;
        }

        public bool IsWidgetVisible
        {
            get { return isVisible; }
        }

        public double CurrentDpiScale
        {
            get { return lastDpiScale; }
        }

        public void SetUserHidden(bool hidden)
        {
            settings.IsWidgetHidden = hidden;
            IntPtr widgetHwnd = widget.Handle;
            if (hidden && widgetHwnd != IntPtr.Zero && isVisible)
            {
                NativeMethods.ShowWindow(widgetHwnd, NativeMethods.SW_HIDE);
                isVisible = false;
            }
        }

        public static double ComputeResponsiveAtomDipSize(double targetDipHeight, double? detectedEmptyComposerDip, double scaleMultiplier)
        {
            double baseDip = Clamp(targetDipHeight * 0.076, 48.0, 84.0);
            if (detectedEmptyComposerDip.HasValue && detectedEmptyComposerDip.Value >= 65.0 && detectedEmptyComposerDip.Value <= 135.0)
            {
                baseDip = Math.Min(baseDip, detectedEmptyComposerDip.Value - 12.0);
            }
            return Clamp(baseDip * scaleMultiplier, 42.0, 148.0);
        }

        public void ForceRepositionFromWidget()
        {
            if (!isVisible)
            {
                return;
            }
            IntPtr widgetHwnd = widget.Handle;
            if (widgetHwnd == IntPtr.Zero)
            {
                return;
            }
            int xPx, yPx, wPx, hPx;
            widget.GetDesiredScreenWindowOriginPx(out xPx, out yPx, out wPx, out hPx);
            if (xPx != lastWinX || yPx != lastWinY || wPx != lastWinW || hPx != lastWinH)
            {
                uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW;
                NativeMethods.SetWindowPos(widgetHwnd, IntPtr.Zero, xPx, yPx, wPx, hPx, flags);
                lastWinX = xPx;
                lastWinY = yPx;
                lastWinW = wPx;
                lastWinH = hPx;
            }
        }

        public void SyncToTarget(double? detectedEmptyComposerDip)
        {
            IntPtr widgetHwnd = widget.Handle;
            if (widgetHwnd == IntPtr.Zero)
            {
                return;
            }
            if (settings.IsWidgetHidden)
            {
                if (isVisible)
                {
                    NativeMethods.ShowWindow(widgetHwnd, NativeMethods.SW_HIDE);
                    isVisible = false;
                }
                return;
            }

            if ((DateTime.UtcNow - lastCacheFlush).TotalSeconds >= 3.0)
            {
                pidKindCache.Clear();
                lastCacheFlush = DateTime.UtcNow;
            }

            IntPtr targetHwnd = IntPtr.Zero;
            NativeMethods.RECT targetRect = new NativeMethods.RECT();

            NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (hWnd == widgetHwnd)
                {
                    return true;
                }
                if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
                {
                    return true;
                }
                if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
                {
                    return true;
                }

                StringBuilder sb = new StringBuilder(64);
                NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
                string cls = sb.ToString();
                if (!cls.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                uint pidVal;
                NativeMethods.GetWindowThreadProcessId(hWnd, out pidVal);
                int pid = (int)pidVal;
                if (pid == selfPid || pid == 0)
                {
                    return true;
                }

                if (GetProcessKind(pid) != 1)
                {
                    return true;
                }

                NativeMethods.RECT r;
                int hr = NativeMethods.DwmGetWindowAttribute(
                    hWnd,
                    NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                    out r,
                    Marshal.SizeOf(typeof(NativeMethods.RECT)));
                if (hr != 0)
                {
                    if (!NativeMethods.GetWindowRect(hWnd, out r))
                    {
                        return true;
                    }
                }

                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w < 300 || h < 200)
                {
                    return true;
                }

                targetHwnd = hWnd;
                targetRect = r;
                return false;
            }, IntPtr.Zero);

            if (targetHwnd == IntPtr.Zero)
            {
                if (uiaProbe != null)
                {
                    uiaProbe.SetTargetWindow(IntPtr.Zero, 0, 0, 1.0);
                }
                if (isVisible)
                {
                    NativeMethods.ShowWindow(widgetHwnd, NativeMethods.SW_HIDE);
                    isVisible = false;
                }
                return;
            }

            double dpiScale = ReadWindowDpiScale(targetHwnd);
            double leftDip = targetRect.Left / dpiScale;
            double topDip = targetRect.Top / dpiScale;
            double widthDip = (targetRect.Right - targetRect.Left) / dpiScale;
            double heightDip = (targetRect.Bottom - targetRect.Top) / dpiScale;

            System.Drawing.Rectangle targetPxRect = new System.Drawing.Rectangle(
                targetRect.Left,
                targetRect.Top,
                Math.Max(1, targetRect.Right - targetRect.Left),
                Math.Max(1, targetRect.Bottom - targetRect.Top));
            WinForms.Screen scr = WinForms.Screen.FromRectangle(targetPxRect);
            System.Drawing.Rectangle waPx = scr.WorkingArea;
            System.Drawing.Rectangle monPx = scr.Bounds;

            double waLeftDip = waPx.Left / dpiScale;
            double waTopDip = waPx.Top / dpiScale;
            double waWidthDip = Math.Max(200.0, waPx.Width / dpiScale);
            double waHeightDip = Math.Max(160.0, waPx.Height / dpiScale);
            Rect workAreaDip = new Rect(waLeftDip, waTopDip, waWidthDip, waHeightDip);
            Rect appBoundsDip = new Rect(leftDip, topDip, Math.Max(200.0, widthDip), Math.Max(160.0, heightDip));

            bool isZoomed = NativeMethods.IsZoomed(targetHwnd);
            bool coversMonitor = targetPxRect.Width >= monPx.Width - 6 && targetPxRect.Height >= monPx.Height - 6;
            bool coversWorkArea = targetPxRect.Width >= waPx.Width - 10 && targetPxRect.Height >= waPx.Height - 10;
            bool isFullOrMax = isZoomed || coversMonitor || coversWorkArea;

            if (uiaProbe != null)
            {
                uiaProbe.SetTargetWindow(targetHwnd, targetRect.Top, targetRect.Bottom, dpiScale);
            }

            Rect effectiveVisible = Rect.Intersect(appBoundsDip, workAreaDip);
            double visHeightDip = (!effectiveVisible.IsEmpty && effectiveVisible.Height >= 160.0)
                ? effectiveVisible.Height
                : Math.Min(heightDip, waHeightDip);
            double atomDip = ComputeResponsiveAtomDipSize(visHeightDip, detectedEmptyComposerDip, settings.ScaleMultiplier);

            this.currentAppBoundsDip = appBoundsDip;
            this.currentWorkAreaDip = workAreaDip;
            this.isAppFullscreenOrMaximized = isFullOrMax;

            bool geomChanged = double.IsNaN(lastTargetLeftDip)
                || Math.Abs(leftDip - lastTargetLeftDip) > 0.5
                || Math.Abs(topDip - lastTargetTopDip) > 0.5
                || Math.Abs(widthDip - lastTargetWidthDip) > 0.5
                || Math.Abs(heightDip - lastTargetHeightDip) > 0.5
                || double.IsNaN(lastWorkLeftDip)
                || Math.Abs(waLeftDip - lastWorkLeftDip) > 0.5
                || Math.Abs(waTopDip - lastWorkTopDip) > 0.5
                || Math.Abs(waWidthDip - lastWorkWidthDip) > 0.5
                || Math.Abs(waHeightDip - lastWorkHeightDip) > 0.5
                || Math.Abs(atomDip - lastAtomSizeDip) > 0.5
                || Math.Abs(dpiScale - lastDpiScale) > 0.01
                || double.IsNaN(lastAnchorX)
                || Math.Abs(settings.AnchorNormX - lastAnchorX) > 0.0005
                || Math.Abs(settings.AnchorNormY - lastAnchorY) > 0.0005
                || isFullOrMax != lastFullscreenOrMax;

            if (geomChanged)
            {
                widget.SetTargetGeometry(appBoundsDip, workAreaDip, atomDip, dpiScale, isFullOrMax);
                lastTargetLeftDip = leftDip;
                lastTargetTopDip = topDip;
                lastTargetWidthDip = widthDip;
                lastTargetHeightDip = heightDip;
                lastWorkLeftDip = waLeftDip;
                lastWorkTopDip = waTopDip;
                lastWorkWidthDip = waWidthDip;
                lastWorkHeightDip = waHeightDip;
                lastAtomSizeDip = atomDip;
                lastDpiScale = dpiScale;
                lastAnchorX = settings.AnchorNormX;
                lastAnchorY = settings.AnchorNormY;
                lastFullscreenOrMax = isFullOrMax;

                if (TargetBoundsUpdated != null)
                {
                    TargetBoundsUpdated();
                }
            }

            int xPx, yPx, wPx, hPx;
            widget.GetDesiredScreenWindowOriginPx(out xPx, out yPx, out wPx, out hPx);

            // Walk upward via GW_HWNDPREV past any AntigravitySakuraMapleOverlay windows so Z-order is:
            // Antigravity -> AntigravitySakuraMapleOverlay -> AntigravityAtomTokenWidget
            IntPtr stackTop = targetHwnd;
            IntPtr probe = targetHwnd;
            for (int step = 0; step < 6; step++)
            {
                IntPtr p = NativeMethods.GetWindow(probe, NativeMethods.GW_HWNDPREV);
                if (p == IntPtr.Zero)
                {
                    break;
                }
                if (p == widgetHwnd)
                {
                    probe = p;
                    continue;
                }
                uint pPid;
                NativeMethods.GetWindowThreadProcessId(p, out pPid);
                if (GetProcessKind((int)pPid) == 2)
                {
                    stackTop = p;
                    probe = p;
                    continue;
                }
                break;
            }

            IntPtr aboveStackTop = NativeMethods.GetWindow(stackTop, NativeMethods.GW_HWNDPREV);
            bool zOrderOk = (aboveStackTop == widgetHwnd);
            bool boundsChanged = (xPx != lastWinX) || (yPx != lastWinY) || (wPx != lastWinW) || (hPx != lastWinH);

            if (!isVisible || boundsChanged || !zOrderOk)
            {
                IntPtr insertAfter = NativeMethods.HWND_TOP;
                if (!zOrderOk && aboveStackTop != IntPtr.Zero)
                {
                    int aboveEx = NativeMethods.GetWindowLong(aboveStackTop, NativeMethods.GWL_EXSTYLE);
                    if ((aboveEx & NativeMethods.WS_EX_TOPMOST) == 0)
                    {
                        insertAfter = aboveStackTop;
                    }
                }

                uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
                if (zOrderOk)
                {
                    flags |= NativeMethods.SWP_NOZORDER;
                }

                NativeMethods.SetWindowPos(widgetHwnd, insertAfter, xPx, yPx, wPx, hPx, flags);
                lastWinX = xPx;
                lastWinY = yPx;
                lastWinW = wPx;
                lastWinH = hPx;
                isVisible = true;
            }
        }

        private static double ReadWindowDpiScale(IntPtr hwnd)
        {
            try
            {
                uint dpi = NativeMethods.GetDpiForWindow(hwnd);
                if (dpi >= 72 && dpi <= 480)
                {
                    return dpi / 96.0;
                }
            }
            catch
            {
            }
            return 1.0;
        }

        // Returns: 0 = other, 1 = Antigravity Desktop, 2 = AntigravitySakuraMapleOverlay
        private int GetProcessKind(int pid)
        {
            if (pid <= 0 || pid == selfPid)
            {
                return 0;
            }
            int cached;
            if (pidKindCache.TryGetValue(pid, out cached))
            {
                return cached;
            }

            int kind = 0;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    string pn = p.ProcessName;
                    if (string.Equals(pn, "AntigravitySakuraMapleOverlay", StringComparison.OrdinalIgnoreCase))
                    {
                        kind = 2;
                    }
                    else if (string.Equals(pn, "Antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        kind = 1;
                        try
                        {
                            if (p.MainModule != null && !string.IsNullOrEmpty(p.MainModule.FileName))
                            {
                                if (p.MainModule.FileName.IndexOf("Antigravity IDE", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    kind = 0;
                                }
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
                kind = 0;
            }

            pidKindCache[pid] = kind;
            return kind;
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) { return min; }
            if (v > max) { return max; }
            return v;
        }
    }
}
