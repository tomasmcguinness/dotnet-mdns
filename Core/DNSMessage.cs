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

                int nameEndIndex = 0;

                for (int x = 0; x < 63; x++)
                {
                    if (questionSpan[x] == 0x00)
                    {
                        nameEndIndex = x;
                        break;
                    }
                    else if (questionSpan[x] == 0xC0)
                    {
                        nameEndIndex = x + 1; // Include the offset byte.
                        break;
                    }
                }

                var nameSpan = questionSpan.Slice(0, nameEndIndex + 1);

                var name = DecodeName(nameSpan, messageSpan);
                contentIndex += (nameEndIndex + 1);

                var type = (RecordType)BitConverter.ToUInt16(messageSpan.Slice(contentIndex, 2));
                contentIndex += 2;

                var @class = (RecordClass)BitConverter.ToUInt16(messageSpan.Slice(contentIndex, 2));
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

                int nameEndIndex = 0;

                for (int x = 0; x < 63; x++)
                {
                    if (questionSpan[x] == 0x00)
                    {
                        nameEndIndex = x;
                        break;
                    }
                    else if (questionSpan[x] == 0xC0)
                    {
                        nameEndIndex = x + 1; // Include the offset byte.
                        break;
                    }
                }

                var nameSpan = questionSpan.Slice(0, nameEndIndex + 1);

                var name = DecodeName(nameSpan, messageSpan);
                contentIndex += (nameEndIndex + 1);

                var type = (RecordType)BitConverter.ToUInt16(messageSpan.Slice(contentIndex, 2));
                contentIndex += 2;

                var @class = (RecordClass)BitConverter.ToUInt16(messageSpan.Slice(contentIndex, 2));
                contentIndex += 2;

                var ttl = (RecordClass)BitConverter.ToUInt16(messageSpan.Slice(contentIndex, 2));
                contentIndex += 2;

                var rdDataLength = BitConverter.ToUInt16(messageSpan.Slice(contentIndex, 2));

                contentIndex += rdDataLength;

                // Add this record to the list of questions being asked.
                //
                Answers.Add(new Record(name, type, @class));
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

        public void AddAnswer(string name, RecordType type, RecordClass @class, string value)
        {
            Answers.Add(new Record(name, type, @class, value));
        }

        public void AddAnswer(string name, RecordType type, RecordClass @class, Dictionary<string, string> values)
        {
            Answers.Add(new Record(name, type, @class, values));
        }

        public void AddQuery(string v, string v1)
        {
            // TODO 
        }

        public byte[] GetBytes()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            var reponseId = new byte[2];
            writer.Write(reponseId);

            var responseHeaderFlags = new byte[2] { IsQuery ? (byte)0x00 : (byte)0x84, 0x00 };
            writer.Write(responseHeaderFlags);

            var questionCountBytes = BitConverter.GetBytes((ushort)0).Reverse().ToArray();
            writer.Write(questionCountBytes);
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

            writer.Flush();

            return ms.ToArray();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Query: {0}", IsQuery);
            sb.AppendFormat("\nQueryCount: {0}", QueryCount);

            foreach (var query in Queries)
            {
                sb.AppendFormat("\n* {0}", query);
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
            if (record.Type == RecordType.PTR)
            {
                var ptrServiceName = EncodeName(record.Value);

                var recordLength = BitConverter.GetBytes((ushort)ptrServiceName.Length).Reverse().ToArray();

                writer.Write(recordLength);

                writer.Write(ptrServiceName);
            }
        }

        private byte[] AddPtr(byte[] outputBuffer, string name, string v2)
        {
            var ptrNodeName = EncodeName(name);

            outputBuffer = outputBuffer.Concat(ptrNodeName).ToArray();

            var type = BitConverter.GetBytes((ushort)12).Reverse().ToArray(); // PTR

            outputBuffer = outputBuffer.Concat(type).ToArray();

            var @class = BitConverter.GetBytes((ushort)1).Reverse().ToArray(); // Internet

            outputBuffer = outputBuffer.Concat(@class).ToArray();

            var ttl = BitConverter.GetBytes((uint)120).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(ttl).ToArray();

            var ptrServiceName = EncodeName(v2);

            var recordLength = BitConverter.GetBytes((ushort)ptrServiceName.Length).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(recordLength).ToArray();

            outputBuffer = outputBuffer.Concat(ptrServiceName).ToArray();

            return outputBuffer;
        }

        private byte[] GetTxtRecord(Dictionary<string, string> values)
        {
            var result = new byte[0];

            foreach (var keypair in values)
            {
                string fullKeyPair = $"{keypair.Key}={keypair.Value}";
                result = result.Concat(new byte[1] { (byte)fullKeyPair.Length }).Concat(Encoding.UTF8.GetBytes(fullKeyPair)).ToArray();
            }

            return result;
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
    }
}
