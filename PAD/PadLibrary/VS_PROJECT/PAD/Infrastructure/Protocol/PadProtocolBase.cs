using System;
using System.Collections.Generic;
using System.Net;

namespace Capatest.Pad
{
    internal abstract class PadProtocolBase : IPadProtocol
    {
        // Protocol byte constants
        protected abstract byte Stx { get; }
        private const byte Adl = 0x00;
        private const byte Adh = 0x00;
        private const byte Eot = 0x04;
        private const byte Stt = 0x01;
        private const byte Sco = 0x05;
        private const byte Rco = 0x06;
        private const byte Rhw = 0x08;

        public abstract PAD_Models Model { get; }
        public abstract int RelayCount { get; }
        public abstract int CounterCount { get; }
        public abstract int AnalogInputCount { get; }
        private readonly bool[] _channelIsFrequency = new bool[4];
        private int _lastScopeChannel;
        private int _lastScopeSampling;

        protected static short CheckSum(int size, byte[] data)
        {
            // XOR-based LRC, matching the legacy PAD_Library (Pad_Communication.cs
            // CheckSum_Calculate) that the firmware was built against. An additive
            // sum diverges from this whenever bytes share set bits, and can overflow
            // a byte boundary to exactly 0x00 for specific payloads (e.g. periodCount
            // = 250 in BuildConfigureCounters), which the firmware mishandles.
            short checksum = 0;
            for (int i = 0; i < size; i++)
            {
                checksum ^= data[i];
            }
            return checksum;
        }

        protected byte LenLow
        {
            get { return (byte)(Stx == 0x01 ? 0x00 : 0x01); }
        }
        protected byte LenHigh
        {
            get { return (byte)(Stx == 0x01 ? 0x01 : 0x00); }
        }

        protected byte[] Header(byte lenPayload)
        {
            if (Stx == 0x01)  // Pad837
            {
                return new byte[] { Stx, Adl, Adh, 0x00, lenPayload };
            }
            else // Pad728  PadRTX
            {
                return new byte[] { Stx, Adl, Adh, lenPayload, 0x00 };
            }
        }

        public byte[] BuildStatusRequest()
        {
            byte[] cmd = new byte[8];
            byte[] hdr = Header(0x01);
            Array.Copy(hdr, cmd, hdr.Length);
            cmd[5] = Stt;
            cmd[6] = 0x01;
            cmd[7] = Eot;
            return cmd;
        }

        public byte[] BuildConfigureCounters(Counters_Mode mode, int periodCount)
        {
            UpdateMode(mode);
            byte[] cmd = new byte[12];
            byte[] data = new byte[5];
            byte[] hdr = Header(0x05);
            Array.Copy(hdr, cmd, hdr.Length);
            cmd[5] = Sco;
            ApplyCounterMode(mode, periodCount, cmd, 6);
            data[0] = cmd[5]; data[1] = cmd[6]; data[2] = cmd[7];
            data[3] = cmd[8]; data[4] = cmd[9];
            cmd[10] = (byte)(CheckSum(5, data) & 0xFF);
            cmd[11] = Eot;
            return cmd;
        }

        public byte[] BuildConfigureCountersAndRelay(Counters_Mode mode, int periodCount, int relay, int relayCount)
        {
            UpdateMode(mode);
            byte[] cmd = new byte[13];
            byte[] data = new byte[6];
            byte[] hdr = Header(0x06);
            Array.Copy(hdr, cmd, hdr.Length);
            cmd[5] = Sco;
            ApplyCounterMode(mode, periodCount, cmd, 6);
            cmd[9] = (byte)(relayCount & 0xFF);
            cmd[10] = (byte)(relay & 0xFF);
            data[0] = cmd[5]; data[1] = cmd[6]; data[2] = cmd[7];
            data[3] = cmd[8]; data[4] = cmd[9]; data[5] = cmd[10];
            cmd[11] = (byte)(CheckSum(6, data) & 0xFF);
            cmd[12] = Eot;
            return cmd;
        }

