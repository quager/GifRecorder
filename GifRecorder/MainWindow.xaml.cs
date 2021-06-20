using System;
using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Windows.Media;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Drawing;
using System.Threading;
using System.IO;

namespace GifRecorder
{
	public partial class MainWindow : Window, INotifyPropertyChanged
	{
		public enum SettingsSide
		{
			OnLeft = 0,
			OnRight = 2
		}

		private Capture _capture;
		private Recorder _recorder;
		private string _filePath;
		private bool _internalUpdate;
		private bool _recording;
		private double _frameHeight;
		private double _frameWidth;
		private int _framesRecorded;
		private BitmapSource _frame;
		private string _recordingTime = "00:00";
		private string _recordedBytes = "0 B";
		private Stopwatch _timer;
		private int _settingsColumn = (int)SettingsSide.OnRight;

		public CornerRadius HeaderCornerRadius => OnLeftSide ? new CornerRadius(5, 0, 0, 0) : new CornerRadius(0, 5, 0, 0);
		public CornerRadius WindowCornerRadius => OnLeftSide ? new CornerRadius(5, 0, 0, 5) : new CornerRadius(0, 5, 5, 0);

		public bool OnLeftSide
		{
			get => _settingsColumn == (int)SettingsSide.OnLeft;
			set
			{
				SettingsColumn = value ? (int)SettingsSide.OnLeft : (int)SettingsSide.OnRight;
				_capture.FrameX = Math.Ceiling(Left + BorderThickness.Left + (OnLeftSide ? ControlPanel.ActualWidth + 2 : 0));

				OnPropertyChanged(nameof(WindowCornerRadius));
				OnPropertyChanged(nameof(HeaderCornerRadius));
			}
		}

		public int SettingsColumn
		{
			get => _settingsColumn;
			set => SetProperty(ref _settingsColumn, value);
		}

		public bool Recording
		{
			get => _recording;
			set
			{
				if (!SetProperty(ref _recording, value))
					return;

				if (value)
				{
					if (!StartRecording())
						SetProperty(ref _recording, false);
				}
				else
					StopRecording();
			}
		}

		public string RecordingTime
		{
			get => _recordingTime;
			set => SetProperty(ref _recordingTime, value);
		}

		public string RecordedBytes
		{
			get => _recordedBytes;
			set => SetProperty(ref _recordedBytes, value);
		}

		public int FramesRecorded
		{
			get => _framesRecorded;
			set => SetProperty(ref _framesRecorded, value);
		}

		public double FrameHeight
		{
			get => _frameHeight;
			set
			{
				if (SetProperty(ref _frameHeight, value))
					_capture.FrameHeight = value;
			}
		}

		public double FrameWidth
		{
			get => _frameWidth;
			set
			{
				if (SetProperty(ref _frameWidth, value))
					_capture.FrameWidth = value;
			}
		}

		public BitmapSource Frame
		{
			get => _frame;
			set => SetProperty(ref _frame, value);
		}

		public string FilePath
		{
			get => _filePath;
			set => SetProperty(ref _filePath, value);
		}

		public event PropertyChangedEventHandler PropertyChanged;

		public MainWindow()
		{
			_capture = new Capture();

			InitializeComponent();
			DataContext = this;

			_capture.FrameCaptured += OnFrameCaptured;
		}

		private void OnFrameCaptured(Bitmap bitmap)
		{
			BitmapSource frame = CreateBitmapSource(bitmap);
			frame.Freeze();
			Frame = frame;

			if (!_recorder.Recording)
				return;

			FormatConvertedBitmap converted = new FormatConvertedBitmap();
			converted.BeginInit();
			converted.Source = frame;
			converted.DestinationFormat = PixelFormats.Indexed8;
			converted.DestinationPalette = BitmapPalettes.Halftone256;
			converted.EndInit();

			_recorder.WriteFrame(converted, _capture.Delay);
			FramesRecorded = _recorder.RecordedFrames;
			RecordedBytes = FormatFileSize(_recorder.RecordedBytes);
		}

