namespace mDNS.Core
{
    internal class PointerRecord : Record
    {
        public PointerRecord(string name, RecordType type, RecordClass @class, uint ttl, string value) : base(name, type, @class, ttl)
        {
            Value = value;
        }

        public string Value { get; }
    }
}