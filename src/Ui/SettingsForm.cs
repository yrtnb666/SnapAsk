using System;
using System.Drawing;
using System.Windows.Forms;
using SnapAsk.Util;

namespace SnapAsk.Ui
{
	/// <summary>设置窗口：按键、提示词、引擎、浏览器都在这儿改。</summary>
	public sealed class SettingsForm : Form
	{
		private readonly Settings Draft;

		private readonly Panel Body;

		private KeyChip ShotChip;

		private KeyChip TextChip;

		private ComboBox EngineBox;

		private TextBox ImagePromptBox;

		private TextBox TextPromptBox;

		private TextBox TimeoutBox;

		private TextBox BrowserBox;

		private TextBox PortBox;

		private TextBox ApiBaseBox;

		private TextBox ApiKeyBox;

		private TextBox ApiModelBox;

		private CheckBox AutoShowBox;

		private CheckBox OnTopBox;

		private CheckBox EmbedBox;

		private CheckBox DarkBox;

		private int Y;

		/// <summary>保存后的配置副本（DialogResult 为 OK 时使用）。</summary>
		public Settings Result
		{
			get
			{
				return Draft;
			}
		}

		public SettingsForm(Settings current)
		{
			Draft = current.Clone();
			Text = "设置";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(580, 660);
			BackColor = Theme.Bg;
			Font = Theme.UiFont;

			Panel footer = new Panel();
			footer.Dock = DockStyle.Bottom;
			footer.Height = 60;
			footer.BackColor = Theme.Bg;
			Controls.Add(footer);

			FlatButton save = new FlatButton();
			save.Text = "保存";
			save.Kind = ButtonKind.Primary;
			save.Size = new Size(96, 34);
			save.Location = new Point(ClientSize.Width - 124, 14);
			save.Anchor = AnchorStyles.Right | AnchorStyles.Top;
			save.Click += delegate { Save(); };
			footer.Controls.Add(save);

			FlatButton cancel = new FlatButton();
			cancel.Text = "取消";
			cancel.Size = new Size(96, 34);
			cancel.Location = new Point(ClientSize.Width - 232, 14);
			cancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
			cancel.DialogResult = DialogResult.Cancel;
			footer.Controls.Add(cancel);

			Body = new Panel();
			Body.Dock = DockStyle.Fill;
			Body.AutoScroll = true;
			Body.BackColor = Theme.Bg;
			Controls.Add(Body);
			Body.BringToFront();

			Y = 16;

			// 按键
			Label keyTitle = Section("快捷键");
			AddRow(keyTitle, 26);

			Label shotLabel = Field("截图出题（按住）");
			AddInline(shotLabel, 0, 150);
			ShotChip = new KeyChip();
			ShotChip.Text = Draft.ShotHoldKey;
			ShotChip.AutoWidth();
			ShotChip.Location = new Point(160, Y - 6);
			ShotChip.Click += delegate
			{
				string picked = CaptureKey("按住哪个键开始框选？", Draft.ShotHoldKey);
				if (!string.IsNullOrEmpty(picked))
				{
					Draft.ShotHoldKey = picked;
					ShotChip.Text = picked;
					ShotChip.AutoWidth();
				}
			};
			Body.Controls.Add(ShotChip);
			AddHint("按住不放 → 拖框 → 松手自动发送", 160 + ShotChip.Width + 12);
			Y += 36;

			Label textLabel = Field("划词出题（点按）");
			AddInline(textLabel, 0, 150);
			TextChip = new KeyChip();
			TextChip.Text = Draft.TextHotkey;
			TextChip.AutoWidth();
			TextChip.Location = new Point(160, Y - 6);
			TextChip.Click += delegate
			{
				string picked = CaptureKey("划词提问用哪个快捷键？", Draft.TextHotkey);
				if (!string.IsNullOrEmpty(picked))
				{
					Draft.TextHotkey = picked;
					TextChip.Text = picked;
					TextChip.AutoWidth();
				}
			};
			Body.Controls.Add(TextChip);
			AddHint("选中文字后按一下，自动取词并提问", 160 + TextChip.Width + 12);
			Y += 44;

			// 引擎
			AddRow(Section("发送方式"), 30);
			Label engineLabel = Field("引擎");
			AddInline(engineLabel, 0, 150);
			EngineBox = new ComboBox();
			EngineBox.DropDownStyle = ComboBoxStyle.DropDownList;
			EngineBox.Font = Theme.UiFont;
			EngineBox.Location = new Point(160, Y - 4);
			EngineBox.Width = 300;
			EngineBox.Items.Add("网页版（推荐：不需要 Key，直接驱动浏览器）");
			EngineBox.Items.Add("API 模式（需要 Key，看不了截图）");
			EngineBox.SelectedIndex = (Draft.Engine == "api") ? 1 : 0;
			Body.Controls.Add(EngineBox);
			Y += 40;

			ImagePromptBox = AddArea("截图提示词", Draft.ImagePrompt, 62);
			TextPromptBox = AddArea("划词提示词（{text} 会被替换成选中的文字）", Draft.TextPrompt, 78);

			// 行为
			AddRow(Section("行为"), 30);
			AutoShowBox = AddCheck("答案出来后自动弹出结果窗", Draft.AutoShowAnswer);
			OnTopBox = AddCheck("结果窗保持置顶", Draft.AnswerOnTop);
			EmbedBox = AddCheck("连上浏览器后自动嵌入主界面左侧", Draft.EmbedBrowser);
			DarkBox = AddCheck("深色主题（下次启动生效）", string.Equals(Draft.Theme, "dark", StringComparison.OrdinalIgnoreCase));

			// 高级
			AddRow(Section("连接与浏览器"), 30);
			TimeoutBox = AddLine("回复超时（秒）", Draft.ReplyTimeoutSec.ToString(), 120);
			PortBox = AddLine("调试端口", Draft.CdpPort.ToString(), 120);
			BrowserBox = AddLine("浏览器路径（留空自动找）", Draft.EdgePath ?? "", 330);
			FlatButton browse = new FlatButton();
			browse.Text = "浏览";
			browse.Size = new Size(70, 28);
			browse.Location = new Point(160 + 338, Y - 40 + 2);
			browse.Click += delegate
			{
				using (OpenFileDialog dialog = new OpenFileDialog())
				{
					dialog.Filter = "浏览器|msedge.exe;chrome.exe|所有程序|*.exe";
					if (dialog.ShowDialog(this) == DialogResult.OK)
					{
						BrowserBox.Text = dialog.FileName;
					}
				}
			};
			Body.Controls.Add(browse);

			AddRow(Section("API（仅 API 模式使用）"), 30);
			ApiBaseBox = AddLine("接口地址", Draft.ApiBase ?? "", 380);
			ApiKeyBox = AddLine("API Key", Draft.ApiKey ?? "", 380);
			ApiModelBox = AddLine("模型名", Draft.ApiModel ?? "", 380);

			Label tip = new Label();
			tip.Text = "提示：网页版第一次用需要在弹出的浏览器窗口里登录一次 DeepSeek，之后会记住登录状态。";
			tip.ForeColor = Theme.Dim;
			tip.Font = Theme.SmallFont;
			tip.AutoSize = false;
			tip.Location = new Point(24, Y + 4);
			tip.Size = new Size(500, 34);
			Body.Controls.Add(tip);
			Y += 44;
		}

