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
		private readonly RichTextBox Body;

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
			RawText = "";
			UserClosed = false;
			TopMost = Pinned;
			CaptionLabel.Text = "题目：" + ((question == null) ? "" : question.Caption);
			RenderBody("");
			SetStatus("正在提问", Theme.Accent);
			ShowIfHidden();
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
				Show();
			}
			TopMost = Pinned;
			BringToFront();
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
			bool atBottom = true;
			try
			{
				Point last = Body.GetPositionFromCharIndex(Body.TextLength);
				atBottom = last.Y <= Body.ClientSize.Height;
			}
			catch
			{
			}
			Body.SuspendLayout();
			Body.Clear();
			Body.SelectionColor = Theme.Answer;
			Body.SelectionFont = Theme.UiFont;
			Body.AppendText(answer);
			Body.ResumeLayout();
			if (atBottom)
			{
				Body.SelectionStart = Body.TextLength;
				Body.SelectionLength = 0;
				Body.ScrollToCaret();
			}
			// 正文没超出可视区时回到顶部，避免短答案被滚到中间
			try
			{
				if (Body.GetPositionFromCharIndex(Body.TextLength).Y <= Body.ClientSize.Height)
				{
					Body.SelectionStart = 0;
					Body.SelectionLength = 0;
					Body.ScrollToCaret();
				}
			}
			catch
			{
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
		}

		/// <summary>把卡片放到框选区域右下角，越界就夹回屏幕内。</summary>
		public void PlaceNear(Rectangle screenRect)
		{
			Rectangle area = Screen.FromPoint(new Point(screenRect.Left, screenRect.Top)).WorkingArea;
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
			if (y + Height > area.Bottom)
			{
				y = area.Bottom - Height - 12;
			}
			if (y < area.Top)
			{
				y = area.Top + 12;
			}
			Location = new Point(x, y);
		}

		public void PlaceNearCursor()
		{
			Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
			int x = Cursor.Position.X + 16;
			int y = Cursor.Position.Y + 16;
			if (x + Width > area.Right)
			{
				x = area.Right - Width - 12;
			}
			if (y + Height > area.Bottom)
			{
				y = area.Bottom - Height - 12;
			}
			Location = new Point(Math.Max(area.Left + 12, x), Math.Max(area.Top + 12, y));
		}
	}
}
