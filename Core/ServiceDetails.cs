﻿namespace Core
{
    public class ServiceDetails
    {
        public ServiceDetails(string service, string name, ushort port, string[] addresses)
        {

            Service = service;
            Name = name;
            Port = port;
            Addresses = addresses;
        }

        public string Service { get; set; }

        public string Name { get; }

        public ushort Port { get; }

        public string[] Addresses { get; }
    }
}