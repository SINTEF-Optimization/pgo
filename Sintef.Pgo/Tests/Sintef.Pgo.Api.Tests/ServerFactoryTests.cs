namespace Sintef.Pgo.Api.Tests
{
	/// <summary>
	/// Tests for <see cref="ServerFactory"/>
	/// </summary>
	[TestClass]
	public class ServerFactoryTests
	{
		[TestMethod]
		public void ServerFactoryCreatesAServer()
		{
			IServer server = ServerFactory.CreateServer();
			Assert.IsNotNull(server);
		}

		[TestMethod]
		public void CreatedServersAreDistinct()
		{
			IServer server1 = ServerFactory.CreateServer();
			IServer server2 = ServerFactory.CreateServer();

			Assert.AreNotSame(server1, server2);
		}
	}
}