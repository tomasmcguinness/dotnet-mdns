using Core;

mDNSService service = new mDNSService();

service.RecordDiscovered += (object sender, Record[] records) =>
{
    foreach (Record record in records)
    {
        Console.WriteLine("Found {0}", record.Name);
    }
};

await service.Perform(new Discovery("D5096097147FB61E-ABABABAB00010001._matter._tcp.local"));