using System.Net;

namespace mDNS.Core
{
    internal class ARecord : Record
    {
        public ARecord(string name, RecordType type, RecordClass @class, uint ttl, IPAddress address)
            : base(name, type, @class, ttl)
        {
            Address = address;
        }

        public IPAddress Address { get; }
    }
}