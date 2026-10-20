using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SnapAsk.Util;

namespace SnapAsk.Engines
{
	/// <summary>
	/// OpenAI 兼容接口引擎（DeepSeek 官方 API / 其它兼容服务）。
	/// 备选方案：不走浏览器、不受网页限流影响，但需要 API Key，且纯文本模型看不了截图。
	/// </summary>
	public sealed class ApiEngine : IChatEngine
	{
		private readonly Settings S;

		private readonly HttpClient Http;

		private readonly List<Dictionary<string, object>> Messages = new List<Dictionary<string, object>>();

		public string Name
		{
			get
			{
				return "API 模式";
			}
		}

		public string Diagnostics { get; private set; }

		public bool NeedsBrowser
		{
			get
			{
				return false;
			}
		}

		public ApiEngine(Settings settings)
		{
			S = settings;
			Http = new HttpClient();
			Http.Timeout = TimeSpan.FromSeconds(Math.Max(30, S.ReplyTimeoutSec));
			Diagnostics = "";
		}

		private bool IsLocalEndpoint()
		{
			string baseUrl = (S.ApiBase ?? "").ToLowerInvariant();
			return baseUrl.Contains("127.0.0.1") || baseUrl.Contains("localhost");
		}

		/// <summary>Base URL 归一化：支持以 /v1 或 /chat/completions 结尾的写法。</summary>
		private string Endpoint()
		{
			string baseUrl = (S.ApiBase ?? "").Trim().TrimEnd('/');
			if (baseUrl.Length == 0)
			{
				baseUrl = "https://api.deepseek.com/v1";
			}
			if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
			{
				return baseUrl;
			}
			if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
			{
				return baseUrl + "/chat/completions";
			}
			return baseUrl + "/v1/chat/completions";
		}

		public Task<string> ConnectAsync(CancellationToken ct)
		{
			if (string.IsNullOrEmpty(S.ApiKey) && !IsLocalEndpoint())
			{
				throw new Exception("API 模式需要在设置里填写 API Key（本地 Ollama 等可留空）。");
			}
			Messages.Clear();
			Diagnostics = "接口: " + Endpoint() + "   模型: " + S.ApiModel;
			return Task.FromResult(Diagnostics);
		}

		public Task ResetAsync(CancellationToken ct)
		{
			Messages.Clear();
			return Task.FromResult(0);
		}

		public async Task<string> AskAsync(string text, string imagePath, Action<string> onProgress, CancellationToken ct)
		{
			if (Messages.Count == 0)
			{
				await ConnectAsync(ct);
			}
			string content = text;
			if (!string.IsNullOrEmpty(imagePath))
			{
				// DeepSeek 官方 API 目前不吃图片；这里明确提示，而不是把图当文本发过去
				content = text + "\r\n\r\n（说明：当前为 API 模式，无法读取截图，请改用网页版模式，"
					+ "或把题目文字复制出来用划词提问。截图已保存在：" + imagePath + "）";
			}
			Dictionary<string, object> user = new Dictionary<string, object>();
			user["role"] = "user";
			user["content"] = content;
			Messages.Add(user);

			string json = Json.Encode(new Dictionary<string, object>
			{
				{ "model", S.ApiModel },
				{ "messages", Messages },
				{ "stream", false },
				{ "temperature", 0.3 }
			});
			using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Endpoint()))
			{
				request.Content = new StringContent(json, Encoding.UTF8, "application/json");
				if (!string.IsNullOrEmpty(S.ApiKey))
				{
					request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", S.ApiKey);
				}
				using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
				{
					linked.CancelAfter(TimeSpan.FromSeconds(S.ReplyTimeoutSec));
					HttpResponseMessage response;
					try
					{
						response = await Http.SendAsync(request, linked.Token);
					}
					catch (OperationCanceledException)
					{
						if (ct.IsCancellationRequested)
						{
							throw;
						}
						throw new TimeoutException("API 请求超时（" + S.ReplyTimeoutSec + " 秒）。");
					}
					using (response)
					{
						string body = await response.Content.ReadAsStringAsync();
						if (!response.IsSuccessStatusCode)
						{
							string detail = ((body == null) ? "" : body.Trim());
							if (detail.Length > 500)
							{
								detail = detail.Substring(0, 500);
							}
							throw new Exception(string.Concat("API 返回 ", (int)response.StatusCode, " ",
								response.StatusCode, ": ", detail));
						}
						Dictionary<string, object> root = Json.Obj(Json.Decode(body));
						List<object> choices = Json.List(Json.Get(root, "choices"));
						if (choices == null || choices.Count == 0)
						{
							throw new Exception("API 响应里没有 choices。");
						}
						Dictionary<string, object> choice = Json.Obj(choices[0]);
						Dictionary<string, object> message = Json.Obj(Json.Get(choice, "message"));
						string answer = Json.GetStr(message, "content");
						if (string.IsNullOrEmpty(answer))
						{
							answer = Json.GetStr(message, "reasoning_content");
						}
						if (answer == null)
						{
							answer = "";
						}
						Messages.Add(new Dictionary<string, object>
						{
							{ "role", "assistant" },
							{ "content", answer }
						});
						if (onProgress != null && answer.Length > 0)
						{
							try
							{
								onProgress(answer);
							}
							catch
							{
							}
						}
						return answer;
					}
				}
			}
		}

		public void Dispose()
		{
			try
			{
				Http.Dispose();
			}
			catch
			{
			}
		}
	}
}
