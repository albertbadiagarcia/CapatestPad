using System;
using System.Collections.Generic;

namespace Capatest.Pad
{
    internal sealed class Pad728Protocol : PadProtocolBase
    {
        protected override byte Stx
        {
            get { return 0x02; }
        }

        public override PAD_Models Model
        {
            get { return PAD_Models.Pad728; }
        }
        public override int RelayCount
        {
            get { return 8; }
        }
        public override int CounterCount
        {
            get { return 2; }
        }
        public override int AnalogInputCount
        {
            get { return 8; }
        }

        public override bool TryParseStatus(byte[] frame, out PadData data)
        {
            data = null;
            byte[] payload;
            if (!TryExtractPayload(frame, 33, out payload))
            {
                return false;
            }
            if (Convert.ToInt32(payload[0]) != 1)
            {
                return false;
            }

            double[] analog = ParseAnalogInputs(payload, 8,
                (lo, hi) => (lo + 256.0 * hi) * (5.0 / 4096.0));

            int digitalIn = Convert.ToInt32(payload[19]);
            int digitalOut = Convert.ToInt32(payload[20]);

            InputChannelReading[] channels = new InputChannelReading[]
            {
                MakeCounterChannel(Read16(payload, 24), 0),
                MakeCounterChannel(Read16(payload, 28), 1)
            };

            uint remoteCmd = Convert.ToUInt32(payload[32]);

            data = new PadData(analog, digitalIn, digitalOut, channels, remoteCmd);
            return true;
        }

        public override byte[] BuildStopScope()
        {
            return BuildStopScopeSamplingCommand();
        }

        protected override double ParseScopeSample(byte[] frame, int index)
        {
            return (frame[5 + (2 * index) + 2] + (255.0 * frame[5 + (2 * index) + 3])) * (5.0 / 4096.0);
        }
    }
}
