# dotnet-mdns
I'm building an mDNS implementation for .Net Core, which supports querying and advertising.

I'm creating my own .Net Matter protocol implementation and mDNS and DNS-SD form a core part of that. It's used for advertising and finding nodes in a Matter Fabric.

Currently this project is development as I build up a nice API around mDNS. There is enough in there to make it work (it can answer queries). but it's not finished and not really ready to use.

## History
This project started out *really* crude and only advertised the services for my [dotnet-homebridge](https://github.com/tomasmcguinness/dotnet-homebridge). 
