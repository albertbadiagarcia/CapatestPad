namespace Capatest.Pad
{
    public sealed class NvsEntry
    {
        public string Key { get; }
        public uint? Value { get; }  // null when the entry has no u32 field (string type)

        public NvsEntry(string key, uint? value)
        {
            Key = key;
            Value = value;
        }
    }
}
