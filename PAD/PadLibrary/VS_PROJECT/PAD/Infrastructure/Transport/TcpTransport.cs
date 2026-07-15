using System;
using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Capatest.Pad
{
    public class TcpTransport : ITransport
    {
        private TcpClient _tcpClient = new TcpClient();
        private ManualResetEvent _connectDoneEvent = new ManualResetEvent(false);
        private readonly byte[] _buffer = new byte[1024];
        private readonly BackgroundWorker _pollingWorker = new BackgroundWorker();
        private readonly object _socketLock = new object();
        private Thread _passiveReceiveThread;
        private volatile bool _passiveReceiveStopping;
        private volatile bool _pollingStopRequested = true;
        private Func<byte[]> _commandFactory;
        private readonly System.Collections.Generic.List<byte> _rxBuffer = new System.Collections.Generic.List<byte>();
        private const int MaxRxBuffer = 8192;

        public string Address { get; set; } = "192.168.1.170";
        public int Port { get; set; } = 1999;
        public int PollingIntervalMs { get; set; } = 250;
        public string ConnectionAddress => $"{Address}:{Port}";

        public event PADStatusChangedEventHandler StatusChanged;
        public event PADWorkingModeChangedEventHandler WorkingModeChanged;
        public event Action<byte[]> FrameReceived;
        public event PADLogEventHandler LogChanged;
        public string LocalIP { get; private set; } = string.Empty;

        public TcpTransport()
        {
            _pollingWorker.WorkerSupportsCancellation = true;
            _pollingWorker.DoWork += OnPollingWorkerTick;
            _pollingWorker.RunWorkerCompleted += OnPollingWorkerCompleted;
        }

        public bool Connect(Func<byte[]> commandFactory)
        {
            try
            {
                _commandFactory = commandFactory;
                _rxBuffer.Clear();
                TcpClient freshClient = new TcpClient();
                lock (_socketLock)
                {
                    _tcpClient = freshClient;
                }
                _connectDoneEvent.Reset();

                PingReply ping = new Ping().Send(Address);
                if (ping.Status != IPStatus.Success)
                {
                    StatusChanged?.Invoke(ConnectionStatus.Reconnecting);
                    return false;
                }

                _tcpClient.BeginConnect(Address, Port, OnConnected, _tcpClient);
                _connectDoneEvent.WaitOne(2000, true);

                if (!_tcpClient.Connected)
                {
                    return false;
                }

                LocalIP = ((IPEndPoint)_tcpClient.Client.LocalEndPoint).Address.ToString();
                _tcpClient.Client.SendTimeout = 2000;
                _tcpClient.Client.ReceiveTimeout = 2000;
                _tcpClient.SendTimeout = 2000;
                _tcpClient.ReceiveTimeout = 2000;

                StatusChanged?.Invoke(ConnectionStatus.Connected);
                return true;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                return false;
            }
        }

        public bool Disconnect(bool triggerReconnect = false)
        {
            try
            {
                _pollingStopRequested = true;
                _passiveReceiveStopping = true;
                StopPolling();
                StopPassiveReceive();
                try { _tcpClient.Client?.Shutdown(SocketShutdown.Both); } catch { }
                try { _tcpClient.Client?.Close(); } catch { }
                try { _tcpClient.Close(); } catch { }

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
            try
            {
                return _tcpClient?.Client?.Connected == true;
            }
            catch
            {
                return false;
            }
        }

        public void Send(byte[] command)
        {
            if (!IsConnected())
            {
                return;
            }
            try
            {
                // Log(Log_Level.Raw, "TXSEND " + BitConverter.ToString(command));
                lock (_socketLock)
                {
                    if (!IsConnected())
                    {
                        return;
                    }
                    _tcpClient.Client.Send(command, 0, command.Length, SocketFlags.None);
                }
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                Disconnect(triggerReconnect: true);
            }
        }


        public void StartPolling()
        {
            _pollingStopRequested = false;
            if (_tcpClient?.Client != null)
            {
                _tcpClient.Client.ReceiveTimeout = 2000;
            }
            if (!_pollingWorker.IsBusy)
            {
                _pollingWorker.RunWorkerAsync();
            }
            WorkingModeChanged?.Invoke(WorkingMode.SendStatus);
        }

        public void StopPolling()
        {
            _pollingStopRequested = true;
            if (_pollingWorker.IsBusy)
            {
                _pollingWorker.CancelAsync();
            }

            WorkingModeChanged?.Invoke(WorkingMode.None);
        }

        private void OnPollingWorkerTick(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = (BackgroundWorker)sender;
            while (!worker.CancellationPending)
            {
                Thread.Sleep(PollingIntervalMs);
                if (worker.CancellationPending || !IsConnected())
                {
                    e.Cancel = worker.CancellationPending || _pollingStopRequested;
                    break;
                }
                try
                {
                    byte[] command = _commandFactory?.Invoke();
                    if (command == null)
                    {
                        continue;
                    }

                    int received;
                    lock (_socketLock)
                    {
                        if (worker.CancellationPending || !IsConnected())
                        {
                            e.Cancel = worker.CancellationPending || _pollingStopRequested;
                            break;
                        }
                        _tcpClient.Client.Send(command, 0, command.Length, SocketFlags.None);
                        received = _tcpClient.Client.Receive(_buffer);
                    }
                    if (received > 0)
                    {
                        IngestAndEmit(_buffer, received);
                    }
                }
                catch (Exception ex)
                {
                    Log(Log_Level.Error, ex.Message);
                    e.Result = false;
                    break;
                }
            }
        }

        private void OnPollingWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                return;  // intentional stop — do not reconnect
            }
            if (_pollingStopRequested)
            {
                return;
            }
            if (e.Result is bool ok && !ok)
            {
                Disconnect(triggerReconnect: true);
                return;
            }
            if (!IsConnected())
            {
                Disconnect(triggerReconnect: true);
            }
        }


        // Reassemble the TCP byte stream into discrete binary protocol frames
        // (STX + ADDR(2) + LEN(2) + payload + XOR + ETX). A single socket Receive
        // may carry several frames concatenated or split one frame across reads —
        // especially while the device streams continuously — so bytes are buffered
        // across reads and only complete, ETX-terminated frames are emitted. This
        // replaces the previous assumption that every Receive() returned exactly
        // one frame, which desynchronised under continuous streaming and produced
        // corrupted status data (e.g. flickering relay states).
        private void IngestAndEmit(byte[] data, int length)
        {
            // Log(Log_Level.Raw, "RXRECV " + BitConverter.ToString(data, 0, length));

            for (int i = 0; i < length; i++)
            {
                _rxBuffer.Add(data[i]);
            }
            if (_rxBuffer.Count > MaxRxBuffer)
            {
                _rxBuffer.RemoveRange(0, _rxBuffer.Count - MaxRxBuffer);
            }

            while (true)
            {
                // Resync to the next frame start (STX = 0x01 for Pad837, 0x02 otherwise).
                int start = -1;
                for (int i = 0; i < _rxBuffer.Count; i++)
                {
                    if (_rxBuffer[i] == 0x01 || _rxBuffer[i] == 0x02)
                    {
                        start = i;
                        break;
                    }
                }
                if (start < 0)
                {
                    _rxBuffer.Clear();
                    return;
                }
                if (start > 0)
                {
                    _rxBuffer.RemoveRange(0, start);
                }

                if (_rxBuffer.Count < 5)
                {
                    return;  // need the 5-byte header to know the length
                }

                byte stx = _rxBuffer[0];
                int payloadLength = stx == 0x01
                    ? (_rxBuffer[3] << 8) | _rxBuffer[4]
                    : (_rxBuffer[2] << 8) | _rxBuffer[3];
                int totalLength = 5 + payloadLength + 2;  // header + payload + XOR + ETX

                if (payloadLength <= 0 || totalLength > MaxRxBuffer)
                {
                    _rxBuffer.RemoveAt(0);  // bogus header (false STX), resync
                    continue;
                }
                if (_rxBuffer.Count < totalLength)
                {
                    return;  // wait for the rest of the frame
                }
                if (_rxBuffer[totalLength - 1] != 0x04)
                {
                    _rxBuffer.RemoveAt(0);  // not a real frame boundary, resync
                    continue;
                }

                byte[] frame = _rxBuffer.GetRange(0, totalLength).ToArray();
                _rxBuffer.RemoveRange(0, totalLength);
                FrameReceived?.Invoke(frame);
            }
        }

        public void StartPassiveReceive()
        {
            _passiveReceiveStopping = false;
            _tcpClient.Client.ReceiveTimeout = 250;
            if (_passiveReceiveThread != null && _passiveReceiveThread.IsAlive)
            {
                return;
            }
            _passiveReceiveThread = new Thread(Listener)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };
            _passiveReceiveThread.Start();
            WorkingModeChanged?.Invoke(WorkingMode.AutoStatus);
        }

        public void StopPassiveReceive()
        {
            _passiveReceiveStopping = true;
            _passiveReceiveThread?.Join(TimeSpan.FromSeconds(2));
            _passiveReceiveThread = null;
            WorkingModeChanged?.Invoke(WorkingMode.None);
        }

        private void Listener()
        {
            byte[] buf = new byte[1024];
            while (!_passiveReceiveStopping && IsConnected())
            {
                try
                {
                    int received = _tcpClient.Client.Receive(buf);
                    if (received > 0)
                    {
                        IngestAndEmit(buf, received);
                    }
                }
                catch (SocketException ex) when (!_passiveReceiveStopping && ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    if (!_passiveReceiveStopping)
                    {
                        Log(Log_Level.Error, ex.Message);
                        Disconnect(triggerReconnect: true);
                    }
                    break;
                }
            }
        }

        private void OnConnected(IAsyncResult result)
        {
            try
            {
                TcpClient client = (TcpClient)result.AsyncState;
                client.EndConnect(result);
                Log(Log_Level.Info, $"Connected to {client.Client.RemoteEndPoint}");
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
            finally
            {
                _connectDoneEvent.Set();
            }
        }

        private void Log(Log_Level level, string message)
        {
            LogChanged?.Invoke(level, message);
        }
    }
}
