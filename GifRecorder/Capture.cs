using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GifRecorder
{
	internal class Capture : IDisposable
	{
		#region Dll import

		[StructLayout(LayoutKind.Sequential)]
		public struct POINT
		{
			public int X;
			public int Y;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct CURSORINFO
		{
			public int Size;
			public int Flags;
			public IntPtr HCursor;
			public POINT ScreenPos;
		}

		[DllImport("user32.dll")]
		public static extern bool GetCursorInfo(out CURSORINFO info);

		#endregion

		private bool _disposed;
		private CancellationTokenSource _cancellationToken;
		private readonly Brush _cursorBrush = new SolidBrush(Color.FromArgb(100, Color.Yellow));
		private const int DefaultFrameRate = 10;
		private const int CursorPointSize = 3;
		private const int CursorArea = 50;

		public bool IsRunning { get; private set; }
		public bool HideCursor { get; set; }
		public double FrameX { get; set; }
		public double FrameY { get; set; }
		public double FrameWidth { get; set; }
		public double FrameHeight { get; set; }
		public int FrameRate { get; set; } = DefaultFrameRate;
		public ushort Delay => (ushort)(100 / FrameRate);

		public event Action<Bitmap> FrameCaptured;

		public void Start()
		{
			if (IsRunning)
				return;

			IsRunning = true;
			_cancellationToken = new CancellationTokenSource();
			Task.Run(Capturing, _cancellationToken.Token);
		}

		public void Stop()
		{
			if (!IsRunning)
				return;

			IsRunning = false;
			_cancellationToken.Cancel();
		}

		private void Capturing()
		{
			while (IsRunning)
			{
				try
				{
					OnCaptureFrame(FrameX, FrameY, FrameWidth, FrameHeight);
				}
				catch (Exception e)
				{
					Debug.WriteLine($"Capturing error: {e.Message}");
				}

				Thread.Sleep(Delay);
			}
		}

		private void OnCaptureFrame(double x, double y, double width, double height)
		{
			Bitmap bitmap = new Bitmap((int)width, (int)height);
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.CopyFromScreen((int)x, (int)y, 0, 0, bitmap.Size);

				if (!HideCursor)
				{
					CURSORINFO info;
					info.Size = Marshal.SizeOf(typeof(CURSORINFO));

					if (GetCursorInfo(out info))
					{
						int left = (int)(info.ScreenPos.X - CursorArea / 2f - x);
						int top = (int)(info.ScreenPos.Y - CursorArea / 2f - y);

						g.FillEllipse(_cursorBrush, new Rectangle(left, top, CursorArea, CursorArea));

						left = (int)(info.ScreenPos.X - CursorPointSize / 2f - x);
						top = (int)(info.ScreenPos.Y - CursorPointSize / 2f - y);

						g.DrawEllipse(Pens.Black, new Rectangle(left, top, CursorPointSize, CursorPointSize));
						g.Flush();
					}
				}
			}

			FrameCaptured?.Invoke(bitmap);
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			_cancellationToken?.Dispose();
			_cancellationToken = null;
			_cursorBrush?.Dispose();
			_disposed = true;
		}
	}
}
