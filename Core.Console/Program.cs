using mDNS.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

//using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole());
//ILogger<mDNSService> logger = factory.CreateLogger<mDNSService>();
ILogger<mDNSService> logger = new NullLogger<mDNSService>();

mDNSService service = new mDNSService(logger);

service.ServiceDiscovered += (object sender, ServiceDetails service) =>
{
    Console.WriteLine("Found {0}", service.Name);
};

await service.Perform(new ServiceDiscovery());

/*
var addresses = new List<string>();

foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
{
    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
    {
        if (ni.OperationalStatus != OperationalStatus.Up) continue;

        Console.WriteLine(ni.Name);

        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
        {
            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)// || ip.Address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Console.WriteLine(ip.Address.ToString());
                addresses.Add(ip.Address.ToString());
            }
        }
    }
}

await service.Perform(new Advertising(new ServiceDetails("_matter._tcp.local", "TOMAS", 11000, addresses.ToArray())));
*/