using Xunit;
using System;
using Newtonsoft.Json;
using System.Net;
using Xunit.Abstractions;
using System.Linq;
using System.Collections.Generic;
using Sintef.Pgo.REST.Client;
using Sintef.Scoop.Utilities;
using System.Threading;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for multiple users using the REST server in multi-user configuration.
	/// Cf. <see cref="UserIgnoringTests"/>
	/// </summary>
	public class MultipleUsersTests : LiveServerFixture
	{
		private NetworkBuilder _builder1 = NetworkBuilder.Create("Gen1[generatorVoltage=1000] -- Line2 -- Node3[consumer]");
		private NetworkBuilder _builder2 = NetworkBuilder.Create("GenX[generatorVoltage=1000] -- LineY -- NodeZ[consumer]");
		private NetworkBuilder _builder3 = NetworkBuilder.Create(
			"GenX[generatorVoltage=1000] -- LineY[open] -- NodeZ[consumer]",
			"GenX -- LineW[open] -- NodeZ");

		private PgoRestClient _client1;
		private PgoRestClient _client2;

		public MultipleUsersTests(ITestOutputHelper output) : base(output)
		{
			Reinitialize(multiUser: true);

			_client1 = CreateClient();
			_client2 = CreateClient();
		}

		[Fact]
		public void NetworksAreListedPerUser()
		{
			_client1.LoadNetworkFromBuilder("id1", _builder1);

			AssertNetworkIds("id1", "");

			_client2.LoadNetworkFromBuilder("id2", _builder1);

			AssertNetworkIds("id1", "id2");

			_client2.LoadNetworkFromBuilder("id1", _builder2);

			AssertNetworkIds("id1", "id1 id2");

			_client1.DeleteNetwork("id1");

			AssertNetworkIds("", "id1 id2");
		}

		[Fact]
		public void OwnNetworCanBeRetrieved()
		{
			_client1.LoadNetworkFromBuilder("id1", _builder1);

			var jsonObject = PgoJsonParser.ConvertToJson(_builder1.Network);
			var json = JsonConvert.SerializeObject(jsonObject);

			Assert.Equal(json, _client1.GetNetworkString("id1"));
		}

		[Fact]
		public void OtherUsersNetworkCannotBeRetrieved()
		{
			_client1.LoadNetworkFromBuilder("id1", _builder1);

			AssertException(() => { _client2.GetNetworkString("id1"); },
				"There is no power network with ID 'id1'");
		}

		[Fact]
		public void SessionsAreListedPerUser()
		{
			_client1.LoadNetworkFromBuilder(NetworkId, _builder1);
			_client2.LoadNetworkFromBuilder(NetworkId, _builder1);

			_client1.CreateJsonSession("session1", NetworkId, _builder1);
			AssertSessionIds("session1", "");

			_client2.CreateJsonSession("session2", NetworkId, _builder1);

			AssertSessionIds("session1", "session2");

			_client2.CreateJsonSession("session1", NetworkId, _builder1);

			AssertSessionIds("session1", "session1 session2");

			_client1.DeleteSession("session1");

			AssertSessionIds("", "session1 session2");
		}

		[Fact]
		public void OwnSessionIsAccessible()
		{
			_client1.LoadNetworkFromBuilder(NetworkId, _builder1);
			_client1.CreateJsonSession("session1", NetworkId, _builder1);

			_client1.GetSessionStatus("session1");
			_client1.GetSessionDemands("session1");
			_client1.StartOptimizing("session1");
			_client1.StopOptimizing("session1");
			_client1.GetBestSolutionInfo("session1");
			_client1.DeleteSession("session1");
		}

		[Fact]
		public void OtherUsersSessionIsInaccessible()
		{
			_client1.LoadNetworkFromBuilder(NetworkId, _builder1);
			_client1.CreateJsonSession("session1", NetworkId, _builder1);

			Fails(() => {_client2.GetSessionStatus("session1"); });
			Fails(() => {_client2.GetSessionDemands("session1"); });
			Fails(() => {_client2.StartOptimizing("session1"); });
			Fails(() => { _client2.StopOptimizing("session1"); });
			Fails(() => { _client2.GetBestSolutionInfo("session1"); });
			Fails(() => {_client2.DeleteSession("session1"); });

			void Fails(Action action)
			{
				AssertException(action, requiredCode: HttpStatusCode.NotFound);
			}
		}

		[Fact]
		public void BestObjectiveValuesAreNotVisibleToOtherUsers()
		{
			// We use builder3 to ensure best solution messages are generated,
			// since its default solution is not feasible.

			// Both users create identical sessions
			_client1.LoadNetworkFromBuilder(NetworkId, _builder3);
			_client1.CreateJsonSession(SessionId, NetworkId, _builder3);

			_client2.LoadNetworkFromBuilder(NetworkId, _builder3);
			_client2.CreateJsonSession(SessionId, NetworkId, _builder3);

			// Both listen for new best objective values
			List<double> client1Values = new List<double>();
			List<double> client2Values = new List<double>();

			using (_client1.ReceiveBestSolutionUpdates(SessionId, solutionStatus => client1Values.Add(solutionStatus.ObjectiveValue)))
			using (_client2.ReceiveBestSolutionUpdates(SessionId, solutionStatus => client2Values.Add(solutionStatus.ObjectiveValue)))
			{
				// One optimizes
				Optimize(_client1);

				// Wait to be sure the SignalR message has arrived
				Thread.Sleep(100);
			}

			// Only that user should receive messages
			Assert.NotEmpty(client1Values);
			Assert.Empty(client2Values);
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
