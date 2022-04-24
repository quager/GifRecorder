using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;

namespace GifRecorder
{
	internal class Recorder : IDisposable
	{
		private const byte LzwMinCodeSize = 8;
        private CancellationTokenSource _cancelRecording;
		private FrameQueue _frameQueue;
		private FileStream _fileStream;
		private BinaryWriter _writer;

		private bool _disposed;
		private ushort _height;
		private ushort _width;

		public int RecordedFrames { get; private set; }

		public long RecordedBytes { get; private set; }

		public bool Recording { get; private set; }

		public QualityLevel ImageQuality { get; set; } = QualityLevel.Normal;

		public delegate void OnFrameRecorded();
		public event OnFrameRecorded FrameRecorded;

		public void Dispose()
		{
			if (_disposed)
				return;

			if (Recording)
				Stop();

			_writer.Close();
			_fileStream.Close();
			_disposed = true;
		}

		/// <summary>
		/// Adds Bitmap Data to the frame queue for writing to a GIF Image
		/// </summary>
		/// <param name="frame">Frame to add</param>
		/// <param name="delay">Delay before frame rendering</param>
		public void AddFrame(Bitmap frame, ushort delay)
		{
			if (!Recording)
				return;

			Frame data = new Frame(frame, delay, ImageQuality);
			_frameQueue.Push(data);
		}

		public async Task WriteFrames()
		{
			await _frameQueue.MoveNextAsync();

			do
			{
				try
				{
					Frame frame = _frameQueue.Current;
					if (!Recording || _cancelRecording.Token.IsCancellationRequested)
						return;

					if (RecordedFrames == 0)
					{
						_width = frame.Width;
						_height = frame.Height;
						WriteHeader(_width, _height, 8);
					}

					byte[] encodedData = LzwEncoder.Encode(frame.Data, 256);
					WriteData(encodedData, _width, _height, 0, 0, frame.Palette);
					WriteControlExtension(frame.Delay);

					RecordedFrames++;
					RecordedBytes = _writer.BaseStream.Position;
					FrameRecorded();
				}
				catch (Exception ex)
                {
					Debug.WriteLine(ex.Message);
                }
			}
			while (await _frameQueue.MoveNextAsync());

			_cancelRecording.Cancel();
			Recording = false;
			_writer.Write((byte)0x3B); // Trailer
		}

		public void Start(string filePath)
		{
			_cancelRecording = new CancellationTokenSource();
			_frameQueue = new FrameQueue(_cancelRecording.Token);
			_fileStream = new FileStream(filePath, FileMode.Create);
			_writer = new BinaryWriter(_fileStream);
			RecordedFrames = 0;
			Recording = true;
			Task.Run(WriteFrames);
		}

		private void Stop()
		{
			if (!Recording)
				return;

			_cancelRecording.Cancel();
			Recording = false;
		}

		private void WriteHeader(ushort width, ushort height, byte colorResolution, byte[] colorTable = null)
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
				int length = BinaryHelper.GetBitLength(colorTable.Length / 3 - 1);
				byte bin = (byte)(length - 1);
				colorTableField |= bin; // Size of Global Color Table
				colorTableField |= 128; // Global Color Table Flag
			}

			_writer.Write(colorTableField);
			_writer.Write((byte)0); // Background Color Index
			_writer.Write((byte)0); // Pixel Aspect Ratio

			// Global Color Table
			if (colorTable != null)
				_writer.Write(colorTable);
		}

		private void WriteData(byte[] data, ushort width, ushort height, ushort left, ushort top, byte[] colorTable = null)
		{
			// Image Descriptor
			_writer.Write((byte)0x2C); // Image Separator
			_writer.Write(left);       // Image Left Position
			_writer.Write(top);        // Image Top Position
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
				int length = BinaryHelper.GetBitLength(colorTable.Length / 3 - 1);
				byte bin = (byte)(length - 1);
				colorTableField = bin;  // Size of Local Color Table
				colorTableField |= 128; // Local Color Table Flag
			}

			_writer.Write(colorTableField);

			// Local Color Table
			if (colorTable != null)
				_writer.Write(colorTable.ToArray());

			// Image Data
			_writer.Write(LzwMinCodeSize); // LZW Minimum Code Size

			for (int i = 0; i < data.Length; i += 255)
			{
				byte blockSize = (byte)Math.Min(255, data.Length - i);
				_writer.Write(blockSize);

				for (int n = 0; n < blockSize; n++)
					_writer.Write(data[i + n]);
			}

			_writer.Write((byte)0); // Block Terminator
		}

		private void WriteControlExtension(ushort delay, bool useTransparency = false, byte transparencyIndex = 0, bool isBackgroundFrame = false)
		{
			_writer.Write((byte)0x21);        // Extension Introducer
			_writer.Write((byte)0xF9);        // Graphic Control Label
			_writer.Write((byte)0x04);        // Block Size

			byte packetFields = 0;
			if (!isBackgroundFrame)
				packetFields = 0x12;          // Disposal Method = 3

			if (useTransparency)
				packetFields |= 1;
			
			_writer.Write(packetFields);      // [Reserved (3 Bits), Disposal Method (3 Bits), User Input Flag (1 Bit), Transparency Flag (1 Bit)]
			_writer.Write(delay);             // Delay Time
			_writer.Write(transparencyIndex); // Transparency Index
			_writer.Write((byte)0);           // Block Terminator
		}
	}
}
