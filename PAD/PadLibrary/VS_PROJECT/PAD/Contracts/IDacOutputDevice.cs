using System.Collections.Generic;

namespace Capatest.Pad
{
    /// <summary>
    /// Device with MCP4922 12-bit DAC outputs (2 channels, 0–5 V).
    /// </summary>
    public interface IDacOutputDevice : IPadDevice
    {
        IReadOnlyList<DacReading> DacReadings { get; }

        void SetDac(int channel, double volts);
        void SetDacRaw(int channel, int raw12);
        void ClearDac();
    }
}
