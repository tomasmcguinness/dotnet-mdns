namespace Core
{
    public class Advertising
    {
        public Advertising(params ServiceDetails[] services)
        {
            Services = services;
        }

        public ServiceDetails[] Services { get; }
    }
}
