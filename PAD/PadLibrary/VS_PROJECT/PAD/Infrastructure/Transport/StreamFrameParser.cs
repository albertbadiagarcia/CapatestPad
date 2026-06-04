using System;
using System.Collections.Generic;
using System.Text;

namespace Capatest.Pad
{
    internal static class StreamFrameParser
    {
        private static readonly byte[] AdfMagic = { 0x41, 0x44, 0x46, 0x31 };
        private const int AdfHeaderLen = 12;
        private const int AdfFrameLen = 16;
        private const int AdfMaxPacketLen = 4096;

        public static void Parse(List<byte> buffer, Action<byte[]> onFrame)
        {
            while (buffer.Count > 0)
            {
                int magicAt = Find(buffer, AdfMagic);

                if (magicAt == 0)
                {
                    if (buffer.Count < AdfHeaderLen)
                    {
                        return;
                    }
                    int count = buffer[8] | (buffer[9] << 8);
                    int totalLen = AdfHeaderLen + count * AdfFrameLen;
                    if (count == 0 || totalLen > AdfMaxPacketLen)
                    {
                        SplitLines(buffer.GetRange(0, 1).ToArray(), onFrame);
                        buffer.RemoveRange(0, 1);
                        continue;
                    }
                    if (buffer.Count < totalLen)
                    {
                        return;
                    }
                    byte[] packet = buffer.GetRange(0, totalLen).ToArray();
                    buffer.RemoveRange(0, totalLen);
                    onFrame(packet);
                    continue;
                }

                if (magicAt > 0)
                {
                    SplitLines(buffer.GetRange(0, magicAt).ToArray(), onFrame);
                    buffer.RemoveRange(0, magicAt);
                    continue;
                }

                int newlineAt = buffer.IndexOf((byte)'\n');
                if (newlineAt < 0)
                {
                    return;
                }
                int lineEnd = (newlineAt > 0 && buffer[newlineAt - 1] == (byte)'\r')
                    ? newlineAt - 1
                    : newlineAt;
                string line = Encoding.UTF8.GetString(buffer.GetRange(0, lineEnd).ToArray());
                buffer.RemoveRange(0, newlineAt + 1);
                if (line.Length > 0)
                {
                    onFrame(Encoding.ASCII.GetBytes(line));
                }
            }
        }

        private static void SplitLines(byte[] bytes, Action<byte[]> onFrame)
        {
            int start = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != (byte)'\n')
                {
                    continue;
                }
                int lineEnd = (i > 0 && bytes[i - 1] == (byte)'\r') ? i - 1 : i;
                if (lineEnd > start)
                {
                    string text = Encoding.UTF8.GetString(bytes, start, lineEnd - start);
                    if (text.Length > 0)
                    {
                        onFrame(Encoding.ASCII.GetBytes(text));
                    }
                }
                start = i + 1;
            }
            if (start < bytes.Length)
            {
                string text = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
                if (text.Length > 0)
                {
                    onFrame(Encoding.ASCII.GetBytes(text));
                }
            }
        }

        private static int Find(List<byte> buffer, byte[] pattern)
        {
            int limit = buffer.Count - pattern.Length;
            for (int i = 0; i <= limit; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
