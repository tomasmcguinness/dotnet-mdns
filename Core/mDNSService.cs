using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace mDNS.Core
{
    public class mDNSService
    {
        public delegate void RecordDiscoveredDelegate(object sender, Record[] record);
        public event RecordDiscoveredDelegate RecordDiscovered;

        private bool _isThreadRunning = true;
        private Thread? _thread = null;

        private List<string> _questionsAsked = new();
        private Dictionary<string, ServiceDetails> _services = new();

        private readonly ILogger<mDNSService> _logger;

        public mDNSService(ILogger<mDNSService> logger)
        {
            _logger = logger;
        }

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

        public async Task Perform(Advertising advert)
        {
            _services = advert.Services.ToDictionary(s => s.Service, s => s);

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
                    _logger.LogInformation(adapter.Description);
                    _logger.LogInformation(String.Empty.PadLeft(adapter.Description.Length, '='));
                    _logger.LogInformation("  Interface type .......................... : {0}", adapter.NetworkInterfaceType);
                    _logger.LogInformation("  Physical Address ........................ : {0}", adapter.GetPhysicalAddress().ToString());
                    _logger.LogInformation("  Minimum Speed............................ : {0}", adapter.Speed);
                    _logger.LogInformation("  Is receive only.......................... : {0}", adapter.IsReceiveOnly);
                    _logger.LogInformation("  Multicast................................ : {0}", adapter.SupportsMulticast);
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

                _logger.LogInformation($"Bound to {selectedNic.Description.ToString()}");
                _logger.LogInformation($"Bound to {selectedInterface.ToString()}");
                _logger.LogInformation($"Address is {address.ToString()}");
                _logger.LogInformation($"Hostname is {hostname.ToString()}");

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

                _logger.LogInformation("Bound to {0}", ipAddress);

                if (_questionsAsked.Any())
                {
                    DNSMessage initialQuery = new DNSMessage(false);

                    foreach (var q in _questionsAsked)
                    {
                        initialQuery.Queries.AddPointer(q);
                    }

                    var outputBuffer = initialQuery.GetBytes();

                    var bytesSent = udpclient.Client.SendTo(outputBuffer, 0, outputBuffer.Length, SocketFlags.None, multicastEndpoint);

                    _logger.LogInformation($"Send Initial Query [{bytesSent}] to [{multicastEndpoint}]");
                }

                while (_isThreadRunning)
                {
                    try
                    {
                        _logger.LogInformation("Waiting for incoming data...");

                        var buffer = new byte[2028];
                        int numberOfbytesReceived = udpclient.Client.ReceiveFrom(buffer, ref senderRemote);

                        // Ignore questions from ourselves.
                        //
                        if (senderRemote == localEndpoint)
                        {
                            continue;
                        }

                        var content = new byte[numberOfbytesReceived];
                        Array.Copy(buffer, 0, content, 0, numberOfbytesReceived);

                        var request = new DNSMessage(content);

                        _logger.LogInformation("─────────────────── BEGIN REQUEST ────────────────────");
                        _logger.LogInformation(request.ToString());
                        _logger.LogInformation("──────────────────── END REQUEST ────────────────────");

                        if (request.IsQuery)
                        {
                            var response = new DNSMessage(true);

                            // Add our answers
                            //
                            Dictionary<string, string> values = new Dictionary<string, string>();

                            if (request.Queries.Any(q => q.Name == "_services._dns-sd._udp.local"))
                            {
                                foreach (var service in _services)
                                {
                                    response.Answers.AddPointer("_services._dns-sd._udp.local", service.Key);

                                    var serviceName = $"{service.Value.Name}.{service.Value.Service}";
                                    var hostName = $"{service.Value.Name}.local";

                                    response.AdditionalInformation.AddPointer("_matter._tcp.local", serviceName);

                                    response.AdditionalInformation.AddService(serviceName, 0, 0, service.Value.Port, hostName);

                                    values = new Dictionary<string, string>();
                                    values.Add("CM", "1");
                                    values.Add("D", "3840");
                                    values.Add("DN", "C# mDNS Test");

                                    response.AdditionalInformation.AddText(serviceName, values);

                                    foreach (var record_address in service.Value.Addresses)
                                    {
                                        response.AdditionalInformation.AddARecord(hostName, record_address);
                                    }

                                }
                            }

                            // Only reply if we have something to say.
                            //
                            if (response.QueryCount > 0 || response.AnswerCount > 0)
                            {
                                _logger.LogInformation("─────────────────── BEGIN RESPONSE ────────────────────");
                                _logger.LogInformation(response.ToString());
                                _logger.LogInformation("──────────────────── END RESPONSE ────────────────────");

                                var outputBuffer = response.GetBytes();

                                ByteArrayToStringDump(outputBuffer);

                                var bytesSent = udpclient.Client.SendTo(outputBuffer, 0, outputBuffer.Length, SocketFlags.None, senderRemote);

                                _logger.LogInformation($"Send {bytesSent} to {senderRemote}");
                            }
                        }
                        else
                        {
                            // Response received. Look through the answers to see if they match the query we sent.
                            // 
                            var firstQuestion = _questionsAsked.FirstOrDefault();

                            if (firstQuestion != null)
                            {
                                if (request.Answers.Any(q => q.Name == firstQuestion))
                                {
                                    _logger.LogInformation($"Response to query {firstQuestion} was received");
                                    RecordDiscovered?.Invoke(this, request.AdditionalInformation.ToArray());
                                }
                            }
                        }
                    }
                    catch (Exception exp)
                    {
                        _logger.LogInformation(exp.Message);
                    }
                }
            }
            catch (Exception exp)
            {
                _logger.LogInformation(exp.Message);

                if (exp.InnerException != null)
                {
                    _logger.LogInformation(exp.InnerException.Message);
                }
            }
        }

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
