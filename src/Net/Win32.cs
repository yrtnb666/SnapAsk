using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SnapAsk.Net
{
	/// <summary>
	/// 最薄的一层 Win32 封装：窗口、键鼠注入、键盘钩子。
	/// 业务判断都放在 Core 里，这里只做 P/Invoke。
	/// </summary>
	internal static class Win32
	{
		public const int WH_KEYBOARD_LL = 13;

		public const int WM_KEYDOWN = 0x0100;

		public const int WM_KEYUP = 0x0101;

		public const int WM_SYSKEYDOWN = 0x0104;

		public const int WM_SYSKEYUP = 0x0105;

		public const uint KEYEVENTF_KEYUP = 0x0002;

		public const byte VK_CONTROL = 0x11;

		public const byte VK_MENU = 0x12;

		public const byte VK_SHIFT = 0x10;

		public const byte VK_LWIN = 0x5B;

		public const byte VK_C = 0x43;

		// ---- 窗口样式 / 显示命令（嵌入浏览器用） ----
		public const int GWL_STYLE = -16;

		public const int WS_CHILD = 0x40000000;

		public const int WS_CAPTION = 0x00C00000;

		public const int WS_THICKFRAME = 0x00040000;

		public const int WS_POPUP = unchecked((int)0x80000000);

		public const int WS_MAXIMIZE = 0x01000000;

		public const int SW_SHOW = 5;

		public const int SW_HIDE = 0;

		public const int SW_RESTORE = 9;

		[StructLayout(LayoutKind.Sequential)]
		public struct RECT
		{
			public int Left;

			public int Top;

			public int Right;

			public int Bottom;

			public int Width
			{
				get
				{
					return Right - Left;
				}
			}

			public int Height
			{
				get
				{
					return Bottom - Top;
				}
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct KBDLLHOOKSTRUCT
		{
			public uint vkCode;

			public uint scanCode;

			public uint flags;

			public uint time;

			public IntPtr dwExtraInfo;
		}

		public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr hMod, uint threadId);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern bool UnhookWindowsHookEx(IntPtr hook);

		[DllImport("user32.dll")]
		public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr GetModuleHandle(string moduleName);

		[DllImport("user32.dll")]
		public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

		[DllImport("user32.dll")]
		public static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		public static extern bool SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		public static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

		[DllImport("user32.dll")]
		public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

		public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

		[DllImport("user32.dll")]
		public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

		[DllImport("user32.dll")]
		public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

		[DllImport("user32.dll")]
		public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

		[DllImport("user32.dll")]
		public static extern IntPtr GetParent(IntPtr hWnd);

		[DllImport("user32.dll")]
		public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

		[DllImport("user32.dll")]
		public static extern int GetWindowLong(IntPtr hWnd, int index);

		[DllImport("user32.dll")]
		public static extern int SetWindowLong(IntPtr hWnd, int index, int value);

		[DllImport("user32.dll")]
		public static extern bool ShowWindow(IntPtr hWnd, int command);

		[DllImport("user32.dll")]
		public static extern bool IsIconic(IntPtr hWnd);

		[DllImport("user32.dll")]
		public static extern bool IsWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		public static extern short GetAsyncKeyState(int virtualKey);

		/// <summary>某个物理键当前是否按着（用于兜底判断「按住键」有没有松开）。</summary>
		public static bool IsKeyDown(int virtualKey)
		{
			try
			{
				return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// 找浏览器窗口：优先按进程 ID 匹配，进程 ID 不可用（复用已有浏览器时为 0）时
		/// 退化成「Chromium 类名 + 标题包含 DeepSeek」。
		/// </summary>
		public static IntPtr FindBrowserWindow(int processId, string titlePart)
		{
			IntPtr exact = IntPtr.Zero;
			IntPtr byTitle = IntPtr.Zero;
			EnumWindowsProc callback = delegate(IntPtr hWnd, IntPtr lParam)
			{
				try
				{
					if (!IsWindowVisible(hWnd))
					{
						return true;
					}
					string cls = GetClass(hWnd);
					if (!string.Equals(cls, "Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase)
						&& !string.Equals(cls, "Chrome_WidgetWin_0", StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
					int pid;
					GetWindowThreadProcessId(hWnd, out pid);
					if (processId > 0 && pid == processId)
					{
						exact = hWnd;
						return false;
					}
					if (byTitle == IntPtr.Zero && !string.IsNullOrEmpty(titlePart))
					{
						string text = GetTitle(hWnd);
						if (text.IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0
							&& text.IndexOf("Microsoft Edge", StringComparison.OrdinalIgnoreCase) < 0)
						{
							// --app 窗口的标题就是页面标题；取最短的，避免命中普通浏览器窗口
							if (byTitle == IntPtr.Zero || text.Length < GetTitle(byTitle).Length)
							{
								byTitle = hWnd;
							}
						}
					}
				}
				catch
				{
				}
				return true;
			};
			try
			{
				EnumWindows(callback, IntPtr.Zero);
			}
			catch
			{
			}
			GC.KeepAlive(callback);
			return (exact != IntPtr.Zero) ? exact : byTitle;
		}

		[DllImport("user32.dll")]
		public static extern bool GetCursorPos(out POINT point);

		[StructLayout(LayoutKind.Sequential)]
		public struct POINT
		{
			public int X;

			public int Y;
		}

		public static string GetTitle(IntPtr hWnd)
		{
			try
			{
				StringBuilder title = new StringBuilder(512);
				GetWindowText(hWnd, title, title.Capacity);
				return title.ToString();
			}
			catch
			{
				return "";
			}
		}

		public static string GetClass(IntPtr hWnd)
		{
			try
			{
				StringBuilder cls = new StringBuilder(256);
				GetClassName(hWnd, cls, cls.Capacity);
				return cls.ToString();
			}
			catch
			{
				return "";
			}
		}
	}
}
