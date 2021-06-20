using GifRecorder;
using NUnit.Framework;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GifRecorderTests
{
	public class Tests
	{
		[Test]
		public void EncodeTest()
		{
			BitmapPalette palette = BitmapPalettes.Halftone256;
			byte[] data = new byte[] { 251, 251, 251, 251, 5, 5, 5, 5, 210, 210, 210, 210, 36, 36, 36, 36 };

			byte[] actual = LzwEncoder.Encode(data, palette.Colors.Count);
			byte[] expected = new byte[] { 0, 247, 9, 220, 87, 160, 96, 1, 105, 8, 165, 145, 88, 72, 34, 32 };

			Assert.AreEqual(expected.Length, actual.Length);
			Assert.AreEqual(expected, actual);
		}

		// https://habr.com/ru/post/274917/
		[TestCase(new byte[] { 0, 0, 0, 0, 2, 2, 2, 2, 4, 4, 4, 4, 5, 5, 5, 5 }, new byte[] { 0x08, 0x0A, 0xD2, 0x42, 0x90, 0x94, 0x59, 0x12 })]
		[TestCase(new byte[] { 3, 6, 1, 7, 3, 6, 1, 7, 3, 6, 1, 7, 3, 6, 1, 7 }, new byte[] { 0x38, 0x16, 0xA7, 0xEC, 0x6D, 0x9D, 0x04 })]
		public void CustomPaletteEncodeTest(byte[] data, byte[] expected)
		{
			List<Color> palette = new List<Color>
			{
				Colors.Green,
				Colors.YellowGreen,
				Colors.Yellow,
				Colors.Purple,
				Colors.Blue,
				Colors.Red,
				Colors.Black,
				Colors.White
			};

			byte[] actual = LzwEncoder.Encode(data, palette.Count);
			Assert.AreEqual(expected, actual);
		}

		[TestCase(0, 1)]
		[TestCase(255, 8)]
		[TestCase(256, 9)]
		[TestCase(511, 9)]
		[TestCase(2048, 12)]
		[TestCase(4095, 12)]
		[TestCase(65535, 16)]
		public void GetBitLengthTest(int input, int expected)
		{
			int actual = BinaryHelper.GetBitLength(input);
			Assert.AreEqual(expected, actual);
		}
	}
}