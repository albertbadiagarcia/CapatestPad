using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Capatest.Pad
{
    internal sealed class UdpDataReceiver
    {
        private const int BufferSize = 163840;
        private Socket _socket;
        private EndPoint _remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
        private readonly byte[] _buffer = new byte[BufferSize];
        private volatile bool _stopped;

        public int Channels { get; set; } = 1;
        public event Action<List<List<double>>> DataReceived;

        public void Bind(string ip, int port)
        {
            Stop();
            _stopped = false;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
            _socket.Bind(new IPEndPoint(IPAddress.Parse(ip), port));
            BeginReceive();
        }

        public void Stop()
        {
            _stopped = true;
            try
            {
                _socket?.Close();
            }
            catch { }
        }

        private void BeginReceive()
        {
            if (_stopped)
            {
                return;
            }
            try
            {
                _socket.BeginReceiveFrom(_buffer, 0, BufferSize, SocketFlags.None,
                    ref _remoteEndpoint, OnReceive, null);
            }
            catch (Exception ex)
            {
                if (!_stopped)
                {
                    Console.WriteLine($"UdpDataReceiver: {ex.Message}");
                }
            }
        }

        private void OnReceive(IAsyncResult ar)
        {
            if (_stopped)
            {
                return;
            }
            try
            {
                int bytes = _socket.EndReceiveFrom(ar, ref _remoteEndpoint);
                List<List<double>> voltages = FastAcquisitionParser.Parse(_buffer, bytes, Channels);
                DataReceived?.Invoke(voltages);
            }
            catch (Exception ex)
            {
                if (!_stopped)
                {
                    Console.WriteLine($"UdpDataReceiver receive: {ex.Message}");
                }
            }

            BeginReceive();
        }
    }
}
