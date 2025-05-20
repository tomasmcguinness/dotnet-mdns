using System;
using System.Collections.Generic;
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
            var queryQuestionCount = BitConverter.ToUInt16(reversedQuestionCountBytes.ToArray());

            var answerCountBytes = messageSpan.Slice(6, 2).ToArray();
            var reversedAnswerCountBytes = questionCountBytes.Reverse();
            var queryAnswerCount = BitConverter.ToUInt16(reversedAnswerCountBytes.ToArray());

            // Ignore Authority Records (NSCOUNT) and Additional Records (ARCOUNT) for now.
            //

            // DNS Header is 12 bytes long.
            //
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
                contentIndex += (nameEndIndex + 1);

                var type = BitConverter.ToUInt16(messageSpan.Slice(contentIndex, 2));
                contentIndex += 2;

                var @class = BitConverter.ToUInt16(messageSpan.Slice(contentIndex, 2));
                contentIndex += 2;

                // Question records don't have TTL & RData.
                //

                // Add the name to the list of questions being asked.
                //
                Questions.Add(name);
            }
        }

        public ushort Id { get; }

        public bool IsQuery { get; }

        public List<string> Questions { get; } = new List<string>();

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
