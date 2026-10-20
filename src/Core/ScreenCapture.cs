using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using SnapAsk.Util;

namespace SnapAsk.Core
{
	/// <summary>屏幕截图：整屏冻结、按框裁剪、存成 PNG 附件。</summary>
	public static class ScreenCapture
	{
		/// <summary>抓整个虚拟桌面（多显示器会拼成一张）。</summary>
		public static Bitmap CaptureVirtualScreen()
		{
			Rectangle bounds = SystemInformation.VirtualScreen;
			if (bounds.Width < 1 || bounds.Height < 1)
			{
				bounds = new Rectangle(0, 0, 1, 1);
			}
			Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
			}
			return bitmap;
		}

		/// <summary>从冻结图里裁一块；越界时自动夹到图内。</summary>
		public static Bitmap Crop(Bitmap source, Rectangle rect)
		{
			if (source == null)
			{
				return null;
			}
			Rectangle safe = Rectangle.Intersect(new Rectangle(0, 0, source.Width, source.Height), rect);
			if (safe.Width < 1 || safe.Height < 1)
			{
				return null;
			}
			Bitmap cropped = new Bitmap(safe.Width, safe.Height, PixelFormat.Format32bppRgb);
			using (Graphics graphics = Graphics.FromImage(cropped))
			{
				graphics.DrawImage(source, new Rectangle(0, 0, safe.Width, safe.Height), safe, GraphicsUnit.Pixel);
			}
			return cropped;
		}

		/// <summary>存成 PNG，返回完整路径。</summary>
		public static string SavePng(Bitmap bitmap)
		{
			if (bitmap == null)
			{
				throw new ArgumentNullException("bitmap");
			}
			Directory.CreateDirectory(Settings.ShotDir);
			string name = string.Format("shot-{0:yyyyMMdd-HHmmss-fff}.png", DateTime.Now);
			string path = Path.Combine(Settings.ShotDir, name);
			bitmap.Save(path, ImageFormat.Png);
			return path;
		}

		/// <summary>只保留最近 max 张截图，避免长期使用把磁盘塞满。</summary>
		public static void Prune(int max)
		{
			try
			{
				if (!Directory.Exists(Settings.ShotDir))
				{
					return;
				}
				FileInfo[] files = new DirectoryInfo(Settings.ShotDir).GetFiles("shot-*.png");
				if (files.Length <= max)
				{
					return;
				}
				Array.Sort(files, delegate(FileInfo a, FileInfo b)
				{
					return b.LastWriteTime.CompareTo(a.LastWriteTime);
				});
				for (int i = max; i < files.Length; i++)
				{
					try
					{
						files[i].Delete();
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
	}
}