        private void UpdateMode(Counters_Mode mode)
        {
            switch (mode)
            {
                case Counters_Mode.All_Counting:
                    for (int i = 0; i < 4; i++)
                    {
                        _channelIsFrequency[i] = false;
                    }
                    break;
                case Counters_Mode.All_Frequency:
                    for (int i = 0; i < 4; i++)
                    {
                        _channelIsFrequency[i] = true;
                    }
                    break;
                case Counters_Mode.First_Counting:
                    _channelIsFrequency[0] = false;
                    break;
                case Counters_Mode.First_Frequency:
                    _channelIsFrequency[0] = true;
                    break;
                case Counters_Mode.Second_Counting:
                    _channelIsFrequency[1] = false;
                    break;
                case Counters_Mode.Second_Frequency:
                    _channelIsFrequency[1] = true;
                    break;
                case Counters_Mode.Third_Counting:
                    _channelIsFrequency[2] = false;
                    break;
                case Counters_Mode.Third_Frequency:
                    _channelIsFrequency[2] = true;
                    break;
                case Counters_Mode.Fourth_Counting:
                    _channelIsFrequency[3] = false;
                    break;
                case Counters_Mode.Fourth_Frequency:
                    _channelIsFrequency[3] = true;
                    break;
            }
        }

        protected InputChannelReading MakeCounterChannel(ulong rawValue, int channelIndex)
        {
            bool isFreq = channelIndex < _channelIsFrequency.Length
                       && _channelIsFrequency[channelIndex];
            if (isFreq)
            {
                return new InputChannelReading(level: false, count: 0, frequencyHz: (double)rawValue);
            }
            return new InputChannelReading(level: false, count: rawValue, frequencyHz: 0);
        }

        private static void ApplyCounterMode(Counters_Mode mode, int periodCount, byte[] cmd, int offset)
        {
            switch (mode)
            {
                case Counters_Mode.All_Counting:    cmd[offset]=0; cmd[offset+1]=0; cmd[offset+2]=0; cmd[offset+3]=0; break;
                case Counters_Mode.All_Frequency:   cmd[offset]=0; cmd[offset+1]=1; cmd[offset+2]=(byte)(periodCount&0xFF); cmd[offset+3]=0; break;
                case Counters_Mode.First_Counting:  cmd[offset]=1; cmd[offset+1]=0; cmd[offset+2]=0; cmd[offset+3]=0; break;
                case Counters_Mode.First_Frequency: cmd[offset]=1; cmd[offset+1]=1; cmd[offset+2]=(byte)(periodCount&0xFF); cmd[offset+3]=0; break;
                case Counters_Mode.Second_Counting: cmd[offset]=2; cmd[offset+1]=0; cmd[offset+2]=0; cmd[offset+3]=0; break;
                case Counters_Mode.Second_Frequency:cmd[offset]=2; cmd[offset+1]=1; cmd[offset+2]=(byte)(periodCount&0xFF); cmd[offset+3]=0; break;
                case Counters_Mode.Third_Counting:  cmd[offset]=3; cmd[offset+1]=0; cmd[offset+2]=0; cmd[offset+3]=0; break;
                case Counters_Mode.Third_Frequency: cmd[offset]=3; cmd[offset+1]=1; cmd[offset+2]=(byte)(periodCount&0xFF); cmd[offset+3]=0; break;
                case Counters_Mode.Fourth_Counting: cmd[offset]=4; cmd[offset+1]=0; cmd[offset+2]=0; cmd[offset+3]=0; break;
                case Counters_Mode.Fourth_Frequency:cmd[offset]=4; cmd[offset+1]=1; cmd[offset+2]=(byte)(periodCount&0xFF); cmd[offset+3]=0; break;
            }
        }

        public virtual byte[] BuildSetRelays(IReadOnlyList<RelayStatus> relays)
        {
            return BuildSetRelaysAndOutputVoltage(relays, 0, 0);
        }

