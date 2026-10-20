using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnapAsk.Ui
{
	/// <summary>圆角路径等绘制小工具。</summary>
	internal static class Draw
	{
		public static GraphicsPath Rounded(Rectangle bounds, int radius)
		{
			int value = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
			GraphicsPath path = new GraphicsPath();
			if (value == 0)
			{
				path.AddRectangle(bounds);
				return path;
			}
			int diameter = value * 2;
			path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
			path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
			path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
			path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
			path.CloseFigure();
			return path;
		}
	}

	public enum ButtonKind
	{
		Primary,
		Secondary,
		Ghost
	}

	/// <summary>圆角、扁平、带悬停反馈的按钮；不依赖任何第三方 UI 库。</summary>
	public class FlatButton : Button
	{
		private bool Hovering;

		private bool Pressing;

		public int CornerRadius { get; set; }

		public ButtonKind Kind { get; set; }

		public FlatButton()
		{
			Kind = ButtonKind.Secondary;
			CornerRadius = 9;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
				| ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
			FlatStyle = FlatStyle.Flat;
			FlatAppearance.BorderSize = 0;
			UseVisualStyleBackColor = false;
			BackColor = Color.Transparent;
			Font = Theme.UiFont;
			ForeColor = Theme.Text;
			Cursor = Cursors.Hand;
		}

		private Color FillColor()
		{
			if (!Enabled)
			{
				return Theme.PanelAlt;
			}
			if (Kind == ButtonKind.Primary)
			{
				return Pressing ? Theme.AccentPressed : (Hovering ? Theme.AccentHover : Theme.Accent);
			}
			if (Kind == ButtonKind.Ghost)
			{
				return Pressing ? Theme.PanelAlt : (Hovering ? Theme.PanelAlt : Color.Transparent);
			}
			return Pressing ? Theme.Border : (Hovering ? Theme.PanelAlt : Theme.Panel);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle bounds = ClientRectangle;
			if (bounds.Width < 2 || bounds.Height < 2)
			{
				return;
			}
			// 先把整个客户区刷成父容器的底色：否则透明按钮不会擦除上一次绘制的内容，
			// 重绘时文字会一层层叠上去（看起来像「重影」）。
			Color surface = (Parent != null) ? Parent.BackColor : BackColor;
			if (surface.A < 255)
			{
				surface = Theme.Bg;
			}
			using (SolidBrush clear = new SolidBrush(surface))
			{
				e.Graphics.FillRectangle(clear, bounds);
			}
			bounds.Width -= 1;
			bounds.Height -= 1;
			Color fill = FillColor();
			using (GraphicsPath path = Draw.Rounded(bounds, CornerRadius))
			{
				if (fill.A > 0)
				{
					using (SolidBrush brush = new SolidBrush(fill))
					{
						e.Graphics.FillPath(brush, path);
					}
				}
				if (Kind == ButtonKind.Secondary)
				{
					using (Pen pen = new Pen(Theme.Border, 1f))
					{
						e.Graphics.DrawPath(pen, path);
					}
				}
			}
			Color textColor;
			if (!Enabled)
			{
				textColor = Theme.Dim;
			}
			else if (Kind == ButtonKind.Primary)
			{
				textColor = Theme.OnAccent;
			}
			else
			{
				textColor = ForeColor;
			}
			TextRenderer.DrawText(e.Graphics, Text, Font, bounds, textColor,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
				| TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			Hovering = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			Hovering = false;
			Pressing = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				Pressing = true;
				Invalidate();
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			Pressing = false;
			Invalidate();
			base.OnMouseUp(e);
		}
	}

	/// <summary>圆角卡片容器。</summary>
	public class CardPanel : Panel
	{
		public int CornerRadius { get; set; }

		public bool Outline { get; set; }

		public CardPanel()
		{
			CornerRadius = 14;
			Outline = true;
			BackColor = Theme.Bg;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
				| ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle bounds = ClientRectangle;
			if (bounds.Width < 2 || bounds.Height < 2)
			{
				return;
			}
			bounds.Width -= 1;
			bounds.Height -= 1;
			using (GraphicsPath path = Draw.Rounded(bounds, CornerRadius))
			{
				using (SolidBrush brush = new SolidBrush(Theme.Panel))
				{
					e.Graphics.FillPath(brush, path);
				}
				if (Outline)
				{
					using (Pen pen = new Pen(Theme.Border, 1f))
					{
						e.Graphics.DrawPath(pen, path);
					}
				}
			}
		}
	}

	/// <summary>键位胶囊，用来显示「Alt」「Ctrl+Alt+Q」。</summary>
	public class KeyChip : Control
	{
		private bool Hovering;

		public KeyChip()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
				| ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
				| ControlStyles.SupportsTransparentBackColor, true);
			BackColor = Color.Transparent;
			Font = Theme.MonoFont;
			Cursor = Cursors.Hand;
			Height = 30;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle bounds = ClientRectangle;
			bounds.Width -= 1;
			bounds.Height -= 1;
			if (bounds.Width < 2 || bounds.Height < 2)
			{
				return;
			}
			using (GraphicsPath path = Draw.Rounded(bounds, 8))
			{
				using (SolidBrush brush = new SolidBrush(Hovering ? Theme.AccentSoft : Theme.Input))
				{
					e.Graphics.FillPath(brush, path);
				}
				using (Pen pen = new Pen(Hovering ? Theme.Accent : Theme.Border, 1f))
				{
					e.Graphics.DrawPath(pen, path);
				}
			}
			TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Theme.Accent,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			Hovering = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			Hovering = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnTextChanged(EventArgs e)
		{
			base.OnTextChanged(e);
			Invalidate();
		}

		/// <summary>按文字长度自动算宽度。</summary>
		public void AutoWidth()
		{
			int width = TextRenderer.MeasureText(Text, Font).Width + 26;
			Width = Math.Max(46, width);
		}
	}

	/// <summary>状态胶囊：小圆点 + 文字。</summary>
	public class StatusPill : Control
	{
		public Color DotColor { get; set; }

		public StatusPill()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
				| ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
				| ControlStyles.SupportsTransparentBackColor, true);
			BackColor = Color.Transparent;
			Font = Theme.SmallFont;
			DotColor = Theme.Dim;
			Height = 24;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle bounds = ClientRectangle;
			bounds.Width -= 1;
			bounds.Height -= 1;
			if (bounds.Width < 2 || bounds.Height < 2)
			{
				return;
			}
			using (GraphicsPath path = Draw.Rounded(bounds, bounds.Height / 2))
			{
				using (SolidBrush brush = new SolidBrush(Theme.PanelAlt))
				{
					e.Graphics.FillPath(brush, path);
				}
				using (Pen pen = new Pen(Theme.Border, 1f))
				{
					e.Graphics.DrawPath(pen, path);
				}
			}
			int cy = Height / 2;
			using (SolidBrush dot = new SolidBrush(DotColor))
			{
				e.Graphics.FillEllipse(dot, 10, cy - 3, 6, 6);
			}
			Rectangle textBounds = new Rectangle(22, 0, Math.Max(0, Width - 30), Height);
			TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Theme.Text,
				TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		}

		public void AutoWidth()
		{
			int width = TextRenderer.MeasureText(Text, Font).Width + 34;
			Width = Math.Max(70, width);
		}
	}

	public enum BadgeKind
	{
		/// <summary>框选截图：虚线框 + 四角。</summary>
		Shot,
		/// <summary>划词：文字行 + 竖线光标。</summary>
		Text,
		/// <summary>浏览器窗口。</summary>
		Browser,
		/// <summary>闪电（品牌标记）。</summary>
		Bolt
	}

	/// <summary>圆角图标块，放在卡片左上角做视觉锚点，全部用 GDI 画，不依赖图片资源。</summary>
	public class IconBadge : Control
	{
		public BadgeKind Kind { get; set; }

		public IconBadge()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
				| ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
				| ControlStyles.SupportsTransparentBackColor, true);
			BackColor = Color.Transparent;
			Size = new Size(30, 30);
			Kind = BadgeKind.Shot;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle bounds = ClientRectangle;
			if (bounds.Width < 8 || bounds.Height < 8)
			{
				return;
			}
			Rectangle box = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);
			if (Kind == BadgeKind.Bolt)
			{
				using (LinearGradientBrush fill = new LinearGradientBrush(box, Theme.Accent, Theme.Accent2, 45f))
				using (GraphicsPath path = Draw.Rounded(box, 9))
				{
					g.FillPath(fill, path);
				}
				float sx = bounds.Width / 30f;
				float sy = bounds.Height / 30f;
				PointF[] bolt = new PointF[]
				{
					new PointF(17f * sx, 6f * sy),
					new PointF(10f * sx, 16f * sy),
					new PointF(14f * sx, 16f * sy),
					new PointF(12.5f * sx, 24f * sy),
					new PointF(20f * sx, 13f * sy),
					new PointF(15.6f * sx, 13f * sy)
				};
				using (SolidBrush white = new SolidBrush(Color.White))
				{
					g.FillPolygon(white, bolt);
				}
				return;
			}
			using (GraphicsPath path = Draw.Rounded(box, 9))
			{
				using (SolidBrush fill = new SolidBrush(Theme.AccentSoft))
				{
					g.FillPath(fill, path);
				}
			}
			using (Pen pen = new Pen(Theme.Accent, 1.6f))
			{
				float w = bounds.Width;
				float h = bounds.Height;
				if (Kind == BadgeKind.Shot)
				{
					// 取景框四角
					float inset = w * 0.28f;
					float len = w * 0.16f;
					pen.StartCap = LineCap.Round;
					pen.EndCap = LineCap.Round;
					g.DrawLines(pen, new PointF[] { new PointF(inset, inset + len), new PointF(inset, inset), new PointF(inset + len, inset) });
					g.DrawLines(pen, new PointF[] { new PointF(w - inset - len, inset), new PointF(w - inset, inset), new PointF(w - inset, inset + len) });
					g.DrawLines(pen, new PointF[] { new PointF(inset, h - inset - len), new PointF(inset, h - inset), new PointF(inset + len, h - inset) });
					g.DrawLines(pen, new PointF[] { new PointF(w - inset - len, h - inset), new PointF(w - inset, h - inset), new PointF(w - inset, h - inset - len) });
					using (SolidBrush dot = new SolidBrush(Theme.Accent))
					{
						g.FillEllipse(dot, w / 2 - 1.5f, h / 2 - 1.5f, 3f, 3f);
					}
				}
				else if (Kind == BadgeKind.Text)
				{
					pen.StartCap = LineCap.Round;
					pen.EndCap = LineCap.Round;
					float left = w * 0.28f;
					g.DrawLine(pen, left, h * 0.34f, w * 0.66f, h * 0.34f);
					g.DrawLine(pen, left, h * 0.52f, w * 0.58f, h * 0.52f);
					g.DrawLine(pen, left, h * 0.70f, w * 0.62f, h * 0.70f);
					using (SolidBrush caret = new SolidBrush(Theme.Accent))
					{
						g.FillRectangle(caret, w * 0.72f, h * 0.30f, 1.8f, h * 0.42f);
					}
				}
				else
				{
					float l = w * 0.26f;
					float r = w * 0.74f;
					float t = h * 0.24f;
					float b = h * 0.76f;
					using (GraphicsPath win = Draw.Rounded(Rectangle.Round(new RectangleF(l, t, r - l, b - t)), 3))
					{
						g.DrawPath(pen, win);
					}
					g.DrawLine(pen, l, t + (b - t) * 0.3f, r, t + (b - t) * 0.3f);
				}
			}
		}
	}
}
