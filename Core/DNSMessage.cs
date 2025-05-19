using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Core
{
    public class DNSMessage
    {
        public DNSMessage(byte[] messageBuffer)
        {
            //ReadOnlySpan<byte> contentSpan = payload.AsSpan();

            //var headerBytes = contentSpan.Slice(0, 12).ToArray();

            // Start reading the header.
            //
            Id = BitConverter.ToUInt16(messageBuffer, 0);

            //var flags = contentSpan.Slice(2, 1);

            var queryResponseBit = messageBuffer[2] >> 7;
            var isQuery = queryResponseBit == 0x00;

            RequestType = isQuery ? RequestType.Query : RequestType.Response;

            var queryResponseStatus = isQuery ? "Query" : "Response";

            var questions = new List<string>();

            var queryQuestionCount = ReadBigEndianUShort(messageBuffer, 4);
            Console.WriteLine($"QuestionCount: {queryQuestionCount}");

            var queryAnswerCount = ReadBigEndianUShort(messageBuffer, 6);
            Console.WriteLine($"AnswerCount: {queryAnswerCount}");

            var contentIndex = 12;

            for (int i = 0; i < queryQuestionCount; i++)
            {
                Console.WriteLine(BitConverter.ToString(messageBuffer.AsSpan().Slice(contentIndex).ToArray()));

                var nameBuffer = new byte[0];

                // Work through the first few bytes. These represent the name. 
                // There is a null terminator.
                //
                foreach (var b in messageBuffer.AsSpan().Slice(contentIndex))
                {
                    nameBuffer = nameBuffer.Concat([b]).ToArray();
                    contentIndex++;

                    if (b == 0x00)
                    {
                        break;
                    }
                    else if (b == 0xC0)
                    {
                        contentIndex++;
                        break;
                    }
                }

                var name = DecodeName(messageBuffer, nameBuffer);

                Console.WriteLine("Name: {0} [{1}]", name, name.Length);
                //contentIndex += nameBuffer.Length;

                var type = BitConverter.ToUInt16(messageBuffer, contentIndex);
                //Console.WriteLine("Type: {0}", ConvertTypeToDescription(type));
                contentIndex += 2;

                var @class = ReadBigEndianUShort(messageBuffer, contentIndex);
                //Console.WriteLine("Class: {0}", @class);
                contentIndex += 2;

                // Question records don't have TTL & RData.

                questions.Add(name);
            }
        }

        public ushort Id { get; set; }

        public RequestType RequestType { get; set; }

        private uint ReadBigEndianUInt(Span<byte> data, int index)
        {
            var bytes = data.Slice(index, 4);
            bytes.Reverse();
            return BitConverter.ToUInt32(bytes);
        }

        private ushort ReadBigEndianUShort(Span<byte> data, int index)
        {
            var bytes = data.Slice(index, 2);
            //bytes.Reverse();
            ushort value = BitConverter.ToUInt16(bytes);

            return value;
        }

        private string DecodeName(byte[] messageBuffer, byte[] nameBuffer)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < nameBuffer.Length; i += 0)
            {
                var length = nameBuffer[i];

                if (length == 0x00)
                {
                    break;
                }
                else if (length == 0xC0)
                {
                    var offset = nameBuffer[i + 1];

                    var nameBufferAtOffset = messageBuffer.Skip(offset).Take(63 - nameBuffer.Length).ToArray();

                    sb.Append(DecodeName(messageBuffer, nameBufferAtOffset));

                    break;
                }

                var bytes = nameBuffer.Skip(i + 1).Take(length).ToArray();
                sb.Append(Encoding.UTF8.GetString(bytes));
                sb.Append(".");
                i += (length + 1);
            }

            return sb.ToString().TrimEnd('.');
        }
    }
}
