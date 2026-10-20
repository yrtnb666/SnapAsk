using System;
using System.IO;

namespace SnapAsk.Util
{
	/// <summary>
	/// 配置：%APPDATA%\SnapAsk\config.json。设置窗口里改，保存即生效。
	/// 反序列化失败或字段非法时由 Normalize() 回落到默认值。
	/// </summary>
	public class Settings
	{
		/// <summary>按住即进入框选的按键，例：Alt / F6 / Ctrl+Shift。</summary>
		public string ShotHoldKey { get; set; }

		/// <summary>划词提问的快捷键（点按），例：Ctrl+Alt+Q。</summary>
		public string TextHotkey { get; set; }

		/// <summary>web = 驱动 DeepSeek 网页版；api = OpenAI 兼容接口。</summary>
		public string Engine { get; set; }

		public string ApiBase { get; set; }

		public string ApiKey { get; set; }

		public string ApiModel { get; set; }

		/// <summary>截图后一起发给模型的问题文本。</summary>
		public string ImagePrompt { get; set; }

		/// <summary>划词提问时的包装模板，{text} 会被替换成选中的文字。</summary>
		public string TextPrompt { get; set; }

		/// <summary>Edge / Chrome 远程调试端口（和 EAgentX 各版本错开）。</summary>
		public int CdpPort { get; set; }

		/// <summary>自定义 msedge.exe / chrome.exe，留空自动探测。</summary>
		public string EdgePath { get; set; }

		public string StartUrl { get; set; }

		public int ReplyTimeoutSec { get; set; }

		/// <summary>命中网页版限流后自动等待重发的次数；0 表示不重试。</summary>
		public int RateLimitRetries { get; set; }

		public int RateLimitWaitSec { get; set; }

		/// <summary>答案出来时自动弹出结果窗。</summary>
		public bool AutoShowAnswer { get; set; }

		/// <summary>结果窗保持置顶。</summary>
		public bool AnswerOnTop { get; set; }

		/// <summary>Edge 启动时隐藏自动化标识，并用 --app 打开。</summary>
		public bool StealthFlags { get; set; }

		/// <summary>连上浏览器后把它嵌进主界面左侧。</summary>
		public bool EmbedBrowser { get; set; }

		/// <summary>嵌入时浏览器面板的宽度（像素）。</summary>
		public int BrowserWidth { get; set; }

		/// <summary>light / dark。</summary>
		public string Theme { get; set; }

		public Selectors Sel { get; set; }

		public static string Dir
		{
			get
			{
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapAsk");
			}
		}

		public static string ConfigPath
		{
			get
			{
				return Path.Combine(Dir, "config.json");
			}
		}

		/// <summary>网页版用的独立浏览器配置目录，和用户日常浏览器互不干扰。</summary>
		public static string ProfileDir
		{
			get
			{
				return Path.Combine(Dir, "edge-profile");
			}
		}

		/// <summary>截图暂存目录；发给网页版后保留，方便复用。</summary>
		public static string ShotDir
		{
			get
			{
				return Path.Combine(Dir, "shots");
			}
		}

		public Settings()
		{
			ShotHoldKey = "Alt";
			TextHotkey = "Ctrl+Alt+Q";
			Engine = "web";
			ApiBase = "https://api.deepseek.com/v1";
			ApiKey = "";
			ApiModel = "deepseek-chat";
			ImagePrompt = "请解答图片中的题目。先给出最终答案，再给出简要步骤和解析。";
			TextPrompt = "请解答下面的题目。先给出最终答案，再给出简要步骤和解析：\r\n\r\n{text}";
			CdpPort = 9666;
			EdgePath = "";
			StartUrl = "https://chat.deepseek.com/";
			ReplyTimeoutSec = 300;
			RateLimitRetries = 2;
			RateLimitWaitSec = 60;
			AutoShowAnswer = true;
			AnswerOnTop = true;
			StealthFlags = true;
			EmbedBrowser = true;
			BrowserWidth = 470;
			Theme = "light";
			Sel = new Selectors();
		}

		public static Settings Load()
		{
			Settings loaded = null;
			try
			{
				if (File.Exists(ConfigPath))
				{
					string json = File.ReadAllText(ConfigPath);
					loaded = Json.Decode<Settings>(json);
				}
			}
			catch
			{
				loaded = null;
			}
			if (loaded == null)
			{
				loaded = new Settings();
			}
			loaded.Normalize();
			return loaded;
		}

