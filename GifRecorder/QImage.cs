using System;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GifRecorderTests")]
namespace GifRecorder
{
	internal enum QualityLevel
	{
		Lowest = 1,
		Low = 2,
		Lower = 3,
		Normal = 4,
		Higher = 5,
		High = 6,
		VeryHigh = 7,
		Highest = 8
	}

	internal class QImage
	{
		private class Node
		{
			public Node[] Children = new Node[8];

			public Node GetOrCreateChild(byte index, bool isLeaf = false)
			{
				if (Children[index] == null)
					Children[index] = isLeaf ? new Leaf() : new Node();

				return Children[index];
			}
		}

		private class Leaf : Node
		{
			private object _locker = new object();
			private bool _colorExists = false;
			private long _redTotal;
			private long _greenTotal;
			private long _blueTotal;
			private bool _hasReplacement;
			private Leaf _replacement;
			private Color _value = Color.White;

			public long Count { get; private set; }

			public void Update(byte red, byte green, byte blue)
			{
				lock (_locker)
				{
					_redTotal += red;
					_greenTotal += green;
					_blueTotal += blue;

					Count++;
					_colorExists = false;
				}
			}

			public void SetReplacement(Leaf replacement)
			{
				_replacement = replacement;
				_hasReplacement = true;
			}

			public Color GetColor()
			{
				if (_hasReplacement)
					return _replacement.GetColor();

				if (_colorExists)
					return _value;

				_value = Color.FromArgb((int)((double)_redTotal / Count), (int)((double)_greenTotal / Count), (int)((double)_blueTotal / Count));
				_colorExists = true;

				return _value;
			}

#if DEBUG
			public override string ToString() => $"Color: ({_value.R}, {_value.G}, {_value.B}), Count: {Count}";
#endif
		}

		private class DataBlock
		{
			public Node Root;
			public int Quality;
			public byte[] Data;
			public Leaf[] Result;
			public int Start;
			public int End;
			public int Offset;
		}

		private int _levels = (int)QualityLevel.Normal;
		private Node _root = new Node();

		public byte[] Palette { get; }
		public byte[] Data { get; private set; }

		public QImage(QualityLevel quality = QualityLevel.Normal)
		{
			_levels = (int)quality;
			Palette = new byte[768]; // 256 RGB bytes
		}

		public void Build(Bitmap image)
		{
			Data = new byte[image.Width * image.Height];
			BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, image.PixelFormat);

			IntPtr pointer = data.Scan0;
			int count = data.Stride * data.Height;
			var bitmapBytes = new byte[count];
			Marshal.Copy(pointer, bitmapBytes, 0, count);

			image.UnlockBits(data);
			image.Dispose();

			Leaf[] imageData = MakeQuantization(bitmapBytes, Data.Length);
			HashSet<Leaf> nodeList = new HashSet<Leaf>(imageData);
			Debug.WriteLine($"Color Number: {nodeList.Count}");

			Dictionary<Color, byte> indices = new Dictionary<Color, byte>();
			List<Leaf> nodes = ReduceSizeTo(nodeList, 256);

			int n = 0;
			foreach (Leaf node in nodes)
			{
				Color value = node.GetColor();
				Palette[n * 3] = value.R;
				Palette[n * 3 + 1] = value.G;
				Palette[n * 3 + 2] = value.B;

				indices[value] = (byte)n;
				n++;
			}

			for (int i = 0; i < imageData.Length; i++)
			{
				Color value = imageData[i].GetColor();

				if (!indices.TryGetValue(value, out byte index))
					throw new Exception("Color Not Found!");

				Data[i] = index;
			}
		}

		internal static byte GetIndex(byte red, byte green, byte blue, int bit)
		{
			int mask = 128 >> bit;
			byte value = (byte)(
				((red & mask) > 0 ? 4 : 0)
				| ((green & mask) > 0 ? 2 : 0)
				| ((blue & mask) > 0 ? 1 : 0)
			);

			return value;
		}

		private Leaf[] MakeQuantization(byte[] bitmapBytes, int dataLength)
		{
			int threads = Environment.ProcessorCount / 2;
			Leaf[] image = new Leaf[dataLength];
			int blockLength = dataLength / threads;

			using (ManualResetEvent resetEvent = new ManualResetEvent(false))
			{
				for (int i = 0; i < threads; i++)
				{
					var block = new DataBlock
					{
						Root = _root,
						Quality = _levels,
						Data = bitmapBytes,
						Result = image,
						Offset = blockLength * i,
						Start = blockLength * i * 4,
						End = i == threads - 1 ? bitmapBytes.Length : blockLength * (i + 1) * 4,
					};

					ThreadPool.QueueUserWorkItem(new WaitCallback(d =>
					{
						DataBlock block = (DataBlock)d;
						MakeBlockQuantization(block);

						if (Interlocked.Decrement(ref threads) == 0)
							resetEvent.Set();
					}), block);
				}

				resetEvent.WaitOne();
			}

			return image;
		}

		private static void MakeBlockQuantization(DataBlock block)
		{
			int n = block.Offset;
			for (int index = block.Start; index < block.End; index++)
			{
				byte b = block.Data[index++];
				byte g = block.Data[index++];
				byte r = block.Data[index++];

				var level = Quantize(block.Root, block.Quality, r, g, b);
				block.Result[n++] = level;
			}
		}

		private static Leaf Quantize(Node root, int levels, byte red, byte green, byte blue)
		{
			Node node = root;
			for (int i = 0; i < levels; i++)
			{
				byte index = GetIndex(red, green, blue, i);
				node = node.GetOrCreateChild(index, i == levels - 1);
			}

			if (node is not Leaf leaf)
				throw new Exception("The Last Node Must be an instance of the Leaf object.");
			
			leaf.Update(red, green, blue);

			return leaf;
		}

		private List<Leaf> ReduceSizeTo(IEnumerable<Leaf> items, int count)
		{
			List<Leaf> result = items.OrderByDescending(l => l.Count).ToList();

			for (int i = result.Count - 1; i >= count; i--)
			{
				var item1 = result[i];
				var item2 = GetNearest(result, item1);

				item1.SetReplacement(item2);
				result.Remove(item1);
			}

			return result;
		}

		private Leaf GetNearest(List<Leaf> items, Leaf source)
		{
			int min = int.MaxValue;
			var color1 = source.GetColor();
			Leaf nearest = null;

			foreach (var item in items)
			{
				if (item == source)
					continue;

				var color2 = item.GetColor();
				int distance = Math.Abs(color1.R - color2.R) + Math.Abs(color1.G - color2.G) + Math.Abs(color1.B - color2.B);

				if (distance < min)
				{
					min = distance;
					nearest = item;
				}
			}

			return nearest;
		}
	}
}
