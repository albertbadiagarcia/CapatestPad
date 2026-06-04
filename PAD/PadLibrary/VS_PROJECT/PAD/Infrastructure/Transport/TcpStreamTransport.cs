using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace Capatest.Pad
{
    public sealed class TcpStreamTransport : ITransport, IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _readerThread;
        private volatile bool _readerRunning;
        private readonly object _writeLock = new object();
        private Timer _pollingTimer;
        private volatile bool _pollingActive;
        private int _pollingTickRunning;
        private Func<byte[]> _commandFactory;

        public string Address { get; set; } = "192.168.1.1";
        public int Port { get; set; } = 1999;
        public int PollingIntervalMs { get; set; } = 200;
        public string ConnectionAddress => $"{Address}:{Port}";

        /// <summary>
        /// Called before each TCP connect attempt. Return false to log a warning; the connect proceeds regardless.
        /// Intended for launching a remote bridge (e.g. socat over SSH) before the socket is opened.
        /// </summary>
        public Func<bool> BeforeConnect { get; set; }

        public event PADStatusChangedEventHandler StatusChanged;
        public event PADWorkingModeChangedEventHandler WorkingModeChanged;
        public event Action<byte[]> FrameReceived;
        public event PADLogEventHandler LogChanged;

        public bool Connect(Func<byte[]> commandFactory)
        {
            if (_client?.Connected == true)
            {
                return true;
            }

            try
            {
                if (BeforeConnect != null)
                {
                    try
                    {
                        if (!BeforeConnect())
                        {
                            Log(Log_Level.Warning, "BeforeConnect returned false — proceeding anyway");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(Log_Level.Warning, $"BeforeConnect failed: {ex.Message}");
                    }
                }

                _commandFactory = commandFactory;

                _client = new TcpClient();
                _client.Connect(Address, Port);
                _stream = _client.GetStream();

                _readerRunning = true;
                _readerThread = new Thread(ReadLoop)
                {
                    Name = "tcpstream-reader",
                    IsBackground = true
                };
                _readerThread.Start();

                Log(Log_Level.Info, $"TCP connected to {Address}:{Port}");
                StatusChanged?.Invoke(ConnectionStatus.Connected);
                return true;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                Close();
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
            return _client?.Connected == true;
        }

        public void Send(byte[] command)
        {
            if (!IsConnected() || _stream == null)
            {
                return;
            }
            lock (_writeLock)
            {
                try
                {
                    _stream.Write(command, 0, command.Length);
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
        public void StopPassiveReceive() { }

        private void ReadLoop()
        {
            List<byte> buffer = new List<byte>(4096);
            byte[] chunk = new byte[256];

            while (_readerRunning)
            {
                try
                {
                    if (_stream == null || !IsConnected())
                    {
                        break;
                    }

                    int read = _stream.Read(chunk, 0, chunk.Length);
                    if (read == 0)
                    {
                        OnDisconnected("Connection closed by remote host");
                        break;
                    }

                    for (int i = 0; i < read; i++)
                    {
                        buffer.Add(chunk[i]);
                    }

                    StreamFrameParser.Parse(buffer, frame => FrameReceived?.Invoke(frame));
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
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null;
                _client = null;
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
