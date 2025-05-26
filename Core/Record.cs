namespace mDNS.Core
{
    public class Record
    {
        public Record(string name, RecordType type, RecordClass @class)
        {
            Name = name;
            Type = type;
            Class = @class;
        }

        public Record(string name, RecordType type, RecordClass @class, uint ttl) : this(name, type, @class)
        {
            TTL = ttl;
        }

        public Record(string name, RecordType type, RecordClass @class, uint ttl, byte[] data) : this(name, type, @class, ttl)
        {
            Data = data;
        }

        public string Name { get; }

        public RecordType Type { get; }

        public RecordClass Class { get; }

        public uint? TTL { get; }

        public byte[]? Data { get; }
    }
}
