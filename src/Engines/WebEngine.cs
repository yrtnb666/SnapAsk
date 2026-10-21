using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SnapAsk.Core;
using SnapAsk.Net;
using SnapAsk.Util;

namespace SnapAsk.Engines
{
	/// <summary>
	/// DeepSeek 网页版引擎：用 CDP 连上真实浏览器标签页，注入辅助脚本，
	/// 然后像人一样写输入框 / 按回车 / 等回答。不需要 API Key，登录状态复用浏览器 profile。
	/// 逻辑沿用 EAgentX 已经跑通的实现（选择器、提交确认、完成判定、限流重试）。
	/// </summary>
	public sealed class WebEngine : IChatEngine
	{
		private const string InputMissingMarker = "输入框: 未找到";

		private readonly Settings S;

		private readonly SemaphoreSlim ConnectGate = new SemaphoreSlim(1, 1);

		private EdgeLauncher Launcher;

		private CdpClient Cdp;

		public string Name
		{
			get
			{
				return "DeepSeek 网页版";
			}
		}

		public string Diagnostics { get; private set; }

		public bool NeedsBrowser
		{
			get
			{
				return true;
			}
		}

		public bool IsConnected
		{
			get
			{
				return Cdp != null && Cdp.IsOpen;
			}
		}

		/// <summary>浏览器主进程 ID（复用已有实例时为 0）。嵌入窗口时用来找窗口。</summary>
		public int BrowserProcessId
		{
			get
			{
				return (Launcher == null) ? 0 : Launcher.ProcessId;
			}
		}

		/// <summary>
		/// 只保证浏览器已经开着（不连 CDP、不注入脚本），给「嵌入浏览器」按钮用。
		/// 这样还没提问时也能先把网页放进主界面里登录。
		/// </summary>
		public async Task<int> EnsureBrowserAsync(CancellationToken ct)
		{
			await ConnectGate.WaitAsync(ct);
			try
			{
				if (Launcher == null)
				{
					Launcher = new EdgeLauncher(S);
				}
				EdgeLauncher launcher = Launcher;
				await Task.Run(delegate
				{
					launcher.EnsureStarted();
				}, ct);
				return launcher.ProcessId;
			}
			finally
			{
				ConnectGate.Release();
			}
		}

		public WebEngine(Settings settings)
		{
			S = settings;
			Diagnostics = "";
		}

		public async Task<string> ConnectAsync(CancellationToken ct)
		{
			await ConnectGate.WaitAsync(ct);
			try
			{
				if (Cdp != null && Cdp.IsOpen && !string.IsNullOrEmpty(Diagnostics))
				{
					return Diagnostics;
				}
				// 只断开 CDP，保留 Launcher：它记着浏览器进程 ID，嵌入窗口还要用
				try
				{
					if (Cdp != null)
					{
						Cdp.Dispose();
					}
				}
				catch
				{
				}
				Cdp = null;
				if (Launcher == null)
				{
					Launcher = new EdgeLauncher(S);
				}
				EdgeLauncher launcher = Launcher;
				await Task.Run(delegate
				{
					launcher.EnsureStarted();
				}, ct);
				string wsUrl = await Launcher.WaitForChatTargetAsync(90000, ct);
				Cdp = new CdpClient();
				await Cdp.ConnectAsync(wsUrl, 15000);
				await Cdp.SendAsync("Runtime.enable", null, 8000);
				await Cdp.SendAsync("Page.enable", null, 8000);
				await EnablePageFocusAsync();
				await EnsureStartUrlAsync(ct);
				await InjectAsync(ct);
				string diag = await ReadDiagnosticsAsync();
				if (diag.IndexOf(InputMissingMarker, StringComparison.Ordinal) >= 0)
				{
					diag = await WaitForLoginAsync(ct);
					if (diag.IndexOf(InputMissingMarker, StringComparison.Ordinal) >= 0)
					{
						throw new Exception("没有在页面上找到聊天输入框。\r\n请先在打开的浏览器窗口里登录 "
							+ S.StartUrl + "，进入对话页后点「重新检测」。\r\n\r\n" + diag);
					}
				}
				Diagnostics = diag;
				return diag;
			}
			finally
			{
				ConnectGate.Release();
			}
		}

