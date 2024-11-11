using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using System;
using Newtonsoft.Json;
using Sintef.Pgo.DataContracts;
using API = Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for how networks with varying properties are handled by the service
	/// </summary>
	public class NetworkHandlingTests : LiveServerFixture
	{
		public NetworkHandlingTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void AnalyzeNetworkWorks()
		{
			CreateDefaultClient(BaranWuModifiedNetworkFile);

			string result = Client.AnalyseNetwork(NetworkId);
			WriteLine?.Invoke(result);
		}

		[Fact]
		public void AnalyzeNonexistentNetworFails()
		{
			CreateDefaultClient();

			AssertException(() => Client.AnalyseNetwork("???"), "Not found");
		}

		[Fact]
		public void ConnectivityCheckForNonexistentNetworFails()
		{
			CreateDefaultClient();

			AssertException(() => Client.NetworkConnectivityStatus("???"), "Not found");
		}

		[Fact]
		public void ConnectivityCheckDetectsDisconnectedNodes()
		{
			CreateDefaultClient();

			var builder = new NetworkBuilder();
			builder.Add("G1[generatorVoltage=1000]");
			builder.Add("C[consumption=(1,0)]");
			builder.Add("Node1");

			Client.LoadNetworkFromBuilder(NetworkId, builder);
			var connectivity = Client.NetworkConnectivityStatus(NetworkId);

			Assert.True(connectivity.Ok);
			AssertEquivalent(connectivity.UnconnectableNodes, new[] { "Node1", "C" });
		}

		[Fact]
		public void ConnectivityCheckDetectsUnbreakableCycle()
		{
			CreateDefaultClient();

			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1 -- Node -- Line2 -- Gen2[generatorVoltage=100]"
			);

			Client.LoadNetworkFromBuilder(NetworkId, builder);
			var connectivity = Client.NetworkConnectivityStatus(NetworkId);

			Assert.False(connectivity.Ok);
			AssertEquivalent(connectivity.UnbreakableCycle, new[] { "Line1", "Line2" });
		}

		[Fact]
		public void ConnectivityCheckAcceptsRemovableCycle()
		{
			CreateDefaultClient();

			// This cycle is removed by aggregating parallel lines:
			var builder = NetworkBuilder.Create(
				"Gen[generatorVoltage=1000] -- L -- C[consumption=(1,0)]",
				"Gen -- Line0 -- Node1 -- Line1 -- Node2 -- Line2 -- Node3 -- Line3 -- Node4",
				"Node2 -- Line4 -- Node4"
			);

			Client.LoadNetworkFromBuilder(NetworkId, builder);
			var connectivity = Client.NetworkConnectivityStatus(NetworkId);

			Assert.True(connectivity.Ok);
		}

		[Fact]
		public void CycleReportIsCorrectWithParallelLines()
		{
			CreateDefaultClient();

			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1 -- Node -- Line2 -- Gen2[generatorVoltage=100]",
				"Gen1 -- Line1b -- Node"
			);

			Client.LoadNetworkFromBuilder(NetworkId, builder);
			var connectivity = Client.NetworkConnectivityStatus(NetworkId);

			Assert.False(connectivity.Ok);
			Assert.True(new HashSet<string>(connectivity.UnbreakableCycle).SetEquals(new[] { "Line1", "Line2" }));
		}

		[Fact]
		public void NetworkConnectivityReportsGloballyInconsistentTransformerModes()
		{
			foreach (var consistent in new[] { true, false })
			{
				// Build a network with two transformers can only be used in inconsistent directions.
				var builder = new NetworkBuilder();
				builder.Add("G1[generatorVoltage=1000] -- Line0 -- Node1");
				builder.Add("Node2 -- Line3 -- Node3");
				builder.Add("Node4[consumption=(1,0.2)]");

				var network = builder.Network;
				var t1 = network.AddTransformer(new[] { ("Node1", 1.0), ("Node2", 1.0) }, name: "T1");
				var t2 = network.AddTransformer(new[] { ("Node3", 1.0), ("Node4", 1.0) }, name: "T2");

				t1.AddMode("Node1", "Node2", TransformerOperationType.FixedRatio);

				if (consistent)
				{
					t2.AddMode("Node3", "Node4", TransformerOperationType.FixedRatio);

					CreateDefaultClient();
					Client.LoadNetworkFromBuilder(NetworkId, builder);
					var connectivity = Client.NetworkConnectivityStatus(NetworkId);

					Assert.True(connectivity.Ok);
					Assert.Null(connectivity.UnbreakableCycle);
					Assert.Null(connectivity.InvalidTransformers);
				}
				else
				{
					t2.AddMode("Node4", "Node3", TransformerOperationType.FixedRatio);

					CreateDefaultClient();
					Client.LoadNetworkFromBuilder(NetworkId, builder);
					var connectivity = Client.NetworkConnectivityStatus(NetworkId);

					Assert.False(connectivity.Ok);
					Assert.Null(connectivity.UnbreakableCycle);
					AssertEquivalent(connectivity.InvalidTransformers, new[] { "T2" });
				}

				Client.DeleteNetwork(NetworkId);
			}
		}

		[Fact]
		public void ThreeWindingTransformerMustHaveAtLeastOneInputWithTwoOutputs()
		{
			string expectedError = "Transformer 'Transformer': A transformer must have at least one input terminal for which there are modes outputting to each of the other terminals";

			var modeSetsWithErrors = new Func<PowerGrid, string>[]
			{
				g =>
				{
					g.Transformers[0].Modes.Add(new TransformerMode { Source = "n2", Target = "n1", Ratio = 1, Bidirectional = false });
					g.Transformers[0].Modes.Add(new TransformerMode { Source = "n3", Target = "n1", Ratio = 1, Bidirectional = false });
					return expectedError;
				},
				g =>
				{
					g.Transformers[0].Modes.Add(new TransformerMode { Source = "n1", Target = "n2", Ratio = 1 });
					return expectedError;
				},
				g =>
				{
					g.Transformers[0].Modes.Add(new TransformerMode { Source = "n1", Target = "n2", Ratio = 1 });
					g.Transformers[0].Modes.Add(new TransformerMode { Source = "n1", Target = "n3", Ratio = 1 });
					return null;
				},
				g =>
				{
					g.Transformers[0].Modes.Add(new TransformerMode { Source = "n2", Target = "n1", Ratio = 1 });
					g.Transformers[0].Modes.Add(new TransformerMode { Source = "n3", Target = "n1", Ratio = 1 });
					return null;
				},
				g =>
				{
					g.Transformers[0].Modes.Add(new TransformerMode { Source = "n1", Target = "n2", Ratio = 1, Bidirectional = false });
					g.Transformers[0].Modes.Add(new TransformerMode { Source = "n1", Target = "n3", Ratio = 1, Bidirectional = false });
					return null;
				},
			};

			foreach (var modeSet in modeSetsWithErrors)
			{
				var grid = new PowerGrid
				{
					Nodes = new List<Node> { new Node { Id = "n1" }, new Node { Id = "n2" }, new Node { Id = "n3" } },
					Lines = new List<API.Line> { },
					SchemaVersion = 1,
					Transformers = new List<API.Transformer>
					{
						new API.Transformer
						{
							Id = "Transformer",
							Connections = new List<TransformerConnection> { new TransformerConnection { NodeId = "n1", Voltage = 5 }, new TransformerConnection { NodeId = "n2", Voltage = 5 }, new TransformerConnection { NodeId =    "n3", Voltage = 5 } },
							Modes = new List<TransformerMode> { },
						}
					}
				};

				CreateDefaultClient();
				var error = modeSet(grid);
				if (error != null)
				{
					AssertException(() => Client.LoadNetworkFromString(NetworkId, JsonConvert.SerializeObject(grid)), error);
				}
				else
				{
					Client.LoadNetworkFromString(NetworkId, JsonConvert.SerializeObject(grid));
					Client.DeleteNetwork(NetworkId);
				}
			}
		}
	}
}
