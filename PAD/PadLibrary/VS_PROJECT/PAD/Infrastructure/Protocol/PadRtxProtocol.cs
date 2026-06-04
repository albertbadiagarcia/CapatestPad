using System;
using System.Collections.Generic;
using System.Net;

namespace Capatest.Pad
{
    internal sealed class PadRtxProtocol : PadProtocolBase
    {
        private const byte StxByte = 0x02;
        protected override byte Stx
        {
            get { return StxByte; }
        }

        public override PAD_Models Model
        {
            get { return PAD_Models.PadRTX; }
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

            double[] analog = new double[8];
            for (int i = 0; i < 8; i++)
            {
                byte lo = payload[(2 * i) + 2];
                int hi = Convert.ToInt32(payload[(2 * i) + 3]);
                if (hi > 127)
                {
                    hi -= 128;
                    double v = (lo + 256.0 * hi) * (5.0 / 32768.0);
                    analog[i] = (5.0 - v) * -1.0;
                }
                else
                {
                    analog[i] = (lo + 256.0 * hi) * (5.0 / 32768.0);
                }
            }

            int digitalIn = Convert.ToInt32(payload[19]);
            int digitalOut = Convert.ToInt32(payload[20]);

            InputChannelReading[] channels = new InputChannelReading[]
            {
                MakeCounterChannel(Read32(payload, 24), 0),
                MakeCounterChannel(Read32(payload, 28), 1),
                MakeCounterChannel(Read32(payload, 32), 2),
                MakeCounterChannel(Read32(payload, 36), 3)
            };

            data = new PadData(analog, digitalIn, digitalOut, channels,
                               Convert.ToUInt32(payload[40]));
            return true;
        }

        public override byte[] BuildStopScope()
        {
            return BuildStopScopeSamplingCommand();
        }

        protected override double ParseScopeSample(byte[] frame, int index)
        {
            return (frame[5 + (2 * index) + 2] + 255.0 * frame[5 + (2 * index) + 3]) * (5.0 / 32768.0);
        }

        public override byte[] BuildChangeLed(byte r, byte g, byte b, Led_Blink_Modes blinkMode)
        {
            byte[] cmd = new byte[12];
            Array.Copy(new byte[] { Stx, 0, 0, 0x05, 0 }, cmd, 5);
            cmd[5] = 0x10;
            cmd[6] = r > 0 ? (byte)1 : (byte)0;
            cmd[7] = g > 0 ? (byte)1 : (byte)0;
            cmd[8] = b > 0 ? (byte)1 : (byte)0;
            cmd[9] = (byte)blinkMode;
            cmd[10] = (byte)(CheckSum(5, new byte[] { cmd[5], cmd[6], cmd[7], cmd[8], cmd[9] }) & 0xFF);
            cmd[11] = 0x04;
            return cmd;
        }

        public override byte[] BuildSetRelays(IReadOnlyList<RelayStatus> relays)
        {
            byte[] cmd = new byte[9];
            Array.Copy(new byte[] { Stx, 0, 0, 0x02, 0 }, cmd, 5);
            cmd[5] = 0x11;
            cmd[6] = EncodeRelayByte(relays);
            cmd[7] = (byte)(CheckSum(3, new byte[] { cmd[3], cmd[5], cmd[6] }) & 0xFF);
            cmd[8] = 0x04;
            return cmd;
        }

        public override byte[] BuildSetRelaysAndOutputVoltage(
            IReadOnlyList<RelayStatus> relays, double ch0, double ch1)
        {
            byte[] cmd = new byte[13];
            Array.Copy(new byte[] { Stx, 0, 0, 0x06, 0 }, cmd, 5);
            cmd[5] = 0x04;
            cmd[6] = EncodeRelayByte(relays);
            short v0 = Convert.ToInt16(Math.Min(ch0, 4.999) * 819.2);
            short v1 = Convert.ToInt16(Math.Min(ch1, 4.999) * 819.2);
            WriteLegacyInt16(cmd, 7, v0);
            WriteLegacyInt16(cmd, 9, v1);
            byte[] data = new byte[7];
            data[0] = cmd[4];
            for (int i = 1; i < data.Length; i++)
            {
                data[i] = cmd[4 + i];
            }
            cmd[11] = (byte)(CheckSum(7, data) & 0xFF);
            cmd[12] = 0x04;
            return cmd;
        }

