using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;
using System.Linq;
using Sintef.Pgo.DataContracts;
using System.IO;
using Sintef.Pgo.Core.Test;
using Sintef.Pgo.REST.Client;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for the REST server
	/// </summary>
	public class LiveCimServerTests : LiveServerFixture
	{
		private PgoRestClient _jsonClient;
		private	CimParsingOptions _diginParsingOptions; 

		public LiveCimServerTests(ITestOutputHelper output) : base(output)
		{
			_diginParsingOptions = new CimParsingOptions { UnitsProfile = CimUnitsProfile.Digin };

			DefaultSessionType = SessionType.Cim;

			CreateDefaultClient();

			Client.GetServerStatus(); // Initializes UserID
			_jsonClient = CreateClient(Client.UserId, SessionType.Json);
		}

		[Fact]
		public void ServerStatusStartsEmpty()
		{
			ServerStatus status = Client.GetServerStatus();

			Assert.Empty(status.Networks);
			Assert.Empty(status.Sessions);
		}

		[Fact]
		public void LoadingValidCimNetworkDataSucceeds()
		{
			Client.LoadNetworkFromFile(NetworkId, TestUtils.DiginCombinedNetworkFile, _diginParsingOptions);

			ServerStatus status = Client.GetServerStatus();

			Assert.True(status.Networks.ToHashSet().SetEquals(new[] { NetworkId }));
		}

		[Fact]
		public void CimNetworkIsNotVisibleInJsonAPI()
		{
			Client.LoadNetworkFromFile(NetworkId, TestUtils.DiginCombinedNetworkFile, _diginParsingOptions);

			Assert.Single(Client.GetServerStatus().Networks);
			Assert.Empty(_jsonClient.GetServerStatus().Networks);
		}

		[Fact]
		public void JsonNetworkIsNotVisibleInCimAPI()
		{
			_jsonClient.LoadNetworkFromFile(NetworkId, BaranWuModifiedNetworkFile);

			Assert.Single(_jsonClient.GetServerStatus().Networks);
			Assert.Empty(Client.GetServerStatus().Networks);
		}

		[Fact]
		public void LoadingNetworkWithInvalidJsonFails()
		{
			// This test does not really do anything, as it fails in the client...

			AssertException(() => Client.LoadNetworkFromString(NetworkId, jsonData: "Bad network json", _diginParsingOptions), 
				requiredMessage: "Unexpected character encountered while parsing value: B. Path '', line 0, position 0.");
		}

		[Fact]
		public void LoadingInvalidNetworkDataFails()
		{
			string networkText = File.ReadAllText(TestUtils.DiginCombinedNetworkFile)
				.Replace("ACLineSegment.r\": 0.0159999", "ACLineSegment.r\": -0.0159999");

			AssertException(() => Client.LoadNetworkFromString(NetworkId, networkText, _diginParsingOptions), 
				requiredMessage: "An error occurred while parsing the network data: " +
					"ACLineSegment with name '04 TELEMA2 ACLS1' (MRID 9d58e5bb-834c-4faa-928c-7da0bb1497d9): " +
					"The resistance 'r' cannot be negative");
		}

		[Fact]
		public void LoadingNetworkTwiceFails()
		{
			Client.LoadNetworkFromFile(NetworkId, TestUtils.DiginCombinedNetworkFile, _diginParsingOptions);

			AssertException(() => Client.LoadNetworkFromFile(NetworkId, TestUtils.DiginCombinedNetworkFile, _diginParsingOptions), "is already loaded");
		}

		[Fact]
		public void DeletingANetworkWorks()
		{
			Client.LoadNetworkFromFile("net1", TestUtils.DiginCombinedNetworkFile, _diginParsingOptions);
			Client.LoadNetworkFromFile("net2", TestUtils.DiginCombinedNetworkFile, _diginParsingOptions);

			AssertEquivalent(Client.GetServerStatus().Networks, new[] { "net1", "net2" });

			Client.DeleteNetwork("net2");

			AssertEquivalent(Client.GetServerStatus().Networks, new[] { "net1" });
		}

		[Fact]
		public void DeletingANonexistentNetworkFails()
		{
			Client.LoadNetworkFromFile("net", TestUtils.DiginCombinedNetworkFile, _diginParsingOptions);

			AssertException(() => Client.DeleteNetwork("???"), "Not found");
		}

		[Fact]
		public void DeletingNetworkRemovesItsSessions()
		{
			string sessionId2 = SessionId + "2";
			string networkId2 = NetworkId + "2";

			SetupStandardSession();
			Client.LoadNetworkFromFile(networkId2, TestUtils.DiginCombinedNetworkFile, _diginParsingOptions);
			Client.CreateCimSession(sessionId2, networkId2, TestUtils.DiginCombinedSshFile, TestUtils.DiginCombinedSshFile);

			AssertSessions("mySession:myNetwork mySession2:myNetwork2");

			Client.DeleteNetwork(NetworkId);

			AssertSessions("mySession2:myNetwork2");
		}
	}
}
