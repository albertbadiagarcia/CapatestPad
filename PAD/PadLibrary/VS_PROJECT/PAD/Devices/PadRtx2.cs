using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace Capatest.Pad
{
    public sealed class PadRtx2 : PadDevice, IWeightScaleDevice, IDacOutputDevice
    {
        private readonly PadRtx2Protocol _rtx2Protocol;
        private IReadOnlyList<WeightReading> _weightReadings = new[]
        {
            WeightReading.Invalid, WeightReading.Invalid, WeightReading.Invalid, WeightReading.Invalid
        };
        private IReadOnlyList<DacReading> _dacReadings = new[] { new DacReading(0), new DacReading(0) };
        private int _fastAcqNextIndex;
        private readonly Queue<string> _pendingNvsKeys = new Queue<string>();
        private readonly object _nvsListLock = new object();
        private List<NvsEntry> _nvsListBuffer;

        public IReadOnlyList<WeightReading> WeightReadings
        {
            get { return _weightReadings; }
        }
        public IReadOnlyList<DacReading> DacReadings
        {
            get { return _dacReadings; }
        }
        public string FirmwareVersion { get; private set; }

        public event Action<IReadOnlyList<WeightReading>> WeightChanged;
        public event Action FastAcquisitionCompleted;
        public event Action<string> VersionReceived;
        public event Action<string, uint?> NvsValueReceived;
        public event Action<IReadOnlyList<NvsEntry>> NvsListReceived;

        public PadRtx2(string portName, int baudRate = 115200)
            : this(new SerialTransport { Address = portName, Port = baudRate }, new PadRtx2Protocol()) { }

        public static PadRtx2 CreateEthernet(string ipAddress, int tcpPort = 1999, Func<bool> beforeConnect = null)
        {
            PadRtx2Protocol protocol = new PadRtx2Protocol();
            return new PadRtx2(new TcpStreamTransport { Address = ipAddress, Port = tcpPort, BeforeConnect = beforeConnect }, protocol);
        }

        public static PadRtx2 CreateEthernetWithBridge(string ipAddress, int tcpPort = 5555)
        {
            return CreateEthernet(
                ipAddress,
                tcpPort,
                SshBridgeLauncher.Create($"root@{ipAddress}", "/home/root/padrtx-serial-bridge.sh"));
        }

        private PadRtx2(ITransport transport, PadRtx2Protocol protocol)
            : base(transport, protocol)
        {
            _rtx2Protocol = protocol;
        }

        public bool Connect(int pollingIntervalMs = 200)
        {
            return base.Connect(Counters_Mode.All_Frequency, pollingIntervalMs);
        }

        protected override void OnStatusParsed(PadData data, byte[] rawFrame)
        {
            IReadOnlyList<WeightReading> weights;
            IReadOnlyList<DacReading> dacs;
            if (_rtx2Protocol.TryGetCachedExtended(out weights, out dacs))
            {
                _weightReadings = weights;
                _dacReadings = dacs;
                WeightChanged?.Invoke(weights);
            }
        }

        public void SetDac(int channel, double volts)
        {
            try
            {
                Transport.Send(_rtx2Protocol.BuildSetDac(channel, volts));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void SetDacRaw(int channel, int raw12)
        {
            try
            {
                Transport.Send(_rtx2Protocol.BuildSetDacRaw(channel, raw12));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void ClearDac()
        {
            try
            {
                Transport.Send(_rtx2Protocol.BuildClearDac());
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        protected override void OnUnrecognizedFrame(byte[] frame)
        {
            if (frame == null || frame.Length == 0)
            {
                return;
            }

            if (frame.Length >= 12 &&
                frame[0] == 0x41 && frame[1] == 0x44 && frame[2] == 0x46 && frame[3] == 0x31)
            {
                OnFastPacket(frame);
                return;
            }

            string line = Encoding.ASCII.GetString(frame).Trim();
            if (line.Length > 0)
            {
                OnTextLine(line);
            }
        }

        private void OnFastPacket(byte[] frame)
        {
            int count = frame[8] | (frame[9] << 8);
            int expectedLen = 12 + count * 16;
            if (count == 0 || frame.Length < expectedLen)
            {
                return;
            }

            int firstIndex = frame[4] | (frame[5] << 8) | (frame[6] << 16) | (frame[7] << 24);
            int flags = frame[10] | (frame[11] << 8);

            if (firstIndex != _fastAcqNextIndex)
            {
                Log(Log_Level.Warning, $"ADC fast gap: expected index {_fastAcqNextIndex}, got {firstIndex}");
            }
            _fastAcqNextIndex = firstIndex + count;

            List<List<double>> channels = new List<List<double>>(8);
            for (int c = 0; c < 8; c++)
            {
                channels.Add(new List<double>(count));
            }
            for (int s = 0; s < count; s++)
            {
                int frameOffset = 12 + s * 16;
                for (int ch = 0; ch < 8; ch++)
                {
                    short raw = (short)(frame[frameOffset + ch * 2] | (frame[frameOffset + ch * 2 + 1] << 8));
                    channels[ch].Add(raw * 10.0 / 65536.0);
                }
            }
            OnFastData(channels);

            if ((flags & 0x0002) != 0)
            {
                Log(Log_Level.Error, "ADC fast acquisition error reported by firmware");
            }
            if ((flags & 0x0001) != 0)
            {
                _fastAcqNextIndex = 0;
                FastAcquisitionCompleted?.Invoke();
            }
        }

        private void OnTextLine(string line)
        {
            IReadOnlyList<NvsEntry> completedList = null;
            lock (_nvsListLock)
            {
                if (_nvsListBuffer != null)
                {
                    string k;
                    uint? v;
                    if (_rtx2Protocol.TryParseNvsLine(line, out k, out v))
                    {
                        _nvsListBuffer.Add(new NvsEntry(k, v));
                        return;
                    }
                    completedList = _nvsListBuffer.AsReadOnly();
                    _nvsListBuffer = null;
                }
            }
            if (completedList != null)
            {
                NvsListReceived?.Invoke(completedList);
            }

            string version;
            if (_rtx2Protocol.TryParseVersionLine(line, out version))
            {
                FirmwareVersion = version;
                VersionReceived?.Invoke(version);
                return;
            }

            string nvsKey;
            uint? nvsValue;
            if (_rtx2Protocol.TryParseNvsLine(line, out nvsKey, out nvsValue))
            {
                NvsValueReceived?.Invoke(nvsKey, nvsValue);
                return;
            }

            if (line.StartsWith("nvs: key not found", StringComparison.OrdinalIgnoreCase))
            {
                string pendingKey = null;
                lock (_pendingNvsKeys)
                {
                    if (_pendingNvsKeys.Count > 0)
                    {
                        pendingKey = _pendingNvsKeys.Dequeue();
                    }
                }
                if (pendingKey != null)
                {
                    NvsValueReceived?.Invoke(pendingKey, null);
                }
            }
        }

        public bool Romboot()
        {
            try
            {
                Transport.Send(_rtx2Protocol.BuildRomboot());
                return true;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                return false;
            }
        }

        public void QueryVersion()
        {
            try
            {
                Transport.Send(_rtx2Protocol.BuildVersion());
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void NvsGet(string key)
        {
            try
            {
                lock (_pendingNvsKeys)
                {
                    _pendingNvsKeys.Enqueue(key);
                }
                Transport.Send(_rtx2Protocol.BuildNvsGet(key));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void NvsSetU32(string key, uint value)
        {
            try
            {
                Transport.Send(_rtx2Protocol.BuildNvsSetU32(key, value));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void NvsDel(string key)
        {
            try
            {
                Transport.Send(_rtx2Protocol.BuildNvsDel(key));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void NvsList()
        {
            try
            {
                lock (_nvsListLock)
                {
                    _nvsListBuffer = new List<NvsEntry>();
                }
                Transport.Send(_rtx2Protocol.BuildNvsList());
            }
            catch (Exception ex)
            {
                lock (_nvsListLock)
                {
                    _nvsListBuffer = null;
                }
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void SetWatchdog(int seconds)
        {
            try
            {
                Transport.Send(_rtx2Protocol.BuildWatchdog(seconds));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void DisableWatchdog()
        {
            SetWatchdog(0);
        }

        public void SetOledScreen(int n)
        {
            try
            {
                Transport.Send(_rtx2Protocol.BuildOledScreen(n));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public static string FindPort()
        {
            return ScanPorts()
                .Where(p => p.Score > 0)
                .OrderByDescending(p => p.Score)
                .Select(p => p.PortName)
                .FirstOrDefault();
        }

        public static IReadOnlyList<string> FindPorts()
        {
            return ScanPorts()
                .Where(p => p.Score > 0)
                .OrderByDescending(p => p.Score)
                .Select(p => p.PortName)
                .ToList();
        }

        private static List<PortCandidate> ScanPorts()
        {
            var results = new List<PortCandidate>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Caption, DeviceID, Manufacturer FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string caption = obj["Caption"]?.ToString() ?? string.Empty;
                        string deviceId = obj["DeviceID"]?.ToString() ?? string.Empty;
                        string manufacturer = obj["Manufacturer"]?.ToString() ?? string.Empty;

                        var portMatch = Regex.Match(caption, @"\(COM(\d+)\)");
                        if (!portMatch.Success)
                        {
                            continue;
                        }
                        string portName = "COM" + portMatch.Groups[1].Value;

                        var vidMatch = Regex.Match(deviceId, @"VID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
                        var pidMatch = Regex.Match(deviceId, @"PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
                        int? vid = vidMatch.Success ? (int?)Convert.ToInt32(vidMatch.Groups[1].Value, 16) : null;
                        int? pid = pidMatch.Success ? (int?)Convert.ToInt32(pidMatch.Groups[1].Value, 16) : null;

                        string text = string.Join(" ", caption, deviceId, manufacturer).ToLowerInvariant();
                        results.Add(new PortCandidate(portName, RatePort(text, manufacturer, vid, pid)));
                    }
                }
            }
            catch
            {
                // WMI unavailable (non-Windows or access denied): score by device-file prefix.
                foreach (string name in System.IO.Ports.SerialPort.GetPortNames())
                {
                    int score = 0;
                    if (name.StartsWith("/dev/ttyACM"))
                    {
                        score = 5; // USB CDC (PadRTX2 via PC USB)
                    }
                    else if (name.StartsWith("/dev/ttyAMA") || name.StartsWith("/dev/ttyAML"))
                    {
                        score = 3; // ARM hardware UART (SOM / embedded board)
                    }
                    else if (name.StartsWith("/dev/ttyUSB"))
                    {
                        score = 2; // USB-serial adapter
                    }
                    else if (name.StartsWith("/dev/ttyS"))
                    {
                        score = 1; // Generic Linux serial
                    }
                    results.Add(new PortCandidate(name, score));
                }
            }
            return results;
        }

        private static int RatePort(string text, string manufacturer, int? vid, int? pid)
        {
            int score = 0;
            if (vid == 0x0483)
            {
                score += 8; // STMicroelectronics VID
            }
            if (vid != null && pid != null)
            {
                score += 3; // Any real USB device
            }
            string mfr = manufacturer.ToLowerInvariant();
            if (mfr.Contains("stmicroelectronics") || mfr.Contains("stmicro") || mfr.Contains("smiletronix"))
            {
                score += 6;
            }
            if (text.Contains("stm32") || text.Contains("stmicroelectronics") ||
                text.Contains("stlink") || text.Contains("usb serial device"))
            {
                score += 4;
            }
            if (text.Contains("usb") || text.Contains("cdc") || text.Contains("serial"))
            {
                score += 1;
            }
            if (text.Contains("bluetooth"))
            {
                score -= 5;
            }
            return score;
        }

        private struct PortCandidate
        {
            public readonly string PortName;
            public readonly int Score;
            public PortCandidate(string portName, int score) { PortName = portName; Score = score; }
        }

        public void LedOff()
        {
            SetLed(0, 0, 0);
        }

        public void StartFastAdc(int frequencyHz, int samples = -1)
        {
            try
            {
                _fastAcqNextIndex = 0;
                Transport.StopPolling();
                Transport.Send(_rtx2Protocol.BuildAdcFastStart(frequencyHz, samples));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void StopFastAdc()
        {
            try
            {
                Transport.Send(_rtx2Protocol.BuildAdcFastStop());
                Transport.StartPolling();
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }
    }
}
