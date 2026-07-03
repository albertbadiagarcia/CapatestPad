using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Capatest.Pad
{
    internal sealed class TcpDataServer
    {
        private TcpListener _listener;
        private Thread _listenThread;
        private TcpClient _client;
        private volatile bool _stopping;
        private int _frameSize;
        private int _readTimeoutMs = 5000;

        public int Channels { get; set; } = 1;
        public event Action<List<List<double>>> DataReceived;

        public void Configure(IPAddress ip, int port, int bufferSizePerChannel, int readTimeoutMs)
        {
            _frameSize = bufferSizePerChannel * Channels * 2;  // 2 bytes per sample
            _readTimeoutMs = readTimeoutMs;
            _listener = new TcpListener(ip, port);
        }

        public void Start()
        {
            _stopping = false;
            if (_listenThread != null && _listenThread.IsAlive)
            {
                return;
            }
            _listenThread = new Thread(Listen) { IsBackground = true };
            _listenThread.Start();
        }

        public bool Stop()
        {
            try
            {
                _stopping = true;
                _listener?.Stop();
                _client?.Close();
                _listenThread?.Join(TimeSpan.FromSeconds(1));
                _listener = null;
                _client = null;
                _listenThread = null;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
        }

        private void Listen()
        {
            try
            {
                _listener.Start();
                while (!_stopping)
                {
                    _client = _listener.AcceptTcpClient();
                    HandleClient(_client);
                }
            }
            catch (Exception ex)
            {
                if (!_stopping)
                {
                    Console.WriteLine($"TcpDataServer: {ex.Message}");
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = _readTimeoutMs;
                byte[] buffer = new byte[Math.Max(_frameSize, 1)];

                while (!_stopping)
                {
                    int read = 0;
                    while (read < _frameSize && !_stopping)
                    {
                        int bytesRead = stream.Read(buffer, read, _frameSize - read);
                        if (bytesRead == 0)
                        {
                            return;
                        }
                        read += bytesRead;
                    }

                    if (_stopping || read == 0)
                    {
                        break;
                    }

                    List<List<double>> voltages = FastAcquisitionParser.Parse(buffer, read, Channels);
                    DataReceived?.Invoke(voltages);
                }
            }
            catch (Exception ex)
            {
                if (!_stopping)
                {
                    Console.WriteLine($"TcpDataServer receive: {ex.Message}");
                }
            }
            finally
            {
                client.Close();
            }
        }
    }
}
