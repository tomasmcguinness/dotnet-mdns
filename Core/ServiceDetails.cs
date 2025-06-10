using System.Collections.Generic;

namespace mDNS.Core
{
    public class ServiceDetails
    {
        public ServiceDetails(string name, string service, ushort port, Dictionary<string, string?> txtValues, string[] addresses)
        {
            Service = service;
            Name = name;
            Port = port;
            TxtValues = txtValues;
            Addresses = addresses;
        }

        public string Service { get; set; }

        public string Name { get; }

        public ushort Port { get; }

        public Dictionary<string, string?> TxtValues { get; } = [];

        public string[] Addresses { get; }
    }
}