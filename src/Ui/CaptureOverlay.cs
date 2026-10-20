using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SnapAsk.Net;
using SnapAsk.Util;

namespace SnapAsk.Ui
{
	/// <summary>
	/// 全屏框选层：显示「已冻结的整屏画面」，用户按下鼠标拖出矩形。
	/// 由于整屏画面在弹层出现之前就抓好了，所以弹层自身不会出现在截图里，
	/// 也不需要「先隐藏窗口再截屏」这种会闪一下的做法。
	///
	/// 结束条件（满足其一）：
	///   · 按住键已松开 + 鼠标已抬起 —— 提交框选；
	///   · 没画框就松开了按住键 —— 取消；
	///   · Esc / 右键 / 其它字母键 —— 取消。
	///
	/// 注意：按住 Alt / Ctrl 这类修饰键时，系统会不停发送重复的 keydown，
	/// 所以绝不能「见到 keydown 就当用户放弃」，否则框选层刚出现就被自己关掉了。
	/// </summary>
	public sealed class CaptureOverlay : Form
	{
		private readonly Bitmap Frozen;

		private readonly string HintKey;

		private readonly Timer Watchdog;

		private Point StartPoint;

		private Point Current;

		private bool Dragging;

		private bool MouseDone;

		private bool TriggerReleased;

		private bool Finished;

		private DateTime ShownAt;

		/// <summary>触发键的虚拟键码，用来忽略它自己的长按重复事件。</summary>
		public int TriggerVk { get; set; }

		/// <summary>框选完成，参数是「冻结图坐标系」里的矩形。</summary>
		public event Action<Rectangle> Confirmed;

		public event Action Cancelled;

		/// <summary>
		/// true = 由「按住键」触发，必须等键松开才提交（松手先于松键时继续等）；
		/// false = 从托盘 / 按钮触发，鼠标抬起就直接提交。
		/// </summary>
		public bool RequireKeyRelease { get; set; }

		public CaptureOverlay(Bitmap frozen, Rectangle virtualBounds, string hintKey)
		{
			Frozen = frozen;
			HintKey = hintKey;
			RequireKeyRelease = true;
			FormBorderStyle = FormBorderStyle.None;
			StartPosition = FormStartPosition.Manual;
			Bounds = virtualBounds;
			TopMost = true;
			ShowInTaskbar = false;
			KeyPreview = true;
			Cursor = Cursors.Cross;
			BackColor = Color.Black;
			DoubleBuffered = true;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
				| ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			// 兜底：万一钩子没收到「松开」，用物理键状态自己判断一次
			Watchdog = new Timer();
			Watchdog.Interval = 200;
			Watchdog.Tick += delegate { OnWatchdog(); };
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			ShownAt = DateTime.Now;
			try
			{
				Activate();
				Win32.SetForegroundWindow(Handle);
			}
			catch
			{
			}
			if (RequireKeyRelease && TriggerVk > 0)
			{
				Watchdog.Start();
			}
		}

		private void OnWatchdog()
		{
			if (Finished)
			{
				Watchdog.Stop();
				return;
			}
			if (TriggerVk <= 0 || Win32.IsKeyDown(TriggerVk))
			{
				return;
			}
			Watchdog.Stop();
			NotifyTriggerReleased();
		}

		/// <summary>主界面收到「松开按住键」时调用。</summary>
		public void NotifyTriggerReleased()
		{
			if (Finished || !RequireKeyRelease)
			{
				return;
			}
			TriggerReleased = true;
			if (!Dragging && !MouseDone)
			{
				// 只是按了一下没画框：当作取消
				Cancel();
				return;
			}
			if (MouseDone)
			{
				Commit();
			}
			// 鼠标还按着：等抬起时再提交
		}

