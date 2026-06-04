namespace Capatest.Pad
{
    /// <summary>HX710A 24-bit differential input reading.</summary>
    public sealed class WeightReading
    {
        public bool IsValid { get; }
        public int RawSigned { get; }
        public double Microvolts { get; }
        public double? CalibratedPercent { get; }

        public static readonly WeightReading Invalid = new WeightReading();

        private WeightReading() { IsValid = false; }

        public WeightReading(int rawSigned24, double? calibratedPercent = null)
        {
            IsValid = true;
            RawSigned = rawSigned24;
            Microvolts = rawSigned24 * 20000.0 / 8388608.0;
            CalibratedPercent = calibratedPercent;
        }
    }
}
