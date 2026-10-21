using System;
using System.Collections.Generic;
using System.Text;

namespace SnapAsk.Util
{
	/// <summary>
	/// 把网页返回的 TeX 公式压成答案卡片能直接阅读的普通文本。
	/// 不追求排版还原，只处理答案里最常见的写法：
	/// \sqrt{3} → √3、\frac{11}{6} → 11/6、a \in [1,3] → a ∈ [1,3]。
	/// </summary>
	public static class ReadableMath
	{
		private static readonly Dictionary<string, string> Commands = BuildCommands();

		private const string SuperChars = "0123456789+-=()ni−";
		private const string SuperMap = "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼⁽⁾ⁿⁱ⁻";
		private const string SubChars = "0123456789+-=()aehijklmnoprstuvx−";
		private const string SubMap = "₀₁₂₃₄₅₆₇₈₉₊₋₌₍₎ₐₑₕᵢⱼₖₗₘₙₒₚᵣₛₜᵤᵥₓ₋";

		/// <summary>把答案里的 $...$ / $$...$$ 公式转成可读文本；代码块原样保留。</summary>
		public static string Convert(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return text ?? "";
			}

			StringBuilder output = new StringBuilder(text.Length);
			int i = 0;
			while (i < text.Length)
			{
				char c = text[i];

				// 代码块/行内代码里的 $ 不是公式，原样复制。
				if (c == '`')
				{
					int run = 1;
					while (i + run < text.Length && text[i + run] == '`')
					{
						run++;
					}
					string fence = new string('`', run);
					int codeEnd = text.IndexOf(fence, i + run, StringComparison.Ordinal);
					if (codeEnd >= 0)
					{
						output.Append(text, i, codeEnd + run - i);
						i = codeEnd + run;
						continue;
					}
				}

				// 有些网页/接口用 \(...\) 或 \[...\] 包裹公式。
				if (c == '\\' && i + 1 < text.Length && (text[i + 1] == '(' || text[i + 1] == '['))
				{
					bool display = text[i + 1] == '[';
					string close = display ? "\\]" : "\\)";
					int mathEnd = FindUnescaped(text, close, i + 2);
					if (mathEnd >= 0)
					{
						string formula = text.Substring(i + 2, mathEnd - (i + 2));
						string readable = ConvertFormula(formula, display).Trim();
						if (display)
						{
							if (readable.Length > 0)
							{
								if (output.Length > 0 && output[output.Length - 1] != '\n')
								{
									output.Append('\n');
								}
								output.Append(readable);
								output.Append('\n');
							}
						}
						else
						{
							output.Append(readable);
						}
						i = mathEnd + close.Length;
						continue;
					}
					i += 2;
					continue;
				}

				if (c != '$')
				{
					output.Append(c);
					i++;
					continue;
				}

				if (i + 1 < text.Length && text[i + 1] == '$')
				{
					int displayEnd = FindUnescaped(text, "$$", i + 2);
					if (displayEnd >= 0)
					{
						string formula = text.Substring(i + 2, displayEnd - (i + 2));
						string readable = ConvertFormula(formula, true).Trim();
						if (readable.Length > 0)
						{
							if (output.Length > 0 && output[output.Length - 1] != '\n')
							{
								output.Append('\n');
							}
							output.Append(readable);
							output.Append('\n');
						}
						i = displayEnd + 2;
						continue;
					}
					// 流式生成时可能只有一个未闭合的 $$，先丢掉。
					i += 2;
					continue;
				}

				int inlineEnd = FindUnescaped(text, "$", i + 1);
				if (inlineEnd >= 0)
				{
					string formula = text.Substring(i + 1, inlineEnd - (i + 1));
					output.Append(ConvertFormula(formula, false));
					i = inlineEnd + 1;
					continue;
				}

				// 未配对的 $ 通常来自流式截断，不显示。
				i++;
			}

			return Normalize(output.ToString());
		}

		private static int FindUnescaped(string text, string needle, int start)
		{
			int at = start;
			while (at <= text.Length - needle.Length)
			{
				int found = text.IndexOf(needle, at, StringComparison.Ordinal);
				if (found < 0)
				{
					return -1;
				}
				int slashes = 0;
				for (int k = found - 1; k >= 0 && text[k] == '\\'; k--)
				{
					slashes++;
				}
				if ((slashes & 1) == 0)
				{
					return found;
				}
				at = found + needle.Length;
			}
			return -1;
		}

