using System;
using System.Collections.Generic;
using System.Net;

namespace Capatest.Pad
{
    public sealed class PadRtx : PadDevice, IFastAcquisitionDevice
    {
        private readonly PadRtxProtocol _rtxProtocol;
        private readonly TcpDataServer _tcpDataServer = new TcpDataServer();
        private readonly UdpDataReceiver _udpReceiver = new UdpDataReceiver();

        public bool IsFastAdcRunning { get; private set; }

        public PadRtx(string ip, int port = 1999)
            : this(ip, port, new PadRtxProtocol()) { }

        private PadRtx(string ip, int port, PadRtxProtocol protocol)
            : base(new TcpTransport { Address = ip, Port = port }, protocol)
        {
            _rtxProtocol = protocol;
        }

        public void StartAdc()
        {
            try
            {
                Transport.Send(_rtxProtocol.BuildStartAdc());
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void StopAdc()
        {
            try
            {
                Transport.Send(_rtxProtocol.BuildStopAdc());
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void ResetAdc()
        {
            StopAdc();
            StartAdc();
        }

        public override void StartAutoStatus(short ms = 80)
        {
            try
            {
                Transport.Send(_rtxProtocol.BuildStartAutoStatus(ms));
                Transport.StopPolling();
                Transport.StartPassiveReceive();
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public override void StopAutoStatus()
        {
            try
            {
                Transport.Send(_rtxProtocol.BuildStopAutoStatus());
                Transport.StopPassiveReceive();
                Transport.StartPolling();
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public bool StartFastAdc(int frequency, int samples, int channels = 8,
                                  int bufferSize = 1024, Socket_Type socketType = Socket_Type.TCP,
                                  int port = 0, string localIp = "")
        {
            try
            {
                if (frequency < 1000 || frequency > 10000)
                {
                    return false;
                }
                if (samples != -1 && (samples < bufferSize || samples % bufferSize != 0))
                {
                    return false;
                }
                if (channels < 1 || channels > 8)
                {
                    return false;
                }
                if (bufferSize > 20480)
                {
                    return false;
                }
                if (string.IsNullOrEmpty(localIp))
                {
                    if (Transport is TcpTransport)
                    {
                        localIp = ((TcpTransport)Transport).LocalIP;

                        if (string.IsNullOrEmpty(localIp))
                        {
                            localIp = GetLocalIp();
                        }
                    }
                    else    
                    {
                        localIp = GetLocalIp();
                    }
                }

                byte[] cmd = _rtxProtocol.BuildOpenFastAcquisition(
                    frequency, samples, channels, bufferSize, socketType, port, localIp);

                int bufferPeriodMs = (int)((long)bufferSize * 1000 / frequency);
                int readTimeoutMs = Math.Max(5000, 3 * bufferPeriodMs);

                switch (socketType)
                {
                    case Socket_Type.TCP:
                        _tcpDataServer.Channels = channels;
                        _tcpDataServer.Configure(IPAddress.Parse(localIp), port, bufferSize, readTimeoutMs);
                        _tcpDataServer.DataReceived -= OnFastAcquisitionData;
                        _tcpDataServer.DataReceived += OnFastAcquisitionData;
                        _tcpDataServer.Start();
                        break;

                    case Socket_Type.UDP:
                        _udpReceiver.Channels = channels;
                        _udpReceiver.DataReceived -= OnFastAcquisitionData;
                        _udpReceiver.DataReceived += OnFastAcquisitionData;
                        _udpReceiver.Bind(localIp, port);
                        break;
                }

                Transport.StopPolling();
                Transport.Send(cmd);
                IsFastAdcRunning = true;
                return true;
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
                return false;
            }
        }

        public void StopFastAdc()
        {
            try
            {
                Transport.Send(_rtxProtocol.BuildCloseFastAcquisition());
                _tcpDataServer.Stop();
                _udpReceiver.Stop();
                IsFastAdcRunning = false;
                Transport.StartPolling();
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        public void SetFastAdcSamplingRate(int samplingRate)
        {
            try
            {
                Transport.Send(_rtxProtocol.BuildSetFastAdcSamplingRate(samplingRate));
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }

        private void OnFastAcquisitionData(List<List<double>> voltages)
        {
            OnFastData(voltages);
        }

        private string GetLocalIp()
        {
            try
            {
                using (System.Net.Sockets.Socket s = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Dgram, 0))
                {
                    s.Connect("8.8.8.8", 65530);
                    return ((System.Net.IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        public override IPAddress GetDeviceIp(int serialNumber)
        {
            try
            {
                string serial = serialNumber.ToString().PadLeft(5, '0');
                string prefix = serialNumber < 10000 ? "padrtx-b-"
                              : serialNumber < 20000 ? "padrtx-c-"
                              :                        "padrtx-d-";

                foreach (System.Net.IPAddress a in System.Net.Dns.GetHostAddresses(prefix + serial))
                {
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return a;
                    }
                }
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
            return IPAddress.Loopback;
        }
    }
}
