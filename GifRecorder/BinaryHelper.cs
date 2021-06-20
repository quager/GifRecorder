namespace GifRecorder
{
	public static class BinaryHelper
	{
		public static int GetBitLength(this int value)
		{
			if (value == 0)
				return 1;

			int length = 0;
			while (value > 0)
			{
				value >>= 1;
				length++;
			}

			return length;
		}
	}
}
