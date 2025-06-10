using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace mDNS.Core
{
    internal class Serialization
    {
        public static byte[] EncodeName(string name)
        {
            var parts = name.Split('.');

            var result = new byte[0];

            foreach (var part in parts)
            {
                int length = part.Length;
                byte lengthByte = Convert.ToByte(length);
                result = result.Concat([lengthByte]).Concat(Encoding.UTF8.GetBytes(part)).ToArray();
            }

            // Add a null terminator.
            //
            return result.Concat(new byte[1] { 0x00 }).ToArray();
        }

        public static string DecodeName(ReadOnlySpan<byte> nameSpan, ReadOnlySpan<byte> messageSpan)
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

        public static byte[] EncodeTextValues(Dictionary<string, string> values)
        {
            var result = new byte[0];

            foreach (var keypair in values)
            {
                string fullKeyPair = $"{keypair.Key}={keypair.Value}";
                result = result.Concat(new byte[1] { (byte)fullKeyPair.Length }).Concat(Encoding.UTF8.GetBytes(fullKeyPair)).ToArray();
            }

            return result;
        }

        public static byte[] EncodeIPv4Address(string ipAddress)
        {
            return EncodeIPAddress(ipAddress, '.');
        }

        public static byte[] EncodeIPAddress(string ipAddress, char separator)
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

        public static byte[] EncodeService(ushort priority, ushort weight, ushort port, string hostname)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            var priorityBytes = BitConverter.GetBytes(priority).Reverse().ToArray();
            writer.Write(priorityBytes);

            var weightBytes = BitConverter.GetBytes(weight).Reverse().ToArray();
            writer.Write(weightBytes);

            var portBytes = BitConverter.GetBytes(port).Reverse().ToArray();
            writer.Write(portBytes);

            var encodedHostname = EncodeName(hostname);

            writer.Write(encodedHostname.ToArray());

            return ms.ToArray();
        }

        public static (ushort port, string hostname) DecodeService(byte[] rdDataBytes, ReadOnlySpan<byte> messageSpan)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            var rdSpan = rdDataBytes.AsSpan();

            var priorityBytes = rdSpan.Slice(0, 2);
            var weightBytes = rdSpan.Slice(2, 2);
            var portBytes = rdSpan.Slice(4, 2);

            DecodeName()

            return (10, "tom");
        }

        internal static IPAddress DecodeARecord(byte[] recordData, ReadOnlySpan<byte> messageSpan)
        {
            throw new NotImplementedException();
        }
    }
}