		private Rectangle Selection()
		{
			int left = Math.Min(StartPoint.X, Current.X);
			int top = Math.Min(StartPoint.Y, Current.Y);
			int width = Math.Abs(Current.X - StartPoint.X);
			int height = Math.Abs(Current.Y - StartPoint.Y);
			return new Rectangle(left, top, width, height);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (e.Button == MouseButtons.Right)
			{
				Cancel();
				return;
			}
			if (e.Button != MouseButtons.Left)
			{
				return;
			}
			StartPoint = e.Location;
			Current = e.Location;
			Dragging = true;
			MouseDone = false;
			Log.Dim("框选起点: " + StartPoint);
			Invalidate();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (!Dragging)
			{
				return;
			}
			Current = e.Location;
			Invalidate();
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			if (e.Button != MouseButtons.Left || !Dragging)
			{
				return;
			}
			Dragging = false;
			Current = e.Location;
			MouseDone = true;
			Log.Dim("框选终点: " + Current + " → " + Selection().Size);
			Invalidate();
			if (TriggerReleased || !RequireKeyRelease)
			{
				Commit();
			}
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (Finished)
			{
				return;
			}
			// 刚弹出的 350ms 内不响应按键：这时候收到的多半是触发键的长按重复
			if ((DateTime.Now - ShownAt).TotalMilliseconds < 350)
			{
				return;
			}
			if (e.KeyCode == Keys.Escape)
			{
				Cancel();
				return;
			}
			// 修饰键（Alt / Ctrl / Shift / Win）长按会不停发 keydown，绝不能当成「放弃」
			if (IsModifier(e.KeyCode))
			{
				return;
			}
			if (TriggerVk > 0 && (int)e.KeyCode == TriggerVk)
			{
				return;
			}
			// 其它按键才视为放弃，避免误触发后卡在这个全屏层里
			Cancel();
		}

		protected override void OnKeyUp(KeyEventArgs e)
		{
			base.OnKeyUp(e);
			if (Finished || !RequireKeyRelease)
			{
				return;
			}
			// 焦点在框选层时，触发键的松开由这里兜底（等价于钩子的 HoldUp）
			if (TriggerVk > 0 && (int)e.KeyCode == TriggerVk)
			{
				Watchdog.Stop();
				NotifyTriggerReleased();
			}
		}

		private static bool IsModifier(Keys key)
		{
			return key == Keys.Alt || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu
				|| key == Keys.Control || key == Keys.LControlKey || key == Keys.RControlKey
				|| key == Keys.Shift || key == Keys.LShiftKey || key == Keys.RShiftKey
				|| key == Keys.LWin || key == Keys.RWin;
		}

		private void Commit()
		{
			if (Finished)
			{
				return;
			}
			Rectangle rect = Selection();
			if (rect.Width < 8 || rect.Height < 8)
			{
				Cancel();
				return;
			}
			Finished = true;
			Watchdog.Stop();
			Rectangle device = ToBitmapRect(rect);
			// 必须先回调再关窗：FormClosed 里会清理冻结图，
			// 先 Close() 会让「裁剪截图」这一步拿到已经被释放的位图。
			Action<Rectangle> handler = Confirmed;
			if (handler != null)
			{
				handler(device);
			}
			Close();
		}

		private void Cancel()
		{
			if (Finished)
			{
				return;
			}
			Finished = true;
			Watchdog.Stop();
			Action handler = Cancelled;
			if (handler != null)
			{
				handler();
			}
			Close();
		}

