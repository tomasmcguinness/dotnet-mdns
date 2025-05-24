using Core;
using System.Net.NetworkInformation;
using System.Net.Sockets;

mDNSService service = new mDNSService();

//service.RecordDiscovered += (object sender, Record[] records) =>
//{
//    foreach (Record record in records)
//    {
//        Console.WriteLine("Found {0}", record.Name);
//    }
//};

//await service.Perform(new Discovery("D5096097147FB61E-ABABABAB00010001._matter._tcp.local"));
//await service.Perform(new Discovery("_http._tcp.local"));

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

//UdpClient receivingUdpClient = new UdpClient(11000);

await service.Perform(new Advertising(new ServiceDetails("_matter._tcp.local", "TOMAS", 11000, addresses.ToArray())));