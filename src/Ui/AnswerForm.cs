using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SnapAsk.Core;

namespace SnapAsk.Ui
{
	/// <summary>
	/// 浮在屏幕上的答案卡片：出现在框选区域旁边，边生成边显示，
	/// 可拖动、可复制全文、可在这个对话里继续追问。
	/// </summary>
	public sealed class AnswerForm : Form
	{
		private const int MinAutoHeight = 240;

		private const int AutoHeightScreenGap = 32;

		private readonly RichTextBox Body;

		private readonly Timer ResizeTimer;

		private int TargetHeight;

		private int TargetTop;

		private int PreferredTop;

		private bool HasPreferredTop;

		private readonly StatusPill Status;

		private readonly FlatButton CopyButton;

		private readonly FlatButton PinButton;

		private readonly FlatButton CloseButton;

		private readonly TextBox FollowBox;

		private readonly FlatButton FollowButton;

		private readonly Panel HeaderPanel;

		private readonly Panel FooterPanel;

		private readonly Label CaptionLabel;

		private string RawText = "";

		/// <summary>已经画进正文的文本，用来判断能不能只追加新增片段。</summary>
		private string RenderedText = "";

		private bool Dragging;

		private Point DragOrigin;

		private bool Pinned = true;

		/// <summary>用户手动点过「×」之后不再自动弹出。</summary>
		private bool UserClosed;

		/// <summary>用户在卡片里追问。</summary>
		public event Action<string> FollowUp;

		public AnswerForm()
		{
			FormBorderStyle = FormBorderStyle.None;
			StartPosition = FormStartPosition.Manual;
			ShowInTaskbar = false;
			TopMost = true;
			Size = new Size(470, 340);
			BackColor = Theme.Panel;
			DoubleBuffered = true;
			KeyPreview = true;
			SetStyle(ControlStyles.ResizeRedraw, true);

			// 答案变多/变少时按内容高度平滑缩放窗口
			ResizeTimer = new Timer();
			ResizeTimer.Interval = 16;
			ResizeTimer.Tick += delegate { OnResizeTick(); };

			Panel header = new Panel();
			header.Dock = DockStyle.Top;
			header.Height = 46;
			header.BackColor = Theme.Panel;
			header.MouseDown += HeaderMouseDown;
			header.MouseMove += HeaderMouseMove;
			header.MouseUp += HeaderMouseUp;
			Controls.Add(header);

			Label title = new Label();
			title.Text = "答案";
			title.Font = Theme.UiFontBold;
			title.ForeColor = Theme.Text;
			title.AutoSize = false;
			title.Location = new Point(18, 13);
			title.Size = new Size(44, 20);
			title.MouseDown += HeaderMouseDown;
			title.MouseMove += HeaderMouseMove;
			title.MouseUp += HeaderMouseUp;
			header.Controls.Add(title);

			Status = new StatusPill();
			Status.Text = "正在提问";
			Status.DotColor = Theme.Accent;
			Status.Height = 22;
			Status.AutoWidth();
			Status.Location = new Point(64, 12);
			header.Controls.Add(Status);

			CloseButton = new FlatButton();
			CloseButton.Text = "×";
			CloseButton.Kind = ButtonKind.Ghost;
			CloseButton.Size = new Size(28, 26);
			CloseButton.Location = new Point(0, 10);
			CloseButton.Font = new Font(Theme.UiFont.FontFamily, 12f, FontStyle.Regular);
			CloseButton.Click += delegate { UserClosed = true; Hide(); };
			header.Controls.Add(CloseButton);

			PinButton = new FlatButton();
			PinButton.Text = "置顶";
			PinButton.Kind = ButtonKind.Ghost;
			PinButton.Size = new Size(46, 26);
			PinButton.Location = new Point(0, 10);
			PinButton.Font = Theme.SmallFont;
			PinButton.Click += delegate
			{
				Pinned = !Pinned;
				TopMost = Pinned;
				PinButton.Text = Pinned ? "置顶" : "普通";
				PinButton.Invalidate();
			};
			header.Controls.Add(PinButton);

			CopyButton = new FlatButton();
			CopyButton.Text = "复制";
			CopyButton.Kind = ButtonKind.Ghost;
			CopyButton.Size = new Size(46, 26);
			CopyButton.Location = new Point(0, 10);
			CopyButton.Font = Theme.SmallFont;
			CopyButton.Click += delegate
			{
				try
				{
					if (!string.IsNullOrEmpty(RawText))
					{
						Clipboard.SetText(RawText);
					}
				}
				catch
				{
				}
			};
			header.Controls.Add(CopyButton);

			HeaderPanel = header;
			header.Resize += delegate { LayoutHeaderButtons(); };

			Panel footer = new Panel();
			footer.Dock = DockStyle.Bottom;
			footer.Height = 46;
			footer.BackColor = Theme.Panel;
			Controls.Add(footer);

			FollowBox = new TextBox();
			FollowBox.BorderStyle = BorderStyle.FixedSingle;
			FollowBox.Font = Theme.UiFont;
			FollowBox.BackColor = Theme.Input;
			FollowBox.ForeColor = Theme.Text;
			FollowBox.Location = new Point(16, 9);
			FollowBox.Width = 340;
			FollowBox.KeyDown += delegate(object sender, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					SendFollowUp();
				}
			};
			footer.Controls.Add(FollowBox);