		/// <summary>把窗体坐标换算成冻结图坐标（多显示器 / DPI 缩放时两者可能不等）。</summary>
		private Rectangle ToBitmapRect(Rectangle rect)
		{
			if (Frozen == null || ClientSize.Width < 1 || ClientSize.Height < 1)
			{
				return rect;
			}
			double sx = Frozen.Width / (double)ClientSize.Width;
			double sy = Frozen.Height / (double)ClientSize.Height;
			int left = (int)Math.Round(rect.Left * sx);
			int top = (int)Math.Round(rect.Top * sy);
			int right = (int)Math.Round(rect.Right * sx);
			int bottom = (int)Math.Round(rect.Bottom * sy);
			return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle full = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
			if (Frozen != null && full.Width > 0 && full.Height > 0)
			{
				g.InterpolationMode = InterpolationMode.Bilinear;
				g.DrawImage(Frozen, full);
			}
			using (SolidBrush dim = new SolidBrush(Color.FromArgb(112, 8, 10, 15)))
			{
				g.FillRectangle(dim, full);
			}

			Rectangle sel = Selection();
			bool hasSelection = MouseDone || Dragging;
			if (hasSelection && sel.Width > 0 && sel.Height > 0)
			{
				Rectangle src = ToBitmapRect(sel);
				if (Frozen != null)
				{
					g.DrawImage(Frozen, sel, src, GraphicsUnit.Pixel);
				}
				using (Pen pen = new Pen(Theme.Accent, 2f))
				{
					g.DrawRectangle(pen, sel.Left, sel.Top, sel.Width - 1, sel.Height - 1);
				}
				DrawSizeBadge(g, sel);
			}
			else
			{
				DrawCenterHint(g, full);
			}
			DrawBottomHint(g, full);
		}

		private void DrawSizeBadge(Graphics g, Rectangle sel)
		{
			string label = sel.Width + " × " + sel.Height;
			Size size = TextRenderer.MeasureText(label, Theme.SmallFont);
			int width = size.Width + 16;
			int height = size.Height + 8;
			int x = sel.Left;
			int y = sel.Top - height - 6;
			if (y < 4)
			{
				y = sel.Top + 6;
			}
			if (x + width > ClientSize.Width - 4)
			{
				x = ClientSize.Width - width - 4;
			}
			Rectangle box = new Rectangle(Math.Max(4, x), y, width, height);
			using (GraphicsPath path = Draw.Rounded(box, 6))
			{
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(235, Theme.Accent)))
				{
					g.FillPath(brush, path);
				}
			}
			TextRenderer.DrawText(g, label, Theme.SmallFont, box, Theme.OnAccent,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		}

		private void DrawCenterHint(Graphics g, Rectangle full)
		{
			string text = "拖拽鼠标框选题目区域";
			Size size = TextRenderer.MeasureText(text, Theme.UiFont);
			Rectangle box = new Rectangle(
				full.Left + (full.Width - size.Width - 40) / 2,
				full.Top + (full.Height - size.Height - 24) / 2,
				size.Width + 40,
				size.Height + 24);
			using (GraphicsPath path = Draw.Rounded(box, 12))
			{
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(190, 12, 14, 20)))
				{
					g.FillPath(brush, path);
				}
			}
			TextRenderer.DrawText(g, text, Theme.UiFont, box, Color.White,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		}

		private void DrawBottomHint(Graphics g, Rectangle full)
		{
			string key = string.IsNullOrEmpty(HintKey) ? "按键" : HintKey;
			string text = RequireKeyRelease
				? ("松开 " + key + " 立即提问   ·   右键 / Esc 取消")
				: "松开鼠标立即提问   ·   右键 / Esc 取消";
			Size size = TextRenderer.MeasureText(text, Theme.SmallFont);
			Rectangle box = new Rectangle(
				full.Left + (full.Width - size.Width - 36) / 2,
				full.Bottom - size.Height - 46,
				size.Width + 36,
				size.Height + 18);
			using (GraphicsPath path = Draw.Rounded(box, box.Height / 2))
			{
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(200, 12, 14, 20)))
				{
					g.FillPath(brush, path);
				}
			}
			TextRenderer.DrawText(g, text, Theme.SmallFont, box, Color.FromArgb(235, 240, 245, 250),
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		}

		protected override void Dispose(bool disposing)
		{
			try
			{
				Watchdog.Stop();
				Watchdog.Dispose();
			}
			catch
			{
			}
			base.Dispose(disposing);
		}
	}
}
