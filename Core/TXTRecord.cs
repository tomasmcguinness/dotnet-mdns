using System.Collections.Generic;

namespace mDNS.Core
{
    public class TXTRecord : Record
    {
        public TXTRecord(string name, RecordType type, RecordClass @class, uint ttl, Dictionary<string, string> values)
            : base(name, type, @class, ttl)
        {
            Values = values;
        }

        public Dictionary<string, string> Values { get; }
    }
}