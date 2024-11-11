using Xunit;
using Newtonsoft.Json;
using Xunit.Abstractions;
using System.Linq;
using Sintef.Pgo.REST.Client;
using Sintef.Scoop.Utilities;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for multiple clients using the REST server in single-user configuration.
	/// Cf. <see cref="MultipleUsersTests"/>
	/// </summary>
	public class SingleUserTests : LiveServerFixture
	{
		private NetworkBuilder _builder = NetworkBuilder.Create("Gen1[generatorVoltage=1000] -- Line2 -- Node3[consumer]");

		private PgoRestClient _client1;
		private PgoRestClient _client2;

		public SingleUserTests(ITestOutputHelper output) : base(output)
		{
			// (The base constructor sets up a single user server)

			_client1 = CreateClient();
			_client2 = CreateClient();
		}


		[Fact]
		public void UserIdIsForcedByServer()
		{
			_client1 = CreateClient("Some user ID");
			_client2 = CreateClient("Another ID");

			Assert.NotEqual(_client1.UserId, _client2.UserId);

			_client1.GetServerStatus();
			_client2.GetServerStatus();

			Assert.Equal(_client1.UserId, _client2.UserId);
		}

		[Fact]
		public void NetworksAreListedIndependentOfClient()
		{
			_client1.LoadNetworkFromBuilder("id1", _builder);

			AssertNetworkIds("id1", "id1");

			_client2.LoadNetworkFromBuilder("id2", _builder);

			AssertNetworkIds("id1 id2", "id1 id2");

			_client2.DeleteNetwork("id1");

			AssertNetworkIds("id2", "id2");
		}

		[Fact]
		public void AnyClientCanRetrieveNetworks()
		{
			_client1.LoadNetworkFromBuilder("id1", _builder);

			var jsonObject = PgoJsonParser.ConvertToJson(_builder.Network);
			var json = JsonConvert.SerializeObject(jsonObject);

			Assert.Equal(json, _client1.GetNetworkString("id1"));
			Assert.Equal(json, _client2.GetNetworkString("id1"));
		}

		[Fact]
		public void SessionsAreListedIndependentOfClient()
		{
			_client1.LoadNetworkFromBuilder(NetworkId, _builder);

			_client1.CreateJsonSession("session1", NetworkId, _builder);
			AssertSessionIds("session1", "session1");

			_client2.CreateJsonSession("session2", NetworkId, _builder);

			AssertSessionIds("session1 session2", "session1 session2");

			_client2.DeleteSession("session1");

			AssertSessionIds("session2", "session2");
		}

		[Fact]
		public void SessionCreatedByOtherClientIsAccessible()
		{
			_client1.LoadNetworkFromBuilder(NetworkId, _builder);
			_client1.CreateJsonSession("session1", NetworkId, _builder);

			_client2.GetSessionStatus("session1");
			_client2.GetSessionDemands("session1");
			_client2.StartOptimizing("session1");
			_client2.StopOptimizing("session1");
			_client2.GetBestSolutionInfo("session1");
			_client2.DeleteSession("session1");
		}

		private void AssertNetworkIds(string client1Ids, string client2Ids)
		{
			Assert.Equal(client1Ids, _client1.GetServerStatus().Networks.OrderBy(x => x).Concatenate(" "));
			Assert.Equal(client2Ids, _client2.GetServerStatus().Networks.OrderBy(x => x).Concatenate(" "));
		}

		private void AssertSessionIds(string client1Ids, string client2Ids)
		{
			Assert.Equal(client1Ids, _client1.GetServerStatus().Sessions.OrderBy(x => x.Id).Select(x => x.Id).Concatenate(" "));
			Assert.Equal(client2Ids, _client2.GetServerStatus().Sessions.OrderBy(x => x.Id).Select(x => x.Id).Concatenate(" "));
		}
	}
}
