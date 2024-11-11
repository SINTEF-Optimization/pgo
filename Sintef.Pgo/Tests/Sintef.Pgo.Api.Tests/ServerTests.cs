using Microsoft.VisualStudio.TestPlatform.Utilities;

namespace Sintef.Pgo.Api.Tests
{
	/// <summary>
	/// Tests for general Server functionality
	/// </summary>
	[TestClass]
	public class ServerTests : ApiTestFixture
	{
		[TestMethod]
		public void ServerStartsEmpty()
		{
			var status = Status;

			Assert.AreEqual(0, status.Sessions.Count);
			Assert.AreEqual(0, status.Networks.Count);
		}

		// (Quotas do not need to be tested, as they are handled entirely in the REST layer)
	}
}