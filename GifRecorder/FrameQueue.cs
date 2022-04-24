using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace GifRecorder
{
	internal class Frame
    {
		public byte[] Data { get; set; }
		public byte[] Palette { get; set; }
		public ushort Width { get; set; }
		public ushort Height { get; set; }
		public ushort Delay { get; set; }

		public Frame(Bitmap bitmap, ushort delay) : this(bitmap, delay, QualityLevel.Normal)
		{
		}

		public Frame(Bitmap bitmap, ushort delay, QualityLevel quality)
        {
			Width = (ushort)bitmap.Width;
			Height = (ushort)bitmap.Height;
			Delay = delay;

			var image = new QImage(quality);
			image.Build(bitmap);

			Data = image.Data;
			Palette = image.Palette;
		}
	}

	internal class FrameQueue
	{
		private ConcurrentQueue<Frame> _queue;
		private CancellationToken _cancellationToken;

		public Frame Current { get; private set; }

		public FrameQueue(CancellationToken cancellationToken)
		{
			_queue = new ConcurrentQueue<Frame>();
			_cancellationToken = cancellationToken;
		}

		public void Push(Frame frame) => _queue.Enqueue(frame);

		public async Task<bool> MoveNextAsync()
		{
			while (!_cancellationToken.IsCancellationRequested)
			{
				await Task.Run(() =>
				{
					while (_queue.IsEmpty && !_cancellationToken.IsCancellationRequested);
				});

				if (_queue.TryDequeue(out Frame frame))
				{
					Current = frame;
					return true;
				}
			}

			return false;
		}
    }
}