		private async Task InjectAsync(CancellationToken ct)
		{
			object injected = await Cdp.EvaluateAsync(PageScript.Build(S.Sel), 15000);
			string status = ((injected == null) ? "" : injected.ToString());
			if (status != "ok")
			{
				throw new Exception("注入页面脚本失败: " + status);
			}
		}

		/// <summary>
		/// 让网页「以为」自己一直有焦点。
		/// 浏览器被嵌进主窗口、软件又最小化时，页面会变成 hidden 并被 Chromium 节流，
		/// 回复的 DOM 就不再更新——我们抓不到答案（小窗内容不同步）。
		/// 打开焦点仿真可以绕开这个节流。
		/// </summary>
		private async Task EnablePageFocusAsync()
		{
			try
			{
				await Cdp.SendAsync("Emulation.setFocusEmulationEnabled",
					new Dictionary<string, object> { { "enabled", true } }, 8000);
				Log.Dim("已开启页面焦点仿真（最小化时也能持续抓到回答）。");
			}
			catch (Exception ex)
			{
				Log.Dim("焦点仿真不可用（不影响正常使用）: " + ex.Message);
			}
		}

		/// <summary>
		/// 确认辅助脚本还在页面上：页面如果被刷新/跳转过，window.__snapask 会丢失，
		/// 这时补一针注入，免得整轮问答直接失败。
		/// </summary>
		private async Task<bool> EnsureHelperAsync()
		{
			try
			{
				// 版本号必须一起校验：页面上可能还留着旧版辅助脚本，只判断 replyState
				// 会让新序列化逻辑永远注入不进去（旧脚本会一直复用）。
				object has = await Cdp.EvaluateAsync("!!(window.__snapask && window.__snapask.v === 2 && window.__snapask.replyState)", 8000).ConfigureAwait(false);
				if (has is bool && (bool)has)
				{
					return true;
				}
			}
			catch
			{
			}
			Log.Dim("页面辅助脚本丢失（页面可能被刷新过），正在重新注入…");
			try
			{
				await InjectAsync(CancellationToken.None).ConfigureAwait(false);
				return true;
			}
			catch (Exception ex)
			{
				Log.Dim("重新注入失败: " + ex.Message);
				return false;
			}
		}