		private static string ConvertFormula(string source, bool display)
		{
			if (string.IsNullOrEmpty(source))
			{
				return "";
			}

			StringBuilder output = new StringBuilder(source.Length);
			int i = 0;
			while (i < source.Length)
			{
				char c = source[i];

				if (c == '\\')
				{
					if (i + 1 >= source.Length)
					{
						i++;
						continue;
					}

					char next = source[i + 1];
					if (next == '\\')
					{
						output.Append('\n');
						i += 2;
						continue;
					}
					if (next == ' ' || next == ',' || next == ';' || next == ':')
					{
						output.Append(' ');
						i += 2;
						continue;
					}
					if (next == '!')
					{
						i += 2;
						continue;
					}
					if (next == '{' || next == '}' || next == '%' || next == '$' || next == '&'
						|| next == '#' || next == '_')
					{
						output.Append(next);
						i += 2;
						continue;
					}

					if (char.IsLetter(next))
					{
						int commandStart = i + 1;
						i = commandStart;
						while (i < source.Length && char.IsLetter(source[i]))
						{
							i++;
						}
						string command = source.Substring(commandStart, i - commandStart);
						if (i < source.Length && source[i] == '*')
						{
							i++;
						}
						AppendCommand(output, command, source, ref i);
						continue;
					}

					// \|、\# 之外的单字符转义，保留字符本身。
					output.Append(next);
					i += 2;
					continue;
				}

				if (c == '{')
				{
					string inner = ReadGroup(source, ref i);
					output.Append(ConvertFormula(inner, false));
					continue;
				}
				if (c == '}')
				{
					i++;
					continue;
				}
				if (c == '^' || c == '_')
				{
					i++;
					string argument = ReadArgument(source, ref i);
					string converted = ConvertFormula(argument, false);
					AppendScript(output, converted, c == '^');
					continue;
				}
				if (c == '&' || c == '~')
				{
					output.Append(' ');
					i++;
					continue;
				}
				if (c == '-')
				{
					output.Append('−');
					i++;
					continue;
				}
				if (c == '*')
				{
					output.Append('·');
					i++;
					continue;
				}

				output.Append(c);
				i++;
			}

			return output.ToString();
		}

		private static void AppendCommand(StringBuilder output, string command, string source, ref int i)
		{
			switch (command)
			{
				case "frac":
				case "dfrac":
				case "tfrac":
				case "cfrac":
				{
					string numerator = ConvertFormula(ReadGroup(source, ref i), false);
					string denominator = ConvertFormula(ReadGroup(source, ref i), false);
					output.Append(Fraction(numerator, denominator));
					return;
				}
				case "sqrt":
				{
					string root = ReadOptional(source, ref i);
					string radicand = ConvertFormula(ReadGroup(source, ref i), false);
					string prefix = "√";
					string trimmedRoot = root.Trim();
					if (trimmedRoot.Length > 0)
					{
						prefix = trimmedRoot == "3" ? "∛" : "√[" + ConvertFormula(root, false) + "]";
					}
					output.Append(prefix);
					output.Append(WrapRadicand(radicand));
					return;
				}
				case "text":
				case "textrm":
				case "textnormal":
				case "mbox":
					output.Append(UnescapeText(ReadGroup(source, ref i)));
					return;
				case "mathrm":
				case "mathbf":
				case "mathit":
				case "mathsf":
				case "mathtt":
				case "mathcal":
				case "mathscr":
				case "operatorname":
					output.Append(ConvertFormula(ReadGroup(source, ref i), false));
					return;
				case "mathbb":
					output.Append(Blackboard(ReadGroup(source, ref i)));
					return;
				case "boxed":
					output.Append(ConvertFormula(ReadGroup(source, ref i), false));
					return;
				case "overline":
				case "bar":
					AppendAccent(output, ConvertFormula(ReadGroup(source, ref i), false), '\u0305');
					return;
				case "hat":
				case "widehat":
					AppendAccent(output, ConvertFormula(ReadGroup(source, ref i), false), '\u0302');
					return;
				case "vec":
				case "overrightarrow":
					AppendAccent(output, ConvertFormula(ReadGroup(source, ref i), false), '\u20D7');
					return;
				case "dot":
					AppendAccent(output, ConvertFormula(ReadGroup(source, ref i), false), '\u0307');
					return;
				case "ddot":
					AppendAccent(output, ConvertFormula(ReadGroup(source, ref i), false), '\u0308');
					return;
				case "left":
				case "right":
					output.Append(ReadDelimiter(source, ref i));
					return;
				case "begin":
				case "end":
					ReadGroup(source, ref i);
					return;
				case "color":
					ReadGroup(source, ref i);
					return;
				case "textcolor":
					ReadGroup(source, ref i);
					output.Append(ConvertFormula(ReadGroup(source, ref i), false));
					return;
				case "colorbox":
					ReadGroup(source, ref i);
					output.Append(ConvertFormula(ReadGroup(source, ref i), false));
					return;
				case "phantom":
				case "hphantom":
				case "vphantom":
				case "label":
					ReadGroup(source, ref i);
					return;
				case "tag":
					output.Append(" (");
					output.Append(ConvertFormula(ReadGroup(source, ref i), false));
					output.Append(')');
					return;
				case "pmod":
					output.Append(" (mod ");
					output.Append(ConvertFormula(ReadGroup(source, ref i), false));
					output.Append(')');
					return;
				case "bmod":
				case "mod":
					output.Append(" mod ");
					return;
				case "limits":
				case "nolimits":
				case "displaystyle":
				case "textstyle":
				case "scriptstyle":
				case "scriptscriptstyle":
					return;
				case "not":
					AppendNot(output, source, ref i);
					return;
			}

			string mapped;
			if (Commands.TryGetValue(command, out mapped))
			{
				output.Append(mapped);
			}
			else
			{
				// 不认识的命令至少不要留下反斜杠，保留命令名。
				output.Append(command);
			}
		}