        public virtual byte[] BuildSetRelaysAndOutputVoltage(IReadOnlyList<RelayStatus> relays, double ch0, double ch1)
        {
            byte[] cmd = new byte[13];
            byte[] hdr = Header(0x06);
            Array.Copy(hdr, cmd, hdr.Length);
            cmd[5] = 0x04;
            cmd[6] = EncodeRelayByte(relays);
            short v0 = Convert.ToInt16(Math.Min(ch0, 4.999) * 819.2);
            short v1 = Convert.ToInt16(Math.Min(ch1, 4.999) * 819.2);
            WriteLegacyInt16(cmd, 7, v0);
            WriteLegacyInt16(cmd, 9, v1);

            byte[] data = new byte[7];
            data[0] = Stx == 0x01 ? cmd[3] : cmd[4];
            for (int i = 1; i < data.Length; i++)
            {
                data[i] = cmd[4 + i];
            }
            cmd[11] = (byte)(CheckSum(7, data) & 0xFF);
            cmd[12] = Eot;
            return cmd;
        }

        public virtual byte[] BuildSetAnalogOutput(double ch0, double ch1)
        {
            return Array.Empty<byte>();
        }

        protected static byte EncodeRelayByte(IReadOnlyList<RelayStatus> relays)
        {
            byte b = 0;
            for (int i = 0; i < Math.Min(relays.Count, 8); i++)
            {
                if (relays[i] == RelayStatus.Opened)
                {
                    b |= (byte)(1 << i);
                }
            }
            return b;
        }

        public byte[] BuildStartScope(int channel, int sampling, int interval)
        {
            _lastScopeChannel = channel;
            _lastScopeSampling = sampling;
            return BuildScopeSamplingCommand(start: true, channel: channel, sampling: sampling);
        }

        protected byte[] BuildStopScopeSamplingCommand()
        {
            return BuildScopeSamplingCommand(start: false, channel: _lastScopeChannel, sampling: _lastScopeSampling);
        }

        private byte[] BuildScopeSamplingCommand(bool start, int channel, int sampling)
        {
            byte[] cmd = new byte[13];
            byte[] hdr = Header(0x06);
            Array.Copy(hdr, cmd, hdr.Length);
            cmd[5] = 0x03;
            cmd[6] = start ? (byte)0x01 : (byte)0x00;
            WriteLegacyInt16(cmd, 7, Convert.ToInt16(sampling));
            cmd[9] = 0x01;
            cmd[10] = (byte)(channel & 0xFF);
            byte[] data = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                data[i] = cmd[5 + i];
            }
            cmd[11] = (byte)(CheckSum(6, data) & 0xFF);
            cmd[12] = Eot;
            return cmd;
        }

        public abstract byte[] BuildStopScope();

        public virtual byte[] BuildClearCounters(int mask = 0xFFF)
        {
            byte[] cmd = new byte[8];
            byte[] hdr = Header(0x01);
            Array.Copy(hdr, cmd, hdr.Length);
            cmd[5] = Rco;
            cmd[6] = (byte)(CheckSum(1, new byte[] { cmd[5] }) & 0xFF);
            cmd[7] = Eot;
            return cmd;
        }

        public byte[] BuildReset()
        {
            byte[] cmd = new byte[8];
            byte[] hdr = Header(0x01);
            Array.Copy(hdr, cmd, hdr.Length);
            cmd[5] = Rhw;
            cmd[6] = (byte)(CheckSum(1, new byte[] { cmd[5] }) & 0xFF);
            cmd[7] = Eot;
            return cmd;
        }

        public virtual byte[] BuildChangeLed(byte r, byte g, byte b, Led_Blink_Modes blinkMode)
        {
            return Array.Empty<byte>();  // override per model
        }