        public override byte[] BuildSetAnalogOutput(double ch0, double ch1)
        {
            byte[] cmd = new byte[12];
            Array.Copy(new byte[] { Stx, 0, 0, 0x05, 0 }, cmd, 5);
            cmd[5] = 0x12;
            short v0 = Convert.ToInt16(Math.Min(ch0, 4.999) * 819.2);
            short v1 = Convert.ToInt16(Math.Min(ch1, 4.999) * 819.2);
            WriteLegacyInt16(cmd, 6, v0);
            WriteLegacyInt16(cmd, 8, v1);
            byte[] data = new byte[] { cmd[3], cmd[5], cmd[6], cmd[7], cmd[8], cmd[9] };
            cmd[10] = (byte)(CheckSum(6, data) & 0xFF);
            cmd[11] = 0x04;
            return cmd;
        }

        public byte[] BuildStartAdc()
        {
            return BuildAdcCommand(0x01);
        }

        public byte[] BuildStopAdc()
        {
            return BuildAdcCommand(0x00);
        }

        private byte[] BuildAdcCommand(byte value)
        {
            byte[] cmd = new byte[9];
            Array.Copy(new byte[] { Stx, 0, 0, 0x02, 0 }, cmd, 5);
            cmd[5] = 0x13;
            cmd[6] = value;
            cmd[7] = (byte)(CheckSum(2, new byte[] { cmd[5], cmd[6] }) & 0xFF);
            cmd[8] = 0x04;
            return cmd;
        }

        public byte[] BuildStartAutoStatus(short ms)
        {
            return new byte[] { Stx, 0, 0, 0x02, 0x00, 0x0A, 0x01, 0x0B, 0x04 };
        }

        public byte[] BuildStopAutoStatus()
        {
            byte[] cmd = new byte[8];
            Array.Copy(new byte[] { Stx, 0, 0, 0x01, 0 }, cmd, 5);
            cmd[5] = 0x0A;
            cmd[6] = (byte)(CheckSum(1, new byte[] { cmd[5] }) & 0xFF);
            cmd[7] = 0x04;
            return cmd;
        }

        public byte[] BuildOpenFastAcquisition(
            int frequency, int samples, int channels, int bufferSize,
            Socket_Type socketType, int port, string localIp)
        {
            int freq16 = (int)(short)frequency;
            int freqLo = freq16 & 0xFF;
            int freqHi = (freq16 - freqLo) / 0xFF;

            int smp16 = (int)(short)samples;
            int smpLo = smp16 & 0xFF;
            int smpHi = (smp16 - smpLo) / 0xFF;

            int buf16 = (int)(short)bufferSize;
            int bufLo = buf16 & 0xFF;
            int bufHi = (buf16 - bufLo) / 0xFF;

            int port16 = (int)(short)port;
            int portLo = port16 & 0xFF;
            int portHi = (port16 - portLo) / 0xFF;

            byte sockByte = socketType == Socket_Type.TCP ? (byte)0x01 : (byte)0x02;

            IPAddress ip = IPAddress.Parse(localIp);
            byte[] ipB = ip.GetAddressBytes();

            byte[] data = new byte[]
            {
                0x03,
                (byte)freqLo, (byte)freqHi,
                (byte)smpLo,  (byte)smpHi,
                (byte)channels,
                (byte)bufLo,  (byte)bufHi,
                (byte)portLo, (byte)portHi,
                sockByte,
                ipB[0], ipB[1], ipB[2], ipB[3]
            };

            List<byte> cmd = new List<byte>
            {
                Stx, 0, 0, (byte)data.Length, 0
            };
            cmd.AddRange(data);
            cmd.Add((byte)(CheckSum(data.Length, data) & 0xFF));
            cmd.Add(0x04);
            return cmd.ToArray();
        }

        public byte[] BuildCloseFastAcquisition()
        {
            byte[] cmd = new byte[8];
            Array.Copy(new byte[] { Stx, 0, 0, 0x01, 0 }, cmd, 5);
            cmd[5] = 0x02; // STOP_FAST_ACQUISITION_CMD
            cmd[6] = (byte)(CheckSum(1, new byte[] { cmd[5] }) & 0xFF);
            cmd[7] = 0x04;
            return cmd;
        }

        public byte[] BuildSetFastAdcSamplingRate(int rate)
        {
            rate = Math.Max(1000, Math.Min(10000, rate));
            int r16 = (int)(short)rate;
            int rLo = r16 & 0xFF;
            int rHi = (r16 - rLo) / 0xFF;
            byte[] data = new byte[] { 0x03, (byte)rLo, (byte)rHi };
            byte[] cmd = new byte[10];
            Array.Copy(new byte[] { Stx, 0, 0, 0x03, 0 }, cmd, 5);
            cmd[5] = 0x03;
            cmd[6] = (byte)rLo;
            cmd[7] = (byte)rHi;
            cmd[8] = (byte)(CheckSum(3, data) & 0xFF);
            cmd[9] = 0x04;
            return cmd;
        }
    }
}
