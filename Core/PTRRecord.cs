namespace mDNS.Core
{
    internal class PTRRecord : Record
    {
        public PTRRecord(string name, RecordType type, RecordClass @class, uint ttl, string value) : base(name, type, @class, ttl)
        {
            Value = value;
        }

        public string Value { get; }
    }
}