        public byte[] BuildChangeIp(string ip, int port, int mac1, int mac2)
        {
            return BuildIpCommand(ip, port, mac1, mac2, 0x08);
        }

        public byte[] BuildChangeIpWifi(string ip, int port, int mac1, int mac2)
        {
            return BuildIpCommand(ip, port, mac1, mac2, 0x09);
        }

        private byte[] BuildIpCommand(string ip, int port, int mac1, int mac2, byte subCmd)
        {
            byte[] cmd = new byte[24];
            byte[] hdr = Header(0x11);
            Array.Copy(hdr, cmd, hdr.Length);
            cmd[5] = subCmd;
            byte[] ipBytes = IPAddress.Parse(ip).GetAddressBytes();
            ipBytes.CopyTo(cmd, 6);
            cmd[10] = 0xFF;
            cmd[11] = 0xFF;
            cmd[12] = 0xFF;
            cmd[13] = 0x00;
            cmd[14] = 0x00;
            cmd[15] = 0x00;
            cmd[16] = 0x00;
            cmd[17] = 0x00;
            WriteLegacyInt16(cmd, 18, Convert.ToInt16(port));
            cmd[20] = (byte)(mac2 & 0xFF);
            cmd[21] = (byte)(mac1 & 0xFF);

            byte[] data = new byte[17];
            for (int i = 0; i < 17; i++)
            {
                data[i] = cmd[5 + i];
            }
            cmd[22] = (byte)(CheckSum(17, data) & 0xFF);
            cmd[23] = Eot;
            return cmd;
        }

        protected static void WriteLegacyInt16(byte[] target, int offset, short value)
        {
            int raw = value;
            target[offset] = (byte)(raw & 0xFF);
            target[offset + 1] = (byte)((raw >> 8) & 0xFF);
        }

        protected static double[] ParseAnalogInputs(byte[] rx, int count, Func<byte, byte, double> scale)
        {
            double[] inputs = new double[count];
            for (int i = 0; i < count; i++)
            {
                inputs[i] = scale(rx[(2 * i) + 2], rx[(2 * i) + 3]);
            }
            return inputs;
        }

        protected static ulong Read32(byte[] rx, int offset)
        {
            return Convert.ToUInt32(rx[offset])
                + (Convert.ToUInt32(rx[offset + 1]) * 256)
                + (Convert.ToUInt32(rx[offset + 2]) * 65536)
                + (Convert.ToUInt32(rx[offset + 3]) * 16777216);
        }

        protected static ulong Read16(byte[] rx, int offset)
        {
            return Convert.ToUInt32(rx[offset]) + (Convert.ToUInt32(rx[offset + 1]) * 256);
        }

        protected bool TryExtractPayload(byte[] frame, int minimumPayloadLength, out byte[] payload)
        {
            payload = null;
            if (frame == null)
            {
                return false;
            }

            if (frame.Length >= minimumPayloadLength && frame[0] != Stx)
            {
                payload = frame;
                return true;
            }

            if (frame.Length < 5 || frame[0] != Stx)
            {
                return false;
            }

            int payloadLength = Stx == 0x01
                ? (frame[3] << 8) | frame[4]
                : (frame[2] << 8) | frame[3];

            if (payloadLength < minimumPayloadLength || frame.Length < 5 + payloadLength)
            {
                return false;
            }

            payload = new byte[payloadLength];
            Array.Copy(frame, 5, payload, 0, payloadLength);
            return true;
        }

        public abstract bool TryParseStatus(byte[] frame, out PadData data);

        public virtual bool TryParseScope(byte[] frame, out List<double> samples)
        {
            samples = null;
            if (frame.Length <= 200)
            {
                return false;
            }
            if (frame[0] != Stx)
            {
                return false;
            }

            samples = new List<double>(500);
            for (int i = 0; i < 500; i++)
            {
                samples.Add(ParseScopeSample(frame, i));
            }
            return true;
        }

        protected abstract double ParseScopeSample(byte[] frame, int index);
    }
}
