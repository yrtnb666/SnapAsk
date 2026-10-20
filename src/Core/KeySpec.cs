using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SnapAsk.Core
{
	/// <summary>
	/// 一个快捷键描述（如 "Alt"、"Ctrl+Alt+Q"）的解析、显示和匹配。
	/// 规则：字符串按 '+' 分段，最后一段是「主键」，前面都是修饰键。
	/// 只有修饰键时（如 "Alt"、"Ctrl+Shift"），最后那个修饰键当主键用。
	/// </summary>
	public sealed class KeySpec
	{
		public bool Ctrl;

		public bool Alt;

		public bool Shift;

		public bool Win;

		/// <summary>主键的虚拟键码（已归一化：左右 Ctrl/Alt/Shift 都归到通用码）。</summary>
		public int Vk;

		/// <summary>规范化后的显示文本。</summary>
		public string Display;

		public static KeySpec Parse(string text)
		{
			if (text == null)
			{
				return null;
			}
			string[] parts = text.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
			{
				return null;
			}
			KeySpec spec = new KeySpec();
			string baseToken = null;
			bool baseIsModifier = false;
			for (int i = 0; i < parts.Length; i++)
			{
				string token = parts[i].Trim();
				string lower = token.ToLowerInvariant();
				bool isLast = (i == parts.Length - 1);
				int modifierVk = ModifierVk(lower);
				if (modifierVk > 0 && !isLast)
				{
					spec.SetModifier(modifierVk);
					continue;
				}
				if (isLast)
				{
					baseToken = token;
					baseIsModifier = (modifierVk > 0);
					if (baseIsModifier)
					{
						spec.Vk = modifierVk;
						spec.SetModifier(modifierVk);
					}
					else
					{
						int vk = VirtualKey(token);
						if (vk <= 0)
						{
							return null;
						}
						spec.Vk = vk;
					}
				}
			}
			if (spec.Vk <= 0)
			{
				return null;
			}
			spec.Display = spec.BuildDisplay(baseToken, baseIsModifier);
			return spec;
		}

		private void SetModifier(int vk)
		{
			if (vk == 17)
			{
				Ctrl = true;
			}
			else if (vk == 18)
			{
				Alt = true;
			}
			else if (vk == 16)
			{
				Shift = true;
			}
			else if (vk == 91)
			{
				Win = true;
			}
		}

		private string BuildDisplay(string baseToken, bool baseIsModifier)
		{
			List<string> pieces = new List<string>();
			if (Ctrl && !(baseIsModifier && Vk == 17))
			{
				pieces.Add("Ctrl");
			}
			if (Alt && !(baseIsModifier && Vk == 18))
			{
				pieces.Add("Alt");
			}
			if (Shift && !(baseIsModifier && Vk == 16))
			{
				pieces.Add("Shift");
			}
			if (Win && !(baseIsModifier && Vk == 91))
			{
				pieces.Add("Win");
			}
			pieces.Add(KeyName(Vk, baseToken));
			return string.Join("+", pieces.ToArray());
		}

		private static int ModifierVk(string lower)
		{
			if (lower == "ctrl" || lower == "control" || lower == "ctl")
			{
				return 17;
			}
			if (lower == "alt" || lower == "menu")
			{
				return 18;
			}
			if (lower == "shift")
			{
				return 16;
			}
			if (lower == "win" || lower == "windows" || lower == "super" || lower == "cmd")
			{
				return 91;
			}
			return 0;
		}

		/// <summary>主键名 → 虚拟键码。只支持 Keys 枚举里能找到的名字。</summary>
		private static int VirtualKey(string token)
		{
			if (string.IsNullOrEmpty(token))
			{
				return 0;
			}
			string name = token.Trim();
			if (name.Length == 1)
			{
				char c = char.ToUpperInvariant(name[0]);
				if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
				{
					return (int)c;
				}
				if (c == '=')
				{
					return 0xBB;
				}
				if (c == '-')
				{
					return 0xBD;
				}
				if (c == ',')
				{
					return 0xBC;
				}
				if (c == '.')
				{
					return 0xBE;
				}
				if (c == '/')
				{
					return 0xBF;
				}
				if (c == ';')
				{
					return 0xBA;
				}
				if (c == '`')
				{
					return 0xC0;
				}
				if (c == '[')
				{
					return 0xDB;
				}
				if (c == ']')
				{
					return 0xDD;
				}
			}
			string alias = AliasToKeysName(name);
			try
			{
				Keys key = (Keys)Enum.Parse(typeof(Keys), alias, true);
				return (int)key;
			}
			catch
			{
				return 0;
			}
		}

		private static string AliasToKeysName(string name)
		{
			string lower = name.ToLowerInvariant();
			if (lower == "esc" || lower == "escape")
			{
				return "Escape";
			}
			if (lower == "space")
			{
				return "Space";
			}
			if (lower == "enter" || lower == "return")
			{
				return "Enter";
			}
			if (lower == "tab")
			{
				return "Tab";
			}
			if (lower == "del" || lower == "delete")
			{
				return "Delete";
			}
			if (lower == "ins" || lower == "insert")
			{
				return "Insert";
			}
			if (lower == "pgup")
			{
				return "PageUp";
			}
			if (lower == "pgdn")
			{
				return "PageDown";
			}
			if (lower == "up")
			{
				return "Up";
			}
			if (lower == "down")
			{
				return "Down";
			}
			if (lower == "left")
			{
				return "Left";
			}
			if (lower == "right")
			{
				return "Right";
			}
			if (lower == "back" || lower == "backspace")
			{
				return "Back";
			}
			return name;
		}

		/// <summary>虚拟键码 → 显示名。</summary>
		public static string KeyName(int vk, string fallback)
		{
			if (vk == 17)
			{
				return "Ctrl";
			}
			if (vk == 18)
			{
				return "Alt";
			}
			if (vk == 16)
			{
				return "Shift";
			}
			if (vk == 91)
			{
				return "Win";
			}
			if (vk >= 0x30 && vk <= 0x39)
			{
				return ((char)vk).ToString();
			}
			if (vk >= 0x41 && vk <= 0x5A)
			{
				return ((char)vk).ToString();
			}
			string name = ((Keys)vk).ToString();
			if (string.Equals(name, "Return", StringComparison.OrdinalIgnoreCase))
			{
				return "Enter";
			}
			if (string.Equals(name, "Escape", StringComparison.OrdinalIgnoreCase))
			{
				return "Esc";
			}
			if (string.Equals(name, "Oemtilde", StringComparison.OrdinalIgnoreCase))
			{
				return "`";
			}
			return ((name == null || name.Length == 0) ? fallback : name);
		}

		/// <summary>归一化虚拟键码：左右 Alt/Ctrl/Shift/Win 合并成通用码。</summary>
		public static int Normalize(int vk)
		{
			if (vk == 160 || vk == 161)
			{
				return 16;
			}
			if (vk == 162 || vk == 163)
			{
				return 17;
			}
			if (vk == 164 || vk == 165)
			{
				return 18;
			}
			if (vk == 92)
			{
				return 91;
			}
			return vk;
		}

		/// <summary>当前按下的键（已归一化）是否命中本组合。</summary>
		public bool Matches(int vk, bool ctrl, bool alt, bool shift, bool win)
		{
			if (vk != Vk)
			{
				return false;
			}
			if (Ctrl != ctrl || Alt != alt || Shift != shift || Win != win)
			{
				return false;
			}
			return true;
		}

		public override string ToString()
		{
			return Display;
		}
	}
}
