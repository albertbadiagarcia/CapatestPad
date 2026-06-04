namespace Capatest.Pad
{
    public sealed class Pad728 : PadDevice
    {
        public Pad728(string ip, int port = 1999)
            : base(
                new TcpTransport { Address = ip, Port = port },
                new Pad728Protocol())
        { }
    }
}
