using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SnapAsk.Util;

namespace SnapAsk.Core
{
	/// <summary>
	/// 内嵌的页面辅助脚本（assets/page_helper.js）。
	/// 单独放文件而不是塞进 C# 字符串，避免大量转义出错。
	/// </summary>
	internal static class PageScript
	{
		private static string Cached;

		public static string Build(Selectors selectors)
		{
			string raw = Raw();
			Dictionary<string, object> cfg = new Dictionary<string, object>();
			cfg["input"] = selectors.Input;
			cfg["send"] = selectors.Send;
			cfg["reply"] = selectors.Reply;
			cfg["generating"] = selectors.Generating;
			return raw.Replace("__CFG__", Json.Encode(cfg));
		}

		private static string Raw()
		{
			if (Cached != null)
			{
				return Cached;
			}
			Assembly assembly = Assembly.GetExecutingAssembly();
			using (Stream stream = assembly.GetManifestResourceStream("page_helper.js"))
			{
				if (stream == null)
				{
					throw new Exception("内嵌资源 page_helper.js 丢失，请重新编译。");
				}
				using (StreamReader reader = new StreamReader(stream, System.Text.Encoding.UTF8))
				{
					Cached = reader.ReadToEnd();
				}
			}
			return Cached;
		}
	}
}
