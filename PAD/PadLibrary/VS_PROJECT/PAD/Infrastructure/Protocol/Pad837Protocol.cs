using System;
using System.Collections.Generic;

namespace Capatest.Pad
{
    /// <summary>
    /// Protocol implementation for PAD837.
    /// 16-bit unsigned analog inputs, 4 x 32-bit counters, 8 relays, LED control.
    /// </summary>
    internal sealed class Pad837Protocol : PadProtocolBase
    {
        protected override byte Stx
        {
            get { return 0x01; }
        }

        public override PAD_Models Model
        {
            get { return PAD_Models.Pad837; }
        }
        public override int RelayCount
        {
            get { return 8; }
        }
        public override int CounterCount
        {
            get { return 4; }
        }
        public override int AnalogInputCount
        {
            get { return 8; }
        }

        public override bool TryParseStatus(byte[] frame, out PadData data)
        {
            data = null;
            byte[] payload;
            if (!TryExtractPayload(frame, 41, out payload))
            {
                return false;
            }
            int frameCase = Convert.ToInt32(payload[0]);
            if (frameCase != 1 && frameCase != 0)
            {
                return false;
            }

            double[] analog = ParseAnalogInputs(payload, 8,
                (lo, hi) => (lo + 255.0 * hi) * (5.0 / 65536.0));

            int digitalIn = Convert.ToInt32(payload[19]);
            int digitalOut = Convert.ToInt32(payload[20]);

            InputChannelReading[] channels = new InputChannelReading[]
            {
                MakeCounterChannel(Read32(payload, 24), 0),
                MakeCounterChannel(Read32(payload, 28), 1),
                MakeCounterChannel(Read32(payload, 32), 2),
                MakeCounterChannel(Read32(payload, 36), 3)
            };

            uint remoteCmd = Convert.ToUInt32(payload[40]);

            data = new PadData(analog, digitalIn, digitalOut, channels, remoteCmd);
            return true;
        }

        public override byte[] BuildStopScope()
        {
            return BuildStopScopeSamplingCommand();
        }

        protected override double ParseScopeSample(byte[] frame, int index)
        {
            return (frame[(2 * index) + 2] + (255.0 * frame[(2 * index) + 3])) * (5.0 / 65536.0);
        }

        public override byte[] BuildChangeLed(byte r, byte g, byte b, Led_Blink_Modes blinkMode)
        {
            return Array.Empty<byte>();
        }

        public byte[] BuildStartAutoStatus(short ms)
        {
            byte[] msBytes = BitConverter.GetBytes(ms);
            byte msLo = msBytes.Length > 0 ? msBytes[0] : (byte)0x32;
            byte msHi = msBytes.Length > 1 ? msBytes[1] : (byte)0x00;

            byte[] cmd = new byte[11];
            cmd[0] = Stx;
            cmd[1] = 0; cmd[2] = 0;
            cmd[3] = 0; cmd[4] = 0x04;
            cmd[5] = 0x0A;
            cmd[6] = 0x01;
            cmd[7] = msLo;
            cmd[8] = msHi;
            byte[] data = new byte[] { cmd[5], cmd[6], cmd[7], cmd[8] };
            cmd[9]  = (byte)(CheckSum(4, data) & 0xFF);
            cmd[10] = 0x04;
            return cmd;
        }

        public byte[] BuildStopAutoStatus()
        {
            byte[] cmd = new byte[9];
            cmd[0] = Stx;
            cmd[1] = 0; cmd[2] = 0;
            cmd[3] = 0; cmd[4] = 0x02;
            cmd[5] = 0x0A;
            cmd[6] = 0x00;
            byte[] data = new byte[] { cmd[5], cmd[6] };
            cmd[7] = (byte)(CheckSum(2, data) & 0xFF);
            cmd[8] = 0x04;
            return cmd;
        }
    }
}
