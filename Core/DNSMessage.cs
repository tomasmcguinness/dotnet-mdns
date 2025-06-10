using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace mDNS.Core
{
    public class DNSMessage
    {
        public DNSMessage(byte[] bytes)
        {
            // DNS Uses Network Byte Order (Big Endian). C# is Little Endian.
            // Reverse all bytes before turning into numbers. TODO Put this in a utility class.
            //
            ReadOnlySpan<byte> messageSpan = bytes.AsSpan();

            // Start reading the header.
            //
            Id = BitConverter.ToUInt16(messageSpan.Slice(0, 2));

            var flags = messageSpan.Slice(2, 2);

            IsQuery = flags[0] == 0x00;

            var questionCountBytes = messageSpan.Slice(4, 2).ToArray();
            var reversedQuestionCountBytes = questionCountBytes.Reverse();
            var queryCount = BitConverter.ToUInt16(reversedQuestionCountBytes.ToArray());

            var answerCountBytes = messageSpan.Slice(6, 2).ToArray();
            var reversedAnswerCountBytes = answerCountBytes.Reverse();
            var answerCount = BitConverter.ToUInt16(reversedAnswerCountBytes.ToArray());

            // Ignore Authority Records (NSCOUNT)
            //
            
            var additionalInformationCountBytes = messageSpan.Slice(10, 2).ToArray();
            var reversedAdditionalInformationCount = additionalInformationCountBytes.Reverse();
            var additionalInformationCount = BitConverter.ToUInt16(reversedAdditionalInformationCount.ToArray());

            // DNS Header is 12 bytes long.
            //
            var contentIndex = 12;

            for (int i = 0; i < queryCount; i++)
            {
                Queries.Add(ParseRecord(messageSpan, true, ref contentIndex));
            }

            for (int i = 0; i < answerCount; i++)
            {
                Answers.Add(ParseRecord(messageSpan, false, ref contentIndex));
            }

            for (int i = 0; i < additionalInformationCount; i++)
            {
                AdditionalInformation.Add(ParseRecord(messageSpan, false, ref contentIndex));
            }
        }

        private Record ParseRecord(ReadOnlySpan<byte> messageSpan, bool isQuery, ref int contentIndex)
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

            var name = Serialization.DecodeName(nameSpan, messageSpan);
            contentIndex += nameLength;

            var typeBytes = messageSpan.Slice(contentIndex, 2).ToArray();
            var reversedTypeBytes = typeBytes.Reverse();
            var type = (RecordType)BitConverter.ToUInt16(reversedTypeBytes.ToArray());
            contentIndex += 2;

            var classBytes = messageSpan.Slice(contentIndex, 2).ToArray();
            var classBytesReversed = classBytes.Reverse();
            var @class = (RecordClass)BitConverter.ToUInt16(classBytesReversed.ToArray());
            contentIndex += 2;

            if (!isQuery)
            {
                var ttlBytes = messageSpan.Slice(contentIndex, 4).ToArray();
                var ttlBytesReversed = ttlBytes.Reverse();
                var ttl = BitConverter.ToUInt32(ttlBytesReversed.ToArray());
                contentIndex += 4;

                var rdDataLengthBytes = messageSpan.Slice(contentIndex, 2).ToArray();
                var rdDataLengthBytesReversed = rdDataLengthBytes.Reverse();
                var rdDataLength = BitConverter.ToUInt16(rdDataLengthBytesReversed.ToArray());
                contentIndex += 2;

                var recordData = messageSpan.Slice(contentIndex, rdDataLength).ToArray();
                contentIndex += rdDataLength;

                if (type == RecordType.PTR)
                {
                    var decodedName = Serialization.DecodeName(recordData, messageSpan);
                    return new PointerRecord(name, type, @class, ttl, decodedName);
                }
                else if (type == RecordType.SRV)
                {
                    (ushort port, string hostname) = Serialization.DecodeService(recordData, messageSpan);
                    return new ServiceRecord(name, type, @class, ttl, port, hostname);
                }
                else if(type == RecordType.A)
                {
                    IPAddress address = Serialization.DecodeARecord(recordData, messageSpan);
                    return new ARecord(name, type, @class, ttl, address);
                }
                else
                {
                    return new Record(name, type, @class, ttl);
                }
            }
            else
            {
                return new Record(name, type, @class);
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

        public List<Record> AllRecords => Queries.Concat(Answers).Concat(AdditionalInformation).ToList();

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

        internal Record? GetRecord(string value)
        {
            return AllRecords.SingleOrDefault(r => r.Name == value);
        }

        internal Record[] GetRecords(string value)
        {
            return AllRecords.Where(r => r.Name == value).ToArray();
        }

        private void SerializeRecord(BinaryWriter writer, Record record)
        {
            var ptrNodeName = Serialization.EncodeName(record.Name);
            writer.Write(ptrNodeName);

            var type = BitConverter.GetBytes((ushort)record.Type).Reverse().ToArray();
            writer.Write(type);

            var @class = BitConverter.GetBytes((ushort)record.Class).Reverse().ToArray(); // Internet
            writer.Write(@class);

            if (record.TTL is not null)
            {
                var ttl = BitConverter.GetBytes((uint)120).Reverse().ToArray();
                writer.Write(ttl);
            }

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
    }

    public static class DNSMessageExtensions
    {
        private static void AddRecord(List<Record> records, string name, RecordType type, RecordClass @class, uint ttl, byte[] data)
        {
            records.Add(new Record(name, type, @class, ttl!, data!));
        }

        public static void AddPointer(this List<Record> records, string name)
        {
            records.Add(new Record(name, RecordType.PTR, RecordClass.Internet));
        }

        public static void AddPointer(this List<Record> records, string name, string value)
        {
            AddRecord(records, name, RecordType.PTR, RecordClass.Internet, 120, Serialization.EncodeName(value));
        }

        public static void AddText(this List<Record> records, string name, Dictionary<string, string> values)
        {
            AddRecord(records, name, RecordType.TXT, RecordClass.Internet, 120, Serialization.EncodeTextValues(values));
        }

        public static void AddService(this List<Record> records, string name, ushort priority, ushort weight, ushort port, string hostname)
        {
            AddRecord(records, name, RecordType.SRV, RecordClass.Internet, 120, Serialization.EncodeService(priority, weight, port, hostname));
        }

        public static void AddARecord(this List<Record> records, string name, string ipAddress)
        {
            AddRecord(records, name, RecordType.A, RecordClass.Internet, 120, Serialization.EncodeIPv4Address(ipAddress));
        }
    }
}
