using System;
using System.Collections.Generic;
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

            //var queryResponseBit = flags..Slice(2, 2);

            var isQuery = true; // queryResponseBit == 0x00;

            RequestType = isQuery ? RequestType.Query : RequestType.Response;

            var queryResponseStatus = isQuery ? "Query" : "Response";

            var questions = new List<string>();

            var queryQuestionCount = BitConverter.ToUInt16(messageSpan.Slice(4, 2));
            Console.WriteLine($"QuestionCount: {queryQuestionCount}");

            var queryAnswerCount = BitConverter.ToUInt16(messageSpan.Slice(6, 2));
            Console.WriteLine($"AnswerCount: {queryAnswerCount}");

            var contentIndex = 12;

            for (int i = 0; i < queryQuestionCount; i++)
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
                Console.WriteLine("Name: {0} [{1}]", name, name.Length);
                contentIndex += (nameEndIndex + 1);

                var type = BitConverter.ToUInt16(messageSpan.Slice(contentIndex, 2));
                //Console.WriteLine("Type: {0}", ConvertTypeToDescription(type));
                contentIndex += 2;

                var @class = BitConverter.ToUInt16(messageSpan.Slice(contentIndex, 2));
                //Console.WriteLine("Class: {0}", @class);
                contentIndex += 2;

                // Question records don't have TTL & RData.
                //

                // Add the name to the list of questions being asked.
                //
                questions.Add(name);
            }
        }

        public ushort Id { get; set; }

        public RequestType RequestType { get; set; }

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
