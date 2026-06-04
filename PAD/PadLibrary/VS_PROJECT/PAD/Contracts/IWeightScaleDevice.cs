using System.Collections.Generic;

namespace Capatest.Pad
{
    /// <summary>
    /// Device with HX710A differential weight-scale inputs (4 channels).
    /// Updated on every status frame.
    /// </summary>
    public interface IWeightScaleDevice : IPadDevice
    {
        IReadOnlyList<WeightReading> WeightReadings { get; }

        event System.Action<IReadOnlyList<WeightReading>> WeightChanged;
    }
}
