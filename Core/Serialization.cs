using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Core
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

            // Null terminator.
            //
            return result.Concat(new byte[1] { 0x00 }).ToArray();
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
    }
}
