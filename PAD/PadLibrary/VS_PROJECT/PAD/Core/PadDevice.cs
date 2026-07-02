using System;
using System.Collections.Generic;
using System.Net;

namespace Capatest.Pad
{
    public abstract class PadDevice : IPadDevice
    {
        private readonly ITransport _transport;
        protected ITransport Transport
        {
            get { return _transport; }
        }
        protected IPadProtocol Protocol
        {
            get { return _protocol; }
        }
        private readonly IPadProtocol _protocol;
        private readonly RelayController _relayController;
        private readonly ReconnectionManager _reconnectionManager;

        private volatile int _statusFrequency = 250;
        private volatile int _counterPeriodCount = 250;
        private volatile Counters_Mode _countersMode = Counters_Mode.All_Frequency;

        public PAD_Models Model
        {
            get { return _protocol.Model; }
        }

        public string ConnectionAddress
        {
            get { return _transport.ConnectionAddress; }
        }
        public int RelayCount
        {
            get { return _protocol.RelayCount; }
        }
        public int CounterCount
        {
            get { return _protocol.CounterCount; }
        }
        public int AnalogInputCount
        {
            get { return _protocol.AnalogInputCount; }
        }

        private volatile WorkingMode _currentWorkingMode = WorkingMode.None;
        public WorkingMode CurrentWorkingMode
        {
            get { return _currentWorkingMode; }
            private set { _currentWorkingMode = value; }
        }

        public int StatusRequestIntervalMs
        {
            get { return _statusFrequency; }
        }

        public IReadOnlyList<RelayStatus> Relays
        {
            get { return _relayController.Relays; }
        }

        public Reconnection_Options Reconnection
        {
            get { return _reconnectionManager.Policy; }
            set { _reconnectionManager.Policy = value; }
        }
        public int ReconnectionAttemptsLimit
        {
            get { return _reconnectionManager.AttemptsLimit; }
            set { _reconnectionManager.AttemptsLimit = value; }
        }

        public event PADDataEventHandler DataChanged;
        public event PADStatusChangedEventHandler ConnectionStatusChanged;
        public event PADScopeEventHandler ScopeReceived;
        public event PADFastAcquisitionEventHandler FastAcquisitionReceived;
        public event WrongPADModelEventHandler WrongModelDetected;
        public event PADLogEventHandler LogChanged;

        protected PadDevice(ITransport transport, IPadProtocol protocol)
        {
            _transport = transport;
            _protocol = protocol;
            _relayController = new RelayController(protocol.RelayCount);

            _reconnectionManager = new ReconnectionManager(
                connectAction: () => Connect(_countersMode, _statusFrequency, _counterPeriodCount),
                deinitializeAction: Deinitialize,
                updateStatus: NotifyStatus,
                isConnected: () => _transport.IsConnected(),
                log: Log);

            if (transport is TcpTransport tcp)
            {
                tcp.LogChanged += (level, msg) => Log(level, msg);
            }
            if (transport is TcpStreamTransport tcpStream)
            {
                tcpStream.LogChanged += (level, msg) => Log(level, msg);
            }

            _transport.StatusChanged += OnTransportStatusChanged;
            _transport.WorkingModeChanged += mode => CurrentWorkingMode = mode;
            _transport.FrameReceived += OnFrameReceived;
        }