		/// <summary>页面停在登录页时，最多等 2 分钟让用户完成登录。</summary>
		private async Task<string> WaitForLoginAsync(CancellationToken ct)
		{
			const int LoginWaitMs = 120000;
			Log.Info("浏览器还停在登录页，请在打开的窗口里登录 DeepSeek；程序最多等 " + (LoginWaitMs / 1000) + " 秒…");
			Stopwatch watch = Stopwatch.StartNew();
			string diag = Diagnostics;
			int round = 0;
			while (watch.ElapsedMilliseconds < LoginWaitMs)
			{
				ct.ThrowIfCancellationRequested();
				await Task.Delay(2500, ct);
				round++;
				try
				{
					object injected = await Cdp.EvaluateAsync(PageScript.Build(S.Sel), 15000);
					if (injected == null || injected.ToString() != "ok")
					{
						continue;
					}
					diag = await ReadDiagnosticsAsync();
					if (diag.IndexOf(InputMissingMarker, StringComparison.Ordinal) < 0)
					{
						Log.Info("检测到页面已进入对话状态，继续。");
						return diag;
					}
					if (round % 8 == 0)
					{
						Log.Dim("仍在等待登录…（已等 " + (watch.ElapsedMilliseconds / 1000) + " 秒）");
					}
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch
				{
					// 页面正在跳转时注入会失败，下一轮再试
				}
			}
			return diag;
		}

		/// <summary>当前页面地址；页面正在跳转时返回空串。</summary>
		private async Task<string> GetHrefAsync()
		{
			try
			{
				object href = await Cdp.EvaluateAsync("location.href", 8000);
				return (href == null) ? "" : href.ToString();
			}
			catch
			{
				return "";
			}
		}

		private async Task EnsureStartUrlAsync(CancellationToken ct)
		{
			string href = await GetHrefAsync();
			if (href.IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return;
			}
			Log.Dim("当前标签页: " + href + "，正在跳转到 " + S.StartUrl);
			await Cdp.SendAsync("Page.navigate", new Dictionary<string, object> { { "url", S.StartUrl } }, 15000);
			for (int i = 0; i < 60; i++)
			{
				ct.ThrowIfCancellationRequested();
				await Task.Delay(1000, ct);
				if ((await GetHrefAsync()).IndexOf("deepseek.com", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					break;
				}
			}
			await WaitReadyAsync(ct);
		}

		private async Task WaitReadyAsync(CancellationToken ct)
		{
			for (int i = 0; i < 60; i++)
			{
				ct.ThrowIfCancellationRequested();
				object state;
				try
				{
					state = await Cdp.EvaluateAsync("document.readyState", 8000);
				}
				catch
				{
					state = null;
				}
				if (state != null && state.ToString() == "complete")
				{
					break;
				}
				await Task.Delay(500, ct);
			}
		}

		public async Task ResetAsync(CancellationToken ct)
		{
			if (Cdp == null)
			{
				throw new Exception("网页引擎还没有连接。");
			}
			await Cdp.SendAsync("Page.navigate", new Dictionary<string, object> { { "url", S.StartUrl } }, 15000);
			await WaitReadyAsync(ct);
			await Task.Delay(1200, ct);
			await InjectAsync(ct);
			for (int i = 0; i < 40; i++)
			{
				ct.ThrowIfCancellationRequested();
				object has;
				try
				{
					has = await Cdp.EvaluateAsync("!!(window.__snapask && window.__snapask.input())", 8000);
				}
				catch
				{
					has = null;
				}
				if (has is bool && (bool)has)
				{
					break;
				}
				await Task.Delay(500, ct);
			}
			Diagnostics = await ReadDiagnosticsAsync();
			Log.Info("已开始新对话。");
		}

		/// <summary>
		/// 提问并等回答。命中网页版「消息发送过于频繁」时按设置等待后自动重发。
		/// imagePath 非空时先把图片塞进网页的附件框，再写字提交。
		/// </summary>
		public async Task<string> AskAsync(string text, string imagePath, Action<string> onProgress, CancellationToken ct)
		{
			int retries = Math.Max(0, Math.Min(10, S.RateLimitRetries));
			int waitSec = Math.Max(10, Math.Min(1800, S.RateLimitWaitSec));
			for (int attempt = 0; ; attempt++)
			{
				string reply = null;
				RateLimitException limited = null;
				// C# 5 不允许在 catch 里 await，所以先把异常存下来，出了 try 再决定等多久
				try
				{
					if (Cdp == null || !Cdp.IsOpen)
					{
						await ConnectAsync(ct);
					}
					if (!string.IsNullOrEmpty(imagePath))
					{
						if (!File.Exists(imagePath))
						{
							throw new Exception("截图文件不见了: " + imagePath);
						}
						Log.Info(await AttachFileAsync(imagePath, ct));
						await Task.Delay(700, ct);
					}
					reply = await SendOnceAsync(text, ct, onProgress);
				}
				catch (RateLimitException ex)
				{
					limited = ex;
				}
				catch (TimeoutException)
				{
					throw;
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex)
				{
					// 连接断了就清掉，下一次提问自动重连
					if (Cdp != null && !Cdp.IsOpen)
					{
						CloseConnection();
					}
					Log.Error(ex.Message);
					throw;
				}
				if (limited == null)
				{
					return reply;
				}
				if (attempt >= retries)
				{
					if (attempt > 0)
					{
						throw new RateLimitException(limited.Message + "\r\n\r\n已自动等待重发 " + attempt
							+ " 次仍未恢复。\r\n可以稍后再试，或在设置里调大「限流重试次数 / 限流等待」。");
					}
					throw limited;
				}
				int delaySec = waitSec << attempt;
				if (delaySec > 1800)
				{
					delaySec = 1800;
				}
				Log.Info("DeepSeek 限流：等待 " + delaySec + " 秒后自动重发（第 " + (attempt + 1) + "/" + retries + " 次重试）…");
				await Task.Delay(delaySec * 1000, ct);
				Log.Info("限流等待结束，重新发送上一条消息…");
			}
		}

		private async Task<string> SendOnceAsync(string text, CancellationToken ct, Action<string> onProgress)
		{
			if (Cdp == null)
			{
				throw new Exception("网页引擎还没有连接。");
			}
			Log.Dim("写入输入框 " + (await SetInputAsync(text)) + " 字符");
			Dictionary<string, object> beforeMap = Json.Obj(await Cdp.EvaluateAsync("window.__snapask.beforeSend()", 10000));
			int beforeMsgs = Json.GetInt(beforeMap, "messages", 0);
			Log.Dim("发送前消息节点: " + beforeMsgs);
			await DispatchEnterAsync();
			bool submitted = await WaitForSubmittedAsync(text, beforeMsgs, 8000, ct);
			if (!submitted)
			{
				Log.Dim("首次提交未被页面接收，正在重试…");
				object emptyValue = await Cdp.EvaluateAsync("window.__snapask.inputEmpty()", 8000);
				if (emptyValue is bool && (bool)emptyValue)
				{
					await SetInputAsync(text);
				}
				object clicked = await Cdp.EvaluateAsync("window.__snapask.clickSend()", 10000);
				string clickInfo = ((clicked == null) ? "" : clicked.ToString());
				if (clickInfo == "no-input")
				{
					throw new Exception("找不到输入框，请确认浏览器已登录且停留在对话页。");
				}
				Log.Dim("点击发送按钮: " + clickInfo);
				submitted = await WaitForSubmittedAsync(text, beforeMsgs, 8000, ct);
			}
			if (!submitted)
			{
				throw new Exception("消息没有成功提交到 DeepSeek 页面。\r\n"
					+ "已尝试真实回车和点击发送按钮，但页面没有新增消息节点。\r\n"
					+ "请在浏览器里手动点一次发送，或刷新页面后重试。");
			}
			Log.Dim("消息已提交，等待回答…");
			Stopwatch watch = Stopwatch.StartNew();
			int timeoutMs = Math.Max(30, S.ReplyTimeoutSec) * 1000;
			string last = null;
			string lastPushed = null;
			long stableMs = 0L;
			long firstTextMs = -1L;
			bool sawGenerating = false;
			int ticks = 0;
			string sentTrim = (text ?? "").Trim();
			while (watch.ElapsedMilliseconds < timeoutMs)
			{
				ct.ThrowIfCancellationRequested();
				// 后台轮询不要回 UI 线程：嵌入浏览器/重排窗口时 UI 线程偶尔会忙，
				// 若这里捕获 UI 上下文，700ms 的等待会被拖成几秒甚至几十秒。
				await Task.Delay(700, ct).ConfigureAwait(false);
				// 页面被刷新过的话辅助脚本会丢，补一针再继续（否则整轮问答直接抓瞎）
				if (ticks % 8 == 0)
				{
					await EnsureHelperAsync().ConfigureAwait(false);
				}
				Dictionary<string, object> replyState = Json.Obj(
					await Cdp.EvaluateAsync("window.__snapask.replyState()", 10000).ConfigureAwait(false));
				bool hasReply = replyState != null && Json.GetBool(replyState, "has", false);
				string current = ((replyState == null) ? "" : (Json.GetStr(replyState, "text") ?? ""));
				int actions = ((replyState != null) ? Json.GetInt(replyState, "actions", 0) : 0);
				int count = ((replyState != null) ? Json.GetInt(replyState, "messages", 0) : 0);
				bool generating = replyState == null || Json.GetBool(replyState, "generating", true);
				// replyState() 已经把「是否限流」一起返回，正常情况不用再多跑一条 CDP 命令
				bool limited = replyState != null && Json.GetBool(replyState, "limited", false);
				if (replyState == null)
				{
					object rateLimitedValue = await Cdp.EvaluateAsync("window.__snapask.rateLimit()", 8000).ConfigureAwait(false);
					limited = rateLimitedValue is bool && (bool)rateLimitedValue;
				}
				if (limited)
				{
					throw RateLimitedException();
				}
				if (hasReply && current.Length > 0 && current.Trim() != sentTrim)
				{
					if (firstTextMs < 0)
					{
						firstTextMs = watch.ElapsedMilliseconds;
					}
					if (current != last)
					{
						last = current;
						stableMs = 0L;
						if (onProgress != null && current != lastPushed)
						{
							lastPushed = current;
							try
							{
								onProgress(current);
							}
							catch
							{
							}
						}
					}
					else
					{
						stableMs += 700;
					}
					if (generating)
					{
						sawGenerating = true;
					}
					bool finished = !generating;
					// 停止按钮消失 + 动作栏已出现 + 文本稳定：正常完成
					if (finished && actions >= 1 && stableMs >= 1600)
					{
						return current.Trim();
					}
					// 见过「生成中」但没有动作栏（旧版页面）：文本稳定 6.4 秒也算完成
					if (finished && actions < 1 && sawGenerating && stableMs >= 6400)
					{
						return current.Trim();
					}
					// 全程没捕捉到「生成中」（短回复）：文本稳定 4.8 秒即认为完成
					if (finished && !sawGenerating && firstTextMs >= 0
						&& watch.ElapsedMilliseconds - firstTextMs >= 4800 && stableMs >= 4800)
					{
						return current.Trim();
					}
				}
				ticks++;
				if (ticks % 14 == 0)
				{
					string where = "";
					if (count == 0)
					{
						where = " · 页面 " + (await GetHrefAsync().ConfigureAwait(false));
					}
					Log.Dim("等待回答: 消息 " + count + " · 生成中 " + (generating ? "是" : "否")
						+ " · 已抓取 " + ((last == null) ? 0 : last.Length) + " 字" + where);
				}
				if (watch.ElapsedMilliseconds > 30000 && last == null)
				{
					Dictionary<string, object> diag = Json.Obj(
						await Cdp.EvaluateAsync("window.__snapask.diag()", 8000).ConfigureAwait(false));
					string sample = ((diag == null) ? "" : (Json.GetStr(diag, "sample") ?? ""));
					if (sample.IndexOf("服务器繁忙", StringComparison.Ordinal) >= 0
						|| sample.IndexOf("请稍后再试", StringComparison.Ordinal) >= 0)
					{
						throw new Exception("DeepSeek 网页提示繁忙 / 限流，请稍后再试。");
					}
				}
			}
			string extra = "";
			try
			{
				extra = await ReadDiagnosticsAsync().ConfigureAwait(false);
			}
			catch
			{
			}
			throw new TimeoutException("等待 DeepSeek 回复超时（" + S.ReplyTimeoutSec + " 秒）。\r\n"
				+ "可能原因：未登录、页面结构变化、网络中断，或页面仍在深度思考。\r\n"
				+ "可以在设置里调大回复超时。\r\n\r\n" + extra);
		}

		private async Task<int> SetInputAsync(string text)
		{
			Dictionary<string, object> setResult = Json.Obj(await Cdp.EvaluateAsync(
				"window.__snapask.setInput(" + Json.Encode(text) + ")", 20000));
			if (setResult == null || !Json.GetBool(setResult, "ok", false))
			{
				string reason = ((setResult == null) ? "返回为空" : Json.GetStr(setResult, "reason"));
				throw new Exception("写入输入框失败: " + reason);
			}
			return Json.GetInt(setResult, "length", 0);
		}

		private async Task<bool> WaitForSubmittedAsync(string text, int beforeMsgs, int timeoutMs, CancellationToken ct)
		{
			// 题目文本可能很长，序列化一次就够了
			string submitCall = "window.__snapask.submitState(" + Json.Encode(text) + ")";
			Stopwatch watch = Stopwatch.StartNew();
			while (watch.ElapsedMilliseconds < timeoutMs)
			{
				ct.ThrowIfCancellationRequested();
				await Task.Delay(400, ct);
				// 一次调用拿齐消息数 / 生成中 / 限流 / 已出现用户消息，减少 CDP 往返
				Dictionary<string, object> state = null;
				try
				{
					state = Json.Obj(await Cdp.EvaluateAsync(submitCall, 8000));
				}
				catch
				{
				}
				if (state != null)
				{
					int now = Json.GetInt(state, "messages", 0);
					if (now > beforeMsgs)
					{
						return true;
					}
					if (Json.GetBool(state, "limited", false))
					{
						throw RateLimitedException();
					}
					if (Json.GetBool(state, "generating", false))
					{
						return true;
					}
					if (Json.GetBool(state, "sent", false))
					{
						return true;
					}
				}
			}
			return false;
		}

		private async Task DispatchEnterAsync()
		{
			await Cdp.SendAsync("Input.dispatchKeyEvent", new Dictionary<string, object>
			{
				{ "type", "keyDown" },
				{ "key", "Enter" },
				{ "code", "Enter" },
				{ "windowsVirtualKeyCode", 13 },
				{ "nativeVirtualKeyCode", 13 },
				{ "text", "\r" },
				{ "unmodifiedText", "\r" }
			}, 8000);
			await Cdp.SendAsync("Input.dispatchKeyEvent", new Dictionary<string, object>
			{
				{ "type", "keyUp" },
				{ "key", "Enter" },
				{ "code", "Enter" },
				{ "windowsVirtualKeyCode", 13 },
				{ "nativeVirtualKeyCode", 13 }
			}, 8000);
		}

		/// <summary>把图片塞进网页的文件输入框，并等缩略图出现（说明上传完成）。</summary>
		private async Task<string> AttachFileAsync(string path, CancellationToken ct)
		{
			int nodeId = await FindFileInputNodeAsync();
			if (nodeId <= 0)
			{
				throw new Exception("页面上没有找到文件上传输入框。\r\n"
					+ "请确认已登录 DeepSeek 并停留在对话页；如果网页改版，可以在设置里重新检测。");
			}
			await Cdp.SendAsync("DOM.setFileInputFiles", new Dictionary<string, object>
			{
				{ "files", new string[] { Path.GetFullPath(path) } },
				{ "nodeId", nodeId }
			}, 30000);
			Stopwatch watch = Stopwatch.StartNew();
			while (watch.ElapsedMilliseconds < 15000)
			{
				ct.ThrowIfCancellationRequested();
				await Task.Delay(500, ct);
				Dictionary<string, object> state;
				try
				{
					state = Json.Obj(await Cdp.EvaluateAsync("window.__snapask.attachState()", 8000));
				}
				catch
				{
					continue;
				}
				int count = Json.GetInt(state, "count", 0);
				bool uploading = Json.GetBool(state, "uploading", false);
				if (count > 0 && !uploading)
				{
					return "截图已附加到网页输入框（" + count + " 个附件节点）";
				}
			}
			return "截图已提交给网页（未确认上传完成，若发送失败请重试）";
		}

		private async Task<int> FindFileInputNodeAsync()
		{
			Dictionary<string, object> docParams = new Dictionary<string, object> { { "depth", 1 } };
			Dictionary<string, object> docResult = Json.Obj(Json.Get(
				await Cdp.SendAsync("DOM.getDocument", docParams, 10000), "result"));
			Dictionary<string, object> root = Json.Obj(Json.Get(docResult, "root"));
			int rootId = Json.GetInt(root, "nodeId", 0);
			if (rootId <= 0)
			{
				return 0;
			}
			Dictionary<string, object> queryResult = Json.Obj(Json.Get(await Cdp.SendAsync("DOM.querySelector",
				new Dictionary<string, object>
				{
					{ "nodeId", rootId },
					{ "selector", "input[type=file]" }
				}, 10000), "result"));
			return Json.GetInt(queryResult, "nodeId", 0);
		}

		public async Task<string> ReadDiagnosticsAsync()
		{
			if (Cdp == null)
			{
				return "尚未连接。";
			}
			Dictionary<string, object> diag = Json.Obj(await Cdp.EvaluateAsync("window.__snapask.diag()", 12000));
			if (diag == null)
			{
				return "无法读取页面状态。";
			}
			System.Text.StringBuilder report = new System.Text.StringBuilder();
			report.AppendLine("页面: " + Json.GetStr(diag, "url"));
			report.AppendLine("标题: " + Json.GetStr(diag, "title"));
			bool hasInput = Json.GetBool(diag, "input", false);
			report.AppendLine("输入框: " + (hasInput
				? ("已找到 (" + Json.GetStr(diag, "inputTag") + " " + Json.GetStr(diag, "placeholder") + ")")
				: "未找到"));
			report.AppendLine("发送按钮: " + (Json.GetBool(diag, "send", false) ? "已找到" : "未找到（会用真实回车键兜底）"));
			report.AppendLine("回复节点: " + Json.GetInt(diag, "replies", 0) + " 个");
			report.AppendLine("正在生成: " + (Json.GetBool(diag, "generating", false) ? "是" : "否"));
			bool login = Json.GetBool(diag, "login", false) || LooksLikeLoginUrl(Json.GetStr(diag, "url"));
			report.AppendLine("登录状态: " + (login ? "检测到登录页，请先在浏览器里登录" : "看起来已登录"));
			return report.ToString().TrimEnd();
		}

		/// <summary>DeepSeek 登录页的地址特征（登录页不一定有密码框，短信/微信登录都没有）。</summary>
		private static bool LooksLikeLoginUrl(string url)
		{
			if (string.IsNullOrEmpty(url))
			{
				return false;
			}
			string lower = url.ToLowerInvariant();
			return lower.IndexOf("/sign_in", StringComparison.Ordinal) >= 0
				|| lower.IndexOf("/signin", StringComparison.Ordinal) >= 0
				|| lower.IndexOf("/sign-in", StringComparison.Ordinal) >= 0
				|| lower.IndexOf("/login", StringComparison.Ordinal) >= 0;
		}

		private static RateLimitException RateLimitedException()
		{
			return new RateLimitException("DeepSeek 提示「消息发送过于频繁，请稍后重试」。\r\n"
				+ "这是网页版的账号级限流，不是程序故障。\r\n"
				+ "建议等 10–30 分钟再试；期间反复发送可能延长限制时间。");
		}

		private void CloseConnection()
		{
			try
			{
				if (Cdp != null)
				{
					Cdp.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				if (Launcher != null)
				{
					Launcher.Dispose();
				}
			}
			catch
			{
			}
			Cdp = null;
			Launcher = null;
		}

		public void Dispose()
		{
			CloseConnection();
			try
			{
				ConnectGate.Dispose();
			}
			catch
			{
			}
		}
	}
}
