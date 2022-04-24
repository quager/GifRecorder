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
		private struct POINT
		{
			public int X;
			public int Y;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct CURSORINFO
		{
			public int Size;
			public int Flags;
			public IntPtr HCursor;
			public POINT ScreenPos;
		}

		[DllImport("user32.dll")]
		private static extern bool GetCursorInfo(out CURSORINFO info);

		[DllImport("gdi32.dll")]
		private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

		private enum DeviceCap
		{
			VERTRES = 10,
			DESKTOPVERTRES = 117,
		}

		#endregion

		public const int DefaultFrameRate = 10;
		private const int CursorPointSize = 3;
		private const int CursorArea = 50;
		private readonly Brush _cursorBrush = new SolidBrush(Color.FromArgb(100, Color.Yellow));
		private CancellationTokenSource _cancellationToken;
		private bool _disposed;
		private double _scalingFactor = 1;
		private object _lockObject = new object();

		public bool IsRunning { get; private set; }
		public bool HideCursor { get; set; }
		public double FrameX { get; set; }
		public double FrameY { get; set; }
		public double FrameWidth { get; set; }
		public double FrameHeight { get; set; }
		public int FrameRate { get; set; } = DefaultFrameRate;
		public ushort Delay => (ushort)(100 / FrameRate);

		public delegate void OnFrameCaptured(Bitmap frame);
		public event OnFrameCaptured FrameCaptured;

		public Capture()
		{
			using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
			{
				IntPtr desktop = graphics.GetHdc();
				int logicalHeight = GetDeviceCaps(desktop, (int)DeviceCap.VERTRES);
				int physicalHeight = GetDeviceCaps(desktop, (int)DeviceCap.DESKTOPVERTRES);
				graphics.ReleaseHdc(desktop);

				UpdateScalingFactor((double)physicalHeight / logicalHeight);
			}
		}

		public void UpdateScalingFactor(double scalingFactor)
		{
			_scalingFactor = scalingFactor;
		}

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

		private async Task Capturing()
		{
			while (IsRunning)
			{
				try
				{
					lock (_lockObject)
						OnCaptureFrame(FrameX, FrameY, FrameWidth, FrameHeight);
				}
				catch (Exception e)
				{
					Debug.WriteLine($"Capturing error: {e.Message}");
				}

				await Task.Delay(1000 / FrameRate);
			}
		}

		private void OnCaptureFrame(double x, double y, double width, double height)
		{
			int bitmapWidth = (int)width + (int)width % 4;
			Bitmap bitmap = new Bitmap((int)width, (int)height);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.CopyFromScreen((int)(x * _scalingFactor), (int)(y * _scalingFactor), 0, 0, bitmap.Size);

				if (!HideCursor)
                    DrawCursor(graphics, x * _scalingFactor, y * _scalingFactor);
            }

			FrameCaptured(bitmap);
		}

        private void DrawCursor(Graphics graphics, double x, double y)
        {
            CURSORINFO info;
            info.Size = Marshal.SizeOf(typeof(CURSORINFO));

            if (GetCursorInfo(out info))
            {
                int areaRadius = (int)(CursorArea * _scalingFactor / 2f);
                int pointRadius = (int)(CursorPointSize * _scalingFactor / 2f);

				if (info.ScreenPos.X < areaRadius || info.ScreenPos.Y < areaRadius)
					return;

                int left = (int)(info.ScreenPos.X - areaRadius - x);
                int top = (int)(info.ScreenPos.Y - areaRadius - y);

				graphics.FillEllipse(_cursorBrush, new Rectangle(left, top, CursorArea, CursorArea));

                left = (int)(info.ScreenPos.X - pointRadius - x);
                top = (int)(info.ScreenPos.Y - pointRadius - y);

				graphics.DrawEllipse(Pens.Black, new Rectangle(left, top, (int)(CursorPointSize * _scalingFactor), (int)(CursorPointSize * _scalingFactor)));
                graphics.Flush();
            }
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
