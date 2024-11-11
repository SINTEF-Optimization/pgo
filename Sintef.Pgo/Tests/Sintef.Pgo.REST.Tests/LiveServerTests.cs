using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;
using Xunit.Abstractions;
using System.Linq;
using System.Collections.Generic;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for the REST server
	/// </summary>
	public class LiveServerTests : LiveServerFixture
	{
		public LiveServerTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void ServerStatusStartsEmpty()
		{
			CreateDefaultClient();

			ServerStatus status = Client.GetServerStatus();

			Assert.Empty(status.Networks);
			Assert.Empty(status.Sessions);
		}

		[Fact]
		public void LoadingValidNetworkDataSucceeds()
		{
			CreateDefaultClient();

			Client.LoadNetworkFromFile(NetworkId, BaranWuModifiedNetworkFile);

			ServerStatus status = Client.GetServerStatus();

			Assert.True(status.Networks.ToHashSet().SetEquals(new[] { NetworkId }));
			Assert.Equal("Baran-wu case", Client.GetNetwork(NetworkId).Name);
		}

		[Fact]
		public void LoadingInvalidNetworkDataFails()
		{
			CreateDefaultClient();

			AssertException(() => Client.LoadNetworkFromString(NetworkId, jsonData: "Bad network json"), "An error occurred while parsing the network data");
		}

		[Fact]
		public void LoadingNetworkTwiceFails()
		{
			CreateDefaultClient(BaranWuModifiedNetworkFile);

			AssertException(() => Client.LoadNetworkFromFile(NetworkId, BaranWuModifiedNetworkFile), "is already loaded");
		}

		[Fact]
		public void NetworksCanBeRetrieved()
		{
			CreateDefaultClient();
			Client.LoadNetworkFromFile("net", BaranWuModifiedNetworkFile);

			var net = Client.GetNetwork("net");
			Assert.Equal("Baran-wu case", net.Name);
		}

		[Fact]
		public void GettingANonexistentNetworkFails()
		{
			CreateDefaultClient(BaranWuModifiedNetworkFile);

			AssertException(() => Client.GetNetwork("???"), "Not found");
		}

		[Fact]
		public void DeletingANetworkWorks()
		{
			CreateDefaultClient();
			Client.LoadNetworkFromFile("net1", BaranWuModifiedNetworkFile);
			Client.LoadNetworkFromFile("net2", BaranWuModifiedNetworkFile);

			AssertEquivalent(Client.GetServerStatus().Networks, new[] { "net1", "net2" });

			Client.DeleteNetwork("net2");

			AssertEquivalent(Client.GetServerStatus().Networks, new[] { "net1" });
		}

		[Fact]
		public void DeletingANonexistentNetworkFails()
		{
			CreateDefaultClient(BaranWuModifiedNetworkFile);

			AssertException(() => Client.DeleteNetwork("???"), "Not found");
		}

		[Fact]
		public void DeletingNetworkRemovesItsSessions()
		{
			string sessionId2 = SessionId + "2";
			string networkId2 = NetworkId + "2";

			SetupStandardSession();
			Client.LoadNetworkFromFile(networkId2, BaranWuModifiedNetworkFile);
			Client.CreateJsonSession(sessionId2, networkId2, BaranWuModifiedDemandsFile, BaranWuModifiedConfigurationFile);

			AssertSessions("mySession:myNetwork mySession2:myNetwork2");

			Client.DeleteNetwork(NetworkId);

			AssertSessions("mySession2:myNetwork2");
		}
	}
}
