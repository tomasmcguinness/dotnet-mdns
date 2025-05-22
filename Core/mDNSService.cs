using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core
{
    public class mDNSService
    {
        public delegate void RecordDiscoveredDelegate(object sender, Record[] record);
        public event RecordDiscoveredDelegate RecordDiscovered;

        private bool _isThreadRunning = true;
        private Thread _thread;

        private List<string> _questionsAsked = new List<string>();

        public async Task Perform(Discovery discovery)
        {
            _questionsAsked.Clear();
            _questionsAsked.Add(discovery.Name);

            var threadStart = new ThreadStart(Start);
            _thread = new Thread(Start);
            _thread.Start();

            // Give it time to finish. TODO Pass this in as an argument.
            //
            await Task.Delay(30000);

            _isThreadRunning = false;

            _thread.Join();
        }

        private void Start()
        {
            try
            {
                var signal = new ManualResetEvent(false);

                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();

                NetworkInterface selectedNic = null;
                //IPv6InterfaceProperties selectedInterface = null;
                IPv4InterfaceProperties selectedInterface = null;

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
                udpclient.Client.Bind(localEndpoint);

                var multOpt = new MulticastOption(multicastAddress, adapterIndex);
                udpclient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, multOpt);

                IPAddress ipAddress = IPAddress.Parse(((IPEndPoint)udpclient.Client.LocalEndPoint).Address.ToString());

                Console.WriteLine("Bound to {0}", ipAddress);

                if (_questionsAsked.Any())
                {
                    DNSMessage initialQuery = new DNSMessage(false);

                    foreach (var q in _questionsAsked)
                    {
                        initialQuery.AddQuery(q, RecordType.PTR, RecordClass.Internet);
                    }

                    var outputBuffer = initialQuery.GetBytes();

                    var bytesSent = udpclient.Client.SendTo(outputBuffer, 0, outputBuffer.Length, SocketFlags.None, multicastEndpoint);

                    Console.WriteLine($"Send Initial Query [{bytesSent}] to [{multicastEndpoint}]");
                }

                while (_isThreadRunning)
                {
                    try
                    {
                        Console.WriteLine("Waiting for incoming data...");

                        var buffer = new byte[2028];
                        int numberOfbytesReceived = udpclient.Client.ReceiveFrom(buffer, ref senderRemote);

                        if (senderRemote == localEndpoint)
                        {
                            continue;
                        }

                        var content = new byte[numberOfbytesReceived];
                        Array.Copy(buffer, 0, content, 0, numberOfbytesReceived);

                        var request = new DNSMessage(content);

                        Console.WriteLine("─────────────────── BEGIN REQUEST ────────────────────");
                        Console.WriteLine(request);
                        Console.WriteLine("──────────────────── END REQUEST ────────────────────");

                        if (request.IsQuery)
                        {
                            var response = new DNSMessage(true);

                            // Copy the original queries into the response.
                            //
                            foreach (var query in request.Queries)
                            {
                                response.Queries.Add(query);
                            }

                            // Add our answers
                            //
                            Dictionary<string, string> values = new Dictionary<string, string>();

                            if (request.Queries.Any(q => q.Name == "_services._dns-sd._udp.local"))
                            {
                                response.AddPointerAnswer("_services._dns-sd._udp.local", "_matter._udp.local");
                                response.AddTextAnswer("_services._dns-sd._udp.local", values);
                            }

                            // Add the header to the output buffer.
                            //
                            if (request.Queries.Any(q => q.Name == "_matterc._udp.local"))
                            {
                                response.AddPointerAnswer("_matter._udp.local", "TOMAS._matter._udp.local");

                                values = new Dictionary<string, string>();
                                values.Add("CM", "1");
                                values.Add("D", "3840");
                                values.Add("DN", "C# mDNS Test");

                                response.AddPointerAnswer("_matter._udp.local", $"TOMAS._matter._udp.local");
                                response.AddServiceAnswer($"TOMAS._matter._udp.local", 0, 0, 51826, hostname);
                                response.AddTextAnswer($"TOMAS._matter._udp.local", values);
                                response.AddARecordAnswer($"TOMAS.local", ipAddress);
                            }

                            var outputBuffer = response.GetBytes();

                            ByteArrayToStringDump(outputBuffer);

                            var bytesSent = udpclient.Client.SendTo(outputBuffer, 0, outputBuffer.Length, SocketFlags.None, senderRemote);

                            Console.WriteLine($"Send {bytesSent} to {senderRemote}");
                        }
                        else
                        {
                            // Response received.
                            // Check if this response includes the questions we asked.
                            // 
                            var firstQuestion = _questionsAsked.First();

                            if (request.Queries.Any(q => q.Name == firstQuestion))
                            {
                                Console.WriteLine($"Response with query {firstQuestion} was received");
                                RecordDiscovered?.Invoke(this, request.Answers.ToArray());
                            }
                        }
                    }
                    catch (Exception exp)
                    {
                        Console.WriteLine(exp.Message);
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

        //private byte[] AddARecord(byte[] outputBuffer, string hostName, string ipAddress)
        //{
        //    var nodeName = EncodeName(hostName);

        //    outputBuffer = outputBuffer.Concat(nodeName).ToArray();

        //    var typeBytes = BitConverter.GetBytes((short)1).Reverse().ToArray(); // A

        //    outputBuffer = outputBuffer.Concat(typeBytes).ToArray();

        //    var @class = BitConverter.GetBytes((short)1).Reverse().ToArray(); // Internet
        //    @class[0] = @class[0].SetBit(7); // set flush to true

        //    outputBuffer = outputBuffer.Concat(@class).ToArray();

        //    var ttl = BitConverter.GetBytes(120).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(ttl).ToArray();

        //    var address = ConvertIPv4Address(ipAddress);

        //    // For IP4, this will be an int32
        //    //
        //    var dataLength = BitConverter.GetBytes((short)address.Length).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(dataLength).ToArray();

        //    outputBuffer = outputBuffer.Concat(address).ToArray();

        //    return outputBuffer;
        //}

        private byte[] ConvertIPv4Address(string ipAddress)
        {
            return ConvertIPAddress(ipAddress, '.');
        }

        private byte[] ConvertIPAddress(string ipAddress, char separator)
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


        //private byte[] AddSrv(byte[] outputBuffer, string host, short priority, short weight, int port, string hostname)
        //{
        //    var nodeName = Utilities.EncodeName(host);

        //    outputBuffer = outputBuffer.Concat(nodeName).ToArray();

        //    var type = BitConverter.GetBytes((short)33).Reverse().ToArray(); // SRV

        //    outputBuffer = outputBuffer.Concat(type).ToArray();

        //    var @class = BitConverter.GetBytes((short)1).Reverse().ToArray(); // Internet
        //    @class[0] = @class[0].SetBit(7); // set flush to true

        //    outputBuffer = outputBuffer.Concat(@class).ToArray();

        //    var ttl = BitConverter.GetBytes(120).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(ttl).ToArray();

        //    var svrName =   EncodeName(hostname);

        //    int totalLength = svrName.Length + 2 + 2 + 2; // name + priority + weight + port

        //    var dataLength = BitConverter.GetBytes((short)totalLength).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(dataLength).ToArray();

        //    var priorityBytes = BitConverter.GetBytes(priority).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(priorityBytes).ToArray();

        //    var weightBytes = BitConverter.GetBytes(weight).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(weightBytes).ToArray();

        //    var portBytes = BitConverter.GetBytes((short)port).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(portBytes).ToArray();

        //    outputBuffer = outputBuffer.Concat(svrName).ToArray();

        //    return outputBuffer;
        //}

        //private byte[] AddTxt(byte[] outputBuffer, string v, Dictionary<string, string> values)
        //{
        //    var nodeName = Utilities.EncodeName(v);

        //    outputBuffer = outputBuffer.Concat(nodeName).ToArray();

        //    var type = BitConverter.GetBytes((short)16).Reverse().ToArray(); // TXT

        //    outputBuffer = outputBuffer.Concat(type).ToArray();

        //    var @class = BitConverter.GetBytes((short)1).Reverse().ToArray(); // Internet
        //    @class[0] = @class[0].SetBit(7); // set flush to true

        //    outputBuffer = outputBuffer.Concat(@class).ToArray();

        //    var ttl = BitConverter.GetBytes(120).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(ttl).ToArray();

        //    var txtRecord = GetTxtRecord(values);

        //    var recordLength = BitConverter.GetBytes((short)txtRecord.Length).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(recordLength).ToArray();

        //    outputBuffer = outputBuffer.Concat(txtRecord).ToArray();

        //    return outputBuffer;
        //}

        //private byte[] AddPtr(byte[] outputBuffer, string v1, string v2)
        //{
        //    var ptrNodeName = Utilities.EncodeName(v1);

        //    outputBuffer = outputBuffer.Concat(ptrNodeName).ToArray();

        //    var type = BitConverter.GetBytes((ushort)12).Reverse().ToArray(); // PTR

        //    outputBuffer = outputBuffer.Concat(type).ToArray();

        //    var @class = BitConverter.GetBytes((ushort)1).Reverse().ToArray(); // Internet

        //    outputBuffer = outputBuffer.Concat(@class).ToArray();

        //    var ttl = BitConverter.GetBytes((uint)120).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(ttl).ToArray();

        //    var ptrServiceName = Utilities.EncodeName(v2);

        //    var recordLength = BitConverter.GetBytes((ushort)ptrServiceName.Length).Reverse().ToArray();

        //    outputBuffer = outputBuffer.Concat(recordLength).ToArray();

        //    outputBuffer = outputBuffer.Concat(ptrServiceName).ToArray();

        //    return outputBuffer;
        //}



        public static void ByteArrayToStringDump(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);

            foreach (byte b in ba)
            {
                Console.Write(b.ToString("x2"));
            }

            Console.Write("\n");
        }
    }
}
