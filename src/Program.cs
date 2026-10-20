using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SnapAsk.Ui;
using SnapAsk.Util;

namespace SnapAsk
{
	internal static class Program
	{
		[DllImport("user32.dll")]
		private static extern bool SetProcessDPIAware();

		[STAThread]
		private static void Main(string[] args)
		{
			if (HasArg(args, "--selftest"))
			{
				Environment.ExitCode = SelfTest.Run();
				return;
			}
			if (HasArg(args, "--probe"))
			{
				Environment.ExitCode = SelfTest.Probe();
				return;
			}

			try
			{
				SetProcessDPIAware();
			}
			catch
			{
			}

			bool createdNew;
			using (Mutex instanceLock = new Mutex(true, "SnapAsk.SingleInstance.7E2B41", out createdNew))
			{
				if (!createdNew)
				{
					MessageBox.Show("SnapAsk 已经在运行了，请在任务栏或托盘里切换。", "SnapAsk",
						MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
					return;
				}
				bool demo = HasArg(args, "--uidemo");
				bool askDemo = HasArg(args, "--askdemo");
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				Settings preview = Settings.Load();
				Theme.Initialize(preview.Theme);
				Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
				Application.ThreadException += OnThreadException;
				AppDomain.CurrentDomain.UnhandledException += OnDomainException;
				try
				{
					Application.Run(new MainForm(demo, askDemo));
				}
				catch (Exception ex)
				{
					Fatal(ex);
				}
				GC.KeepAlive(instanceLock);
			}
		}

		private static bool HasArg(string[] args, string name)
		{
			if (args == null)
			{
				return false;
			}
			for (int i = 0; i < args.Length; i++)
			{
				if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(args[i], "-" + name.TrimStart('-'), StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
		{
			Fatal(e.Exception);
		}

		private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
		{
			Fatal(e.ExceptionObject as Exception);
		}

		/// <summary>兜底异常处理：写日志 + 弹窗，绝不静默退出。</summary>
		private static void Fatal(Exception ex)
		{
			try
			{
				Log.Error((ex == null) ? "未知异常" : ex.ToString());
			}
			catch
			{
			}
			try
			{
				MessageBox.Show("SnapAsk 遇到错误：\r\n\r\n"
					+ ((ex == null) ? "未知异常" : ex.Message) + "\r\n\r\n日志: " + Log.FilePath,
					"SnapAsk", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			catch
			{
			}
		}
	}
}
