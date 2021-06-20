using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GifRecorder
{
	public static class LzwEncoder
	{
		private static readonly IEqualityComparer<byte[]> _comparer = new ArrayComparer();
		private const int MaxLzwCodeLength = 4095;

		private class DataStream
		{
			private const int DataLength = 8;
			private List<byte> _data;
			private int _codeShift;
			private byte _lastCode;
			private int _rest;

			public DataStream()
			{
				_data = new List<byte>();
				_codeShift = 0;
				_lastCode = 0;
				_rest = 0;
			}

			public void Write(int value, int minBitLength)
			{
				int code = value << _codeShift;
				_lastCode |= (byte)code;
				_rest = code >> DataLength;

				int codeLength = _codeShift + minBitLength;
				_codeShift = codeLength % DataLength;

				while (codeLength >= DataLength)
				{
					_data.Add(_lastCode);
					_lastCode = (byte)_rest;
					_rest >>= DataLength;
					codeLength -= DataLength;
				}

				if (_rest > byte.MaxValue)
					Write(_rest, minBitLength);
			}

			public byte[] GetData()
			{
				if (_codeShift > 0)
				{
					_data.Add(_lastCode);
					_codeShift = 0;
					_lastCode = 0;
					_rest = 0;
				}

				if (_data.Count > 0)
					return _data.ToArray();

				return Array.Empty<byte>();
			}
		}

		private class ArrayComparer : IEqualityComparer<byte[]>
		{
			public bool Equals(byte[] x, byte[] y)
			{
				if (x.Length != y.Length)
					return false;

				for (int i = x.Length - 1; i >= 0; i--)
				{
					if (x[i] != y[i])
						return false;
				}

				return true;
			}

			public int GetHashCode([DisallowNull] byte[] obj)
			{
				long hash = obj.Length;
				foreach (byte item in obj)
					hash += item.GetHashCode();

				return (int)hash;
			}
		}

		public static byte[] Encode(byte[] data, int dictSize)
		{
			int clearCode = dictSize;
			int endCode = clearCode + 1;
			int lastIndex = endCode + 1;
			int bitsPerCode = lastIndex.GetBitLength();

			var output = new DataStream();
			var codeTable = new Dictionary<byte[], int>(_comparer);
			ResetCodeTable(codeTable, output, dictSize, clearCode, bitsPerCode);

			(int Index, byte[] Value) previous = (data[0], new byte[] { data[0] });
			int startBitsPerCode = bitsPerCode;

			for (int i = 1; i < data.Length; i++)
			{
				byte currentCode = data[i];
				int previousIndex = previous.Index;

				byte[] newCode = CreateNewCode(previous.Value, currentCode);
				previous = (currentCode, new byte[] { currentCode });

				if (codeTable.TryGetValue(newCode, out int index))
				{
					previous = (index, newCode);
					continue;
				}

				output.Write(previousIndex, bitsPerCode);

				if (lastIndex > MaxLzwCodeLength)
				{
					ResetCodeTable(codeTable, output, dictSize, clearCode, bitsPerCode);
					lastIndex = endCode + 1;
					bitsPerCode = startBitsPerCode;
				}
				else
				{
					codeTable.Add(newCode, lastIndex);
					CheckMinLength(lastIndex, ref bitsPerCode);
					lastIndex++;
				}
			}

			output.Write(previous.Index, bitsPerCode);
			output.Write(endCode, bitsPerCode);
			byte[] result = output.GetData();

			return result;
		}

		private static void ResetCodeTable(Dictionary<byte[], int> table, DataStream output, int dictSize, int clearCode, int bitsPerCode)
		{
			output.Write(clearCode, bitsPerCode);

			table.Clear();
			for (int i = 0; i < dictSize; i++)
				table.Add(new[] { (byte)i }, i);
		}

		private static byte[] CreateNewCode(byte[] old, byte addition)
		{
			byte[] result = new byte[old.Length + 1];
			Array.Copy(old, result, old.Length);
			result[result.Length - 1] = addition;

			return result;
		}

		private static void CheckMinLength(int lastIndex, ref int minLength)
		{
			int bitLength = lastIndex.GetBitLength();

			if (bitLength > minLength)
				minLength = bitLength;
		}
	}
}
