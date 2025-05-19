namespace Core.Tests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            var query = Convert.FromHexString("0000000004000000000000000f5f636f6d70616e696f6e2d6c696e6b045f746370056c6f63616c00000c8001045f686170c01c000c8001075f72646c696e6bc01c000c8001045f686170045f756470c021000c8001");

            var message = new DNSMessage(query);
        }
    }
}