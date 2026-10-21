using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SnapAsk.Net;

namespace SnapAsk.Core
{
	/// <summary>
	/// 取「当前选中文字」：向当前前台窗口发一次 Ctrl+C，然后把剪贴板里的文字读回来。
	/// 剪贴板 API 要求 STA 线程，所以整段逻辑放在独立的 STA 线程里跑，
	/// 调用方 await 结果，主界面不会因为等待剪贴板而卡住。
	/// </summary>
	public static class ClipboardGrab
	{
		/// <summary>异步取词：等待期间不占用 UI 线程（最多约 waitMs 毫秒）。</summary>
		public static Task<string> GrabSelectedTextAsync(int waitMs)
		{
			TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
			Thread worker = new Thread(delegate()
			{
				string result = null;
				try
				{
					result = GrabCore(waitMs);
				}
				catch
				{
					result = null;
				}
				completion.TrySetResult(result);
			});
			worker.IsBackground = true;
			worker.SetApartmentState(ApartmentState.STA);
			worker.Start();
			return completion.Task;
		}

		private static string GrabCore(int waitMs)
		{
			string before = ReadText();
			SendCopy();
			Stopwatch watch = Stopwatch.StartNew();
			while (watch.ElapsedMilliseconds < waitMs)
			{
				Thread.Sleep(60);
				string now = ReadText();
				if (!string.IsNullOrEmpty(now) && !string.Equals(now, before, StringComparison.Ordinal))
				{
					return now;
				}
			}
			return null;
		}

		private static string ReadText()
		{
			try
			{
				if (!Clipboard.ContainsText())
				{
					return null;
				}
				return Clipboard.GetText();
			}
			catch
			{
				return null;
			}
		}

		/// <summary>当前前台窗口是不是浏览器（有些浏览器在失焦时会忽略注入的按键）。</summary>
		private static void SendCopy()
		{
			try
			{
				Win32.keybd_event(Win32.VK_CONTROL, 0, 0u, UIntPtr.Zero);
				Thread.Sleep(12);
				Win32.keybd_event(Win32.VK_C, 0, 0u, UIntPtr.Zero);
				Thread.Sleep(18);
				Win32.keybd_event(Win32.VK_C, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
				Thread.Sleep(12);
				Win32.keybd_event(Win32.VK_CONTROL, 0, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
			}
			catch
			{
			}
		}
	}
}