			FollowButton = new FlatButton();
			FollowButton.Text = "追问";
			FollowButton.Kind = ButtonKind.Primary;
			FollowButton.Size = new Size(64, 26);
			FollowButton.Location = new Point(0, 8);
			FollowButton.Click += delegate { SendFollowUp(); };
			footer.Controls.Add(FollowButton);

			FooterPanel = footer;
			footer.Resize += delegate { LayoutFooter(); };

			CaptionLabel = new Label();
			CaptionLabel.Dock = DockStyle.Top;
			CaptionLabel.Height = 24;
			CaptionLabel.Padding = new Padding(16, 0, 16, 0);
			CaptionLabel.TextAlign = ContentAlignment.MiddleLeft;
			CaptionLabel.Font = Theme.SmallFont;
			CaptionLabel.ForeColor = Theme.Dim;
			CaptionLabel.BackColor = Theme.Panel;
			CaptionLabel.AutoSize = false;
			CaptionLabel.AutoEllipsis = true;
			CaptionLabel.Text = "";
			Controls.Add(CaptionLabel);

			Body = new RichTextBox();
			Body.Dock = DockStyle.Fill;
			Body.BorderStyle = BorderStyle.None;
			Body.BackColor = Theme.Panel;
			Body.ForeColor = Theme.Answer;
			Body.Font = Theme.UiFont;
			Body.ReadOnly = true;
			Body.DetectUrls = false;
			Body.ScrollBars = RichTextBoxScrollBars.Vertical;
			Body.Margin = new Padding(0);
			Controls.Add(Body);
			Body.BringToFront();

			// 停靠顺序由 z-order 决定：先排 header，再排题目摘要和底部，最后正文填满剩余
			Controls.SetChildIndex(Body, 0);
			Controls.SetChildIndex(FooterPanel, 1);
			Controls.SetChildIndex(CaptionLabel, 2);
			Controls.SetChildIndex(HeaderPanel, 3);
			Padding = new Padding(16, 0, 16, 0);
			LayoutHeaderButtons();
			LayoutFooter();
		}

		private void LayoutHeaderButtons()
		{
			if (HeaderPanel == null || CloseButton == null)
			{
				return;
			}
			int right = HeaderPanel.Width - 6;
			CloseButton.Left = Math.Max(0, right - CloseButton.Width);
			PinButton.Left = Math.Max(0, CloseButton.Left - PinButton.Width - 2);
			CopyButton.Left = Math.Max(0, PinButton.Left - CopyButton.Width - 2);
		}

		private void LayoutFooter()
		{
			if (FooterPanel == null || FollowButton == null)
			{
				return;
			}
			FollowButton.Left = Math.Max(0, FooterPanel.Width - FollowButton.Width - 6);
			FollowBox.Left = 4;
			FollowBox.Top = 9;
			FollowBox.Height = 26;
			FollowBox.Width = Math.Max(80, FollowButton.Left - FollowBox.Left - 8);
		}