		private static BitmapSource CreateBitmapSource(Bitmap bitmap)
		{
			System.Drawing.Imaging.BitmapData bitmapData = bitmap.LockBits(
				new Rectangle(0, 0, bitmap.Width, bitmap.Height),
				System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

			BitmapSource bitmapSource = BitmapSource.Create(
				bitmapData.Width, bitmapData.Height,
				bitmap.HorizontalResolution, bitmap.VerticalResolution,
				PixelFormats.Bgr32, null,
				bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Stride);

			bitmap.UnlockBits(bitmapData);
			return bitmapSource;
		}

		private bool StartRecording()
		{
			if (string.IsNullOrWhiteSpace(FilePath))
			{
				MessageBox.Show("File path cannot be empty!");
				return false;
			}

			try
			{
				_recorder = new Recorder();
				_recorder.Start(FilePath);
			}
			catch (IOException)
			{
				return false;
			}

			_capture.Start();

			_timer = Stopwatch.StartNew();
			Task.Run(() =>
			{
				while (_timer?.IsRunning == true)
				{
					RecordingTime = _timer.Elapsed.ToString("mm\\:ss");
					Thread.Sleep(TimeSpan.FromSeconds(1));
				}
			});

			return true;
		}

		private void StopRecording()
		{
			_timer.Stop();
			_capture.Stop();
			_recorder.Stop();
			_recorder.Dispose();
		}

		private static string FormatFileSize(long recordedBytes)
		{
			decimal giga = Math.Round(recordedBytes / 1000000000m, 2);
			if (giga >= 1)
				return $"{giga} GB";

			decimal mega = Math.Round(recordedBytes / 1000000m, 2);
			if (mega >= 1)
				return $"{mega} MB";

			decimal kilo = Math.Round(recordedBytes / 1000m, 2);
			if (kilo >= 1)
				return $"{kilo} KB";

			return $"{recordedBytes} B";
		}

		private void OnClose(object sender, RoutedEventArgs e) => Close();

		private void Frame_Closing(object sender, CancelEventArgs e)
		{
			if (Recording)
				StopRecording();
		}

		private void Frame_MouseDown(object sender, MouseButtonEventArgs e) => DragMove();

		private void OnSelectFile(object sender, RoutedEventArgs e)
		{
			var dialog = new SaveFileDialog
			{
				Filter = "GIF Image|*.gif",
				AddExtension = true,
				FilterIndex = 0,
				Title = "Select file to save the result"
			};

			if (dialog.ShowDialog() != true)
				return;

			FilePath = dialog.FileName;
		}

		private void OnLocationChanged(object sender, EventArgs e)
		{
			_capture.FrameX = Math.Ceiling(Left + BorderThickness.Left + (OnLeftSide ? ControlPanel.ActualWidth + 2 : 0));
			_capture.FrameY = Math.Ceiling(Top + BorderThickness.Top);
		}

		private void OnSizeChanged(object sender, SizeChangedEventArgs e)
		{
			if (_internalUpdate)
				return;

			FrameWidth = Math.Truncate(e.NewSize.Width - BorderThickness.Left - BorderThickness.Right - ControlPanel.ActualWidth - 2);
			FrameHeight = Math.Truncate(e.NewSize.Height - BorderThickness.Top - BorderThickness.Bottom);
		}

		private void SetHdSize(object sender, RoutedEventArgs e) => SetSize(1280, 720);

		private void SetFullHdSize(object sender, RoutedEventArgs e) => SetSize(1920, 1080);

		private void SetSize(int width, int height)
		{
			_internalUpdate = true;

			FrameWidth = width;
			FrameHeight = height;

			Width = width + BorderThickness.Left + BorderThickness.Right + ControlPanel.ActualWidth + 2;
			Height = height + BorderThickness.Top + BorderThickness.Bottom;

			_internalUpdate = false;
		}

		private bool SetProperty<T>(ref T instance, object value, [CallerMemberName] string propertyName = null)
		{
			if (Equals(instance, value))
				return false;

			instance = (T)value;
			OnPropertyChanged(propertyName);
			return true;
		}

		private void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