		private Label Section(string text)
		{
			Label label = new Label();
			label.Text = text;
			label.Font = Theme.UiFontBold;
			label.ForeColor = Theme.Text;
			label.AutoSize = true;
			return label;
		}

		private Label Field(string text)
		{
			Label label = new Label();
			label.Text = text;
			label.ForeColor = Theme.Dim;
			label.Font = Theme.UiFont;
			label.AutoSize = true;
			return label;
		}

		private void AddRow(Control control, int gap)
		{
			control.Location = new Point(24, Y);
			Body.Controls.Add(control);
			Y += gap;
		}

		private void AddInline(Control control, int x, int width)
		{
			control.Location = new Point(24 + x, Y);
			control.MaximumSize = new Size(width, 0);
			Body.Controls.Add(control);
		}

		private void AddHint(string text, int x)
		{
			Label label = new Label();
			label.Text = text;
			label.ForeColor = Theme.Dim;
			label.Font = Theme.SmallFont;
			label.AutoSize = true;
			label.Location = new Point(24 + x, Y);
			Body.Controls.Add(label);
		}

		private TextBox AddLine(string label, string value, int width)
		{
			Label field = Field(label);
			field.Location = new Point(24, Y + 4);
			field.Size = new Size(128, 20);
			field.AutoSize = false;
			Body.Controls.Add(field);

			TextBox box = new TextBox();
			box.Text = value;
			box.Font = Theme.UiFont;
			box.BorderStyle = BorderStyle.FixedSingle;
			box.Location = new Point(160, Y);
			box.Width = width;
			Body.Controls.Add(box);
			Y += 38;
			return box;
		}

