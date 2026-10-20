using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SnapAsk.Core;
using SnapAsk.Util;

namespace SnapAsk
{
	/// <summary>
	/// 无界面自检：不依赖浏览器、不弹窗口，验证键位解析、页面脚本、配置读写、截图链路。
	/// 用法: SnapAsk.exe --selftest   结果同时写到 exe 同目录的 selftest.log
	/// </summary>
	internal static class SelfTest
	{
		private static readonly StringBuilder Output = new StringBuilder();

		private static int Passed;

		private static int Failed;

		[DllImport("kernel32.dll")]
		private static extern bool AttachConsole(int processId);

		public static int Run()
		{
			try
			{
				AttachConsole(-1);
			}
			catch
			{
			}

			Line("SnapAsk 自检 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			Line("------------------------------------------------------------");

			Check("键位解析 Alt", delegate
			{
				KeySpec spec = KeySpec.Parse("Alt");
				Assert(spec != null, "解析为 null");
				Assert(spec.Vk == 18, "Vk 应为 18，实际 " + spec.Vk);
				Assert(spec.Alt, "Alt 标志缺失");
				Assert(spec.Display == "Alt", "显示名应为 Alt，实际 " + spec.Display);
			});

			Check("键位解析 Ctrl+Alt+Q", delegate
			{
				KeySpec spec = KeySpec.Parse("Ctrl+Alt+Q");
				Assert(spec != null, "解析为 null");
				Assert(spec.Vk == 81, "Vk 应为 81，实际 " + spec.Vk);
				Assert(spec.Ctrl && spec.Alt && !spec.Shift, "修饰键标志不对");
				Assert(spec.Display == "Ctrl+Alt+Q", "显示名不对: " + spec.Display);
				Assert(spec.Matches(81, true, true, false, false), "应命中 Ctrl+Alt+Q");
				Assert(!spec.Matches(81, true, false, false, false), "不该只按 Ctrl+Q 就命中");
			});

			Check("键位解析 F6 / Ctrl+Shift", delegate
			{
				KeySpec f6 = KeySpec.Parse("F6");
				Assert(f6 != null && f6.Vk == 117, "F6 虚键码应为 117");
				KeySpec combo = KeySpec.Parse("Ctrl+Shift");
				Assert(combo != null && combo.Vk == 16, "Ctrl+Shift 主键应为 Shift(16)");
				Assert(combo.Ctrl && combo.Shift, "Ctrl+Shift 修饰键标志不对");
				Assert(combo.Display == "Ctrl+Shift", "显示名不对: " + combo.Display);
			});

			Check("非法键位返回 null", delegate
			{
				Assert(KeySpec.Parse("") == null, "空串应返回 null");
				Assert(KeySpec.Parse("NoSuchKey") == null, "未知键名应返回 null");
			});

			Check("页面脚本注入串可用", delegate
			{
				string script = PageScript.Build(new Selectors());
				Assert(script.Length > 3000, "脚本太短，可能没读到内嵌资源");
				Assert(script.IndexOf("__CFG__", StringComparison.Ordinal) < 0, "__CFG__ 没有被替换");
				Assert(script.IndexOf("window.__snapask", StringComparison.Ordinal) > 0, "缺少 __snapask 挂载");
				Assert(script.IndexOf("\"input\"", StringComparison.Ordinal) > 0, "选择器 JSON 没写进去");
			});

			Check("配置归一化", delegate
			{
				Settings settings = new Settings();
				settings.CdpPort = 0;
				settings.ReplyTimeoutSec = 5;
				settings.TextPrompt = "没有占位符";
				settings.ShotHoldKey = "";
				settings.Normalize();
				Assert(settings.CdpPort == 9666, "端口应回落到 9666");
				Assert(settings.ReplyTimeoutSec == 30, "超时应回落到 30");
				Assert(settings.TextPrompt.IndexOf("{text}", StringComparison.Ordinal) >= 0, "模板应补回 {text}");
				Assert(settings.ShotHoldKey == "Alt", "截图键应回落到 Alt");
			});

			Check("配置 JSON 往返", delegate
			{
				Settings source = new Settings();
				source.ShotHoldKey = "F6";
				source.CdpPort = 9777;
				string json = Json.Encode(source);
				Settings back = Json.Decode<Settings>(json);
				Assert(back != null, "反序列化失败");
				Assert(back.ShotHoldKey == "F6", "ShotHoldKey 丢了");
				Assert(back.CdpPort == 9777, "CdpPort 丢了");
				Assert(back.Sel != null && back.Sel.Input != null, "选择器丢了");
				Assert(back.TextPrompt != null && back.TextPrompt.IndexOf("{text}", StringComparison.Ordinal) >= 0,
					"提示词模板丢了");
			});

			Check("题目摘要", delegate
			{
				Question question = new Question();
				question.Source = "  1 + 1 = ?  ";
				Assert(question.Caption == "1 + 1 = ?", "摘要应去掉首尾空白，实际: " + question.Caption);
				question.ImagePath = "x.png";
				Assert(question.HasImage, "HasImage 判断错");
				question.Source = new string('长', 200);
				Assert(question.Caption.Length <= 71, "摘要应被截断");
			});

			Check("截图链路（整屏 → 裁剪 → 存 PNG）", delegate
			{
				using (Bitmap full = ScreenCapture.CaptureVirtualScreen())
				{
					Assert(full != null, "整屏截图失败");
					Assert(full.Width > 10 && full.Height > 10, "截图尺寸异常");
					using (Bitmap crop = ScreenCapture.Crop(full, new Rectangle(0, 0, 120, 90)))
					{
						Assert(crop != null, "裁剪失败");
						Assert(crop.Width == 120 && crop.Height == 90,
							"裁剪尺寸不对: " + crop.Width + "x" + crop.Height);
						string path = ScreenCapture.SavePng(crop);
						Assert(File.Exists(path), "PNG 没写出来");
						FileInfo info = new FileInfo(path);
						Assert(info.Length > 200, "PNG 内容太小");
						File.Delete(path);
					}
					Assert(ScreenCapture.Crop(full, new Rectangle(-500, -500, 10, 10)) == null,
						"越界裁剪应返回 null");
				}
			});

			Line("------------------------------------------------------------");
			Line(string.Format("结果: {0} 通过, {1} 失败", Passed, Failed));
			Line("日志目录: " + Settings.Dir);

			try
			{
				string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log");
				File.WriteAllText(path, Output.ToString(), new UTF8Encoding(true));
			}
			catch
			{
			}
			try
			{
				Console.WriteLine(Output.ToString());
			}
			catch
			{
			}
			return (Failed == 0) ? 0 : 1;
		}

		private static void Check(string name, Action body)
		{
			try
			{
				body();
				Passed++;
				Line("[通过] " + name);
			}
			catch (Exception ex)
			{
				Failed++;
				Line("[失败] " + name + " → " + ex.Message);
			}
		}

		private static void Assert(bool condition, string message)
		{
			if (!condition)
			{
				throw new Exception(message);
			}
		}

		private static void Line(string text)
		{
			Output.AppendLine(text);
		}

		/// <summary>
		/// 网页链路体检：启动/复用浏览器 → 连 CDP → 打开 DeepSeek → 注入脚本 → 读页面状态。
		/// 不需要登录也能验证「能不能连上、脚本能不能注入、输入框和上传入口在不在」。
		/// 用法: SnapAsk.exe --probe   结果写到 exe 同目录的 probe.log
		/// </summary>
		public static int Probe()
		{
			Settings settings = Settings.Load();
			StringBuilder report = new StringBuilder();
			int code = 1;
			try
			{
				report.AppendLine("SnapAsk 连接体检 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
				report.AppendLine("端口: " + settings.CdpPort + "   地址: " + settings.StartUrl);

				SnapAsk.Net.EdgeLauncher launcher = new SnapAsk.Net.EdgeLauncher(settings);
				launcher.EnsureStarted();
				report.AppendLine("[通过] 浏览器已就绪: " + (launcher.BrowserPath ?? "(复用已有实例)"));

				string wsUrl = launcher.WaitForChatTargetAsync(90000, System.Threading.CancellationToken.None)
					.GetAwaiter().GetResult();
				report.AppendLine("[通过] 找到页面: " + wsUrl);

				using (SnapAsk.Net.CdpClient cdp = new SnapAsk.Net.CdpClient())
				{
					cdp.ConnectAsync(wsUrl, 15000).GetAwaiter().GetResult();
					report.AppendLine("[通过] CDP 连接成功");
					cdp.SendAsync("Page.enable", null, 8000).GetAwaiter().GetResult();
					cdp.SendAsync("Runtime.enable", null, 8000).GetAwaiter().GetResult();

					string href = Convert.ToString(cdp.EvaluateAsync("location.href", 8000).GetAwaiter().GetResult());
					report.AppendLine("当前地址: " + href);
					if (href == null || href.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) < 0)
					{
						cdp.SendAsync("Page.navigate", new Dictionary<string, object>
						{
							{ "url", settings.StartUrl }
						}, 15000).GetAwaiter().GetResult();
						System.Threading.Thread.Sleep(5000);
						href = Convert.ToString(cdp.EvaluateAsync("location.href", 8000).GetAwaiter().GetResult());
						report.AppendLine("跳转后地址: " + href);
					}

					object injected = cdp.EvaluateAsync(PageScript.Build(settings.Sel), 15000).GetAwaiter().GetResult();
					string status = (injected == null) ? "" : injected.ToString();
					report.AppendLine((status == "ok" ? "[通过]" : "[失败]") + " 注入辅助脚本: " + status);

					Dictionary<string, object> diag = Json.Obj(
						cdp.EvaluateAsync("window.__snapask.diag()", 10000).GetAwaiter().GetResult());
					if (diag != null)
					{
						report.AppendLine("标题: " + Json.GetStr(diag, "title"));
						report.AppendLine("输入框: " + (Json.GetBool(diag, "input", false) ? "已找到" : "未找到"));
						report.AppendLine("发送按钮: " + (Json.GetBool(diag, "send", false) ? "已找到" : "未找到"));
						report.AppendLine("回复节点: " + Json.GetInt(diag, "replies", 0));
						report.AppendLine("登录页: " + (Json.GetBool(diag, "login", false) ? "是（需要登录）" : "否"));
						report.AppendLine("页面采样: " + Json.GetStr(diag, "sample"));
					}
					else
					{
						report.AppendLine("[失败] diag() 没有返回对象");
					}

					Dictionary<string, object> doc = Json.Obj(Json.Get(cdp.SendAsync("DOM.getDocument",
						new Dictionary<string, object> { { "depth", 1 } }, 10000).GetAwaiter().GetResult(), "result"));
					int rootId = Json.GetInt(Json.Obj(Json.Get(doc, "root")), "nodeId", 0);
					Dictionary<string, object> found = null;
					if (rootId > 0)
					{
						found = Json.Obj(Json.Get(cdp.SendAsync("DOM.querySelector", new Dictionary<string, object>
						{
							{ "nodeId", rootId },
							{ "selector", "input[type=file]" }
						}, 10000).GetAwaiter().GetResult(), "result"));
					}
					report.AppendLine("图片上传入口: " + ((Json.GetInt(found, "nodeId", 0) > 0)
						? "已找到（截图可以自动附图）"
						: "未找到（登录后才会有，或页面已改版）"));

					bool ready = diag != null
						&& Json.GetBool(diag, "input", false)
						&& !Json.GetBool(diag, "login", false);
					report.AppendLine(ready ? "结论: 可以直接提问。" : "结论: 页面还没进入对话状态（大概率需要先在浏览器里登录一次）。");
					code = ready ? 0 : 2;
				}
				launcher.Dispose();
			}
			catch (Exception ex)
			{
				report.AppendLine("[失败] " + ex.GetType().Name + ": " + ex.Message);
				code = 1;
			}

			try
			{
				File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe.log"),
					report.ToString(), new UTF8Encoding(true));
			}
			catch
			{
			}
			try
			{
				Console.WriteLine(report.ToString());
			}
			catch
			{
			}
			return code;
		}
	}
}