		public Settings Clone()
		{
			Settings copy = new Settings();
			copy.ShotHoldKey = ShotHoldKey;
			copy.TextHotkey = TextHotkey;
			copy.Engine = Engine;
			copy.ApiBase = ApiBase;
			copy.ApiKey = ApiKey;
			copy.ApiModel = ApiModel;
			copy.ImagePrompt = ImagePrompt;
			copy.TextPrompt = TextPrompt;
			copy.CdpPort = CdpPort;
			copy.EdgePath = EdgePath;
			copy.StartUrl = StartUrl;
			copy.ReplyTimeoutSec = ReplyTimeoutSec;
			copy.RateLimitRetries = RateLimitRetries;
			copy.RateLimitWaitSec = RateLimitWaitSec;
			copy.AutoShowAnswer = AutoShowAnswer;
			copy.AnswerOnTop = AnswerOnTop;
			copy.StealthFlags = StealthFlags;
			copy.EmbedBrowser = EmbedBrowser;
			copy.BrowserWidth = BrowserWidth;
			copy.Theme = Theme;
			copy.Sel = ((Sel == null) ? new Selectors() : Sel.Clone());
			return copy;
		}

		/// <summary>把所有非法值收回到合理范围，缺的选择器补回默认值。</summary>
		public void Normalize()
		{
			if (string.IsNullOrEmpty(Engine))
			{
				Engine = "web";
			}
			if (string.IsNullOrEmpty(ShotHoldKey))
			{
				ShotHoldKey = "Alt";
			}
			if (string.IsNullOrEmpty(TextHotkey))
			{
				TextHotkey = "Ctrl+Alt+Q";
			}
			if (string.IsNullOrEmpty(ApiBase))
			{
				ApiBase = "https://api.deepseek.com/v1";
			}
			if (string.IsNullOrEmpty(ApiModel))
			{
				ApiModel = "deepseek-chat";
			}
			if (string.IsNullOrEmpty(ImagePrompt))
			{
				ImagePrompt = "请解答图片中的题目。先给出最终答案，再给出简要步骤和解析。";
			}
			if (string.IsNullOrEmpty(TextPrompt) || TextPrompt.IndexOf("{text}", StringComparison.Ordinal) < 0)
			{
				TextPrompt = "请解答下面的题目。先给出最终答案，再给出简要步骤和解析：\r\n\r\n{text}";
			}
			if (string.IsNullOrEmpty(StartUrl))
			{
				StartUrl = "https://chat.deepseek.com/";
			}
			if (CdpPort < 1 || CdpPort > 65535)
			{
				CdpPort = 9666;
			}
			if (BrowserWidth < 280)
			{
				BrowserWidth = 280;
			}
			if (BrowserWidth > 1600)
			{
				BrowserWidth = 1600;
			}
			if (ReplyTimeoutSec < 30)
			{
				ReplyTimeoutSec = 30;
			}
			if (ReplyTimeoutSec > 3600)
			{
				ReplyTimeoutSec = 3600;
			}
			if (RateLimitRetries < 0)
			{
				RateLimitRetries = 0;
			}
			if (RateLimitRetries > 10)
			{
				RateLimitRetries = 10;
			}
			if (RateLimitWaitSec < 10)
			{
				RateLimitWaitSec = 10;
			}
			if (RateLimitWaitSec > 1800)
			{
				RateLimitWaitSec = 1800;
			}
			if (!string.Equals(Theme, "dark", StringComparison.OrdinalIgnoreCase))
			{
				Theme = "light";
			}
			if (Sel == null)
			{
				Sel = new Selectors();
			}
			Selectors defaults = new Selectors();
			if (string.IsNullOrEmpty(Sel.Input))
			{
				Sel.Input = defaults.Input;
			}
			if (string.IsNullOrEmpty(Sel.Send))
			{
				Sel.Send = defaults.Send;
			}
			if (string.IsNullOrEmpty(Sel.Reply))
			{
				Sel.Reply = defaults.Reply;
			}
			if (string.IsNullOrEmpty(Sel.Generating))
			{
				Sel.Generating = defaults.Generating;
			}
		}

		public void Save()
		{
			try
			{
				if (!Directory.Exists(Dir))
				{
					Directory.CreateDirectory(Dir);
				}
				string tempPath = ConfigPath + ".tmp";
				File.WriteAllText(tempPath, Json.Encode(this));
				bool replaced = false;
				try
				{
					if (File.Exists(ConfigPath))
					{
						File.Replace(tempPath, ConfigPath, null);
					}
					else
					{
						File.Move(tempPath, ConfigPath);
					}
					replaced = true;
				}
				catch
				{
				}
				if (!replaced)
				{
					File.Copy(tempPath, ConfigPath, true);
					try
					{
						File.Delete(tempPath);
					}
					catch
					{
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error("保存配置失败: " + ex.Message);
			}
		}
	}
}
