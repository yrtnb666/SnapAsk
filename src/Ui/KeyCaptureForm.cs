using System;
using System.Drawing;
using System.Windows.Forms;

namespace SnapAsk.Ui
{
	/// <summary>
	/// 录键小窗口：点开后按一下想要的键或组合键即可。
	///   · 只按修饰键（Alt / Ctrl / Shift / Win）→ 记录单个修饰键，适合「按住」；
	///   · 按修饰键 + 普通键 → 记录组合键，适合「点按」。
	/// </summary>
	public sealed class KeyCaptureForm : Form
	{
		private readonly Label Hint;

		private bool ComboFormed;

		/// <summary>录到的键位文本；取消时为 null。</summary>
		public string Result { get; private set; }

		public KeyCaptureForm(string title, string current)
		{
			Text = title;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(380, 168);
			BackColor = Theme.Panel;
			Font = Theme.UiFont;
			KeyPreview = true;

			Label caption = new Label();
			caption.Text = "请按下要使用的按键";
			caption.Font = Theme.UiFontBold;
			caption.ForeColor = Theme.Text;
			caption.AutoSize = true;
			caption.Location = new Point(24, 22);
			Controls.Add(caption);

			Hint = new Label();
			Hint.Text = "当前：" + current + "\r\n单独按 Alt / Ctrl 这类修饰键，就记录成「按住」键。";
			Hint.ForeColor = Theme.Dim;
			Hint.Font = Theme.SmallFont;
			Hint.AutoSize = false;
			Hint.Location = new Point(24, 50);
			Hint.Size = new Size(330, 40);
			Controls.Add(Hint);

			Label big = new Label();
			big.Text = "…";
			big.Font = new Font(Theme.UiFont.FontFamily, 22f, FontStyle.Bold);
			big.ForeColor = Theme.Accent;
			big.TextAlign = ContentAlignment.MiddleCenter;
			big.Location = new Point(24, 92);
			big.Size = new Size(240, 44);
			big.Name = "bigLabel";
			Controls.Add(big);

			FlatButton cancel = new FlatButton();
			cancel.Text = "取消";
			cancel.Size = new Size(80, 32);
			cancel.Location = new Point(ClientSize.Width - 104, 100);
			cancel.Click += delegate { Close(); };
			Controls.Add(cancel);
		}

		private static bool IsModifier(Keys key)
		{
			return key == Keys.Control || key == Keys.LControlKey || key == Keys.RControlKey
				|| key == Keys.Alt || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu
				|| key == Keys.Shift || key == Keys.LShiftKey || key == Keys.RShiftKey
				|| key == Keys.LWin || key == Keys.RWin;
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			e.Handled = true;
			e.SuppressKeyPress = true;

			if (e.KeyCode == Keys.Escape)
			{
				Close();
				return;
			}
			if (IsModifier(e.KeyCode))
			{
				ShowPending(ModifierName(e.KeyCode));
				return;
			}
			ComboFormed = true;
			string text = BuildText(e.KeyData, e.KeyCode);
			ShowPending(text);
			Result = text;
			DialogResult = DialogResult.OK;
			Close();
		}

		protected override void OnKeyUp(KeyEventArgs e)
		{
			base.OnKeyUp(e);
			if (!IsModifier(e.KeyCode) || ComboFormed)
			{
				return;
			}
			string text = ModifierName(e.KeyCode);
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			Result = text;
			DialogResult = DialogResult.OK;
			Close();
		}

		private void ShowPending(string text)
		{
			foreach (Control control in Controls)
			{
				if (control.Name == "bigLabel")
				{
					control.Text = text;
				}
			}
		}

		private static string ModifierName(Keys key)
		{
			if (key == Keys.Control || key == Keys.LControlKey || key == Keys.RControlKey)
			{
				return "Ctrl";
			}
			if (key == Keys.Alt || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu)
			{
				return "Alt";
			}
			if (key == Keys.Shift || key == Keys.LShiftKey || key == Keys.RShiftKey)
			{
				return "Shift";
			}
			if (key == Keys.LWin || key == Keys.RWin)
			{
				return "Win";
			}
			return null;
		}

		private static string BuildText(Keys data, Keys keyCode)
		{
			string baseName = BaseName(keyCode);
			System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
			if ((data & Keys.Control) == Keys.Control)
			{
				parts.Add("Ctrl");
			}
			if ((data & Keys.Alt) == Keys.Alt)
			{
				parts.Add("Alt");
			}
			if ((data & Keys.Shift) == Keys.Shift)
			{
				parts.Add("Shift");
			}
			parts.Add(baseName);
			return string.Join("+", parts.ToArray());
		}

		private static string BaseName(Keys keyCode)
		{
			if (keyCode >= Keys.D0 && keyCode <= Keys.D9)
			{
				return ((int)(keyCode - Keys.D0)).ToString();
			}
			if (keyCode >= Keys.NumPad0 && keyCode <= Keys.NumPad9)
			{
				return "NumPad" + ((int)(keyCode - Keys.NumPad0)).ToString();
			}
			if (keyCode >= Keys.A && keyCode <= Keys.Z)
			{
				return keyCode.ToString();
			}
			if (keyCode >= Keys.F1 && keyCode <= Keys.F24)
			{
				return keyCode.ToString();
			}
			string name = keyCode.ToString();
			if (name == "Return")
			{
				return "Enter";
			}
			if (name == "Escape")
			{
				return "Esc";
			}
			if (name == "Oemtilde")
			{
				return "`";
			}
			return name;
		}
	}
}
