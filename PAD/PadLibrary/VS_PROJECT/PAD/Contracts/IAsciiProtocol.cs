using System.Collections.Generic;

namespace Capatest.Pad
{
    public interface IAsciiProtocol : IPadProtocol
    {
        // Parses the full 284-char status frame including weight (with load %) and DAC fields.
        bool TryParseFullStatusLine(
            string line,
            out PadData data,
            out IReadOnlyList<WeightReading> weightReadings,
            out IReadOnlyList<DacReading>    dacReadings);

        // Extra command builders (return ASCII bytes)
        byte[] BuildSetDac(int channel, double volts);
        byte[] BuildSetDacRaw(int channel, int raw12);
        byte[] BuildClearDac();
        // BuildClearRelay and BuildClearCounters are in IPadProtocol
        byte[] BuildClearRelay();
        byte[] BuildAdcFastStart(int frequencyHz, int samples = -1);
        byte[] BuildAdcFastStop();
    }
}
