using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SnapAsk.Core;
using SnapAsk.Engines;
using SnapAsk.Util;

namespace SnapAsk.Ui
{
	/// <summary>
	/// 主界面：左边嵌入式 DeepSeek 网页，右边快捷键卡片 + 答题记录 + 输入框。
	/// 两条出题链路：
	///   按住键 → 冻结全屏 → 框选 → 截图 + 提示词 发给网页版；
	///   选中文字后点按键 → Ctrl+C 取词 → 套进模板 → 发给网页版。
	/// 答案边说边显示在浮动卡片里，同时写进右侧记录。
	/// </summary>
	public sealed class MainForm : Form
	{
		private Settings S;

		private HotkeyHook Hook;

		private IChatEngine Engine;

		private AnswerForm Answer;

		private CaptureOverlay Overlay;

		private Bitmap Frozen;

		private NotifyIcon Tray;

		private BrowserEmbedder Embedder;

		private StatusPill Status;

		private FlatButton BtnSettings;

		private FlatButton BtnEmbed;

		private FlatButton BtnSend;

		private FlatButton BtnShot;

		private FlatButton BtnTheme;

		private KeyChip ShotChip;

		private KeyChip TextChip;

		private RichTextBox Transcript;

		private TextBox Input;

		private Label InputHint;

		private Panel HeaderPanel;

		private Panel FooterPanel;

		private Panel CardsPanel;

		private Panel InputPanel;

		private CardPanel ShotCard;

		private CardPanel TextCard;

		private Panel BrowserHost;

		private Panel BrowserPanel;

		private Panel BrowserPlaceholder;

		private Splitter BrowserSplitter;

		private readonly Queue<Question> Pending = new Queue<Question>();

		private bool Running;

		private CancellationTokenSource Cts = new CancellationTokenSource();

		private bool HasCaptured;

		private bool DetachedByMinimize;

		private bool Quitting;

		private readonly bool DemoMode;

		private readonly bool AskDemo;

		public MainForm()
			: this(false)
		{
		}

		/// <summary>demo = true 时启动后自动弹一张示例答案卡（仅用于界面自查）。</summary>
		public MainForm(bool demo)
			: this(demo, false)
		{
		}

		/// <summary>askDemo = true 时启动 4 秒后自动发一个真实问题，用来验证「最小化时小窗同步」。</summary>
		public MainForm(bool demo, bool askDemo)
		{
			DemoMode = demo;
			AskDemo = askDemo;
			S = Settings.Load();
			Text = "SnapAsk 截题助手";
			StartPosition = FormStartPosition.CenterScreen;
			ClientSize = new Size(1120, 700);
			MinimumSize = new Size(880, 560);
			BackColor = Theme.Bg;
			Font = Theme.UiFont;
			Icon = LoadAppIcon();
			KeyPreview = true;

			BuildUi();
			ApplySettingsToUi();
			InstallHook();
			BuildTray();
			ScreenCapture.Prune(60);
			Log.Info("SnapAsk 启动：截图键 " + S.ShotHoldKey + " · 划词键 " + S.TextHotkey
				+ " · 嵌入浏览器 " + (S.EmbedBrowser ? "开" : "关"));
		}

		private static Icon LoadAppIcon()
		{
			try
			{
				Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
				if (icon != null)
				{
					return icon;
				}
			}
			catch
			{
			}
			return SystemIcons.Application;
		}

		// ================================================================ 界面搭建

		private void BuildUi()
		{
			Panel header = BuildHeader();
			Panel footer = BuildFooter();
			Panel body = BuildBody();

			Controls.Add(body);
			Controls.Add(footer);
			Controls.Add(header);
			// 停靠顺序：先 header / footer，body 最后填满中间
			Controls.SetChildIndex(body, 0);
			Controls.SetChildIndex(footer, 1);
			Controls.SetChildIndex(header, 2);

			LayoutHeader();
			LayoutFooter();
			LayoutCards();
			LayoutInput();
			LayoutBrowser();
		}

		private Panel BuildHeader()
		{
			HeaderPanel = new Panel();
			HeaderPanel.Dock = DockStyle.Top;
			HeaderPanel.Height = 78;
			HeaderPanel.BackColor = Theme.Bg;

			IconBadge mark = new IconBadge();
			mark.Kind = BadgeKind.Bolt;
			mark.Size = new Size(34, 34);
			mark.Location = new Point(22, 22);
			HeaderPanel.Controls.Add(mark);

			Label title = new Label();
			title.Text = "SnapAsk";
			title.Font = Theme.TitleFont;
			title.ForeColor = Theme.Text;
			title.AutoSize = true;
			title.Location = new Point(66, 15);
			HeaderPanel.Controls.Add(title);

			Label sub = new Label();
			sub.Text = "按住键框选题目 · 松手自动问 DeepSeek";
			sub.Font = Theme.SmallFont;
			sub.ForeColor = Theme.Dim;
			sub.AutoSize = true;
			sub.Location = new Point(68, 44);
			HeaderPanel.Controls.Add(sub);

			Status = new StatusPill();
			Status.Text = "未连接";
			Status.DotColor = Theme.Dim;
			Status.AutoWidth();
			Status.Height = 26;
			Status.Location = new Point(0, 24);
			HeaderPanel.Controls.Add(Status);

			BtnEmbed = new FlatButton();
			BtnEmbed.Text = "嵌入浏览器";
			BtnEmbed.Kind = ButtonKind.Secondary;
			BtnEmbed.Size = new Size(100, 30);
			BtnEmbed.Location = new Point(0, 22);
			BtnEmbed.Click += delegate { ToggleEmbed(); };
			HeaderPanel.Controls.Add(BtnEmbed);

			BtnSettings = new FlatButton();
			BtnSettings.Text = "设置";
			BtnSettings.Kind = ButtonKind.Ghost;
			BtnSettings.Size = new Size(62, 30);
			BtnSettings.Location = new Point(0, 22);
			BtnSettings.Click += delegate { OpenSettings(); };
			HeaderPanel.Controls.Add(BtnSettings);

			HeaderPanel.Resize += delegate { LayoutHeader(); };
			return HeaderPanel;
		}