        private void OnFrameReceived(byte[] frame)
        {
            try
            {
                _reconnectionManager.ResetAttempts();

                PadData data;
                if (_protocol.TryParseStatus(frame, out data))
                {
                    _relayController.DecodeFromBitmask(data.DigitalOutput);
                    Log(Log_Level.Raw, $"Relays = {string.Join("|", _relayController.Relays)}");
                    SafeInvokeAll(DataChanged, data);
                    OnStatusParsed(data, frame);
                    return;
                }

                List<double> samples;
                if (_protocol.TryParseScope(frame, out samples))
                {
                    SafeInvokeAll(ScopeReceived, samples);
                    return;
                }

                OnUnrecognizedFrame(frame);
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        private void OnTransportStatusChanged(ConnectionStatus status)
        {
            try
            {
                Log(Log_Level.Info, $"Connection status: {status}");

                if (status == ConnectionStatus.Connected)
                {
                    _transport.PollingIntervalMs = _statusFrequency;
                }
                else if (status == ConnectionStatus.Reconnecting)
                {
                    status = _reconnectionManager.HandleReconnecting();
                }

                NotifyStatus(status);
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }


        public bool Connect(Counters_Mode counterMode, int statusFrequency = 250, int counterPeriodCount = 250)
        {
            try
            {
                Log(Log_Level.Info, "Connecting...");
                _reconnectionManager.OnConnectionRequested();

                _countersMode = counterMode;
                _statusFrequency = statusFrequency;
                if (counterPeriodCount > 0)
                {
                    _counterPeriodCount = counterPeriodCount;
                }
                else if (_counterPeriodCount <= 0)
                {
                    _counterPeriodCount = statusFrequency;
                }
                _transport.PollingIntervalMs = statusFrequency;
                _relayController.Reset();

                bool connected = _transport.Connect(
                    () => _protocol.BuildStatusRequest());

                if (connected)
                {
                    _transport.StartPolling();
                    byte[] cfgCmd = _protocol.BuildConfigureCounters(counterMode, _counterPeriodCount);
                    if (cfgCmd.Length > 0)
                    {
                        _transport.Send(cfgCmd);
                    }
                }

                return connected;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                return false;
            }
        }

        public bool Disconnect()
        {
            try
            {
                Log(Log_Level.Info, "Disconnecting...");
                _reconnectionManager.OnDisconnectionRequested();
                return _transport.Disconnect();
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                return false;
            }
        }

        public bool IsConnected()
        {
            return _transport.IsConnected();
        }

        public void SetStatusRequestInterval(int ms)
        {
            _statusFrequency = ms;
            _transport.PollingIntervalMs = ms;
        }


        public void SetRelays(RelayAction[] actions)
        {
            try
            {
                IReadOnlyList<RelayStatus> relays = _relayController.ApplyAndSnapshot(actions);
                _transport.Send(_protocol.BuildSetRelays(relays));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void SetRelaysAndOutputVoltage(RelayAction[] actions, double ch0, double ch1)
        {
            try
            {
                IReadOnlyList<RelayStatus> relays = _relayController.ApplyAndSnapshot(actions);
                _transport.Send(_protocol.BuildSetRelaysAndOutputVoltage(
                    relays, ch0, ch1));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void CloseAllRelays()
        {
            try
            {
                IReadOnlyList<RelayStatus> relays = _relayController.ResetAndSnapshot();
                _transport.Send(_protocol.BuildSetRelays(relays));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void SetAnalogOutput(double ch0, double ch1)
        {
            try
            {
                _transport.Send(_protocol.BuildSetAnalogOutput(ch0, ch1));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }


        public bool ConfigureCounters(Counters_Mode mode, int periodCount = 0)
        {
            try
            {
                _countersMode = mode;
                if (periodCount > 0)
                {
                    _counterPeriodCount = periodCount;
                }
                _transport.Send(_protocol.BuildConfigureCounters(mode, _counterPeriodCount));
                return true;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                return false;
            }
        }

        public bool ConfigureCountersAndRelay(Counters_Mode mode, int relay, int relayCount, int periodCount = 0)
        {
            try
            {
                _countersMode = mode;
                if (periodCount > 0)
                {
                    _counterPeriodCount = periodCount;
                }
                _transport.Send(_protocol.BuildConfigureCountersAndRelay(
                    mode, _counterPeriodCount, relay, relayCount));
                return true;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                return false;
            }
        }

        public bool ClearCounters(int mask = 0xFFF)
        {
            try
            {
                _transport.Send(_protocol.BuildClearCounters(mask));
                return true;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                return false;
            }
        }


        public void StartScope(int channel, int sampling, int interval)
        {
            try
            {
                _transport.Send(_protocol.BuildStartScope(channel, sampling, interval));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void StopScope()
        {
            try
            {
                _transport.Send(_protocol.BuildStopScope());
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }


        public virtual void StartAutoStatus(short ms = 80)
        {
            Log(Log_Level.Warning, $"{Model} does not support AutoStatus.");
        }

        public virtual void StopAutoStatus() { }

        public virtual void StartFastStatus(int channel)
        {
            Log(Log_Level.Warning, $"{Model} does not support FastStatus.");
        }

        public virtual void StopFastStatus() { }


        public bool Reset()
        {
            try
            {
                _transport.Send(_protocol.BuildReset());
                return true;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                return false;
            }
        }

        public void ChangeAddress(string ip, int port = 1999, int mac1 = 0, int mac2 = 0)
        {
            try
            {
                _transport.Send(_protocol.BuildChangeIp(ip, port, mac1, mac2));
                _transport.Address = ip;
                _transport.Port = port;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void ChangeAddressWifi(string ip, int port = 1999, int mac1 = 0, int mac2 = 0)
        {
            try
            {
                _transport.Send(_protocol.BuildChangeIpWifi(ip, port, mac1, mac2));
                _transport.Address = ip;
                _transport.Port = port;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void SetLed(byte r, byte g, byte b, Led_Blink_Modes blink = Led_Blink_Modes.None)
        {
            try
            {
                byte[] cmd = _protocol.BuildChangeLed(r, g, b, blink);
                if (cmd.Length > 0)
                {
                    _transport.Send(cmd);
                }
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public virtual IPAddress GetDeviceIp(int serialNumber)
        {
            Log(Log_Level.Warning, "GetDeviceIp not supported by this transport.");
            return IPAddress.Loopback;
        }

        public virtual Dictionary<string, string> DiscoverDevices()
        {
            Log(Log_Level.Warning, "DiscoverDevices not supported by this transport.");
            return new Dictionary<string, string>();
        }

        protected virtual void OnStatusParsed(PadData data, byte[] rawFrame) { }

        protected virtual void OnUnrecognizedFrame(byte[] frame) { }

        private void Deinitialize()
        {
            _transport.StopPolling();
            if (_transport.IsConnected())
            {
                _transport.Disconnect();
            }
        }

        private void NotifyStatus(ConnectionStatus status)
        {
            Log(Log_Level.Info, $"New status: {status}");
            SafeInvokeAll(ConnectionStatusChanged, status);
        }

        protected void OnFastData(List<List<double>> voltages)
        {
            SafeInvokeAll(FastAcquisitionReceived, voltages);
        }

        private void SafeInvokeAll(Delegate multicast, object arg)
        {
            if (multicast == null)
            {
                return;
            }
            foreach (Delegate d in multicast.GetInvocationList())
            {
                try
                {
                    d.DynamicInvoke(arg);
                }
                catch (Exception ex)
                {
                    Log(Log_Level.Error, $"Event handler error: {ex.Message}");
                }
            }
        }

        protected void Log(Log_Level level, string message)
        {
            LogChanged?.Invoke(level, message);
        }
    }
}
