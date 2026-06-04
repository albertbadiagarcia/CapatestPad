using System.Collections.Generic;

namespace Capatest.Pad
{
    public delegate void PADDataEventHandler(PadData e);
    public delegate void PADScopeEventHandler(List<double> e);
    public delegate void PADFastAcquisitionEventHandler(List<List<double>> e);
    public delegate void PADStatusChangedEventHandler(ConnectionStatus status);
    public delegate void PADWorkingModeChangedEventHandler(WorkingMode status);
    public delegate void SetOldPadVoltageEventHandler(double channel0, double channel1);
    public delegate void WrongPADModelEventHandler(PAD_Models modelDetected);
    public delegate void PADLogEventHandler(Log_Level level, string message);
}
