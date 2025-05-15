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

                NetworkInterface selectedNic = null;
                //IPv6InterfaceProperties selectedInterface = null;
                IPv4InterfaceProperties selectedInterface = null;

                var localIP = GetLocalIPAddress();

                foreach (NetworkInterface adapter in nics)
                {
                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    Console.WriteLine(adapter.Description);
                    Console.WriteLine(String.Empty.PadLeft(adapter.Description.Length, '='));
                    Console.WriteLine("  Interface type .......................... : {0}", adapter.NetworkInterfaceType);
                    Console.WriteLine("  Physical Address ........................ : {0}", adapter.GetPhysicalAddress().ToString());
                    Console.WriteLine("  Minimum Speed............................ : {0}", adapter.Speed);
                    Console.WriteLine("  Is receive only.......................... : {0}", adapter.IsReceiveOnly);
                    Console.WriteLine("  Multicast................................ : {0}", adapter.SupportsMulticast);
                    Console.WriteLine();
                }

                foreach (NetworkInterface adapter in nics)
                {
                    IPInterfaceProperties ip_properties = adapter.GetIPProperties();

                    //if(adapter.NetworkInterfaceType != NetworkInterfaceType.Ethernet)
                    //{
                    //    continue;
                    //}

                    //if (adapter.GetIPProperties().MulticastAddresses.Count == 0)
                    //{
                    //    continue; // most of VPN adapters will be skipped
                    //}

                    //if (OperationalStatus.Up != adapter.OperationalStatus)
                    //{
                    //    continue; // this adapter is off or not connected
                    //}

                    //IPv6InterfaceProperties p = adapter.GetIPProperties().GetIPv6Properties();

                    //if (null == p)
                    //{
                    //    continue; // IPv6 is not configured on this adapter
                    //}

                    IPv4InterfaceProperties p = adapter.GetIPProperties().GetIPv4Properties();

                    if (null == p)
                    {
                        continue; // IPv4 is not configured on this adapter
                    }

                    //foreach (UnicastIPAddressInformation ip in adapter.GetIPProperties().UnicastAddresses)
                    //{
                    //    if (ip.Address.AddressFamily == AddressFamily.InterNetwork && ip.Address.ToString() == localIP)
                    //    {
                    //        selectedInterface = p;
                    //        selectedNic = adapter;  // Return the Network Interface found
                    //        break;
                    //    }
                    //}

                    if (adapter.Description == "Hyper-V Virtual Ethernet Adapter #3")
                    {
                        selectedInterface = p;
                        selectedNic = adapter;

                        break;
                    }
                }

                var addressBytes = selectedNic.GetPhysicalAddress().GetAddressBytes();

                var address = BitConverter.ToString(addressBytes).Replace("-", "");
                var hostname = Dns.GetHostName();

                Console.WriteLine($"Bound to {selectedNic.Description.ToString()}");
                Console.WriteLine($"Bound to {selectedInterface.ToString()}");
                Console.WriteLine($"Address is {address.ToString()}");
                Console.WriteLine($"Hostname is {hostname.ToString()}");

                //IPAddress multicastAddress = IPAddress.Parse("FF02::FB");
                //IPEndPoint multicastEndpoint = new IPEndPoint(multicastAddress, 5353);
                //EndPoint localEndpoint = new IPEndPoint(IPAddress.IPv6Any, 5353);
                //EndPoint senderRemote = new IPEndPoint(IPAddress.IPv6Any, 0);

                //using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

                //socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
                //socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, 1);

                //socket.Bind(localEndpoint);

                //socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(multicastAddress));

                //using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                //{
                //var localAddress = IPAddress.Parse("192.168.1.52");

                IPAddress multicastAddress = IPAddress.Parse("224.0.0.251");
                IPEndPoint multicastEndpoint = new IPEndPoint(multicastAddress, 5353);
                EndPoint localEndpoint = new IPEndPoint(IPAddress.Any, 5353);
                EndPoint senderRemote = new IPEndPoint(IPAddress.Any, 0);



                var udpclient = new UdpClient();
                udpclient.ExclusiveAddressUse = false;
                udpclient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                int adapterIndex = selectedNic.GetIPProperties().GetIPv4Properties().Index;
                udpclient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.HostToNetworkOrder(adapterIndex));
                //udpclient.JoinMulticastGroup(multicastAddress);
                udpclient.Client.Bind(localEndpoint);

                var multOpt = new MulticastOption(multicastAddress, adapterIndex);
                udpclient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, multOpt);

                //socket.ExclusiveAddressUse = false;

                //socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                //socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, (int)IPAddress.HostToNetworkOrder(selectedInterface.Index));

                //socket.Bind(localEndpoint);

                //socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(multicastAddress));

                IPAddress ipAddress = IPAddress.Parse(((IPEndPoint)udpclient.Client.LocalEndPoint).Address.ToString());

                Console.WriteLine("Bound to {0}", ipAddress);


                while (true)
                {
                    try
                    {
                        Console.WriteLine("Waiting for incoming data...");

                        var buffer = new byte[2048];
                        int numberOfbytesReceived = udpclient.Client.ReceiveFrom(buffer, ref senderRemote);

                        var content = new byte[numberOfbytesReceived];
                        Array.Copy(buffer, 0, content, 0, numberOfbytesReceived);

                        var contentSpan = content.AsSpan();

                        // Start reading the header.
                        //
                        var id = BitConverter.ToInt16(contentSpan.Slice(0, 2));

                        var flags = contentSpan.Slice(2, 1);

                        var queryResponseBit = flags[0] >> 7;
                        var isQuery = queryResponseBit == 0x00;

                        if (!isQuery)
                        {
                            Console.WriteLine("Not a query. Ignoring.");
                            continue;
                        }

                        var queryResponseStatus = isQuery ? "Query" : "Response";

                        var questions = new List<string>();

                        Console.WriteLine("───────────────── START REQUEST ─────────────────");
                        Console.WriteLine($"Request received with ID: {id}");

                        Console.WriteLine($"Id: {id}");
                        Console.WriteLine($"Query/Response: {queryResponseStatus}");

                        var queryQuestionCount = ReadBigEndianUShort(contentSpan, 4); ;
                        Console.WriteLine($"QuestionCount: {queryQuestionCount}");

                        var queryAnswerCount = ReadBigEndianUShort(contentSpan, 6);
                        Console.WriteLine($"AnswerCount: {queryAnswerCount}");

                        ByteArrayToStringDump(content);

                        var resourceRecordsSpan = contentSpan.Slice(12);

                        int resourceRecordsSpanIndex = 0;

                        for (int i = 0; i < queryQuestionCount; i++)
                        {
                            var resourceRecordSpan = resourceRecordsSpan.Slice(resourceRecordsSpanIndex);

                            var index = 0;

                            var nameBuffer = new byte[0];

                            // Work through the first few bytes. These represent the name. 
                            // There is a null terminator.
                            //
                            foreach (var b in resourceRecordSpan)
                            {
                                nameBuffer = nameBuffer.Concat([b]).ToArray();

                                if (b == 0x00)
                                {
                                    break;
                                }
                            }

                            var name = DecodeName(nameBuffer);

                            Console.WriteLine("Name: {0} [{1}]", name, name.Length);
                            index += nameBuffer.Length;

                            var type = ReadBigEndianUShort(resourceRecordSpan, index);
                            Console.WriteLine("Type: {0}", ConvertTypeToDescription(type));
                            index += 2;

                            var @class = ReadBigEndianUShort(resourceRecordSpan, index);
                            Console.WriteLine("Class: {0}", ReadBigEndianUShort(resourceRecordSpan, index));
                            index += 2;

                            //index += 4;
                            //Console.WriteLine("TTL: {0} ", name, name.Length);
                            //var rdLength = ReadBigEndianUShort(resourceRecordSpan, index);
                            //index += 2;
                            //Console.WriteLine("RD Length: {0} [{1}]", name, name.Length);

                            //index += rdLength;
                            //Console.WriteLine("RD Data: {0} [{1}]", name, name.Length);

                            if (type == 12)
                            {
                                questions.Add(name);
                            }

                            resourceRecordsSpanIndex += index;
                        }

                        for (int i = 0; i < queryAnswerCount; i++)
                        {
                            var resourceRecordSpan = resourceRecordsSpan.Slice(resourceRecordsSpanIndex);

                            var index = 0;

                            var nameBuffer = new byte[0];

                            foreach (var b in resourceRecordSpan)
                            {
                                nameBuffer = nameBuffer.Concat([b]).ToArray();

                                if (b == 0x00)
                                {
                                    break;
                                }
                            }

                            var name = Encoding.UTF8.GetString(nameBuffer);

                            //Console.WriteLine("Name: {0} [{1}]", name, name.Length);
                            index += nameBuffer.Length;

                            var type = ReadBigEndianUShort(resourceRecordSpan, index);
                            //Console.WriteLine("Type: {0}", type);
                            index += 2;

                            var @class = ReadBigEndianUShort(resourceRecordSpan, index);
                            //Console.WriteLine("Class: {0}", ReadBigEndianUShort(resourceRecordSpan, index));
                            index += 2;

                            if (type == 12)
                            {
                                var ttl = ReadBigEndianUInt(resourceRecordSpan, index);
                                index += 4;
                                //Console.WriteLine("TTL: {0} ", ttl);

                                var rdLength = ReadBigEndianUShort(resourceRecordSpan, index);
                                index += 2;
                                //Console.WriteLine("RD Length: {0}", rdLength);

                                var rdDataBytes = resourceRecordSpan.Slice(index, rdLength);
                                var rdData = Encoding.UTF8.GetString(rdDataBytes.ToArray());
                                index += rdLength;
                                //Console.WriteLine("RD Data: {0}", rdData);
                            }

                            resourceRecordsSpanIndex += index;
                        }

                        Console.WriteLine("──────────────────── END REQUEST ────────────────────");

                        Console.WriteLine();

                        // Build the header that indicates this is a response.
                        //
                        var outputBuffer = new byte[0];

                        var reponseId = new byte[2];
                        var responseHeaderFlags = new byte[2];

                        var bitArray = new BitArray(responseHeaderFlags);

                        // We're using 15 and 10 since the Endianness of this bytes is reversed :)
                        //
                        bitArray.Set(15, true); // QR
                        bitArray.Set(10, true); // AA

                        bitArray.CopyTo(responseHeaderFlags, 0);

                        var questionCountBytes = BitConverter.GetBytes((ushort)0).Reverse().ToArray();
                        var answerCountBytes = BitConverter.GetBytes((ushort)5).Reverse().ToArray();
                        var additionalCounts = BitConverter.GetBytes((ushort)0).Reverse().ToArray();
                        var otherCounts = BitConverter.GetBytes((ushort)0).Reverse().ToArray();

                        // Add the header to the output buffer.
                        //
                        outputBuffer = outputBuffer.Concat(reponseId).Concat(responseHeaderFlags).Concat(questionCountBytes).Concat(answerCountBytes).Concat(additionalCounts).Concat(otherCounts).ToArray();

                        if (questions.Contains("_services._dns-sd._udp.local"))
                        {
                            outputBuffer = AddPtr(outputBuffer, "_services._dns-sd._udp.local", "_matterc._udp.local");
                            outputBuffer = AddTxt(outputBuffer, $"_services._dns-sd._udp.local", new Dictionary<string, string>());
                        }

                        if (questions.Contains("_matterc._udp.local"))
                        {
                            outputBuffer = AddPtr(outputBuffer, "_matterc._udp.local", $"TOMAS._matterc._udp.local");

                            outputBuffer = AddPtr(outputBuffer, "_matterc._udp.local", $"TOMAS._matterc._udp.local");
                            outputBuffer = AddSrv(outputBuffer, $"TOMAS._matterc._udp.local", 0, 0, 51826, hostname);

                            Dictionary<string, string> values = new Dictionary<string, string>();
                            values.Add("CM", "1");
                            values.Add("D", "3840");
                            values.Add("DN", "C# mDNS Test");

                            outputBuffer = AddTxt(outputBuffer, $"TOMAS._matterc._udp.local", values);
                            outputBuffer = AddARecord(outputBuffer, $"TOMAS.local", "AAAA", ipAddress.ToString());
                        }

                        ByteArrayToStringDump(outputBuffer);

                        var bytesSent = udpclient.Client.SendTo(outputBuffer, 0, outputBuffer.Length, SocketFlags.None, senderRemote);

                        Console.WriteLine($"Wrote {bytesSent}");
                    }
                    catch (Exception exp)
                    {
                        Console.WriteLine(exp.Message);
                    }
                }
                //}
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

        private object ReadBigEndianUInt(Span<byte> data, int index)
        {
            var bytes = data.Slice(index, 4);
            bytes.Reverse();
            return BitConverter.ToUInt32(bytes);
        }

        private string ConvertTypeToDescription(ushort type)
        {
            switch (type)
            {
                case 1:
                    return "A";
                case 12:
                    return "PTR";
                case 16:
                    return "TXT";
                case 28:
                    return "AAAA";
                default:
                    return "UNKNOWN";
            }
        }

        private ushort ReadBigEndianUShort(Span<byte> data, int index)
        {
            var bytes = data.Slice(index, 2);
            bytes.Reverse();
            return BitConverter.ToUInt16(bytes);
        }

        private byte[] AddARecord(byte[] outputBuffer, string hostName, string type, string ipAddress)
        {
            var nodeName = EncodeName(hostName);

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
            var parts = ipAddress.Split(':');

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

        private byte[] AddSrv(byte[] outputBuffer, string host, short priority, short weight, int port, string hostname)
        {
            var nodeName = EncodeName(host);

            outputBuffer = outputBuffer.Concat(nodeName).ToArray();

            var type = BitConverter.GetBytes((short)33).Reverse().ToArray(); // SRV

            outputBuffer = outputBuffer.Concat(type).ToArray();

            var @class = BitConverter.GetBytes((short)1).Reverse().ToArray(); // Internet
            @class[0] = @class[0].SetBit(7); // set flush to true

            outputBuffer = outputBuffer.Concat(@class).ToArray();

            var ttl = BitConverter.GetBytes(120).Reverse().ToArray();

            outputBuffer = outputBuffer.Concat(ttl).ToArray();

            var svrName = EncodeName(hostname);

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
            var nodeName = EncodeName(v);

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
            var ptrNodeName = EncodeName(v1);

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

        public static void ByteArrayToStringDump(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);

            int count = 0;

            foreach (byte b in ba)
            {
                Console.Write(b.ToString("x2"));
                Console.Write(" ");

                count++;

                if (count % 2 == 0)
                {
                    Console.Write("  ");
                }
            }

            Console.Write("\n");
        }

        private byte[] EncodeName(string v)
        {
            var parts = v.Split('.');

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

        private string DecodeName(byte[] nameBuffer)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < nameBuffer.Length; i += 0)
            {
                var length = nameBuffer[i];

                if (length == 0x00)
                {
                    break;
                }

                var bytes = nameBuffer.Skip(i + 1).Take(length).ToArray();
                sb.Append(Encoding.UTF8.GetString(bytes));
                sb.Append(".");
                i += length + 1;
            }

            return sb.ToString().TrimEnd('.');
        }

        static string GetLocalIPAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 80); // Connect to google or any other site
                    return (socket.LocalEndPoint as System.Net.IPEndPoint)?.Address.ToString();
                }
            }
            catch
            {
                return string.Empty;
            }
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
