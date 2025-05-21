using Core;

mDNSService service = new mDNSService();

service.RecordDiscovered += (object sender, Record record) =>
{
    Console.WriteLine("Found {0}", record.Name);
};


await service.Perform(new Discovery("_matter._tcp.local"));