		private static void AppendNot(StringBuilder output, string source, ref int i)
		{
			SkipSpaces(source, ref i);
			if (i >= source.Length)
			{
				output.Append('¬');
				return;
			}
			if (source[i] == '=')
			{
				output.Append('≠');
				i++;
				return;
			}
			if (source[i] == '\\')
			{
				int start = i;
				i++;
				if (i < source.Length && char.IsLetter(source[i]))
				{
					while (i < source.Length && char.IsLetter(source[i]))
					{
						i++;
					}
				}
				else if (i < source.Length)
				{
					i++;
				}
				string inner = source.Substring(start, i - start);
				if (inner == "\\in")
				{
					output.Append('∉');
				}
				else if (inner == "\\subset")
				{
					output.Append('⊄');
				}
				else if (inner == "\\supset")
				{
					output.Append('⊅');
				}
				else
				{
					output.Append('¬');
				}
				return;
			}
			output.Append('¬');
		}

		private static void AppendAccent(StringBuilder output, string text, char accent)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			if (text.Length == 1)
			{
				output.Append(text);
				output.Append(accent);
				return;
			}
			output.Append(text);
			output.Append(accent);
		}

		private static string Fraction(string numerator, string denominator)
		{
			if (string.IsNullOrEmpty(denominator))
			{
				return numerator;
			}
			return WrapFractionPart(numerator) + "/" + WrapFractionPart(denominator);
		}

		private static string WrapFractionPart(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return "0";
			}
			if (IsSimple(text))
			{
				return text;
			}
			if (text[0] == '-' || text[0] == '−')
			{
				return text;
			}
			return "(" + text + ")";
		}

		private static string WrapRadicand(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return "";
			}
			if ((IsSimple(text) && text.IndexOf('/') < 0)
				|| (text[0] == '(' && text[text.Length - 1] == ')'))
			{
				return text;
			}
			return "(" + text + ")";
		}

		private static bool IsSimple(string text)
		{
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (char.IsLetterOrDigit(c) || char.IsNumber(c) || c == '.' || c == '/'
					|| c == '√' || c == '°' || c == '′')
				{
					continue;
				}
				return false;
			}
			return true;
		}

		private static void AppendScript(StringBuilder output, string text, bool superscript)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			if (superscript && (text == "°" || text == "∘" || text == "′" || text == "″"))
			{
				output.Append(text == "∘" ? "°" : text);
				return;
			}
			string chars = superscript ? SuperChars : SubChars;
			string map = superscript ? SuperMap : SubMap;
			bool canMap = true;
			for (int i = 0; i < text.Length; i++)
			{
				if (chars.IndexOf(text[i]) < 0)
				{
					canMap = false;
					break;
				}
			}
			if (canMap)
			{
				for (int i = 0; i < text.Length; i++)
				{
					output.Append(map[chars.IndexOf(text[i])]);
				}
				return;
			}

			output.Append(superscript ? '^' : '_');
			if (text.Length > 1 && text[0] == '(' && text[text.Length - 1] == ')')
			{
				output.Append(text);
			}
			else
			{
				output.Append('(');
				output.Append(text);
				output.Append(')');
			}
		}

		private static string Blackboard(string text)
		{
			switch (text.Trim())
			{
				case "R":
					return "ℝ";
				case "N":
					return "ℕ";
				case "Z":
					return "ℤ";
				case "Q":
					return "ℚ";
				case "C":
					return "ℂ";
				case "P":
					return "ℙ";
				default:
					return text;
			}
		}

		private static string ReadGroup(string source, ref int i)
		{
			SkipSpaces(source, ref i);
			if (i >= source.Length)
			{
				return "";
			}
			if (source[i] == '{')
			{
				int start = i + 1;
				int depth = 0;
				i++;
				while (i < source.Length)
				{
					char c = source[i];
					if (c == '\\')
					{
						i += 2;
						continue;
					}
					if (c == '{')
					{
						depth++;
					}
					else if (c == '}')
					{
						if (depth == 0)
						{
							string inner = source.Substring(start, i - start);
							i++;
							return inner;
						}
						depth--;
					}
					i++;
				}
				return source.Substring(start);
			}

			if (source[i] == '\\')
			{
				int start = i;
				i++;
				if (i < source.Length && char.IsLetter(source[i]))
				{
					while (i < source.Length && char.IsLetter(source[i]))
					{
						i++;
					}
				}
				else if (i < source.Length)
				{
					i++;
				}
				return source.Substring(start, i - start);
			}

			return source[i++].ToString();
		}

		private static string ReadArgument(string source, ref int i)
		{
			SkipSpaces(source, ref i);
			if (i >= source.Length)
			{
				return "";
			}
			if (source[i] == '{')
			{
				return ReadGroup(source, ref i);
			}
			if (source[i] == '\\')
			{
				return ReadGroup(source, ref i);
			}
			char c = source[i];
			i++;
			return c.ToString();
		}

		private static string ReadOptional(string source, ref int i)
		{
			SkipSpaces(source, ref i);
			if (i >= source.Length || source[i] != '[')
			{
				return "";
			}
			int start = i + 1;
			while (i < source.Length && source[i] != ']')
			{
				i++;
			}
			string value = source.Substring(start, i - start);
			if (i < source.Length)
			{
				i++;
			}
			return value;
		}

		private static string ReadDelimiter(string source, ref int i)
		{
			SkipSpaces(source, ref i);
			if (i >= source.Length)
			{
				return "";
			}
			if (source[i] == '\\')
			{
				i++;
				if (i >= source.Length)
				{
					return "";
				}
				if (char.IsLetter(source[i]))
				{
					int start = i;
					while (i < source.Length && char.IsLetter(source[i]))
					{
						i++;
					}
					string command = source.Substring(start, i - start);
					switch (command)
					{
						case "lvert":
						case "rvert":
						case "vert":
							return "|";
						case "langle":
							return "⟨";
						case "rangle":
							return "⟩";
						case "lfloor":
							return "⌊";
						case "rfloor":
							return "⌋";
						case "lceil":
							return "⌈";
						case "rceil":
							return "⌉";
						default:
							return "";
					}
				}
				char escaped = source[i];
				i++;
				if (escaped == '.')
				{
					return "";
				}
				return escaped == '{' ? "{" : escaped == '}' ? "}" : escaped.ToString();
			}
			char delimiter = source[i];
			i++;
			if (delimiter == '.')
			{
				return "";
			}
			return delimiter.ToString();
		}

		private static void SkipSpaces(string source, ref int i)
		{
			while (i < source.Length && (source[i] == ' ' || source[i] == '\t' || source[i] == '\r'
				|| source[i] == '\n'))
			{
				i++;
			}
		}

		private static string UnescapeText(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return "";
			}
			StringBuilder output = new StringBuilder(text.Length);
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (c == '\\' && i + 1 < text.Length)
				{
					char next = text[i + 1];
					if (next == '\\')
					{
						output.Append('\n');
						i++;
						continue;
					}
					if (next == ' ' || next == '{' || next == '}' || next == '%' || next == '$'
						|| next == '&' || next == '#' || next == '_')
					{
						output.Append(next == ' ' ? ' ' : next);
						i++;
						continue;
					}
				}
				output.Append(c);
			}
			return output.ToString();
		}

		private static string Normalize(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return "";
			}
			string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			StringBuilder output = new StringBuilder(text.Length);
			int pendingBlank = 0;
			bool hasContent = false;
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].TrimEnd();
				if (line.Length == 0)
				{
					pendingBlank++;
					continue;
				}
				if (hasContent)
				{
					output.Append('\n');
					if (pendingBlank > 0)
					{
						output.Append('\n');
					}
				}
				output.Append(line);
				hasContent = true;
				pendingBlank = 0;
			}
			return output.ToString();
		}

		private static Dictionary<string, string> BuildCommands()
		{
			Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
			map["pm"] = "±";
			map["mp"] = "∓";
			map["times"] = "×";
			map["cdot"] = "·";
			map["div"] = "÷";
			map["ast"] = "∗";
			map["star"] = "⋆";
			map["circ"] = "∘";
			map["bullet"] = "•";
			map["le"] = "≤";
			map["leq"] = "≤";
			map["ge"] = "≥";
			map["geq"] = "≥";
			map["ne"] = "≠";
			map["neq"] = "≠";
			map["approx"] = "≈";
			map["equiv"] = "≡";
			map["sim"] = "∼";
			map["simeq"] = "≃";
			map["cong"] = "≅";
			map["propto"] = "∝";
			map["in"] = "∈";
			map["notin"] = "∉";
			map["ni"] = "∋";
			map["subset"] = "⊂";
			map["subseteq"] = "⊆";
			map["supset"] = "⊃";
			map["supseteq"] = "⊇";
			map["cup"] = "∪";
			map["cap"] = "∩";
			map["setminus"] = "∖";
			map["emptyset"] = "∅";
			map["varnothing"] = "∅";
			map["infty"] = "∞";
			map["partial"] = "∂";
			map["nabla"] = "∇";
			map["forall"] = "∀";
			map["exists"] = "∃";
			map["nexists"] = "∄";
			map["neg"] = "¬";
			map["land"] = "∧";
			map["lor"] = "∨";
			map["wedge"] = "∧";
			map["vee"] = "∨";
			map["oplus"] = "⊕";
			map["otimes"] = "⊗";
			map["to"] = "→";
			map["rightarrow"] = "→";
			map["leftarrow"] = "←";
			map["gets"] = "←";
			map["leftrightarrow"] = "↔";
			map["Rightarrow"] = "⇒";
			map["Leftarrow"] = "⇐";
			map["Leftrightarrow"] = "⇔";
			map["iff"] = "⇔";
			map["implies"] = "⇒";
			map["mapsto"] = "↦";
			map["uparrow"] = "↑";
			map["downarrow"] = "↓";
			map["updownarrow"] = "↕";
			map["angle"] = "∠";
			map["triangle"] = "△";
			map["perp"] = "⊥";
			map["parallel"] = "∥";
			map["nparallel"] = "∦";
			map["therefore"] = "∴";
			map["because"] = "∵";
			map["degree"] = "°";
			map["ldots"] = "…";
			map["cdots"] = "⋯";
			map["vdots"] = "⋮";
			map["ddots"] = "⋱";
			map["quad"] = "  ";
			map["qquad"] = "    ";
			map["sin"] = "sin";
			map["cos"] = "cos";
			map["tan"] = "tan";
			map["cot"] = "cot";
			map["sec"] = "sec";
			map["csc"] = "csc";
			map["arcsin"] = "arcsin";
			map["arccos"] = "arccos";
			map["arctan"] = "arctan";
			map["log"] = "log";
			map["ln"] = "ln";
			map["lg"] = "lg";
			map["exp"] = "exp";
			map["lim"] = "lim";
			map["max"] = "max";
			map["min"] = "min";
			map["sup"] = "sup";
			map["inf"] = "inf";
			map["det"] = "det";
			map["alpha"] = "α";
			map["beta"] = "β";
			map["gamma"] = "γ";
			map["delta"] = "δ";
			map["epsilon"] = "ε";
			map["varepsilon"] = "ε";
			map["zeta"] = "ζ";
			map["eta"] = "η";
			map["theta"] = "θ";
			map["vartheta"] = "ϑ";
			map["iota"] = "ι";
			map["kappa"] = "κ";
			map["lambda"] = "λ";
			map["mu"] = "μ";
			map["nu"] = "ν";
			map["xi"] = "ξ";
			map["omicron"] = "ο";
			map["pi"] = "π";
			map["varpi"] = "ϖ";
			map["rho"] = "ρ";
			map["varrho"] = "ϱ";
			map["sigma"] = "σ";
			map["varsigma"] = "ς";
			map["tau"] = "τ";
			map["upsilon"] = "υ";
			map["phi"] = "φ";
			map["varphi"] = "ϕ";
			map["chi"] = "χ";
			map["psi"] = "ψ";
			map["omega"] = "ω";
			map["Gamma"] = "Γ";
			map["Delta"] = "Δ";
			map["Theta"] = "Θ";
			map["Lambda"] = "Λ";
			map["Xi"] = "Ξ";
			map["Pi"] = "Π";
			map["Sigma"] = "Σ";
			map["Upsilon"] = "Υ";
			map["Phi"] = "Φ";
			map["Psi"] = "Ψ";
			map["Omega"] = "Ω";
			return map;
		}
	}
}
