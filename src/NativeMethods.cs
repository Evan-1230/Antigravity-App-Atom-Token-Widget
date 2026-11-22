using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AntigravityAtomTokenWidget
{
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;

        public const uint GW_HWNDPREV = 3;
        public const uint GW_OWNER = 4;

        public const int SW_HIDE = 0;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    }

    internal static class SqliteNative
    {
        public const int SQLITE_OK = 0;
        public const int SQLITE_ROW = 100;
        public const int SQLITE_DONE = 101;
        public const int SQLITE_OPEN_READONLY = 0x00000001;

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int sqlite3_open_v2(byte[] filenameUtf8, out IntPtr ppDb, int flags, IntPtr zVfs);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int sqlite3_busy_timeout(IntPtr db, int ms);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int sqlite3_prepare_v2(IntPtr db, byte[] zSqlUtf8, int nByte, out IntPtr ppStmt, out IntPtr pzTail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int sqlite3_step(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern long sqlite3_column_int64(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int sqlite3_column_int(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr sqlite3_column_text(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr sqlite3_column_blob(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int sqlite3_column_bytes(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int sqlite3_column_type(IntPtr stmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int sqlite3_close_v2(IntPtr db);
    }

    internal sealed class ReadOnlySqlite : IDisposable
    {
        private IntPtr db = IntPtr.Zero;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private ReadOnlySqlite(IntPtr dbHandle)
        {
            this.db = dbHandle;
        }

        public static ReadOnlySqlite Open(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }
            byte[] pathBytes = ToNullTerminatedUtf8(filePath);
            IntPtr dbPtr;
            int rc = SqliteNative.sqlite3_open_v2(pathBytes, out dbPtr, SqliteNative.SQLITE_OPEN_READONLY, IntPtr.Zero);
            if (rc != SqliteNative.SQLITE_OK || dbPtr == IntPtr.Zero)
            {
                if (dbPtr != IntPtr.Zero)
                {
                    SqliteNative.sqlite3_close_v2(dbPtr);
                }
                return null;
            }
            SqliteNative.sqlite3_busy_timeout(dbPtr, 150);
            return new ReadOnlySqlite(dbPtr);
        }

        public SqliteQuery Prepare(string sql)
        {
            if (db == IntPtr.Zero)
            {
                return null;
            }
            byte[] sqlBytes = ToNullTerminatedUtf8(sql);
            IntPtr stmt;
            IntPtr tail;
            int rc = SqliteNative.sqlite3_prepare_v2(db, sqlBytes, -1, out stmt, out tail);
            if (rc != SqliteNative.SQLITE_OK || stmt == IntPtr.Zero)
            {
                if (stmt != IntPtr.Zero)
                {
                    SqliteNative.sqlite3_finalize(stmt);
                }
                return null;
            }
            return new SqliteQuery(stmt);
        }

        private static byte[] ToNullTerminatedUtf8(string s)
        {
            int count = Utf8NoBom.GetByteCount(s);
            byte[] buf = new byte[count + 1];
            Utf8NoBom.GetBytes(s, 0, s.Length, buf, 0);
            buf[count] = 0;
            return buf;
        }

        public void Dispose()
        {
            if (db != IntPtr.Zero)
            {
                SqliteNative.sqlite3_close_v2(db);
                db = IntPtr.Zero;
            }
        }
    }

    internal sealed class SqliteQuery : IDisposable
    {
        private IntPtr stmt;

        public SqliteQuery(IntPtr statement)
        {
            this.stmt = statement;
        }

        public bool StepRow()
        {
            if (stmt == IntPtr.Zero)
            {
                return false;
            }
            int rc = SqliteNative.sqlite3_step(stmt);
            return rc == SqliteNative.SQLITE_ROW;
        }

        public int GetInt32(int col)
        {
            return SqliteNative.sqlite3_column_int(stmt, col);
        }

        public long GetInt64(int col)
        {
            return SqliteNative.sqlite3_column_int64(stmt, col);
        }

        public string GetString(int col)
        {
            if (SqliteNative.sqlite3_column_type(stmt, col) == 5)
            {
                return "";
            }
            IntPtr ptr = SqliteNative.sqlite3_column_text(stmt, col);
            if (ptr == IntPtr.Zero)
            {
                return "";
            }
            int len = SqliteNative.sqlite3_column_bytes(stmt, col);
            if (len <= 0)
            {
                return "";
            }
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        public byte[] GetBlob(int col)
        {
            if (SqliteNative.sqlite3_column_type(stmt, col) == 5)
            {
                return null;
            }
            IntPtr ptr = SqliteNative.sqlite3_column_blob(stmt, col);
            int len = SqliteNative.sqlite3_column_bytes(stmt, col);
            if (ptr == IntPtr.Zero || len <= 0)
            {
                return null;
            }
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, len);
            return buf;
        }

        public void Dispose()
        {
            if (stmt != IntPtr.Zero)
            {
                SqliteNative.sqlite3_finalize(stmt);
                stmt = IntPtr.Zero;
            }
        }
    }
}
