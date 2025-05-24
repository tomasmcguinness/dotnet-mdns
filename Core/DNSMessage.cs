using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Core
{
    public class DNSMessage
    {
        public DNSMessage(byte[] bytes)
        {
            ReadOnlySpan<byte> messageSpan = bytes.AsSpan();

            // Start reading the header.
            //
            Id = BitConverter.ToUInt16(messageSpan.Slice(0, 2));

            var flags = messageSpan.Slice(2, 2);

            IsQuery = flags[0] == 0x00;

            // DNS Uses Network Byte Order (Big Endian). C# is Little Endian.
            //
            var questionCountBytes = messageSpan.Slice(4, 2).ToArray();
            var reversedQuestionCountBytes = questionCountBytes.Reverse();
            var queryCount = BitConverter.ToUInt16(reversedQuestionCountBytes.ToArray());

            var answerCountBytes = messageSpan.Slice(6, 2).ToArray();
            var reversedAnswerCountBytes = answerCountBytes.Reverse();
            var answerCount = BitConverter.ToUInt16(reversedAnswerCountBytes.ToArray());

            // Ignore Authority Records (NSCOUNT) and Additional Records (ARCOUNT) for now.
            //

            // DNS Header is 12 bytes long.
            //
            var contentIndex = 12;

            for (int i = 0; i < queryCount; i++)
            {
                var nameBuffer = new byte[0];

                // Work through the first few bytes. These represent the name. 
                // There is a null terminator.
                //
                var questionSpan = messageSpan.Slice(contentIndex);

                int nameLength = 0;

                for (int x = 0; x < 255; x++)
                {
                    if (questionSpan[x] == 0x00)
                    {
                        nameLength = x + 1; // Add one to account for the loop starting at 0.
                        break;
                    }
                    // If this starts with a 11, it means it's a pointer.
                    //
                    else if ((0x3F & questionSpan[x]) == 0)
                    {
                        nameLength = x + 2; // Include the offset byte.
                        break;
                    }
                }

                var nameSpan = questionSpan.Slice(0, nameLength);

                var name = DecodeName(nameSpan, messageSpan);
                contentIndex += nameLength;

                var typeBytes = messageSpan.Slice(contentIndex, 2).ToArray();
                var reversedTypeBytes = typeBytes.Reverse();
                var type = (RecordType)BitConverter.ToUInt16(reversedTypeBytes.ToArray());
                contentIndex += 2;

                var classBytes = messageSpan.Slice(contentIndex, 2).ToArray();
                var classBytesReversed = classBytes.Reverse();
                var @class = (RecordClass)BitConverter.ToUInt16(classBytesReversed.ToArray());
                contentIndex += 2;

                // Query records don't have TTL & RData.
                //

                // Add this record to the list of questions being asked.
                //
                Queries.Add(new Record(name, type, @class));
            }

            for (int i = 0; i < answerCount; i++)
            {
                var nameBuffer = new byte[0];

                // Work through the first few bytes. These represent the name. 
                // There is a null terminator.
                //
                var questionSpan = messageSpan.Slice(contentIndex);

                int nameLength = 0;

                for (int x = 0; x < 255; x++)
                {
                    if (questionSpan[x] == 0x00)
                    {
                        nameLength = x + 1;
                        break;
                    }
                    else if (questionSpan[x] == 0xC0 || questionSpan[x] == 0xC1)
                    {
                        nameLength = x + 2; // Include the offset byte.
                        break;
                    }
                }

                var nameSpan = questionSpan.Slice(0, nameLength);

                var name = DecodeName(nameSpan, messageSpan);
                contentIndex += nameLength;

                var typeBytes = messageSpan.Slice(contentIndex, 2).ToArray();
                var reversedTypeBytes = typeBytes.Reverse();
                var type = (RecordType)BitConverter.ToUInt16(reversedTypeBytes.ToArray());
                contentIndex += 2;

                var classBytes = messageSpan.Slice(contentIndex, 2).ToArray();
                var classBytesReversed = classBytes.Reverse();
                var @class = (RecordClass)BitConverter.ToUInt16(classBytesReversed.ToArray());
                contentIndex += 2;

                var ttlBytes = messageSpan.Slice(contentIndex, 4).ToArray();
                var ttlBytesReversed = ttlBytes.Reverse();
                var ttl = BitConverter.ToUInt32(ttlBytesReversed.ToArray());
                contentIndex += 4;

                var rdDataLengthBytes = messageSpan.Slice(contentIndex, 2).ToArray();
                var rdDataLengthBytesReversed = rdDataLengthBytes.Reverse();
                var rdDataLength = BitConverter.ToUInt16(rdDataLengthBytesReversed.ToArray());
                contentIndex += 2;

                var rdDataBytes = messageSpan.Slice(contentIndex, rdDataLength).ToArray();
                contentIndex += rdDataLength;

                // Add this record to the list of answers.
                //
                Answers.Add(new Record(name, type, @class, ttl, rdDataBytes));
            }
        }

        public DNSMessage(bool isResponse)
        {
            IsQuery = !isResponse;
        }

        public ushort Id { get; }

        public bool IsQuery { get; }

        public ushort QueryCount => (ushort)Queries.Count;

        public ushort AnswerCount => (ushort)Answers.Count;

        public ushort AdditionalInformationCount => (ushort)AdditionalInformation.Count;

        public List<Record> Queries { get; } = [];

        public List<Record> Answers { get; } = [];

        public List<Record> AdditionalInformation { get; } = [];

        public byte[] GetBytes()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            var reponseId = new byte[2];
            writer.Write(reponseId);

            var responseHeaderFlags = new byte[2] { IsQuery ? (byte)0x00 : (byte)0x84, 0x00 };
            writer.Write(responseHeaderFlags);

            var queryCountBytes = BitConverter.GetBytes((ushort)QueryCount).Reverse().ToArray();
            writer.Write(queryCountBytes);

            var answerCountBytes = BitConverter.GetBytes((ushort)AnswerCount).Reverse().ToArray();
            writer.Write(answerCountBytes);

            var authorityCountBytes = BitConverter.GetBytes((ushort)0).Reverse().ToArray();
            writer.Write(authorityCountBytes);

            var additionalInformationCountBytes = BitConverter.GetBytes(AdditionalInformationCount).Reverse().ToArray();
            writer.Write(additionalInformationCountBytes);

            foreach (var answer in Answers)
            {
                SerializeRecord(writer, answer);
            }

            foreach (var query in Queries)
            {
                SerializeRecord(writer, query);
            }

            foreach (var query in AdditionalInformation)
            {
                SerializeRecord(writer, query);
            }

            writer.Flush();

            return ms.ToArray();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Type: {0}", IsQuery ? "Query" : "Response");

            sb.AppendFormat("\nQueryCount: {0}", QueryCount);

            foreach (var record in Queries)
            {
                sb.AppendFormat("\n* {0} {1}", record.Name, record.Type);
            }

            sb.AppendFormat("\nAnswerCount: {0}", AnswerCount);

            foreach (var record in Answers)
            {
                sb.AppendFormat("\n* {0} {1}", record.Name, record.Type);
            }

            sb.AppendFormat("\nAdditionalInformationCount: {0}", AdditionalInformationCount);

            foreach (var record in AdditionalInformation)
            {
                sb.AppendFormat("\n* {0} {1}", record.Name, record.Type);
            }

            return sb.ToString();
        }

        private void SerializeRecord(BinaryWriter writer, Record record)
        {
            var ptrNodeName = Serialization.EncodeName(record.Name);
            writer.Write(ptrNodeName);

            var type = BitConverter.GetBytes((ushort)record.Type).Reverse().ToArray();
            writer.Write(type);

            var @class = BitConverter.GetBytes((ushort)record.Class).Reverse().ToArray(); // Internet
            writer.Write(@class);

            var ttl = BitConverter.GetBytes((uint)120).Reverse().ToArray();
            writer.Write(ttl);

            // Set the RDData
            //
            if (record.Data is not null)
            {
                var recordData = record.Data;

                var recordLength = BitConverter.GetBytes((ushort)recordData.Length).Reverse().ToArray();

                writer.Write(recordLength);

                if (recordData.Length > 0)
                {
                    writer.Write(recordData);
                }
            }
        }

        private string DecodeName(ReadOnlySpan<byte> nameSpan, ReadOnlySpan<byte> messageSpan)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < nameSpan.Length; i += 0)
            {
                var length = nameSpan[i];

                if (length == 0x00)
                {
                    break;
                }
                // If this starts with a 11, it means it's a pointer.
                //
                else if ((0x3F & length) == 0)
                {
                    var offset = nameSpan[i + 1];

                    var nameSpanAtOffset = messageSpan.Slice(offset);

                    sb.Append(DecodeName(nameSpanAtOffset, messageSpan));

                    break;
                }

                var bytes = nameSpan.Slice(i + 1, length).ToArray();
                sb.Append(Encoding.UTF8.GetString(bytes));
                sb.Append(".");
                i += (length + 1);
            }

            return sb.ToString().TrimEnd('.');
        }
    }

    public static class DNSMessageExtensions
    {
        private static void AddRecord(List<Record> records, string name, RecordType type, RecordClass @class, byte[]? data)
        {
            records.Add(new Record(name, type, @class, 120, data!));
        }

        public static void AddPointer(this List<Record> records, string name)
        {
            AddRecord(records, name, RecordType.PTR, RecordClass.Internet, null);
        }

        public static void AddPointer(this List<Record> records, string name, string value)
        {
            AddRecord(records, name, RecordType.PTR, RecordClass.Internet, Serialization.EncodeName(value));
        }

        public static void AddText(this List<Record> records, string name, Dictionary<string, string> values)
        {
            AddRecord(records, name, RecordType.TXT, RecordClass.Internet, Serialization.EncodeTextValues(values));
        }

        public static void AddService(this List<Record> records, string name, ushort priority, ushort weight, ushort port, string hostname)
        {
            AddRecord(records, name, RecordType.SRV, RecordClass.Internet, Serialization.EncodeService(priority, weight, port, hostname));
        }

        public static void AddARecord(this List<Record> records, string name, string ipAddress)
        {
            AddRecord(records, name, RecordType.A, RecordClass.Internet, Serialization.EncodeIPv4Address(ipAddress));
        }
    }
}
