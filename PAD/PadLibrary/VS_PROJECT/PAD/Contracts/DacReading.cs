namespace Capatest.Pad
{
    /// <summary>MCP4922 12-bit DAC channel reading.</summary>
    public sealed class DacReading
    {
        public int Raw12 { get; } // 0–4095
        public double Volts
        {
            get { return Raw12 * 5.0 / 4096.0; }
        }

        public DacReading(int raw12)
        {
            Raw12 = raw12;
        }
    }
}
