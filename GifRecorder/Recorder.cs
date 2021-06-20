using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GifRecorder
{
	internal class Recorder : IDisposable
	{
		private readonly object _locker = new object();
		private FileStream _fileStream;
		private BinaryWriter _writer;

		private bool _disposed;
		private ushort _height;
		private ushort _width;

		public int RecordedFrames { get; private set; }

		public long RecordedBytes { get; private set; }

		public bool Recording { get; private set; }

		/// <summary>
		/// Writes Bitmap Data to GIF Image
		/// </summary>
		/// <param name="bitmap">Source Bitmap to write</param>
		/// <param name="delay">Delay Time - If not 0, specifies a delay of 1/100 second to wait before rendering the next frame.</param>
		/// <param name="transparencyIndex">Transparency Index</param>
		public void WriteFrame(BitmapSource bitmap, ushort delay = 0, byte transparencyIndex = 0)
		{
			lock (_locker)
			{
				if (!Recording)
					return;

				if (RecordedFrames == 0)
				{
					List<byte> colorsData = GetBitmapPalette(bitmap);

					_width = (ushort)bitmap.PixelWidth;
					_height = (ushort)bitmap.PixelHeight;
					WriteHeader(_width, _height, 8, colorsData);
				}

				byte[] bitmapData = GetBitmapData(bitmap);
				byte[] encodedData = LzwEncoder.Encode(bitmapData, 256);
				WriteData(encodedData, _width, _height, 8);
				WriteControlExtension(delay, transparencyIndex);
				RecordedFrames++;
				RecordedBytes = _writer.BaseStream.Position;
			}
		}

		public void Start(string filePath)
		{
			_fileStream = new FileStream(filePath, FileMode.Create);
			_writer = new BinaryWriter(_fileStream);
			RecordedFrames = 0;
			Recording = true;
		}

		public void Stop()
		{
			if (!Recording)
				return;

			Recording = false;
			_writer.Write((byte)0x3B); // Trailer
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			_writer.Close();
			_fileStream.Close();
			_disposed = true;
		}

		private static byte[] GetBitmapData(BitmapSource bitmap)
		{
			byte[] bitmapData = new byte[bitmap.PixelHeight * bitmap.PixelWidth];
			int stride = bitmap.PixelWidth + (bitmap.PixelWidth % 4);
			bitmap.CopyPixels(bitmapData, stride, 0);

			return bitmapData;
		}

		private static List<byte> GetBitmapPalette(BitmapSource bitmap)
		{
			BitmapPalette palette = bitmap.Palette;

			var colorsData = new List<byte>();
			foreach (Color color in palette.Colors)
			{
				colorsData.Add(color.R);
				colorsData.Add(color.G);
				colorsData.Add(color.B);
			}

			while (colorsData.Count < 768)
				colorsData.Add(0);

			return colorsData;
		}

		private void WriteHeader(ushort width, ushort height, byte colorResolution, IList<byte> colorTable = null)
		{
			byte[] signature = new byte[] { (byte)'G', (byte)'I', (byte)'F' };
			byte[] version = new byte[] { (byte)'8', (byte)'9', (byte)'a' };

			_writer.Write(signature);
			_writer.Write(version);

			// Logical Screen Descriptor
			_writer.Write(width);
			_writer.Write(height);

			byte colorTableField = (byte)((colorResolution - 1) << 4);

			if (colorTable != null)
			{
				int length = BinaryHelper.GetBitLength(colorTable.Count / 3 - 1);
				byte bin = (byte)(length - 1);
				colorTableField |= bin; // Size of Global Color Table
				colorTableField |= 128; // Global Color Table Flag
			}

			_writer.Write(colorTableField);
			_writer.Write((byte)0); // Background Color Index
			_writer.Write((byte)0); // Pixel Aspect Ratio

			// Global Color Table
			if (colorTable != null)
				_writer.Write(colorTable.ToArray());
		}

		private void WriteData(byte[] data, ushort width, ushort height, byte lzwMinCodeSize, IList<byte> colorTable = null)
		{
			// Image Descriptor
			_writer.Write((byte)0x2C); // Image Separator
			_writer.Write((ushort)0);  // Image Left Position
			_writer.Write((ushort)0);  // Image Top Position
			_writer.Write(width);      // Image Width
			_writer.Write(height);     // Image Height

			// Local Color Table Flag        1 Bit
			// Interlace Flag                1 Bit
			// Sort Flag                     1 Bit
			// Reserved                      2 Bits
			// Size of Local Color Table     3 Bits
			byte colorTableField = 0;

			if (colorTable != null)
			{
				int length = BinaryHelper.GetBitLength(colorTable.Count / 3 - 1);
				byte bin = (byte)(length - 1);
				colorTableField = bin;  // Size of Local Color Table
				colorTableField |= 128; // Local Color Table Flag
			}

			_writer.Write(colorTableField);

			// Local Color Table
			if (colorTable != null)
				_writer.Write(colorTable.ToArray());

			// Image Data
			_writer.Write(lzwMinCodeSize); // LZW Minimum Code Size

			for (int i = 0; i < data.Length; i += 255)
			{
				byte blockSize = (byte)Math.Min(255, data.Length - i);
				_writer.Write(blockSize);

				for (int n = 0; n < blockSize; n++)
					_writer.Write(data[i + n]);
			}

			_writer.Write((byte)0); // Block Terminator
		}

		private void WriteControlExtension(ushort delay, byte transparencyIndex)
		{
			_writer.Write((byte)0x21);        // Extension Introducer
			_writer.Write((byte)0xF9);        // Graphic Control Label
			_writer.Write((byte)0x04);        // Block Size

			if (transparencyIndex > 0)
				_writer.Write((byte)0x01);
			else
				_writer.Write((byte)0x00);    // [Reserved (3 Bits), Disposal Method (3 Bits), User Input Flag (1 Bit), Transparency Flag (1 Bit)]

			_writer.Write(delay);             // Delay Time
			_writer.Write(transparencyIndex); // Transparency Index
			_writer.Write((byte)0);           // Block Terminator
		}
	}
}
