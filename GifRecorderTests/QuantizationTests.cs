using NUnit.Framework;
using GifRecorder;
using System.Text;

namespace GifRecorderTests
{
	public class QuantizationTests
	{
		[TestCase(255, 255, 255, ExpectedResult = "77777777")]
		[TestCase(255, 255, 0, ExpectedResult = "66666666")]
		[TestCase(255, 0, 255, ExpectedResult = "55555555")]
		[TestCase(255, 0, 0, ExpectedResult = "44444444")]
		[TestCase(0, 255, 255, ExpectedResult = "33333333")]
		[TestCase(0, 255, 0, ExpectedResult = "22222222")]
		[TestCase(0, 0, 255, ExpectedResult = "11111111")]
		[TestCase(0, 0, 0, ExpectedResult = "00000000")]
		[TestCase(15, 51, 85, ExpectedResult = "01234567")]
		[TestCase(240, 204, 170, ExpectedResult = "76543210")]
		public string GetColorIndexTest(byte r, byte g, byte b)
		{
			StringBuilder builder = new StringBuilder();
			for (int i = 0; i < 8; i++)
			{
				var index = QImage.GetIndex(r, g, b, i);
				builder.Append(index);
			}

			return builder.ToString();
		}
	}
}
