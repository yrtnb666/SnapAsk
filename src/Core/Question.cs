using System.Drawing;

namespace SnapAsk.Core
{
	/// <summary>一条待解答的题目：要么是截图，要么是选中的文字。</summary>
	public sealed class Question
	{
		/// <summary>发给模型的完整文本（已套好提示词模板）。</summary>
		public string Text;

		/// <summary>用户实际「内容」：截图摘要或选中的原文，用于界面显示。</summary>
		public string Source;

		/// <summary>截图文件路径；为 null 表示纯文本题。</summary>
		public string ImagePath;

		/// <summary>框选区域（屏幕坐标），用来决定结果窗出现的位置。</summary>
		public Rectangle Region;

		public bool HasImage
		{
			get
			{
				return !string.IsNullOrEmpty(ImagePath);
			}
		}

		/// <summary>界面上显示的题目摘要。</summary>
		public string Caption
		{
			get
			{
				string text = (Source ?? Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
				if (text.Length > 70)
				{
					text = text.Substring(0, 70) + "…";
				}
				return text;
			}
		}
	}
}
