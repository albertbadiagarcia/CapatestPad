using System;

namespace Capatest.Pad
{
    public sealed class Pad837 : PadDevice
    {
        private readonly Pad837Protocol _protocol837;

        public Pad837(string ip, int port = 1999)
            : this(ip, port, new Pad837Protocol()) { }

        private Pad837(string ip, int port, Pad837Protocol protocol)
            : base(new TcpTransport { Address = ip, Port = port }, protocol)
        {
            _protocol837 = protocol;
        }

        public override void StartAutoStatus(short ms = 80)
        {
            try
            {
                Transport.Send(_protocol837.BuildStartAutoStatus(ms));
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
                Transport.Send(_protocol837.BuildStopAutoStatus());
                Transport.StopPassiveReceive();
                Transport.StartPolling();
            }
            catch (Exception ex)
            {
                Log(Log_Level.Error, ex.Message);
            }
        }
    }
}
