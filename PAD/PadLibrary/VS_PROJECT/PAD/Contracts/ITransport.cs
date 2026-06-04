namespace Capatest.Pad
{
    public interface ITransport
    {
        string Address { get; set; }
        int Port { get; set; }
        string ConnectionAddress { get; }
        int PollingIntervalMs { get; set; }
        event PADStatusChangedEventHandler StatusChanged;
        event PADWorkingModeChangedEventHandler WorkingModeChanged;
        bool Connect(System.Func<byte[]> commandFactory);
        bool Disconnect(bool triggerReconnect = false);
        bool IsConnected();
        void Send(byte[] command);
        void StartPolling();
        void StopPolling();
        void StartPassiveReceive();
        void StopPassiveReceive();
        event System.Action<byte[]> FrameReceived;
    }
}
