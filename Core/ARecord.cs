namespace mDNS.Core
{
    internal class ARecord : Record
    {
        public ARecord(string name, RecordType type, RecordClass @class, uint ttl, ushort port, string hostname)
            : base(name, type, @class, ttl)
        {
            Port = port;
            Hostname = hostname;
        }

        public ushort Port { get; }

        public string Hostname { get; }
    }
}