using System;
using System.Threading;
using System.Threading.Tasks;

namespace SnapAsk.Engines
{
	/// <summary>
	/// 答题引擎抽象：网页版（CDP 页面自动化）和 API 模式实现同一套接口，
	/// 主流程对两者完全一致。
	/// </summary>
	public interface IChatEngine : IDisposable
	{
		/// <summary>引擎名，显示在界面上。</summary>
		string Name { get; }

		/// <summary>最近一次连接 / 页面诊断信息。</summary>
		string Diagnostics { get; }

		/// <summary>是否需要先拉起浏览器。</summary>
		bool NeedsBrowser { get; }

		/// <summary>连接（网页版会启动浏览器）。返回一行人类可读的状态。</summary>
		Task<string> ConnectAsync(CancellationToken ct);

		/// <summary>提一个问题，边生成边回调 onProgress；返回完整答案。</summary>
		Task<string> AskAsync(string text, string imagePath, Action<string> onProgress, CancellationToken ct);

		/// <summary>开一段新对话。</summary>
		Task ResetAsync(CancellationToken ct);
	}

	/// <summary>网页版限流的专用异常（只有它会被自动等待重发）。</summary>
	internal sealed class RateLimitException : Exception
	{
		public RateLimitException(string message)
			: base(message)
		{
		}
	}
}
