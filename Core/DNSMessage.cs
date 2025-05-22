using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
                    else if (questionSpan[x] == 0xC0)
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
                    else if (questionSpan[x] == 0xC0)
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

        public List<Record> Queries { get; } = [];

        public List<Record> Answers { get; } = [];

        public void AddAnswer(string name, RecordType type, RecordClass @class, byte[] data)
        {
            Answers.Add(new Record(name, type, @class, 120, data));
        }

        public void AddQuery(string name, RecordType type, RecordClass @class)
        {
            Queries.Add(new Record(name, type, @class));
        }

        public void AddPointerAnswer(string name, string value)
        {
            AddAnswer(name, RecordType.PTR, RecordClass.Internet, EncodeName(value));
        }

        public void AddTextAnswer(string name, Dictionary<string, string> values)
        {
            AddAnswer(name, RecordType.TXT, RecordClass.Internet, EncodeTextValues(values));
        }

        public void AddServiceAnswer(string name, ushort priority, ushort weight, ushort port, string hostname)
        {
            AddAnswer(name, RecordType.SRV, RecordClass.Internet, EncodeService(priority, weight, port, hostname));
        }

        public void AddARecordAnswer(string name, IPAddress ipAddress)
        {
            AddAnswer(name, RecordType.A, RecordClass.Internet, EncodeIPv4Address(ipAddress));
        }

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

            var additionalCounts = BitConverter.GetBytes((ushort)0).Reverse().ToArray();
            writer.Write(additionalCounts);

            var otherCounts = BitConverter.GetBytes((ushort)0).Reverse().ToArray();
            writer.Write(otherCounts);

            foreach (var answer in Answers)
            {
                SerializeRecord(writer, answer);
            }

            foreach (var query in Queries)
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

            foreach (var query in Queries)
            {
                sb.AppendFormat("\n* {0}", query.Name);
            }

            sb.AppendFormat("\nAnswerCount: {0}", AnswerCount);

            foreach (var answer in Answers)
            {
                sb.AppendFormat("\n* {0}", answer.Name);
            }

            return sb.ToString();
        }

        private void SerializeRecord(BinaryWriter writer, Record record)
        {
            var ptrNodeName = EncodeName(record.Name);

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
                var ptrServiceName = record.Data;

                var recordLength = BitConverter.GetBytes((ushort)ptrServiceName.Length).Reverse().ToArray();

                writer.Write(recordLength);

                if (ptrServiceName.Length > 0)
                {
                    writer.Write(ptrServiceName);
                }
            }
        }

        private static byte[] EncodeName(string name)
        {
            var parts = name.Split('.');

            var result = new byte[0];

            foreach (var part in parts)
            {
                int length = part.Length;
                byte lengthByte = Convert.ToByte(length);
                result = result.Concat([lengthByte]).Concat(Encoding.UTF8.GetBytes(part)).ToArray();
            }

            // Null terminator.
            //
            return result.Concat(new byte[1] { 0x00 }).ToArray();
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
                else if (length == 0xC0)
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

        private byte[] EncodeTextValues(Dictionary<string, string> values)
        {
            var result = new byte[0];

            foreach (var keypair in values)
            {
                string fullKeyPair = $"{keypair.Key}={keypair.Value}";
                result = result.Concat(new byte[1] { (byte)fullKeyPair.Length }).Concat(Encoding.UTF8.GetBytes(fullKeyPair)).ToArray();
            }

            return result;
        }

        private byte[] EncodeIPv4Address(IPAddress ipAddress)
        {
            return EncodeIPAddress(ipAddress.ToString(), '.');
        }

        private byte[] EncodeIPAddress(string ipAddress, char separator)
        {
            var parts = ipAddress.Split(separator);

            byte[] result = new byte[4];

            int index = 0;

            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    result[index++] = 0x00;
                }
                else
                {
                    result[index++] = byte.Parse(part);
                }
            }

            return result;
        }

        private byte[] EncodeService(ushort priority, ushort weight, ushort port, string hostname)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            var serviceName = EncodeName(hostname);

            int totalLength = serviceName.Length + 2 + 2 + 2; // name + priority + weight + port

            var dataLength = BitConverter.GetBytes((ushort)totalLength).Reverse().ToArray();
            writer.Write(dataLength);

            var priorityBytes = BitConverter.GetBytes(priority).Reverse().ToArray();
            writer.Write(priorityBytes);

            var weightBytes = BitConverter.GetBytes(weight).Reverse().ToArray();
            writer.Write(weightBytes);

            var portBytes = BitConverter.GetBytes(port).Reverse().ToArray();
            writer.Write(portBytes);

            writer.Write(serviceName.ToArray());

            return ms.ToArray();
        }
    }
}