		private void HeaderMouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left)
			{
				return;
			}
			// 拖动时先停下自动缩放，否则窗口会一边被拖一边自己移动
			ResizeTimer.Stop();
			Dragging = true;
			DragOrigin = new Point(e.X, e.Y);
			Point screen = ((Control)sender).PointToScreen(DragOrigin);
			DragOrigin = new Point(screen.X - Left, screen.Y - Top);
		}

		private void HeaderMouseMove(object sender, MouseEventArgs e)
		{
			if (!Dragging)
			{
				return;
			}
			Point screen = ((Control)sender).PointToScreen(new Point(e.X, e.Y));
			Left = screen.X - DragOrigin.X;
			Top = screen.Y - DragOrigin.Y;
		}

		private void HeaderMouseUp(object sender, MouseEventArgs e)
		{
			Dragging = false;
			// 记住用户拖到的新位置，之后自动缩放以这里为锚点
			PreferredTop = Top;
			HasPreferredTop = true;
			UpdateAutoHeight();
		}

		private void SendFollowUp()
		{
			string text = (FollowBox.Text ?? "").Trim();
			if (text.Length == 0)
			{
				return;
			}
			FollowBox.Clear();
			Action<string> handler = FollowUp;
			if (handler != null)
			{
				handler(text);
			}
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (e.KeyCode == Keys.Escape)
			{
				UserClosed = true;
				Hide();
			}
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			if (!HasPreferredTop)
			{
				PreferredTop = Top;
				HasPreferredTop = true;
			}
			UpdateAutoHeight();
		}

		protected override void OnVisibleChanged(EventArgs e)
		{
			base.OnVisibleChanged(e);
			if (ResizeTimer == null)
			{
				return;
			}
			if (!Visible)
			{
				ResizeTimer.Stop();
			}
			else
			{
				UpdateAutoHeight();
			}
		}

		/// <summary>
		/// 按答案正文的实际高度调整窗口：答案多就长高，答案少就缩回，
		/// 高度用定时器逐帧靠近目标值，形成平滑动画。
		/// 超过屏幕可用高度时不再长高，正文内部改为滚动。
		/// </summary>
		private void UpdateAutoHeight()
		{
			if (Body == null || !Body.IsHandleCreated || Body.ClientSize.Height <= 0)
			{
				return;
			}
			int chrome = ClientSize.Height - Body.ClientSize.Height;
			if (chrome < 0)
			{
				chrome = 0;
			}
			int content = MeasureContentHeight();
			Rectangle area = WorkingArea();
			int maxHeight = area.Height - AutoHeightScreenGap;
			if (maxHeight < MinAutoHeight)
			{
				// 极端小的可用桌面：优先保证还能看见，不强行撑到最小高度
				maxHeight = Math.Max(160, area.Height - 16);
			}
			int target = chrome + content + 12;
			if (target < MinAutoHeight)
			{
				target = MinAutoHeight;
			}
			if (target > maxHeight)
			{
				target = maxHeight;
			}

			// 以用户放置/拖动的位置为锚点；长高后放不下就让整个窗口向上让位
			int top = HasPreferredTop ? PreferredTop : Top;
			if (top + target > area.Bottom - 8)
			{
				top = area.Bottom - 8 - target;
			}
			if (top < area.Top + 8)
			{
				top = area.Top + 8;
			}

			TargetHeight = target;
			TargetTop = top;
			if (!Visible || ResizeTimer.Enabled)
			{
				return;
			}
			if (Math.Abs(Height - TargetHeight) <= 1 && Math.Abs(Top - TargetTop) <= 1)
			{
				return;
			}
			ResizeTimer.Start();
		}

		/// <summary>正文实际需要的高度（和当前滚动位置无关）。</summary>
		private int MeasureContentHeight()
		{
			if (Body.TextLength == 0)
			{
				return 0;
			}
			try
			{
				Point first = Body.GetPositionFromCharIndex(0);
				Point last = Body.GetPositionFromCharIndex(Body.TextLength - 1);
				int lineHeight = Math.Max(1, Body.Font.Height);
				int height = last.Y - first.Y + lineHeight;
				return Math.Max(0, height);
			}
			catch
			{
				return Body.ClientSize.Height;
			}
		}

		private Rectangle WorkingArea()
		{
			try
			{
				return Screen.FromControl(this).WorkingArea;
			}
			catch
			{
				return Screen.PrimaryScreen.WorkingArea;
			}
		}

		private void OnResizeTick()
		{
			int diffHeight = TargetHeight - Height;
			int diffTop = TargetTop - Top;
			if (Math.Abs(diffHeight) <= 1 && Math.Abs(diffTop) <= 1)
			{
				ResizeTimer.Stop();
				SetBounds(Left, TargetTop, Width, TargetHeight);
				return;
			}
			int newHeight = Height + StepTo(diffHeight);
			int newTop = Top + StepTo(diffTop);
			if (newHeight < 1)
			{
				newHeight = 1;
			}
			SetBounds(Left, newTop, Width, newHeight);
		}

		/// <summary>每次靠近目标约 1/4，接近时至少走 2px，避免动画尾巴太慢。</summary>
		private static int StepTo(int diff)
		{
			if (diff == 0)
			{
				return 0;
			}
			int step = Math.Abs(diff) / 4;
			if (step < 2)
			{
				step = 2;
			}
			if (step > 96)
			{
				step = 96;
			}
			return (diff < 0) ? -step : step;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && ResizeTimer != null)
			{
				try
				{
					ResizeTimer.Stop();
					ResizeTimer.Dispose();
				}
				catch
				{
				}
			}
			base.Dispose(disposing);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Rectangle bounds = ClientRectangle;
			bounds.Width -= 1;
			bounds.Height -= 1;
			if (bounds.Width < 4 || bounds.Height < 4)
			{
				return;
			}
			using (GraphicsPath path = Draw.Rounded(bounds, 14))
			{
				using (Pen pen = new Pen(Theme.Border, 1f))
				{
					e.Graphics.DrawPath(pen, path);
				}
			}
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (ClientRectangle.Width < 4 || ClientRectangle.Height < 4)
			{
				return;
			}
			try
			{
				using (GraphicsPath path = Draw.Rounded(ClientRectangle, 14))
				{
					Region old = Region;
					Region = new Region(path);
					if (old != null)
					{
						old.Dispose();
					}
				}
			}
			catch
			{
			}
		}

		/// <summary>设置「默认是否置顶」，由主界面的设置决定。</summary>
		public void SetDefaultPin(bool onTop)
		{
			Pinned = onTop;
			TopMost = onTop;
			PinButton.Text = onTop ? "置顶" : "普通";
			PinButton.Invalidate();
		}

		/// <summary>新问题开始：清空正文并放进题目摘要。</summary>
		public void BeginQuestion(Question question)
		{
			BeginQuestion(question, true);
		}

		/// <summary>show = false 时只更新内容、不弹窗，避免「自动弹出结果窗」关闭时闪一下。</summary>
		public void BeginQuestion(Question question, bool show)
		{
			RawText = "";
			UserClosed = false;
			TopMost = Pinned;
			CaptionLabel.Text = "题目：" + ((question == null) ? "" : question.Caption);
			RenderBody("");
			SetStatus("正在提问", Theme.Accent);
			if (show)
			{
				ShowIfHidden();
			}
		}

		private void ShowIfHidden()
		{
			if (!Visible)
			{
				Show();
			}
			BringToFront();
		}

		/// <summary>
		/// 有新内容到达时保证卡片看得见。
		/// 主窗口最小化时答案也可能到，这时卡片还藏着就等于「内容没同步过来」。
		/// 用户手动点 × 关掉之后不再自动弹出。
		/// </summary>
		public void EnsureVisible()
		{
			if (InvokeRequired)
			{
				BeginInvoke((Action)delegate { EnsureVisible(); });
				return;
			}
			if (UserClosed)
			{
				return;
			}
			if (!Visible)
			{
				// 已经显示时不要再反复 BringToFront：流式回答每 700ms 调一次，
				// 反复提升窗口会打断用户当前的操作。
				TopMost = Pinned;
				Show();
				BringToFront();
			}
		}

		public void SetStatus(string text, Color dot)
		{
			if (InvokeRequired)
			{
				BeginInvoke((Action)delegate { SetStatus(text, dot); });
				return;
			}
			Status.Text = text;
			Status.DotColor = dot;
			Status.AutoWidth();
			Status.Invalidate();
		}

		/// <summary>流式更新答案正文（整段替换，保持用户滚动位置不变）。</summary>
		public void UpdateAnswer(string text)
		{
			if (InvokeRequired)
			{
				BeginInvoke((Action)delegate { UpdateAnswer(text); });
				return;
			}
			if (text == null)
			{
				text = "";
			}
			RawText = text;
			RenderBody(text);
		}

		private void RenderBody(string answer)
		{
			if (answer == null)
			{
				answer = "";
			}
			bool atBottom = IsAtBottom();
			int firstVisible = 0;
			if (!atBottom && Body.TextLength > 0)
			{
				try
				{
					firstVisible = Body.GetCharIndexFromPosition(new Point(1, 1));
				}
				catch
				{
					firstVisible = 0;
				}
			}

			// 流式回答绝大多数时候只是在末尾追加：只补新增的那一段，
			// 避免每个片段都把整篇正文清空重排（长答案会明显闪烁、越来越慢）。
			bool appendOnly = RenderedText.Length > 0
				&& answer.Length >= RenderedText.Length
				&& string.CompareOrdinal(answer, 0, RenderedText, 0, RenderedText.Length) == 0;

			Body.SuspendLayout();
			try
			{
				if (!appendOnly)
				{
					Body.Clear();
					RenderedText = "";
				}
				if (answer.Length > RenderedText.Length)
				{
					Body.SelectionStart = Body.TextLength;
					Body.SelectionLength = 0;
					Body.SelectionColor = Theme.Answer;
					Body.SelectionFont = Theme.UiFont;
					Body.AppendText(answer.Substring(RenderedText.Length));
				}
				RenderedText = answer;
			}
			finally
			{
				Body.ResumeLayout();
			}

			try
			{
				bool fits = ContentFits();
				if (atBottom)
				{
					Body.SelectionStart = Body.TextLength;
					Body.SelectionLength = 0;
					Body.ScrollToCaret();
				}
				if (fits)
				{
					// 正文没超出可视区时回到顶部，避免短答案被滚到中间
					Body.SelectionStart = 0;
					Body.SelectionLength = 0;
					Body.ScrollToCaret();
				}
				else if (!atBottom)
				{
					// 用户往回翻了：保持原来的阅读位置，别把他拽回底部
					Body.SelectionStart = Math.Min(firstVisible, Body.TextLength);
					Body.SelectionLength = 0;
					Body.ScrollToCaret();
				}
			}
			catch
			{
			}
			UpdateAutoHeight();
		}

		private bool IsAtBottom()
		{
			try
			{
				if (Body.TextLength == 0)
				{
					return true;
				}
				Point last = Body.GetPositionFromCharIndex(Body.TextLength - 1);
				return last.Y <= Body.ClientSize.Height;
			}
			catch
			{
				return true;
			}
		}

		/// <summary>正文总高度是否没超过可视区（和当前滚动位置无关）。</summary>
		private bool ContentFits()
		{
			try
			{
				if (Body.TextLength == 0)
				{
					return true;
				}
				Point first = Body.GetPositionFromCharIndex(0);
				Point last = Body.GetPositionFromCharIndex(Body.TextLength - 1);
				if (first.Y < 0 || last.Y < 0)
				{
					return false;
				}
				int lineHeight = Math.Max(1, Body.Font.Height);
				return (last.Y - first.Y + lineHeight) <= Body.ClientSize.Height;
			}
			catch
			{
				return false;
			}
		}

		public void SetQuestionCaption(string caption)
		{
			CaptionLabel.Text = "题目：" + (caption ?? "");
		}

		/// <summary>仅供界面自查使用：当前正文文本。</summary>
		public string BodyText
		{
			get
			{
				return Body.Text;
			}
		}

		public void Finish()
		{
			SetStatus("已完成", Theme.Success);
			UpdateAutoHeight();
		}

		public void ShowFailure(string message)
		{
			if (InvokeRequired)
			{
				BeginInvoke((Action)delegate { ShowFailure(message); });
				return;
			}
			SetStatus("出错了", Theme.Error);
			RawText = message;
			RenderBody(message);
			ShowIfHidden();
			UpdateAutoHeight();
		}

		/// <summary>动画中的目标高度；动画还没开始时用当前高度。</summary>
		private int LayoutHeight()
		{
			return (TargetHeight > 0) ? TargetHeight : Height;
		}

		/// <summary>把卡片放到框选区域右下角，越界就夹回屏幕内。</summary>
		public void PlaceNear(Rectangle screenRect)
		{
			Rectangle area = Screen.FromPoint(new Point(screenRect.Left, screenRect.Top)).WorkingArea;
			int height = LayoutHeight();
			int x = screenRect.Right + 12;
			int y = screenRect.Top;
			if (x + Width > area.Right)
			{
				x = screenRect.Left - Width - 12;
			}
			if (x < area.Left)
			{
				x = area.Left + 12;
			}
			if (y + height > area.Bottom)
			{
				y = area.Bottom - height - 12;
			}
			if (y < area.Top)
			{
				y = area.Top + 12;
			}
			Location = new Point(x, y);
			PreferredTop = y;
			HasPreferredTop = true;
			UpdateAutoHeight();
		}

		public void PlaceNearCursor()
		{
			Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
			int height = LayoutHeight();
			int x = Cursor.Position.X + 16;
			int y = Cursor.Position.Y + 16;
			if (x + Width > area.Right)
			{
				x = area.Right - Width - 12;
			}
			if (y + height > area.Bottom)
			{
				y = area.Bottom - height - 12;
			}
			y = Math.Max(area.Top + 12, y);
			Location = new Point(Math.Max(area.Left + 12, x), y);
			PreferredTop = y;
			HasPreferredTop = true;
			UpdateAutoHeight();
		}
	}
}
