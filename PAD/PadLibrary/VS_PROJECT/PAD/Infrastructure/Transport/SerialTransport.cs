using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace Capatest.Pad
{
    public sealed class SerialTransport : ITransport, IDisposable
    {
        private SerialPort _port;
        private Thread _readerThread;
        private volatile bool _readerRunning;
        private readonly object _writeLock = new object();
        private Timer _pollingTimer;
        private volatile bool _pollingActive;
        private int _pollingTickRunning;
        private Func<byte[]> _commandFactory;

        public string Address { get; set; } = "COM1";
        public int Port { get; set; } = 115200;
        public int PollingIntervalMs { get; set; } = 200;
        public string ConnectionAddress => $"{Address} @{Port}baud";

        public event PADStatusChangedEventHandler StatusChanged;
        public event PADWorkingModeChangedEventHandler WorkingModeChanged;
        public event Action<byte[]> FrameReceived;
        public event PADLogEventHandler LogChanged;

        public bool Connect(Func<byte[]> commandFactory)
        {
            if (_port?.IsOpen == true)
            {
                return true;
            }

            try
            {
                _commandFactory = commandFactory;

                _port = new SerialPort(Address, Port == 0 ? 115200 : Port,
                    Parity.None, 8, StopBits.One)
                {
                    DtrEnable = true,
                    RtsEnable = true,
                    Handshake = Handshake.None,
                    Encoding = Encoding.UTF8,
                    ReadTimeout = SerialPort.InfiniteTimeout,
                    WriteTimeout = 1000
                };

                _port.Open();

                _readerRunning = true;
                _readerThread = new Thread(ReadLoop)
                {
                    Name = "serial-reader",
                    IsBackground = true
                };
                _readerThread.Start();

                Log(Log_Level.Info, $"Serial port {Address} opened");
                StatusChanged?.Invoke(ConnectionStatus.Connected);
                return true;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                StatusChanged?.Invoke(ConnectionStatus.Disconnected);
                return false;
            }
        }

        public bool Disconnect(bool triggerReconnect = false)
        {
            try
            {
                StopPolling();
                _readerRunning = false;
                Close();
                _readerThread?.Join(TimeSpan.FromSeconds(1));
                _readerThread = null;

                StatusChanged?.Invoke(triggerReconnect
                    ? ConnectionStatus.Reconnecting
                    : ConnectionStatus.Disconnected);
                return true;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                return true;
            }
        }

        public bool IsConnected()
        {
            return _port?.IsOpen == true;
        }

        public void Send(byte[] command)
        {
            if (!IsConnected() || _port == null)
            {
                return;
            }
            lock (_writeLock)
            {
                try
                {
                    _port.Write(command, 0, command.Length);
                }
                catch (Exception ex)
                {
                    Log(Log_Level.Error, ex.Message);
                    OnDisconnected($"Write failed: {ex.Message}");
                }
            }
        }


        public void StartPolling()
        {
            _pollingActive = true;
            _pollingTimer?.Dispose();
            _pollingTimer = new Timer(OnPollingTick, null,
                PollingIntervalMs, PollingIntervalMs);
            WorkingModeChanged?.Invoke(WorkingMode.SendStatus);
        }

        public void StopPolling()
        {
            _pollingActive = false;
            _pollingTimer?.Dispose();
            _pollingTimer = null;
            WorkingModeChanged?.Invoke(WorkingMode.None);
        }


        public void StartPassiveReceive() { }
        public void StopPassiveReceive()  { }


        private void ReadLoop()
        {
            List<byte> buffer = new List<byte>(4096);
            byte[] chunk = new byte[256];

            while (_readerRunning)
            {
                try
                {
                    if (_port == null || !_port.IsOpen)
                    {
                        break;
                    }

                    int read = _port.Read(chunk, 0, chunk.Length);
                    for (int i = 0; i < read; i++)
                    {
                        buffer.Add(chunk[i]);
                    }

                    StreamFrameParser.Parse(buffer, frame => FrameReceived?.Invoke(frame));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_readerRunning)
                    {
                        OnDisconnected($"Read failed: {ex.Message}");
                    }
                    break;
                }
            }
        }

        private void OnPollingTick(object state)
        {
            if (!_pollingActive || !IsConnected())
            {
                return;
            }
            if (Interlocked.Exchange(ref _pollingTickRunning, 1) == 1)
            {
                return;
            }

            try
            {
                if (!_pollingActive || !IsConnected())
                {
                    return;
                }
                byte[] cmd = _commandFactory?.Invoke();
                if (cmd != null)
                {
                    Send(cmd);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _pollingTickRunning, 0);
            }
        }

        private void OnDisconnected(string reason)
        {
            Log(Log_Level.Warning, reason);
            Close();
            StatusChanged?.Invoke(ConnectionStatus.Reconnecting);
        }

        private void Close()
        {
            lock (_writeLock)
            {
                if (_port != null)
                {
                    try
                    {
                        if (_port.IsOpen)
                        {
                            _port.Close();
                        }
                    }
                    catch { }
                    _port.Dispose();
                    _port = null;
                }
            }
        }

        private void Log(Log_Level level, string message)
        {
            LogChanged?.Invoke(level, message);
        }

        public void Dispose()
        {
            StopPolling();
            _readerRunning = false;
            Close();
            _readerThread?.Join(TimeSpan.FromSeconds(1));
        }
    }
}
