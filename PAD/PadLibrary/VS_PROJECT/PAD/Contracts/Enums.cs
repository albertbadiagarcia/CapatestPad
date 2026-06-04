namespace Capatest.Pad
{
    public enum ConnectionStatus
    {
        Disconnected,
        Reconnecting,
        Connected,
        UnableToReconnect
    }

    public enum Counters_Mode
    {
        All_Counting,
        All_Frequency,
        First_Counting,
        First_Frequency,
        Second_Counting,
        Second_Frequency,
        Third_Counting,
        Third_Frequency,
        Fourth_Counting,
        Fourth_Frequency
    }

    public enum RelayStatus { Closed, Opened }

    public enum WorkingMode { None, SendStatus, Scope, AutoStatus, FastStatus }

    public enum Reconnection_Options { Till_The_End_Of_Time, User_Configured_Limit, Never }

    public enum PAD_Models { Pad728, Pad837, PadRTX, PadRTX2 }

    public enum Led_Blink_Modes { None, Slow, Fast }

    public enum RelayAction { None, Close, Open }

    public enum Socket_Type { TCP, UDP }

    public enum Log_Level { Raw, Trace, Debug, Info, Warning, Error }
}