		private TextBox AddArea(string label, string value, int height)
		{
			Label field = Field(label);
			field.Location = new Point(24, Y);
			Body.Controls.Add(field);
			Y += 24;

			TextBox box = new TextBox();
			box.Text = value;
			box.Font = Theme.UiFont;
			box.BorderStyle = BorderStyle.FixedSingle;
			box.Multiline = true;
			box.ScrollBars = ScrollBars.Vertical;
			box.Location = new Point(24, Y);
			box.Size = new Size(500, height);
			Body.Controls.Add(box);
			Y += height + 18;
			return box;
		}

		private CheckBox AddCheck(string text, bool value)
		{
			CheckBox box = new CheckBox();
			box.Text = text;
			box.Checked = value;
			box.ForeColor = Theme.Text;
			box.Font = Theme.UiFont;
			box.AutoSize = true;
			box.Location = new Point(26, Y);
			Body.Controls.Add(box);
			Y += 30;
			return box;
		}

		private string CaptureKey(string title, string current)
		{
			using (KeyCaptureForm form = new KeyCaptureForm(title, current))
			{
				if (form.ShowDialog(this) == DialogResult.OK)
				{
					return form.Result;
				}
			}
			return null;
		}

		private void Save()
		{
			Draft.ShotHoldKey = ShotChip.Text;
			Draft.TextHotkey = TextChip.Text;
			Draft.Engine = (EngineBox.SelectedIndex == 1) ? "api" : "web";
			Draft.ImagePrompt = ImagePromptBox.Text;
			Draft.TextPrompt = TextPromptBox.Text;
			Draft.AutoShowAnswer = AutoShowBox.Checked;
			Draft.AnswerOnTop = OnTopBox.Checked;
			Draft.EmbedBrowser = EmbedBox.Checked;
			Draft.Theme = DarkBox.Checked ? "dark" : "light";
			Draft.EdgePath = BrowserBox.Text.Trim();

			int number;
			if (int.TryParse(TimeoutBox.Text.Trim(), out number))
			{
				Draft.ReplyTimeoutSec = number;
			}
			if (int.TryParse(PortBox.Text.Trim(), out number))
			{
				Draft.CdpPort = number;
			}
			Draft.ApiBase = ApiBaseBox.Text.Trim();
			Draft.ApiKey = ApiKeyBox.Text.Trim();
			Draft.ApiModel = ApiModelBox.Text.Trim();

			// 两个键位不能一样，否则按住键会同时触发划词
			if (string.Equals(Draft.ShotHoldKey, Draft.TextHotkey, StringComparison.OrdinalIgnoreCase))
			{
				MessageBox.Show(this, "「截图按键」和「划词快捷键」不能相同。", "设置",
					MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			if (Core.KeySpec.Parse(Draft.ShotHoldKey) == null)
			{
				MessageBox.Show(this, "截图按键无法识别：" + Draft.ShotHoldKey, "设置",
					MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			if (Core.KeySpec.Parse(Draft.TextHotkey) == null)
			{
				MessageBox.Show(this, "划词快捷键无法识别：" + Draft.TextHotkey, "设置",
					MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}

			Draft.Normalize();
			Draft.Save();
			DialogResult = DialogResult.OK;
			Close();
		}
	}
}
