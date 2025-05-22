namespace Core
{
    public class Advertising
    {
        public Advertising(string name, int port)
        {
            Name = name;
            Port = port;
        }

        public string Name { get; }

        public int Port { get; }
    }
}
