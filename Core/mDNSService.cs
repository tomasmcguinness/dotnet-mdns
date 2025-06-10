using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public delegate void ServiceDiscoveredDelegate(object sender, ServiceDetails service);
        public event ServiceDiscoveredDelegate ServiceDiscovered;

        private bool _isThreadRunning = true;
        private Thread? _thread = null;

        private Dictionary<string, ServiceDetails> _services = new();

        private readonly ILogger<mDNSService> _logger;
        private bool _isPerformingServiceDiscovery;

        public mDNSService(ILogger<mDNSService> logger)
        {
            _logger = logger;
        }

        public void Perform(ServiceDiscovery discovery)
        {
            _isPerformingServiceDiscovery = true;

            var threadStart = new ThreadStart(Start);
            _thread = new Thread(Start);
            _thread.Start();
        }

        public async Task Perform(Advertising advert)
        {
            _services = advert.Services.ToDictionary(s => s.Service, s => s);

            var threadStart = new ThreadStart(Start);
            _thread = new Thread(Start);
            _thread.Start();
        }

        public void Stop()
        {
            _isThreadRunning = false;

            if (_thread != null)
            {
                _thread.Join();
                _thread = null;
            }
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

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                while (_isThreadRunning)
                {
                    try
                    {
                        if (_isPerformingServiceDiscovery)
                        {
                            // Send a service discovery query every 2 seconds.
                            //
                            if (stopwatch.Elapsed.Seconds > 2)
                            {
                                DNSMessage serviceDiscoveryQuery = new DNSMessage(false);

                                serviceDiscoveryQuery.Queries.AddPointer("_services._dns-sd._udp.local");

                                var outputBuffer = serviceDiscoveryQuery.GetBytes();

                                var bytesSent = udpclient.Client.SendTo(outputBuffer, 0, outputBuffer.Length, SocketFlags.None, multicastEndpoint);

                                _logger.LogInformation($"Sent Service Discovery Query [{bytesSent}] to [{multicastEndpoint}]");

                                stopwatch.Restart();
                            }
                        }

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
                            // Response received. 
                            // 
                            // Does this contain a DNS-SD answer??
                            //
                            var responseContainsServices = request.Answers.Any(q => q.Name == "_services._dns-sd._udp.local");

                            if (responseContainsServices)
                            {
                                var answerRecords = request.Answers.Where(q => q.Name == "_services._dns-sd._udp.local");

                                foreach (var answerRecord in answerRecords)
                                {
                                    var pointer = answerRecord as PointerRecord;

                                    var matchingRecord = request.GetRecord(pointer!.Value);

                                    if (matchingRecord is not null)
                                    {
                                        var matchingRecordPointer = matchingRecord as PointerRecord;

                                        var serviceType = pointer.Value;
                                        var serviceName = matchingRecordPointer!.Value;

                                        var serviceRecords = request.GetRecords(matchingRecordPointer!.Value);

                                        ushort servicePort = 1000;
                                        string? serviceHostname = null;
                                        List<string> addresses = [];

                                        foreach (var serviceRecord in serviceRecords)
                                        {
                                            if (serviceRecord.Type == RecordType.SRV)
                                            {
                                                var r = serviceRecord as ServiceRecord;
                                                servicePort = r.Port;
                                                serviceHostname = r.Hostname;
                                            }
                                        }

                                        var hostnameRecords = request.GetRecords(serviceHostname);

                                        foreach (var addressRecord in hostnameRecords)
                                        {
                                            if (addressRecord.Type == RecordType.A)
                                            {
                                                var r = addressRecord as ARecord;
                                                addresses.Add(r.Address.ToString());
                                            }
                                            //else if (addressRecord.Type == RecordType.TXT)
                                            //{
                                            //    var r = addressRecord as TXTRecord
                                            //    addresses.Add(r.IPAddress.ToString());
                                            //}
                                        }

                                        var serviceDetails = new ServiceDetails(serviceName, serviceType, servicePort, addresses.ToArray());

                                        ServiceDiscovered?.Invoke(this, serviceDetails);
                                    }
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
