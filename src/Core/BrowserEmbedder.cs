using System;
using System.Drawing;
using System.Windows.Forms;
using SnapAsk.Net;
using SnapAsk.Util;

namespace SnapAsk.Core
{
	/// <summary>
	/// 把 Edge / Chrome 的窗口「塞进」主界面的面板里（SetParent 方案）。
	/// 和 EAgentX 的做法一致：
	///   · 先记下原来的样式和位置，取消 WS_POPUP / WS_CAPTION / WS_THICKFRAME，加上 WS_CHILD；
	///   · 最大化 / 最小化的窗口必须先还原，否则 SetParent 之后 MoveWindow 缩不小；
	///   · 换父窗口后 Chromium 会自己重排一次，所以要延时再校准一两次尺寸；
	///   · 恢复 / 退出时一定要 SetParent 回桌面，否则浏览器窗口会跟着主窗口一起销毁。
	/// </summary>
	public sealed class BrowserEmbedder : IDisposable
	{
		private readonly Control Host;

		private readonly Timer RelayoutTimer;

		private IntPtr Window = IntPtr.Zero;

		private int SavedStyle;

		private Win32.RECT SavedRect;

		private int RelayoutPass;

		public bool Embedded
		{
			get
			{
				return Window != IntPtr.Zero;
			}
		}

		public BrowserEmbedder(Control hostPanel)
		{
			Host = hostPanel;
			RelayoutTimer = new Timer();
			RelayoutTimer.Interval = 150;
			RelayoutTimer.Tick += delegate { OnRelayoutTick(); };
			Host.Resize += delegate { Relayout(); };
		}

		/// <summary>按进程 ID（或其标题）找到浏览器窗口并嵌入；找不到返回 false。</summary>
		public bool Attach(int processId, string titlePart)
		{
			if (Embedded)
			{
				return true;
			}
			IntPtr hwnd = Win32.FindBrowserWindow(processId, titlePart);
			if (hwnd == IntPtr.Zero)
			{
				return false;
			}
			SavedStyle = Win32.GetWindowLong(hwnd, Win32.GWL_STYLE);
			if ((SavedStyle & Win32.WS_MAXIMIZE) != 0 || Win32.IsIconic(hwnd))
			{
				Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
				Application.DoEvents();
				SavedStyle = Win32.GetWindowLong(hwnd, Win32.GWL_STYLE);
			}
			Win32.GetWindowRect(hwnd, out SavedRect);
			if (!Host.IsHandleCreated)
			{
				return false;
			}
			Window = hwnd;
			Win32.SetParent(hwnd, Host.Handle);
			int style = (SavedStyle | Win32.WS_CHILD)
				& ~Win32.WS_POPUP & ~Win32.WS_CAPTION & ~Win32.WS_THICKFRAME & ~Win32.WS_MAXIMIZE;
			Win32.SetWindowLong(hwnd, Win32.GWL_STYLE, style);
			Relayout();
			Win32.ShowWindow(hwnd, Win32.SW_SHOW);
			StartRelayout();
			Log.Info("浏览器已嵌入主窗口（句柄 " + hwnd.ToString() + "）。");
			return true;
		}

		public void Detach()
		{
			IntPtr hwnd = Window;
			Window = IntPtr.Zero;
			StopRelayout();
			if (hwnd == IntPtr.Zero)
			{
				return;
			}
			try
			{
				Win32.SetParent(hwnd, IntPtr.Zero);
				Win32.SetWindowLong(hwnd, Win32.GWL_STYLE, SavedStyle);
				Rectangle area = Screen.PrimaryScreen.WorkingArea;
				int width = Math.Max(420, Math.Min(SavedRect.Width, area.Width));
				int height = Math.Max(320, Math.Min(SavedRect.Height, area.Height));
				int left = Math.Max(area.Left + 20, Math.Min(SavedRect.Left, area.Right - width - 20));
				int top = Math.Max(area.Top + 20, Math.Min(SavedRect.Top, area.Bottom - height - 20));
				Win32.MoveWindow(hwnd, left, top, width, height, true);
				Win32.ShowWindow(hwnd, Win32.SW_SHOW);
				Log.Info("浏览器已恢复成独立窗口。");
			}
			catch (Exception ex)
			{
				Log.Error("恢复浏览器窗口失败: " + ex.Message);
			}
		}

		public void Relayout()
		{
			if (!Embedded || Host == null)
			{
				return;
			}
			try
			{
				int width = Math.Max(160, Host.ClientSize.Width);
				int height = Math.Max(120, Host.ClientSize.Height);
				Win32.MoveWindow(Window, 0, 0, width, height, true);
			}
			catch
			{
			}
		}

		/// <summary>换父窗口后 Chromium 会异步重排；连做几次校准，避免只显示左上角。</summary>
		private void StartRelayout()
		{
			RelayoutPass = 0;
			RelayoutTimer.Stop();
			RelayoutTimer.Start();
		}

		/// <summary>
		/// 强制嵌入的浏览器重画一次。
		/// 主窗口最小化再还原后，Chromium 的子窗口常停在「上次合成的画面」上不动，
		/// 看起来就是内容没同步过来；隐藏再显示 + 重新校准尺寸能让它重新合成。
		/// </summary>
		public void RefreshWindow()
		{
			if (!Embedded)
			{
				return;
			}
			try
			{
				Win32.ShowWindow(Window, Win32.SW_HIDE);
				Win32.ShowWindow(Window, Win32.SW_SHOW);
				Relayout();
				StartRelayout();
			}
			catch
			{
			}
		}

		private void StopRelayout()
		{
			try
			{
				RelayoutTimer.Stop();
			}
			catch
			{
			}
		}

		private void OnRelayoutTick()
		{
			RelayoutPass++;
			Relayout();
			if (RelayoutPass >= 4)
			{
				StopRelayout();
			}
		}

		public void Dispose()
		{
			try
			{
				Detach();
			}
			catch
			{
			}
			try
			{
				RelayoutTimer.Dispose();
			}
			catch
			{
			}
		}
	}
}
