using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Core
{
    public class mDNSService
    {
        public void Start()
        {
            try
            {
                var signal = new ManualResetEvent(false);

                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();

                IPv6InterfaceProperties selectedInterface = null;

                foreach (NetworkInterface adapter in nics)
                {
                    IPInterfaceProperties ip_properties = adapter.GetIPProperties();

                    if (adapter.GetIPProperties().MulticastAddresses.Count == 0)
                    {
                        continue; // most of VPN adapters will be skipped
                    }

                    if (OperationalStatus.Up != adapter.OperationalStatus)
                    {
                        continue; // this adapter is off or not connected
                    }

                    IPv6InterfaceProperties p = adapter.GetIPProperties().GetIPv6Properties();

                    if (null == p)
                    {
                        continue; // IPv6 is not configured on this adapter
                    }

                    selectedInterface = p;

                    break;
                }

                Console.WriteLine($"Bound to {selectedInterface.ToString()}");

                IPAddress multicastAddress = IPAddress.Parse("FF02::FB");
                IPEndPoint multicastEndpoint = new IPEndPoint(multicastAddress, 5353);
                EndPoint localEndpoint = new IPEndPoint(IPAddress.IPv6Any, 5353);
                EndPoint senderRemote = new IPEndPoint(IPAddress.IPv6Any, 0);

                using (var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, true);

                    socket.Bind(localEndpoint);

                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(multicastAddress));

                    while (true)
                    {
                        try
                        {
                            var buffer = new byte[2048];
                            int numberOfbytesReceived = socket.ReceiveFrom(buffer, ref senderRemote);

                            var content = new byte[numberOfbytesReceived];
                            Array.Copy(buffer, 0, content, 0, numberOfbytesReceived);

                            ByteArrayToStringDump(content);

                            if (content[2] != 0x00)
                            {
                                Console.WriteLine("Not a query. Ignoring.");
                                continue;
                            }

                            // Build the header that indicates this is a response.
                            //
                            var outputBuffer = new byte[0];

                            var flags = new byte[2];

                            var bitArray = new BitArray(flags);

                            // We're using 15 and 10 since the Endianness of this bytes is reversed :)
                            //
                            bitArray.Set(15, true); // QR
                            bitArray.Set(10, true); // AA

                            bitArray.CopyTo(flags, 0);

                            var questionCount = BitConverter.GetBytes((short)1).Reverse().ToArray();
                            var answerCount = BitConverter.GetBytes((short)4).Reverse().ToArray();
                            var additionalCounts = BitConverter.GetBytes((short)1).Reverse().ToArray();
                            var otherCounts = BitConverter.GetBytes((short)0).Reverse().ToArray();

                            // Add the header to the output buffer.
                            //
                            outputBuffer = outputBuffer.Concat(otherCounts).Concat(flags.Reverse()).Concat(questionCount).Concat(answerCount).Concat(otherCounts).Concat(additionalCounts).ToArray();

                            int endOfHeaderBufferLength = outputBuffer.Length;

                            var nodeName = GetName("_services._dns-sd._udp.local");

                            outputBuffer = outputBuffer.Concat(nodeName).ToArray();

                            var typeBytes = BitConverter.GetBytes((short)12).Reverse().ToArray(); // PTR

                            outputBuffer = outputBuffer.Concat(typeBytes).ToArray();

                            var @class = BitConverter.GetBytes((short)1).Reverse().ToArray(); // Internet

                            outputBuffer = outputBuffer.Concat(@class).ToArray();

                            Dictionary<string, string> values = new Dictionary<string, string>();
                            values.Add("sf", "1");
                            values.Add("ff", "0x00");
                            values.Add("ci", "2");
                            values.Add("id", "C5:22:3D:E3:CE:D6");
                            values.Add("md", "Climenole");
                            values.Add("s#", "1");
                            values.Add("c#", "678");

                            outputBuffer = AddTxt(outputBuffer, "Jarvis._hap._tcp.local", values);
                            outputBuffer = AddPtr(outputBuffer, "_services._dns-sd._udp.local", "_hap._tcp.local");
                            outputBuffer = AddPtr(outputBuffer, "_hap._tcp.local", "Jarvis._hap._tcp.local");
                            outputBuffer = AddSrv(outputBuffer, "Jarvis._hap._tcp.local", 0, 0, 51826, "Surface.local");

                            //outputBuffer = AddARecord(outputBuffer, "Surface.local", "A", ipAddress);

                            ByteArrayToStringDump(outputBuffer);

                            Thread.Sleep(50);

                            var bytesSent = socket.SendTo(outputBuffer, 0, outputBuffer.Length, SocketFlags.None, senderRemote);

                            Console.WriteLine($"Wrote {bytesSent}");
                        }
                        catch (Exception exp)
                        {
                            Console.WriteLine(exp.Message);
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                Console.WriteLine(exp.Message);

                if (exp.InnerException != null)
                {
                    Console.WriteLine(exp.InnerException.Message);

                }
            }
        }

        private byte[] AddARecord(byte[] outputBuffer, string hostName, string type, string ipAddress)
        {
            var nodeName = GetName(hostName);

            outputBuffer = outputBuffer.Concat(nodeName).ToArray();

            var typeBytes = BitConverter.GetBytes((short)1).Reverse().ToArray(); // A

            outputBuffer = outputBuffer.Concat(typeBytes).ToArray();

            var @class = BitConverter.GetBytes((short)1).Reverse().ToArray(); // Internet
            @class[0] = @class[0].SetBit(7); // set flush to true

            outputBuffer = outputBuffer.Concat(@class).ToArray();

            var ttl = BitConverter.GetBytes(120).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(ttl).ToArray();

            var address = ConvertIpAddress(ipAddress);

            // For IP4, this will be an int32
            var dataLength = BitConverter.GetBytes((short)address.Length).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(dataLength).ToArray();

            outputBuffer = outputBuffer.Concat(address).ToArray();

            //outputBuffer = outputBuffer.Concat(new byte[2] { 0xC0, 0x0C }).ToArray();

            return outputBuffer;
        }

        private byte[] ConvertIpAddress(string ipAddress)
        {
            var parts = ipAddress.Split('.');

            byte[] result = new byte[4];

            int index = 0;

            foreach (var part in parts)
            {
                result[index++] = byte.Parse(part);
            }

            return result;
        }

        private byte[] AddSrv(byte[] outputBuffer, string host, short priority, short weight, int port, string v4)
        {
            var nodeName = GetName(host);

            outputBuffer = outputBuffer.Concat(nodeName).ToArray();

            var type = BitConverter.GetBytes((short)33).Reverse().ToArray(); // SRV

            outputBuffer = outputBuffer.Concat(type).ToArray();

            var @class = BitConverter.GetBytes((short)1).Reverse().ToArray(); // Internet
            @class[0] = @class[0].SetBit(7); // set flush to true

            outputBuffer = outputBuffer.Concat(@class).ToArray();

            var ttl = BitConverter.GetBytes(120).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(ttl).ToArray();

            var svrName = GetName(v4);

            int totalLength = svrName.Length + 2 + 2 + 2; // name + priority + weight + port

            var dataLength = BitConverter.GetBytes((short)totalLength).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(dataLength).ToArray();

            var priorityBytes = BitConverter.GetBytes(priority).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(priorityBytes).ToArray();

            var weightBytes = BitConverter.GetBytes(weight).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(weightBytes).ToArray();

            var portBytes = BitConverter.GetBytes((short)port).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(portBytes).ToArray();

            outputBuffer = outputBuffer.Concat(svrName).ToArray();

            return outputBuffer;
        }

        private byte[] AddTxt(byte[] outputBuffer, string v, Dictionary<string, string> values)
        {
            var nodeName = GetName(v);

            outputBuffer = outputBuffer.Concat(nodeName).ToArray();

            var type = BitConverter.GetBytes((short)16).Reverse().ToArray(); // TXT

            outputBuffer = outputBuffer.Concat(type).ToArray();

            var @class = BitConverter.GetBytes((short)1).Reverse().ToArray(); // Internet
            @class[0] = @class[0].SetBit(7); // set flush to true

            outputBuffer = outputBuffer.Concat(@class).ToArray();

            var ttl = BitConverter.GetBytes(120).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(ttl).ToArray();

            var txtRecord = GetTxtRecord(values);

            var recordLength = BitConverter.GetBytes((short)txtRecord.Length).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(recordLength).ToArray();

            outputBuffer = outputBuffer.Concat(txtRecord).ToArray();

            return outputBuffer;
        }

        private byte[] AddPtr(byte[] outputBuffer, string v1, string v2)
        {
            var ptrNodeName = GetName(v1);

            outputBuffer = outputBuffer.Concat(ptrNodeName).ToArray();

            var type = BitConverter.GetBytes((short)12).Reverse().ToArray(); // PTR

            outputBuffer = outputBuffer.Concat(type).ToArray();

            var @class = BitConverter.GetBytes((short)1).Reverse().ToArray(); // Internet
            //@class[0] = @class[0].SetBit(7); // set flush to true

            outputBuffer = outputBuffer.Concat(@class).ToArray();

            var ttl = BitConverter.GetBytes(120).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(ttl).ToArray();

            var ptrServiceName = GetName(v2);

            var recordLength = BitConverter.GetBytes((short)ptrServiceName.Length).Reverse().ToArray();

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

        public static void ByteArrayToStringDump(byte[] ba)
        {
            Console.WriteLine("***************************");

            StringBuilder hex = new StringBuilder(ba.Length * 2);

            int count = 0;

            foreach (byte b in ba)
            {
                Console.Write(b.ToString("x2"));
                Console.Write(" ");

                count++;

                if (count % 8 == 0)
                {
                    Console.Write("  ");
                }

                if (count % 16 == 0)
                {
                    Console.WriteLine();
                }
            }

            Console.WriteLine();
            Console.WriteLine("***************************");
        }

        private byte[] GetName(string v)
        {
            var parts = v.Split('.');

            var result = new byte[0];

            foreach (var part in parts)
            {
                int length = part.Length;
                byte lengthByte = Convert.ToByte(length);
                result = result.Concat(new byte[1] { lengthByte }).Concat(Encoding.UTF8.GetBytes(part)).ToArray();
            }

            // Null terminator.
            //
            return result.Concat(new byte[1] { 0x00 }).ToArray();
        }

        private void WriteAsNewLineHexToConsole(byte[] buffer, string description)
        {
            foreach (byte b in buffer)
            {
                Console.Write(b.ToString("X2"));
                Console.Write(" ");
            }

            Console.Write(description);

            Console.WriteLine();
        }
    }

    public static class ByteExtensions
    {
        public static bool IsBitSet(this byte b, int pos)
        {
            if (pos < 0 || pos > 7)
                throw new ArgumentOutOfRangeException("pos", "Index must be in the range of 0-7.");

            return (b & (1 << pos)) != 0;
        }

        public static byte SetBit(this byte b, int pos)
        {
            if (pos < 0 || pos > 7)
                throw new ArgumentOutOfRangeException("pos", "Index must be in the range of 0-7.");

            return (byte)(b | (1 << pos));
        }

        public static string ToBinaryString(this byte b)
        {
            return Convert.ToString(b, 2).PadLeft(8, '0');
        }
    }
}
