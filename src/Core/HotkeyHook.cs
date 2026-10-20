using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SnapAsk.Net;
using SnapAsk.Util;

namespace SnapAsk.Core
{
	/// <summary>
	/// 全局键盘钩子（WH_KEYBOARD_LL）。
	/// 两种触发方式：
	///   1. 按住键 —— 按下触发 HoldDown，松开触发 HoldUp（用来做「按住→拖框→松手发送」）；
	///   2. 点按键 —— 组合键按下触发 Tap（用来做划词提问）。
	/// 钩子回调运行在安装它的线程（主 UI 线程），为了不拖慢系统输入，
	/// 回调里只做判断 + BeginInvoke 派发，绝不阻塞。
	/// </summary>
	public sealed class HotkeyHook : IDisposable
	{
		private readonly Control Sync;

		private readonly HashSet<int> DownKeys = new HashSet<int>();

		private Win32.LowLevelKeyboardProc Callback;

		private IntPtr Handle = IntPtr.Zero;

		private bool HoldActive;

		private DateTime LastTap = DateTime.MinValue;

		/// <summary>暂停识别（设置窗口在录键时用）。</summary>
		public bool Suspended { get; set; }

		public KeySpec HoldKey { get; set; }

		public KeySpec TapKey { get; set; }

		public event Action HoldDown;

		public event Action HoldUp;

		public event Action Tap;

		public bool Installed
		{
			get
			{
				return Handle != IntPtr.Zero;
			}
		}

		public HotkeyHook(Control sync)
		{
			Sync = sync;
			Callback = OnKey;
		}

		public bool Install()
		{
			if (Handle != IntPtr.Zero)
			{
				return true;
			}
			try
			{
				Handle = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, Callback, Win32.GetModuleHandle(null), 0u);
			}
			catch (Exception ex)
			{
				Log.Error("安装键盘钩子失败: " + ex.Message);
				Handle = IntPtr.Zero;
			}
			if (Handle == IntPtr.Zero)
			{
				Log.Error("安装键盘钩子失败（返回 0），全局快捷键不可用。");
				return false;
			}
			Log.Info("全局键盘钩子已安装。");
			return true;
		}

		private IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
		{
			try
			{
				if (nCode >= 0 && !Suspended)
				{
					int message = wParam.ToInt32();
					Win32.KBDLLHOOKSTRUCT data = (Win32.KBDLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
						lParam, typeof(Win32.KBDLLHOOKSTRUCT));
					int vk = KeySpec.Normalize((int)data.vkCode);
					bool isDown = (message == Win32.WM_KEYDOWN || message == Win32.WM_SYSKEYDOWN);
					bool isUp = (message == Win32.WM_KEYUP || message == Win32.WM_SYSKEYUP);
					if (isDown)
					{
						// 长按会重复发 KEYDOWN，用集合去重
						if (DownKeys.Add(vk))
						{
							CheckPress(vk);
						}
					}
					else if (isUp)
					{
						DownKeys.Remove(vk);
						CheckRelease(vk);
					}
				}
			}
			catch
			{
			}
			return Win32.CallNextHookEx(Handle, nCode, wParam, lParam);
		}

		private void CheckPress(int vk)
		{
			bool ctrl = DownKeys.Contains(17);
			bool alt = DownKeys.Contains(18);
			bool shift = DownKeys.Contains(16);
			bool win = DownKeys.Contains(91);

			KeySpec hold = HoldKey;
			if (hold != null && !HoldActive && hold.Matches(vk, ctrl, alt, shift, win))
			{
				HoldActive = true;
				Dispatch(HoldDown);
				return;
			}
			KeySpec tap = TapKey;
			if (tap != null && tap.Matches(vk, ctrl, alt, shift, win))
			{
				// 连按防抖：350ms 内不重复触发
				if ((DateTime.Now - LastTap).TotalMilliseconds > 350)
				{
					LastTap = DateTime.Now;
					Dispatch(Tap);
				}
			}
		}

		private void CheckRelease(int vk)
		{
			if (!HoldActive || HoldKey == null)
			{
				return;
			}
			if (vk == HoldKey.Vk)
			{
				HoldActive = false;
				Dispatch(HoldUp);
			}
		}

		/// <summary>派发到 UI 线程，钩子回调本身马上返回。</summary>
		private void Dispatch(Action handler)
		{
			if (handler == null)
			{
				return;
			}
			try
			{
				if (Sync != null && Sync.IsHandleCreated)
				{
					Sync.BeginInvoke(handler);
				}
				else
				{
					handler();
				}
			}
			catch
			{
			}
		}

		/// <summary>外部取消捕获时重置按住状态，避免下一次按键被误判。</summary>
		public void ResetHold()
		{
			HoldActive = false;
		}

		public void Dispose()
		{
			if (Handle != IntPtr.Zero)
			{
				try
				{
					Win32.UnhookWindowsHookEx(Handle);
				}
				catch
				{
				}
				Handle = IntPtr.Zero;
			}
		}
	}
}
