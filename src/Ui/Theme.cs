using System;
using System.Drawing;

namespace SnapAsk.Ui
{
	/// <summary>
	/// 全局配色与字体。浅色 / 深色两套，切换时由 MainForm 重建界面。
	/// 界面代码不要直接写死颜色，一律走 Theme。
	/// </summary>
	public static class Theme
	{
		public static bool Dark;

		public static Color Bg;

		public static Color Panel;

		public static Color PanelAlt;

		public static Color Input;

		public static Color Border;

		public static Color Text;

		public static Color Dim;

		public static Color Accent;

		public static Color AccentHover;

		public static Color AccentPressed;

		public static Color AccentSoft;

		public static Color Accent2;

		public static Color Browser;

		public static Color OnAccent;

		public static Color User;

		public static Color Answer;

		public static Color Error;

		public static Color Success;

		public static Color ScrollThumb;

		public static Color Overlay;

		public static Font UiFont;

		public static Font UiFontBold;

		public static Font TitleFont;

		public static Font SmallFont;

		public static Font MonoFont;

		private static bool Ready;

		public static string ModeName
		{
			get
			{
				return Dark ? "dark" : "light";
			}
		}

		public static void Initialize(string mode)
		{
			Dark = string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase);
			LoadColors();
			string family = PickFamily(new string[] { "Microsoft YaHei UI", "微软雅黑", "Microsoft YaHei", "Segoe UI" });
			string mono = PickFamily(new string[] { "Consolas", "Cascadia Mono", "Courier New" });
			UiFont = new Font(family, 9.5f, FontStyle.Regular);
			UiFontBold = new Font(family, 9.5f, FontStyle.Bold);
			TitleFont = new Font(family, 15f, FontStyle.Bold);
			SmallFont = new Font(family, 8.5f, FontStyle.Regular);
			MonoFont = new Font(mono, 9.5f, FontStyle.Regular);
			Ready = true;
		}

		public static void SetMode(string mode)
		{
			Initialize(mode);
		}

		private static void LoadColors()
		{
			if (Dark)
			{
				Bg = Color.FromArgb(23, 25, 31);
				Panel = Color.FromArgb(31, 34, 42);
				PanelAlt = Color.FromArgb(38, 42, 52);
				Input = Color.FromArgb(28, 31, 39);
				Border = Color.FromArgb(52, 57, 69);
				Text = Color.FromArgb(230, 232, 238);
				Dim = Color.FromArgb(154, 163, 178);
				Accent = Color.FromArgb(124, 140, 248);
				AccentHover = Color.FromArgb(148, 163, 255);
				AccentPressed = Color.FromArgb(108, 124, 230);
				AccentSoft = Color.FromArgb(43, 47, 69);
				Accent2 = Color.FromArgb(167, 139, 250);
				Browser = Color.FromArgb(18, 20, 26);
				OnAccent = Color.FromArgb(248, 249, 255);
				User = Color.FromArgb(143, 180, 255);
				Answer = Color.FromArgb(230, 232, 238);
				Error = Color.FromArgb(240, 138, 130);
				Success = Color.FromArgb(111, 211, 181);
				ScrollThumb = Color.FromArgb(74, 81, 98);
				Overlay = Color.FromArgb(185, 8, 10, 15);
			}
			else
			{
				Bg = Color.FromArgb(243, 245, 250);
				Panel = Color.FromArgb(255, 255, 255);
				PanelAlt = Color.FromArgb(241, 243, 248);
				Input = Color.FromArgb(247, 249, 252);
				Border = Color.FromArgb(226, 230, 238);
				Text = Color.FromArgb(32, 37, 49);
				Dim = Color.FromArgb(111, 119, 135);
				Accent = Color.FromArgb(91, 91, 214);
				AccentHover = Color.FromArgb(75, 75, 199);
				AccentPressed = Color.FromArgb(63, 63, 176);
				AccentSoft = Color.FromArgb(238, 238, 251);
				Accent2 = Color.FromArgb(139, 92, 246);
				Browser = Color.FromArgb(234, 237, 244);
				OnAccent = Color.FromArgb(252, 252, 255);
				User = Color.FromArgb(37, 99, 235);
				Answer = Color.FromArgb(32, 37, 49);
				Error = Color.FromArgb(192, 57, 43);
				Success = Color.FromArgb(35, 128, 103);
				ScrollThumb = Color.FromArgb(197, 203, 215);
				Overlay = Color.FromArgb(115, 31, 35, 48);
			}
		}

		private static string PickFamily(string[] candidates)
		{
			try
			{
				foreach (string name in candidates)
				{
					Font probe = new Font(name, 9f);
					bool matched = string.Equals(probe.Name, name, StringComparison.OrdinalIgnoreCase);
					probe.Dispose();
					if (matched)
					{
						return name;
					}
				}
			}
			catch
			{
			}
			return SystemFonts.MessageBoxFont.FontFamily.Name;
		}

		public static bool IsReady
		{
			get
			{
				return Ready;
			}
		}
	}
}
