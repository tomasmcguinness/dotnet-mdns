using System.Collections.Generic;

namespace Core
{
    public class Record
    {
        public Record(string name, RecordType type, RecordClass @class)
        {
            Name = name;
            Type = type;
            Class = @class;
        }

        public Record(string name, RecordType type, RecordClass @class, string value) : this(name, type, @class)
        {
            Value = value;
        }

        public Record(string name, RecordType type, RecordClass @class, Dictionary<string, string> values) : this(name, type, @class)
        {
        }

        public string Name { get; }

        public RecordType Type { get; }

        public RecordClass Class { get; }

        public string? Value { get; }
    }
}
