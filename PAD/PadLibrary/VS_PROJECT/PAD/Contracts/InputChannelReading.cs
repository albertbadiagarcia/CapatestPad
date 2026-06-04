namespace Capatest.Pad
{
    public sealed class InputChannelReading
    {
        /// <summary>Current logic level of the input (true = high).</summary>
        public bool Level { get; }

        /// <summary>Total accumulated pulse count (32-bit low from PadRTX2 frame).</summary>
        public ulong Count { get; }

        /// <summary>
        /// Estimated frequency in Hz, with up to 2 decimal places.
        /// 0 means no valid estimate (counter idle, too slow, or device does not report it).
        /// </summary>
        public double FrequencyHz { get; }

        public InputChannelReading(bool level, ulong count, double frequencyHz = 0)
        {
            Level = level;
            Count = count;
            FrequencyHz = frequencyHz;
        }
    }
}