		private Panel BuildFooter()
		{
			FooterPanel = new Panel();
			FooterPanel.Dock = DockStyle.Bottom;
			FooterPanel.Height = 46;
			FooterPanel.BackColor = Theme.Bg;
			return FooterPanel;
		}

		private Panel BuildBody()
		{
			Panel body = new Panel();
			body.Dock = DockStyle.Fill;
			body.BackColor = Theme.Bg;
			body.Padding = new Padding(22, 0, 22, 10);

			Panel side = new Panel();
			side.Dock = DockStyle.Fill;
			side.BackColor = Theme.Bg;
			body.Controls.Add(side);

			BuildBrowserHost(body);

			TableLayoutPanel grid = new TableLayoutPanel();
			grid.Dock = DockStyle.Fill;
			grid.ColumnCount = 1;
			grid.RowCount = 3;
			grid.BackColor = Theme.Bg;
			grid.Padding = new Padding(0, 0, 0, 0);
			grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 124f));
			grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));
			side.Controls.Add(grid);

			// ---- 快捷键卡片 ----
			CardsPanel = new Panel();
			CardsPanel.Dock = DockStyle.Fill;
			CardsPanel.BackColor = Theme.Bg;
			grid.Controls.Add(CardsPanel, 0, 0);

			ShotCard = BuildHotkeyCard(BadgeKind.Shot, "截图出题", "按住不放 → 拖框 → 松手发送", out ShotChip);
			CardsPanel.Controls.Add(ShotCard);
			ShotCard.Resize += delegate { LayoutShotCard(); };

			TextCard = BuildHotkeyCard(BadgeKind.Text, "划词出题", "选中一行文字后点按一下", out TextChip);
			CardsPanel.Controls.Add(TextCard);
			TextCard.Resize += delegate { LayoutTextCard(); };

			CardsPanel.Resize += delegate { LayoutCards(); };

			// ---- 答题记录 ----
			CardPanel logCard = new CardPanel();
			logCard.Dock = DockStyle.Fill;
			logCard.CornerRadius = 14;
			logCard.Padding = new Padding(18, 12, 18, 12);
			grid.Controls.Add(logCard, 0, 1);

			Panel logHead = new Panel();
			logHead.Dock = DockStyle.Top;
			logHead.Height = 28;
			logHead.BackColor = Theme.Panel;
			logCard.Controls.Add(logHead);

			Label logTitle = new Label();
			logTitle.Text = "答题记录";
			logTitle.Font = Theme.UiFontBold;
			logTitle.ForeColor = Theme.Text;
			logTitle.AutoSize = true;
			logTitle.Location = new Point(0, 5);
			logHead.Controls.Add(logTitle);

			FlatButton clear = new FlatButton();
			clear.Text = "清空";
			clear.Kind = ButtonKind.Ghost;
			clear.Font = Theme.SmallFont;
			clear.Size = new Size(52, 24);
			clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			clear.Location = new Point(logHead.Width - 52, 2);
			clear.Click += delegate
			{
				Transcript.Clear();
				AppendHint();
			};
			logHead.Controls.Add(clear);
			logHead.Resize += delegate { clear.Left = logHead.Width - clear.Width; };

			Transcript = new RichTextBox();
			Transcript.Dock = DockStyle.Fill;
			Transcript.BorderStyle = BorderStyle.None;
			Transcript.BackColor = Theme.Panel;
			Transcript.ForeColor = Theme.Text;
			Transcript.Font = Theme.UiFont;
			Transcript.ReadOnly = true;
			Transcript.DetectUrls = false;
			logCard.Controls.Add(Transcript);
			logCard.Controls.SetChildIndex(Transcript, 0);

			// ---- 输入区 ----
			InputPanel = new Panel();
			InputPanel.Dock = DockStyle.Fill;
			InputPanel.BackColor = Theme.Bg;
			grid.Controls.Add(InputPanel, 0, 2);

			Input = new TextBox();
			Input.Font = Theme.UiFont;
			Input.BorderStyle = BorderStyle.FixedSingle;
			Input.BackColor = Theme.Input;
			Input.ForeColor = Theme.Text;
			Input.Multiline = true;
			Input.WordWrap = false;
			Input.ScrollBars = ScrollBars.None;
			Input.Location = new Point(0, 14);
			Input.Height = 38;
			InputPanel.Controls.Add(Input);
			Input.KeyDown += delegate(object sender, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					SendTyped();
				}
			};
			Input.TextChanged += delegate
			{
				if (InputHint != null)
				{
					InputHint.Visible = (Input.TextLength == 0);
				}
			};

			InputHint = new Label();
			InputHint.Text = "也可以直接输入题目，回车发送";
			InputHint.Font = Theme.SmallFont;
			InputHint.ForeColor = Theme.Dim;
			InputHint.BackColor = Theme.Input;
			InputHint.AutoSize = false;
			InputHint.Cursor = Cursors.IBeam;
			InputHint.Location = new Point(10, 24);
			InputHint.Size = new Size(320, 18);
			InputHint.Click += delegate { Input.Focus(); };
			InputPanel.Controls.Add(InputHint);
			InputHint.BringToFront();

			BtnSend = new FlatButton();
			BtnSend.Text = "提问";
			BtnSend.Kind = ButtonKind.Primary;
			BtnSend.Size = new Size(84, 38);
			BtnSend.Location = new Point(0, 14);
			BtnSend.Click += delegate { SendTyped(); };
			InputPanel.Controls.Add(BtnSend);

			BtnShot = new FlatButton();
			BtnShot.Text = "截图出题";
			BtnShot.Kind = ButtonKind.Secondary;
			BtnShot.Size = new Size(92, 38);
			BtnShot.Location = new Point(0, 14);
			BtnShot.Click += delegate { BeginCapture(false); };
			InputPanel.Controls.Add(BtnShot);

			InputPanel.Resize += delegate { LayoutInput(); };

			body.Resize += delegate { LayoutBrowser(); };
			return body;
		}

		private void BuildBrowserHost(Panel body)
		{
			BrowserHost = new Panel();
			BrowserHost.Dock = DockStyle.Left;
			BrowserHost.Width = S.BrowserWidth;
			BrowserHost.BackColor = Theme.Bg;
			BrowserHost.Padding = new Padding(0, 0, 12, 0);
			BrowserHost.Visible = false;

			BrowserPanel = new Panel();
			BrowserPanel.Dock = DockStyle.Fill;
			BrowserPanel.BorderStyle = BorderStyle.FixedSingle;
			BrowserPanel.BackColor = Theme.Browser;
			BrowserPanel.Visible = false;
			BrowserHost.Controls.Add(BrowserPanel);

			BrowserPlaceholder = new Panel();
			BrowserPlaceholder.Dock = DockStyle.Fill;
			BrowserPlaceholder.BackColor = Theme.PanelAlt;
			BrowserHost.Controls.Add(BrowserPlaceholder);

			Label phTitle = new Label();
			phTitle.Text = "DeepSeek 网页版";
			phTitle.Font = Theme.UiFontBold;
			phTitle.ForeColor = Theme.Text;
			phTitle.TextAlign = ContentAlignment.MiddleCenter;
			phTitle.AutoSize = false;
			phTitle.Dock = DockStyle.Top;
			phTitle.Height = 120;
			BrowserPlaceholder.Controls.Add(phTitle);

			Label phHint = new Label();
			phHint.Text = "点右上角「嵌入浏览器」，\r\n把登录页和对话都放进这一栏。";
			phHint.Font = Theme.SmallFont;
			phHint.ForeColor = Theme.Dim;
			phHint.TextAlign = ContentAlignment.TopCenter;
			phHint.AutoSize = false;
			phHint.Dock = DockStyle.Fill;
			BrowserPlaceholder.Controls.Add(phHint);
			phHint.BringToFront();
			phTitle.BringToFront();

			BrowserSplitter = new Splitter();
			BrowserSplitter.Dock = DockStyle.Right;
			BrowserSplitter.Width = 6;
			BrowserSplitter.MinSize = 300;
			BrowserSplitter.MinExtra = 460;
			BrowserSplitter.BackColor = Theme.Bg;
			BrowserSplitter.Cursor = Cursors.SizeWE;
			BrowserSplitter.Visible = false;
			BrowserSplitter.SplitterMoved += delegate
			{
				S.BrowserWidth = BrowserHost.Width;
				S.Save();
			};
			BrowserHost.Controls.Add(BrowserSplitter);

			// 停靠顺序：分隔条先占右侧 6px，两个填充面板最后填剩余区域
			BrowserHost.Controls.SetChildIndex(BrowserSplitter, 2);
			BrowserHost.Controls.SetChildIndex(BrowserPlaceholder, 1);
			BrowserHost.Controls.SetChildIndex(BrowserPanel, 0);

			body.Controls.Add(BrowserHost);
			body.Controls.SetChildIndex(BrowserHost, 1);

			Embedder = new BrowserEmbedder(BrowserPanel);
		}

		private CardPanel BuildHotkeyCard(BadgeKind badge, string title, string hint, out KeyChip chip)
		{
			CardPanel card = new CardPanel();
			card.CornerRadius = 14;
			card.Size = new Size(280, 116);

			IconBadge icon = new IconBadge();
			icon.Kind = badge;
			icon.Size = new Size(28, 28);
			icon.Location = new Point(16, 14);
			card.Controls.Add(icon);

			Label head = new Label();
			head.Text = title;
			head.Font = Theme.UiFontBold;
			head.ForeColor = Theme.Text;
			head.AutoSize = true;
			head.Location = new Point(52, 18);
			card.Controls.Add(head);

			Label tip = new Label();
			tip.Text = hint;
			tip.Font = Theme.SmallFont;
			tip.ForeColor = Theme.Dim;
			tip.AutoSize = true;
			tip.Location = new Point(16, 50);
			card.Controls.Add(tip);

			chip = new KeyChip();
			chip.Text = "Alt";
			chip.AutoWidth();
			chip.Height = 30;
			chip.Location = new Point(16, 74);
			card.Controls.Add(chip);

			FlatButton change = new FlatButton();
			change.Text = "改键";
			change.Kind = ButtonKind.Ghost;
			change.Font = Theme.SmallFont;
			change.Size = new Size(50, 24);
			change.Location = new Point(0, 16);
			change.Tag = card;
			card.Controls.Add(change);

			FlatButton tryIt = new FlatButton();
			tryIt.Text = "试一下";
			tryIt.Kind = ButtonKind.Secondary;
			tryIt.Font = Theme.SmallFont;
			tryIt.Size = new Size(64, 28);
			tryIt.Location = new Point(0, 75);
			tryIt.Tag = card;
			card.Controls.Add(tryIt);

			if (badge == BadgeKind.Shot)
			{
				change.Click += delegate { ChangeShotKey(); };
				tryIt.Click += delegate { BeginCapture(false); };
			}
			else
			{
				change.Click += delegate { ChangeTextKey(); };
				tryIt.Click += delegate { GrabTextAndAsk(); };
			}
			return card;
		}

		private void LayoutHeader()
		{
			if (HeaderPanel == null || Status == null)
			{
				return;
			}
			int right = HeaderPanel.Width - 22;
			BtnSettings.Location = new Point(right - BtnSettings.Width, 24);
			BtnEmbed.Location = new Point(BtnSettings.Left - 8 - BtnEmbed.Width, 24);
			Status.Location = new Point(BtnEmbed.Left - 14 - Status.Width, 26);
		}

		private void LayoutFooter()
		{
			if (FooterPanel == null)
			{
				return;
			}
			int x = 22;
			x = AddFooterButton("新对话", x, delegate { StartNewChat(); });
			x = AddFooterButton("检测页面", x, delegate { Probe(); });
			x = AddFooterButton("配置目录", x, delegate { OpenConfigDir(); });
			x = AddFooterButton("答案窗口", x, delegate { ShowAnswer(); });

			BtnTheme = new FlatButton();
			BtnTheme.Text = "切换主题";
			BtnTheme.Kind = ButtonKind.Ghost;
			BtnTheme.Font = Theme.SmallFont;
			BtnTheme.Size = new Size(76, 28);
			BtnTheme.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			BtnTheme.Location = new Point(FooterPanel.Width - 98, 9);
			BtnTheme.Click += delegate { ToggleTheme(); };
			FooterPanel.Controls.Add(BtnTheme);

			Label version = new Label();
			version.Text = "v1.1";
			version.Font = Theme.SmallFont;
			version.ForeColor = Theme.Dim;
			version.AutoSize = true;
			version.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			version.Location = new Point(FooterPanel.Width - 22 - version.Width, 15);
			FooterPanel.Controls.Add(version);
			FooterPanel.Resize += delegate
			{
				version.Left = FooterPanel.Width - 22 - version.Width;
				BtnTheme.Left = version.Left - 16 - BtnTheme.Width;
			};
			version.Left = FooterPanel.Width - 22 - version.Width;
			BtnTheme.Left = version.Left - 16 - BtnTheme.Width;
		}

		private int AddFooterButton(string text, int x, EventHandler onClick)
		{
			FlatButton button = new FlatButton();
			button.Text = text;
			button.Kind = ButtonKind.Ghost;
			button.Font = Theme.SmallFont;
			button.Size = new Size(TextRenderer.MeasureText(text, Theme.SmallFont).Width + 24, 28);
			button.Location = new Point(x, 9);
			button.Click += onClick;
			FooterPanel.Controls.Add(button);
			return x + button.Width + 2;
		}

		private void LayoutCards()
		{
			if (CardsPanel == null || ShotCard == null)
			{
				return;
			}
			int gap = 12;
			int width = (CardsPanel.Width - gap) / 2;
			if (width < 120)
			{
				return;
			}
			ShotCard.SetBounds(0, 0, width, 116);
			TextCard.SetBounds(width + gap, 0, CardsPanel.Width - width - gap, 116);
			LayoutShotCard();
			LayoutTextCard();
		}

		private void LayoutShotCard()
		{
			LayoutCardButtons(ShotCard);
		}

		private void LayoutTextCard()
		{
			LayoutCardButtons(TextCard);
		}

		private void LayoutCardButtons(CardPanel card)
		{
			if (card == null)
			{
				return;
			}
			foreach (Control control in card.Controls)
			{
				if (!(control is FlatButton))
				{
					continue;
				}
				FlatButton button = (FlatButton)control;
				if (button.Text == "改键")
				{
					button.Left = card.Width - button.Width - 14;
				}
				else if (button.Text == "试一下")
				{
					button.Left = card.Width - button.Width - 14;
				}
			}
		}

		private void LayoutInput()
		{
			if (InputPanel == null)
			{
				return;
			}
			BtnSend.Location = new Point(InputPanel.Width - BtnSend.Width, 14);
			BtnShot.Location = new Point(BtnSend.Left - 8 - BtnShot.Width, 14);
			Input.Width = Math.Max(90, BtnShot.Left - 10);
			if (InputHint != null)
			{
				InputHint.Location = new Point(10, 24);
				InputHint.Width = Math.Max(60, Input.Width - 22);
			}
		}

		private void LayoutBrowser()
		{
			if (BrowserHost == null || !BrowserHost.Visible)
			{
				return;
			}
			int max = Math.Max(300, ClientSize.Width - 500);
			int width = Math.Max(300, Math.Min(S.BrowserWidth, max));
			if (width != BrowserHost.Width)
			{
				BrowserHost.Width = width;
			}
		}

		private void ApplySettingsToUi()
		{
			if (ShotChip != null)
			{
				ShotChip.Text = S.ShotHoldKey;
				ShotChip.AutoWidth();
			}
			if (TextChip != null)
			{
				TextChip.Text = S.TextHotkey;
				TextChip.AutoWidth();
			}
			if (Tray != null)
			{
				Tray.Text = "SnapAsk 截题助手"; 
			}
		}

		private void AppendHint()
		{
			if (Transcript == null || Transcript.TextLength > 0)
			{
				return;
			}
			Transcript.SelectionStart = 0;
			Transcript.SelectionColor = Theme.Dim;
			Transcript.SelectionFont = Theme.SmallFont;
			Transcript.AppendText("按住 " + S.ShotHoldKey + " 拖框圈住题目，松手就会发给 DeepSeek；"
				+ "选中文字后按 " + S.TextHotkey + " 也能提问。\r\n"
				+ "第一次使用需要先在左侧浏览器里登录一次。\r\n\r\n");
		}

		// ================================================================ 嵌入浏览器

		private async void ToggleEmbed()
		{
			if (Embedder != null && Embedder.Embedded)
			{
				DetachBrowser();
				return;
			}
			await EmbedBrowserAsync(true);
		}

		private async Task EmbedBrowserAsync(bool fromUser)
		{
			if (string.Equals(S.Engine, "api", StringComparison.Ordinal))
			{
				Notify("API 模式没有浏览器", "把设置里的发送方式改回网页版，才能嵌入浏览器。");
				return;
			}
			// 窗口最小化 / 藏在托盘里时不能把浏览器嵌进去：
			// 嵌进去就跟着一起隐藏，页面停止渲染，回答抓不回来（小窗不同步）。
			// 这时保持独立窗口，等窗口还原再自动嵌入。
			if (!Visible || WindowState == FormWindowState.Minimized)
			{
				DetachedByMinimize = true;
				Log.Dim("窗口当前处于最小化，浏览器先保持独立窗口（还原后自动嵌入）。");
				return;
			}
			try
			{
				if (Engine == null)
				{
					Engine = CreateEngine();
				}
				WebEngine web = Engine as WebEngine;
				if (web == null)
				{
					return;
				}
				SetStatus("打开浏览器", Theme.Accent);
				// 先把承载面板显示出来（浏览器窗口必须有一个可见的父窗口才看得到）
				BrowserHost.Visible = true;
				BrowserSplitter.Visible = true;
				BrowserPlaceholder.Visible = false;
				BrowserPanel.Visible = true;
				LayoutBrowser();
				BrowserPanel.Refresh();
				int processId = await web.EnsureBrowserAsync(Cts.Token);
				bool ok = Embedder.Attach(processId, "DeepSeek");
				for (int i = 0; i < 12 && !ok; i++)
				{
					await Task.Delay(500);
					ok = Embedder.Attach(processId, "DeepSeek");
				}
				if (!ok)
				{
					ShowBrowserHost(false);
					SetStatus("未找到窗口", Theme.Error);
					Notify("没找到浏览器窗口", "请点「检测页面」等 DeepSeek 页面打开后再嵌入。");
					return;
				}
				BtnEmbed.Text = "恢复窗口";
				SetStatus("已嵌入", Theme.Success);
				LayoutBrowser();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				ShowBrowserHost(false);
				SetStatus("出错", Theme.Error);
				if (fromUser)
				{
					Notify("嵌入失败", ex.Message);
				}
			}
		}

		private void DetachBrowser()
		{
			try
			{
				if (Embedder != null)
				{
					Embedder.Detach();
				}
			}
			catch
			{
			}
			ShowBrowserHost(false);
			BtnEmbed.Text = "嵌入浏览器";
		}

		private void ShowBrowserHost(bool show)
		{
			if (BrowserHost == null)
			{
				return;
			}
			BrowserHost.Visible = show;
			BrowserSplitter.Visible = show;
			BrowserPanel.Visible = show && Embedder != null && Embedder.Embedded;
			BrowserPlaceholder.Visible = show && !BrowserPanel.Visible;
			if (show)
			{
				LayoutBrowser();
			}
		}

		// ================================================================ 快捷键

		private void InstallHook()
		{
			Hook = new HotkeyHook(this);
			Hook.HoldKey = KeySpec.Parse(S.ShotHoldKey);
			Hook.TapKey = KeySpec.Parse(S.TextHotkey);
			Hook.HoldDown += delegate { BeginCapture(true); };
			Hook.HoldUp += delegate { EndHold(); };
			Hook.Tap += delegate { GrabTextAndAsk(); };
			if (!Hook.Install())
			{
				SetStatus("钩子失败", Theme.Error);
				AppendTranscript("错误", "全局快捷键安装失败（可能被安全软件拦截）。可以点「截图出题」按钮代替。", Theme.Error);
			}
		}

		private void ChangeShotKey()
		{
			using (KeyCaptureForm form = new KeyCaptureForm("按住哪个键开始框选？", S.ShotHoldKey))
			{
				if (form.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(form.Result))
				{
					return;
				}
				if (string.Equals(form.Result, S.TextHotkey, StringComparison.OrdinalIgnoreCase))
				{
					Notify("键位冲突", "这个键已经用于划词提问，请换一个。");
					return;
				}
				S.ShotHoldKey = form.Result;
				S.Save();
				ApplySettingsToUi();
				Hook.HoldKey = KeySpec.Parse(S.ShotHoldKey);
				AppendTranscript("设置", "截图按键已改为 " + S.ShotHoldKey, Theme.Dim);
			}
		}

		private void ChangeTextKey()
		{
			using (KeyCaptureForm form = new KeyCaptureForm("划词提问用哪个快捷键？", S.TextHotkey))
			{
				if (form.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(form.Result))
				{
					return;
				}
				if (string.Equals(form.Result, S.ShotHoldKey, StringComparison.OrdinalIgnoreCase))
				{
					Notify("键位冲突", "这个键已经用于截图出题，请换一个。");
					return;
				}
				S.TextHotkey = form.Result;
				S.Save();
				ApplySettingsToUi();
				Hook.TapKey = KeySpec.Parse(S.TextHotkey);
				AppendTranscript("设置", "划词快捷键已改为 " + S.TextHotkey, Theme.Dim);
			}
		}

		// ================================================================ 截图

		private void BeginCapture(bool byHotkey)
		{
			if (Overlay != null || Frozen != null)
			{
				return;
			}
			try
			{
				Frozen = ScreenCapture.CaptureVirtualScreen();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				Notify("截图失败", ex.Message);
				DisposeFrozen();
				return;
			}
			Rectangle bounds = SystemInformation.VirtualScreen;
			Overlay = new CaptureOverlay(Frozen, bounds, S.ShotHoldKey);
			Overlay.RequireKeyRelease = byHotkey;
			KeySpec hold = KeySpec.Parse(S.ShotHoldKey);
			Overlay.TriggerVk = (hold == null) ? 0 : hold.Vk;
			Overlay.Confirmed += OnCaptureConfirmed;
			Overlay.Cancelled += OnCaptureCancelled;
			Overlay.FormClosed += delegate
			{
				Overlay = null;
				if (Frozen != null && !HasCaptured)
				{
					DisposeFrozen();
				}
				HasCaptured = false;
			};
			HasCaptured = false;
			Overlay.Show();
			Log.Dim(byHotkey ? "框选层已弹出（按住 " + S.ShotHoldKey + "）。" : "框选层已弹出（点击触发）。");
		}

		private void EndHold()
		{
			if (Overlay != null)
			{
				Overlay.NotifyTriggerReleased();
			}
		}

		private void OnCaptureCancelled()
		{
			DisposeFrozen();
			Overlay = null;
			Log.Dim("框选已取消。");
		}

		private void OnCaptureConfirmed(Rectangle deviceRect)
		{
			HasCaptured = true;
			Bitmap cropped = null;
			try
			{
				cropped = ScreenCapture.Crop(Frozen, deviceRect);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			finally
			{
				DisposeFrozen();
				Overlay = null;
			}
			if (cropped == null)
			{
				Notify("框选太小", "请框住完整的题目再松手。");
				return;
			}
			Rectangle virtualBounds = SystemInformation.VirtualScreen;
			string path;
			try
			{
				path = ScreenCapture.SavePng(cropped);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				Notify("保存截图失败", ex.Message);
				cropped.Dispose();
				return;
			}
			Rectangle screenRect = new Rectangle(
				deviceRect.X + virtualBounds.Left,
				deviceRect.Y + virtualBounds.Top,
				deviceRect.Width,
				deviceRect.Height);
			cropped.Dispose();

			Question question = new Question();
			question.ImagePath = path;
			question.Text = S.ImagePrompt;
			question.Source = "截图 " + deviceRect.Width + "×" + deviceRect.Height;
			question.Region = screenRect;
			Log.Info("截图已保存: " + path);
			Enqueue(question);
		}

		private void DisposeFrozen()
		{
			if (Frozen != null)
			{
				try
				{
					Frozen.Dispose();
				}
				catch
				{
				}
				Frozen = null;
			}
		}

		// ================================================================ 划词 / 输入

		private void GrabTextAndAsk()
		{
			string selected = ClipboardGrab.GrabSelectedText(420);
			if (string.IsNullOrEmpty(selected) || selected.Trim().Length == 0)
			{
				Notify("没有取到文字", "请先用鼠标选中要提问的文字，再按 " + S.TextHotkey + "。");
				return;
			}
			Question question = new Question();
			question.Source = selected.Trim();
			question.Text = S.TextPrompt.Replace("{text}", question.Source);
			question.Region = Rectangle.Empty;
			Enqueue(question);
		}

		private void SendTyped()
		{
			string text = (Input.Text ?? "").Trim();
			if (text.Length == 0)
			{
				return;
			}
			Input.Clear();
			Question question = new Question();
			question.Source = text;
			question.Text = S.TextPrompt.Replace("{text}", text);
			question.Region = Rectangle.Empty;
			Enqueue(question);
		}

		// ================================================================ 队列

		private void Enqueue(Question question)
		{
			lock (Pending)
			{
				Pending.Enqueue(question);
			}
			AppendTranscript("你", question.Caption, Theme.User);
			if (!Running)
			{
				Running = true;
				RunQueue();
			}
			else
			{
				SetStatus("排队中", Theme.Accent);
			}
		}

		private async void RunQueue()
		{
			while (true)
			{
				Question question = null;
				lock (Pending)
				{
					if (Pending.Count == 0)
					{
						Running = false;
						return;
					}
					question = Pending.Dequeue();
				}
				try
				{
					await ProcessAsync(question);
				}
				catch (Exception ex)
				{
					Log.Error(ex);
					ShowError(ex.Message);
				}
			}
		}

		private async Task ProcessAsync(Question question)
		{
			SetStatus("发送中", Theme.Accent);
			if (Answer == null)
			{
				Answer = new AnswerForm();
				Answer.FollowUp += delegate(string text) { FollowUp(text); };
				Answer.SetDefaultPin(S.AnswerOnTop);
			}
			Answer.BeginQuestion(question);
			Answer.SetQuestionCaption(question.Caption);
			if (question.Region.IsEmpty)
			{
				Answer.PlaceNearCursor();
			}
			else
			{
				Answer.PlaceNear(question.Region);
			}
			if (!S.AutoShowAnswer)
			{
				Answer.Hide();
			}

			try
			{
				if (Engine == null)
				{
					Engine = CreateEngine();
				}
				WebEngine web = Engine as WebEngine;
				bool needConnect = (web != null && !web.IsConnected) || (web == null);
				if (needConnect)
				{
					Answer.SetStatus("连接页面", Theme.Accent);
					await Engine.ConnectAsync(Cts.Token);
				}
				if (web != null && S.EmbedBrowser && (Embedder == null || !Embedder.Embedded))
				{
					await EmbedBrowserAsync(false);
				}
				SetStatus("解答中", Theme.Accent);
				Answer.SetStatus("生成中", Theme.Accent);
				string answer = await Engine.AskAsync(question.Text, question.ImagePath,
					delegate(string partial)
					{
						Answer.UpdateAnswer(partial);
						if (S.AutoShowAnswer)
						{
							Answer.EnsureVisible();
						}
					}, Cts.Token);
				Answer.UpdateAnswer(answer);
				Answer.Finish();
				SetStatus("已完成", Theme.Success);
				AppendTranscript("答", answer, Theme.Answer);
				SaveHistory(question, answer);
			}
			catch (OperationCanceledException)
			{
				Answer.SetStatus("已停止", Theme.Dim);
				SetStatus("空闲", Theme.Dim);
			}
			catch (Exception ex)
			{
				ShowError(ex.Message);
			}
		}

		private void FollowUp(string text)
		{
			Question question = new Question();
			question.Source = text;
			question.Text = text;
			question.Region = Rectangle.Empty;
			lock (Pending)
			{
				Pending.Enqueue(question);
			}
			Answer.SetStatus("排队中", Theme.Accent);
			if (!Running)
			{
				Running = true;
				RunQueue();
			}
		}

		private IChatEngine CreateEngine()
		{
			DisposeEngine();
			IChatEngine created = (string.Equals(S.Engine, "api", StringComparison.Ordinal))
				? (IChatEngine)new ApiEngine(S)
				: new WebEngine(S);
			Log.Info("使用引擎: " + created.Name);
			return created;
		}

		private void DisposeEngine()
		{
			if (Engine != null)
			{
				try
				{
					Engine.Dispose();
				}
				catch
				{
				}
				Engine = null;
			}
		}

		private void ShowError(string message)
		{
			SetStatus("出错", Theme.Error);
			AppendTranscript("错误", message, Theme.Error);
			if (Answer != null)
			{
				Answer.ShowFailure(message);
			}
		}

		// ================================================================ 其它动作

		private async void Probe()
		{
			SetStatus("检测中", Theme.Accent);
			try
			{
				if (Engine == null)
				{
					Engine = CreateEngine();
				}
				string diag = await Engine.ConnectAsync(Cts.Token);
				SetStatus("已连接", Theme.Success);
				AppendTranscript("检测", diag, Theme.Dim);
				if (S.EmbedBrowser && (Embedder == null || !Embedder.Embedded))
				{
					await EmbedBrowserAsync(false);
				}
			}
			catch (Exception ex)
			{
				SetStatus("未连接", Theme.Error);
				AppendTranscript("检测失败", ex.Message, Theme.Error);
			}
		}

		private async void StartNewChat()
		{
			if (Engine == null)
			{
				AppendTranscript("提示", "还没连接，先提一个问题。", Theme.Dim);
				return;
			}
			SetStatus("新建对话", Theme.Accent);
			try
			{
				await Engine.ResetAsync(Cts.Token);
				SetStatus("已连接", Theme.Success);
				AppendTranscript("提示", "已开始新对话。", Theme.Dim);
			}
			catch (Exception ex)
			{
				SetStatus("出错", Theme.Error);
				AppendTranscript("错误", ex.Message, Theme.Error);
			}
		}

		private void OpenSettings()
		{
			using (SettingsForm form = new SettingsForm(S))
			{
				if (form.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}
				S = form.Result;
				bool themeChanged = !string.Equals(Theme.ModeName, S.Theme, StringComparison.OrdinalIgnoreCase);
				if (themeChanged)
				{
					Theme.SetMode(S.Theme);
					RebuildUi();
				}
				ApplySettingsToUi();
				if (Hook != null)
				{
					Hook.HoldKey = KeySpec.Parse(S.ShotHoldKey);
					Hook.TapKey = KeySpec.Parse(S.TextHotkey);
				}
				// 端口、引擎、浏览器路径可能变了：断开重连，嵌入状态一并重置
				DetachBrowser();
				DisposeEngine();
				AppendTranscript("设置", "已保存。", Theme.Dim);
			}
		}

		private void RebuildUi()
		{
			string keep = (Transcript == null) ? "" : Transcript.Text;
			if (Answer != null)
			{
				Answer.Dispose();
				Answer = null;
			}
			if (Embedder != null)
			{
				Embedder.Dispose();
				Embedder = null;
			}
			Controls.Clear();
			BuildUi();
			ApplySettingsToUi();
			if (Transcript != null)
			{
				if (keep.Length > 0)
				{
					Transcript.Text = keep;
					Transcript.SelectionStart = Transcript.TextLength;
					Transcript.ScrollToCaret();
				}
				else
				{
					AppendHint();
				}
			}
		}

		private void ToggleTheme()
		{
			S.Theme = Theme.Dark ? "light" : "dark";
			S.Save();
			Theme.SetMode(S.Theme);
			RebuildUi();
			AppendTranscript("设置", "主题切换为" + (Theme.Dark ? "深色" : "浅色") + "。", Theme.Dim);
		}

		private void OpenConfigDir()
		{
			try
			{
				Directory.CreateDirectory(Settings.Dir);
				System.Diagnostics.Process.Start("explorer.exe", Settings.Dir);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		private void ShowAnswer()
		{
			if (Answer == null)
			{
				Notify("还没有答案", "先截图或划词提一个问题。");
				return;
			}
			Answer.Show();
			Answer.BringToFront();
		}

		// ================================================================ 托盘

		private void BuildTray()
		{
			Tray = new NotifyIcon();
			Tray.Icon = Icon;
			Tray.Text = "SnapAsk 截题助手";
			Tray.Visible = true;

			ContextMenuStrip menu = new ContextMenuStrip();
			menu.Items.Add("显示主窗口", null, delegate { ShowMain(); });
			menu.Items.Add("截图出题（" + S.ShotHoldKey + " 按住）", null, delegate { BeginCapture(false); });
			menu.Items.Add("划词出题（" + S.TextHotkey + "）", null, delegate { GrabTextAndAsk(); });
			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add("嵌入 / 恢复浏览器", null, delegate { ToggleEmbed(); });
			menu.Items.Add("设置", null, delegate { ShowMain(); OpenSettings(); });
			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add("退出", null, delegate { ReallyQuit(); });
			Tray.ContextMenuStrip = menu;
			Tray.DoubleClick += delegate { ShowMain(); };
		}

		private void ShowMain()
		{
			Show();
			WindowState = FormWindowState.Normal;
			Activate();
		}

		private void ReallyQuit()
		{
			Quitting = true;
			Close();
		}

		protected override void OnActivated(EventArgs e)
		{
			base.OnActivated(e);
			// 最小化再还原后，嵌入的浏览器需要强制重画一次，否则停在旧画面
			if (Embedder != null && Embedder.Embedded)
			{
				Embedder.RefreshWindow();
			}
		}

		/// <summary>
		/// 窗口被最小化 / 藏进托盘时，嵌入的浏览器也会跟着隐藏。
		/// 页面一隐藏，DeepSeek 就停止渲染对话，回答抓不回来（小窗卡在「生成中」）。
		/// 所以这时把浏览器放回独立窗口，让它继续跑；窗口还原后再嵌回来。
		/// </summary>
		private void OnAppHidden()
		{
			if (Embedder == null || !Embedder.Embedded || DetachedByMinimize)
			{
				return;
			}
			DetachedByMinimize = true;
			try
			{
				Embedder.Detach();
				ShowBrowserHost(false);
				BtnEmbed.Text = "嵌入浏览器";
				Log.Dim("窗口已最小化，浏览器暂时放回独立窗口（保持页面活跃，回答照常同步）。");
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		private void OnAppShown()
		{
			if (!DetachedByMinimize)
			{
				return;
			}
			DetachedByMinimize = false;
			Log.Dim("窗口已还原，重新嵌入浏览器。");
			System.Threading.Tasks.Task embedTask = EmbedBrowserAsync(false);
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (WindowState == FormWindowState.Minimized)
			{
				OnAppHidden();
			}
			else if (Visible)
			{
				OnAppShown();
			}
		}

		protected override void OnVisibleChanged(EventArgs e)
		{
			base.OnVisibleChanged(e);
			if (!Visible)
			{
				OnAppHidden();
			}
			else if (WindowState != FormWindowState.Minimized)
			{
				OnAppShown();
			}
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			ClampToScreen();
			if (Transcript != null && Transcript.TextLength == 0)
			{
				AppendHint();
			}
			if (DemoMode)
			{
				ShowDemoAnswer();
				System.Threading.Tasks.Task embedTask = EmbedBrowserAsync(false);
			}
			if (AskDemo)
			{
				System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
				timer.Interval = 9000;
				timer.Tick += delegate
				{
					timer.Stop();
					timer.Dispose();
					Question question = new Question();
					question.Source = "请用一句话说明 1+1 等于几";
					question.Text = S.TextPrompt.Replace("{text}", question.Source);
					question.Region = Rectangle.Empty;
					Enqueue(question);
				};
				timer.Start();
			}
		}

		private void ShowDemoAnswer()
		{
			Question sample = new Question();
			sample.Source = "截图 420×180";
			Answer = new AnswerForm();
			Answer.FollowUp += delegate(string text) { FollowUp(text); };
			Answer.BeginQuestion(sample);
			Answer.SetQuestionCaption(sample.Caption);
			Answer.PlaceNearCursor();
			Answer.UpdateAnswer("最终答案：x = 3\r\n\r\n步骤：\r\n1. 两边同时减去 5，得 2x = 6；\r\n2. 两边同时除以 2，得 x = 3；\r\n3. 代回原式验证：2×3 + 5 = 11，成立。");
			Answer.Finish();
			AppendTranscript("答", "（示例）最终答案：x = 3", Theme.Answer);
		}

		/// <summary>高分屏 / 小屏幕下防止窗口比桌面还大。</summary>
		private void ClampToScreen()
		{
			try
			{
				Rectangle area = Screen.FromControl(this).WorkingArea;
				int width = Math.Min(Width, Math.Max(720, area.Width - 24));
				int height = Math.Min(Height, Math.Max(480, area.Height - 24));
				if (width == Width && height == Height)
				{
					return;
				}
				MinimumSize = new Size(Math.Min(MinimumSize.Width, width), Math.Min(MinimumSize.Height, height));
				Size = new Size(width, height);
				Location = new Point(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2);
			}
			catch
			{
			}
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (!Quitting && e.CloseReason == CloseReason.UserClosing)
			{
				// 关窗口只是收进托盘，快捷键继续可用
				e.Cancel = true;
				Hide();
				if (Tray != null && !Tray.Visible)
				{
					Tray.Visible = true;
				}
				return;
			}
			try
			{
				Cts.Cancel();
			}
			catch
			{
			}
			DisposeFrozen();
			if (Embedder != null)
			{
				Embedder.Dispose();
				Embedder = null;
			}
			DisposeEngine();
			if (Hook != null)
			{
				Hook.Dispose();
			}
			if (Tray != null)
			{
				Tray.Visible = false;
				Tray.Dispose();
			}
			if (Answer != null)
			{
				Answer.Dispose();
			}
			base.OnFormClosing(e);
		}

		// ================================================================ 杂项

		private void SetStatus(string text, Color dot)
		{
			if (Status == null)
			{
				return;
			}
			if (Status.InvokeRequired)
			{
				BeginInvoke((Action)delegate { SetStatus(text, dot); });
				return;
			}
			Status.Text = text;
			Status.DotColor = dot;
			Status.AutoWidth();
			Status.Invalidate();
			LayoutHeader();
		}

		private void AppendTranscript(string who, string text, Color color)
		{
			if (Transcript == null)
			{
				return;
			}
			if (Transcript.InvokeRequired)
			{
				BeginInvoke((Action)delegate { AppendTranscript(who, text, color); });
				return;
			}
			Transcript.SelectionStart = Transcript.TextLength;
			Transcript.SelectionColor = Theme.Dim;
			Transcript.SelectionFont = Theme.SmallFont;
			Transcript.AppendText(string.Format("[{0:HH:mm:ss}] {1}　", DateTime.Now, who));
			Transcript.SelectionColor = color;
			Transcript.SelectionFont = Theme.UiFont;
			Transcript.AppendText(((text == null) ? "" : text) + "\r\n\r\n");
			Transcript.SelectionStart = Transcript.TextLength;
			Transcript.ScrollToCaret();
		}

		private void Notify(string title, string message)
		{
			try
			{
				if (Tray != null)
				{
					Tray.BalloonTipTitle = title;
					Tray.BalloonTipText = message;
					Tray.ShowBalloonTip(2500);
				}
			}
			catch
			{
			}
			Log.Info(title + "：" + message.Replace("\r", " ").Replace("\n", " "));
		}

		private void SaveHistory(Question question, string answer)
		{
			try
			{
				string dir = Path.Combine(Settings.Dir, "history");
				Directory.CreateDirectory(dir);
				string path = Path.Combine(dir, string.Format("{0:yyyy-MM-dd}.log", DateTime.Now));
				string block = string.Format("[{0:yyyy-MM-dd HH:mm:ss}]\r\n题目：{1}\r\n{2}\r\n答案：\r\n{3}\r\n{4}\r\n",
					DateTime.Now, question.Caption, (question.HasImage ? ("截图：" + question.ImagePath) : ""),
					answer, new string('-', 60));
				File.AppendAllText(path, block);
			}
			catch
			{
			}
		}
	}
}
