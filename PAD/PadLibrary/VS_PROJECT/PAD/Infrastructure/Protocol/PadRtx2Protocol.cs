using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Capatest.Pad
{
    internal sealed class PadRtx2Protocol : IPadProtocol, IAsciiProtocol
    {
        private static readonly Regex StatusRx = new Regex(
            @"^([0-9A-F]{32});" +
            @"((?:[01][0-9A-F]{14}){12});" +
            @"([0-9A-F]{6});" +
            @"([01]{12});" +
            @"([0-9A-F]{41});" +
            @"([0-9A-F]{8})$",
            RegexOptions.Compiled);

        private static readonly Regex VersionLineRx = new Regex(
            @"^version:\s+(.+)",
            RegexOptions.Compiled);

        private static readonly Regex NvsGetRx = new Regex(
            @"^([A-Za-z0-9_]+):\s+len=\d+(?:\s+u32=(\d+))?",
            RegexOptions.Compiled);

        private static readonly Regex UptimeLineRx = new Regex(
            @"^uptime:\s+(\d+)\s*ms",
            RegexOptions.Compiled);

        // 'nvs list' table header: "key type len seq".
        private static readonly Regex NvsListHeaderRx = new Regex(
            @"^key\s+type\s+len\s+seq\s*$",
            RegexOptions.Compiled);

        // 'nvs list' entry row, e.g. "service_time u32 4 5327" (key type len seq).
        // The row carries no value; the trailing number is the NVS sequence, not the u32 value.
        private static readonly Regex NvsListEntryRx = new Regex(
            @"^([A-Za-z0-9_]+)\s+(u8|i8|u16|i16|u32|i32|u64|i64|str|blob)\s+\d+\s+\d+\s*$",
            RegexOptions.Compiled);

        public PAD_Models Model
        {
            get { return PAD_Models.PadRTX2; }
        }
        public int RelayCount
        {
            get { return 12; }
        }
        public int CounterCount
        {
            get { return 12; }
        }
        public int AnalogInputCount
        {
            get { return 8; }
        }
        private readonly bool[] _channelIsFrequency = new bool[12];
        private readonly object _modeLock = new object();
        private WeightReading[] _cachedWeights;
        private DacReading[] _cachedDacs;

        public byte[] BuildStatusRequest()
        {
            return Ascii("status\r\n");
        }

        public byte[] BuildConfigureCounters(Counters_Mode mode, int periodCount)
        {
            UpdateMode(mode);
            return Array.Empty<byte>();  // PadRTX2 counters are always running
        }

        public byte[] BuildConfigureCountersAndRelay(Counters_Mode mode, int periodCount, int relay, int relayCount)
        {
            UpdateMode(mode);
            return Array.Empty<byte>();
        }

        private void UpdateMode(Counters_Mode mode)
        {
            lock (_modeLock)
            {
                switch (mode)
                {
                    case Counters_Mode.All_Counting:
                        for (int i = 0; i < 12; i++)
                        {
                            _channelIsFrequency[i] = false;
                        }
                        break;

                    case Counters_Mode.All_Frequency:
                        for (int i = 0; i < 12; i++)
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
        }

        public byte[] BuildSetRelays(IReadOnlyList<RelayStatus> relays)
        {
            int bitmask = 0;
            int n = Math.Min(relays.Count, 12);
            for (int i = 0; i < n; i++)
            {
                if (relays[i] == RelayStatus.Opened)
                {
                    bitmask |= (1 << i);
                }
            }
            return Ascii($"relay 0xfff 0x{bitmask:X3}\r\n");
        }

        public byte[] BuildSetRelaysAndOutputVoltage(IReadOnlyList<RelayStatus> relays, double ch0, double ch1)
        {
            return BuildSetRelays(relays);  // output voltage is separate on PadRTX2
        }

        public byte[] BuildSetAnalogOutput(double ch0, double ch1)
        {
            return Ascii($"dac dac0 v {ch0.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}\r\n" +
                         $"dac dac1 v {ch1.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}\r\n");
        }

        public byte[] BuildStartScope(int channel, int sampling, int interval)
        {
            return Ascii($"adc fast {sampling} -1\r\n");
        }

        public byte[] BuildStopScope()
        {
            return Ascii("adc fast stop\r\n");
        }

        public byte[] BuildReset()
        {
            return Ascii("reset\r\n");
        }

        public byte[] BuildChangeLed(byte r, byte g, byte b, Led_Blink_Modes blinkMode)
        {
            // Note: the RTX2 firmware 'led rgb' command only accepts the three
            // colour components and has no blink parameter, so blinkMode is
            // intentionally ignored here. The only animated mode the firmware
            // exposes is the colour cycle, available through BuildLedCycle().
            return Ascii($"led rgb {r} {g} {b}\r\n");
        }

        public byte[] BuildLedCycle()
        {
            return Ascii("led cycle\r\n");
        }

        public byte[] BuildChangeIp(string ip, int port, int mac1, int mac2)
        {
            return Array.Empty<byte>();  // not applicable
        }

        public byte[] BuildChangeIpWifi(string ip, int port, int mac1, int mac2)
        {
            return Array.Empty<byte>();
        }

        public bool TryParseStatus(byte[] frame, out PadData data)
        {
            data = null;
            if (frame == null || frame.Length < 284)
            {
                return false;
            }

            string line = Encoding.ASCII.GetString(frame).TrimEnd('\r', '\n');
            IReadOnlyList<WeightReading> weights;
            IReadOnlyList<DacReading> dacs;
            bool ok = TryParseFullStatusLine(line, out data, out weights, out dacs);
            if (ok)
            {
                _cachedWeights = (WeightReading[])weights;
                _cachedDacs    = (DacReading[])dacs;
            }
            return ok;
        }

        internal bool TryGetCachedExtended(
            out IReadOnlyList<WeightReading> weights,
            out IReadOnlyList<DacReading> dacs)
        {
            weights = _cachedWeights;
            dacs    = _cachedDacs;
            return _cachedWeights != null;
        }

        public bool TryParseScope(byte[] frame, out List<double> samples)
        {
            samples = null;
            return false;
        }

        public bool TryParseFullStatusLine(
            string line,
            out PadData data,
            out IReadOnlyList<WeightReading> weightReadings,
            out IReadOnlyList<DacReading> dacReadings)
        {
            data = null;
            weightReadings = null;
            dacReadings = null;

            if (line == null || line.Length < 284)
            {
                return false;
            }

            Match m = StatusRx.Match(line);
            if (!m.Success)
            {
                return false;
            }

            string crcHex = line.Substring(276, 8);
            uint crcExpected = Crc32Mpeg2.Compute(line.Substring(0, 276));
            uint crcActual = Convert.ToUInt32(crcHex, 16);
            if (crcExpected != crcActual)
            {
                return false;
            }

            double[] analog = new double[8];
            for (int i = 0; i < 8; i++)
            {
                string hex = line.Substring(i * 4, 4);
                short raw = (short)Convert.ToUInt16(hex, 16);
                analog[i] = raw * 10.0 / 65536.0;
            }

            // Input channels
            InputChannelReading[] inputChannels = new InputChannelReading[12];
            for (int i = 0; i < 12; i++)
            {
                int @base = 33 + i * 15;
                bool level = line[@base] == '1';
                ulong count = Convert.ToUInt32(line.Substring(@base + 1, 8), 16);
                uint freqCentiHz = Convert.ToUInt32(line.Substring(@base + 9, 6), 16);
                double freq = freqCentiHz / 100.0;
                inputChannels[i] = new InputChannelReading(level, count, freq);
            }

            // DAC
            DacReading[] dacs = new DacReading[2];
            dacs[0] = new DacReading(Convert.ToInt32(line.Substring(214, 3), 16));
            dacs[1] = new DacReading(Convert.ToInt32(line.Substring(217, 3), 16));
            dacReadings = dacs;

            // Relays (chars 221-232)
            int relayBitmask = 0;
            for (int i = 0; i < 12; i++)
            {
                if (line[221 + i] == '1')
                {
                    relayBitmask |= (1 << i);
                }
            }

            // Weight / HX710A (chars 234-274)
            // Layout: validMask(1) + 4×6 raw24 + 4×4 load percent ×100 (signed 16-bit)
            int validMask = Convert.ToInt32(line.Substring(234, 1), 16);
            WeightReading[] weights = new WeightReading[4];
            for (int i = 0; i < 4; i++)
            {
                if ((validMask & (1 << i)) != 0)
                {
                    int raw24 = Convert.ToInt32(line.Substring(235 + i * 6, 6), 16);
                    // Sign-extend 24-bit
                    if ((raw24 & 0x800000) != 0)
                    {
                        raw24 |= unchecked((int)0xFF000000);
                    }
                    int percentRaw16 = Convert.ToInt32(line.Substring(259 + i * 4, 4), 16);
                    short percentSigned = (short)(percentRaw16 & 0xFFFF);
                    double? calibratedPercent = (percentSigned == -1)
                        ? (double?)null
                        : percentSigned / 100.0;
                    weights[i] = new WeightReading(raw24, calibratedPercent);
                }
                else
                {
                    weights[i] = WeightReading.Invalid;
                }
            }
            weightReadings = weights;

            bool[] modeCopy;
            lock (_modeLock)
            {
                modeCopy = (bool[])_channelIsFrequency.Clone();
            }
            data = new PadData(analog, relayBitmask, inputChannels, 0, modeCopy);
            return true;
        }

        public byte[] BuildSetDac(int channel, double volts)
        {
            volts = Math.Max(0, Math.Min(5, volts));
            string ch = channel == 0 ? "dac0" : "dac1";
            return Ascii($"dac {ch} v {volts.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}\r\n");
        }

        public byte[] BuildSetDacRaw(int channel, int raw12)
        {
            raw12 = Math.Max(0, Math.Min(4095, raw12));
            string ch = channel == 0 ? "dac0" : "dac1";
            return Ascii($"dac {ch} raw {raw12}\r\n");
        }

        public byte[] BuildClearDac()
        {
            return Ascii("dac clear\r\n");
        }

        public byte[] BuildClearRelay()
        {
            return Ascii("relay 0xfff 0x000\r\n");
        }

        public byte[] BuildClearCounters(int mask = 0xFFF)
        {
            if (mask == 0xFFF)
            {
                return Ascii("inccnt clear all\r\n");
            }
            return Ascii($"inccnt clear mask {mask}\r\n");
        }

        public byte[] BuildWatchdog(int seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }
            return Ascii($"watchdog {seconds}\r\n");
        }

        public byte[] BuildOledScreen(int n)
        {
            return Ascii($"oled screen {n}\r\n");
        }

        public byte[] BuildAdcFastStart(int frequencyHz, int samples = -1)
        {
            frequencyHz = Math.Max(1, Math.Min(10000, frequencyHz));
            return Ascii($"adc fast {frequencyHz} {samples}\r\n");
        }

        public byte[] BuildAdcFastStop()
        {
            return Ascii("adc fast stop\r\n");
        }

        public byte[] BuildRomboot()
        {
            return Ascii("romboot\r\n");
        }

        public byte[] BuildVersion()
        {
            return Ascii("version\r\n");
        }

        public byte[] BuildUptime()
        {
            return Ascii("uptime\r\n");
        }

        public byte[] BuildNvsGet(string key)
        {
            return Ascii($"nvs get {key}\r\n");
        }

        public byte[] BuildNvsSetU32(string key, uint value)
        {
            return Ascii($"nvs setu32 {key} {value}\r\n");
        }

        public byte[] BuildNvsDel(string key)
        {
            return Ascii($"nvs del {key}\r\n");
        }

        public byte[] BuildNvsList()
        {
            return Ascii("nvs list\r\n");
        }

        public bool TryParseVersionLine(string line, out string version)
        {
            version = null;
            if (line == null)
            {
                return false;
            }
            Match m = VersionLineRx.Match(line);
            if (!m.Success)
            {
                return false;
            }
            version = m.Groups[1].Value.Trim();
            return true;
        }

        public bool TryParseUptimeLine(string line, out uint uptimeMs)
        {
            uptimeMs = 0;
            if (line == null)
            {
                return false;
            }
            Match m = UptimeLineRx.Match(line);
            if (!m.Success)
            {
                return false;
            }
            return uint.TryParse(m.Groups[1].Value, out uptimeMs);
        }

        public bool IsNvsListHeader(string line)
        {
            if (line == null)
            {
                return false;
            }
            return NvsListHeaderRx.IsMatch(line);
        }

        public bool TryParseNvsListEntry(string line, out string key)
        {
            key = null;
            if (line == null)
            {
                return false;
            }
            Match m = NvsListEntryRx.Match(line);
            if (!m.Success)
            {
                return false;
            }
            key = m.Groups[1].Value;
            return true;
        }

        public bool TryParseNvsLine(string line, out string key, out uint? value)
        {
            key = null;
            value = null;
            if (line == null)
            {
                return false;
            }
            Match m = NvsGetRx.Match(line);
            if (!m.Success)
            {
                return false;
            }
            key = m.Groups[1].Value;
            if (m.Groups[2].Success)
            {
                value = uint.Parse(m.Groups[2].Value);
            }
            return true;
        }

        private static byte[] Ascii(string s)
        {
            return Encoding.ASCII.GetBytes(s);
        }
    }
}
