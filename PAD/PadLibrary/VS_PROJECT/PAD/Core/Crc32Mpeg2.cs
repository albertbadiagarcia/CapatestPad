using System.Text;

namespace Capatest.Pad
{
    internal static class Crc32Mpeg2
    {
        public static uint Compute(string asciiData)
        {
            return Compute(Encoding.ASCII.GetBytes(asciiData));
        }

        public static uint Compute(byte[] data, int length = -1)
        {
            if (length < 0)
            {
                length = data.Length;
            }
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= (uint)data[i] << 24;
                for (int b = 0; b < 8; b++)
                {
                    crc = (crc & 0x80000000) != 0
                        ? ((crc << 1) ^ 0x04C11DB7)
                        : (crc << 1);
                }
            }
            return crc;
        }
    }
}
