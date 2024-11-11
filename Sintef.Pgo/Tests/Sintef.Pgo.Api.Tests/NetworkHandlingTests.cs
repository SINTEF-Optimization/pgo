using Sintef.Pgo.Core;
using Sintef.Pgo.Core.Test;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Api.Tests
{
	/// <summary>
	/// Tests for network handling functionality in the .NET API
	/// </summary>
	[TestClass]
	public class NetworkHandlingTests : ApiTestFixture
	{
		[TestMethod]
		public void NetworksCanBeAddedAndRemoved()
		{
			AddJsonTestNetwork(id: "id1");

			Assert.AreEqual("id1", NetworkIds);

			AddCimTestNetwork(id: "id2");

			Assert.AreEqual("id1 id2", NetworkIds);

			RemoveNetwork("id1");

			Assert.AreEqual("id2", NetworkIds);

			RemoveNetwork("id2");

			Assert.AreEqual("", NetworkIds);
		}

		[TestMethod]
		public void NullArgumentsAreCaught_NetworkFunctions()
		{
			// AddNetwork

			TestUtils.AssertException(() => _server.AddNetwork("id", (PowerGrid)null!), "Value cannot be null. (Parameter 'network')");
			TestUtils.AssertException(() => _server.AddNetwork(null!, new PowerGrid()), "Value cannot be null. (Parameter 'networkId')");

			TestUtils.AssertException(() => _server.AddNetwork("id", (CimNetworkData)null!), "Value cannot be null. (Parameter 'cimNetworkData')");
			TestUtils.AssertException(() => _server.AddNetwork(null!, new CimNetworkData()), "Value cannot be null. (Parameter 'networkId')");

			TestUtils.AssertException(() => _server.AddNetwork("id", (CimJsonLdNetworkData)null!), "Value cannot be null. (Parameter 'networkData')");
			TestUtils.AssertException(() => _server.AddNetwork(null!, new CimJsonLdNetworkData()), "Value cannot be null. (Parameter 'networkId')");

			{
				var noNetwork = new CimNetworkData { ConversionOptions = new() };
				var noOptions = new CimNetworkData { Network = new(), ConversionOptions = null };

				TestUtils.AssertException(() => _server.AddNetwork("id", noNetwork), "Bad network data: Network is null");
				TestUtils.AssertException(() => _server.AddNetwork("id", noOptions), "Bad network data: ConversionOptions is null");
			}
			{
				var noNetwork = new CimJsonLdNetworkData { ConversionOptions = new() };
				var noOptions = new CimJsonLdNetworkData { Network = new(), ConversionOptions = null };

				TestUtils.AssertException(() => _server.AddNetwork("id", noNetwork), "Bad network data: Network is null");
				TestUtils.AssertException(() => _server.AddNetwork("id", noOptions), "Bad network data: ConversionOptions is null");
			}

			// Other functions

			TestUtils.AssertException(() => _server.RemoveNetwork(null!), "Value cannot be null. (Parameter 'networkId')");
			TestUtils.AssertException(() => _server.AnalyzeNetwork(null!, true), "Value cannot be null. (Parameter 'networkId')");
			TestUtils.AssertException(() => _server.AnalyzeNetworkConnectivity(null!), "Value cannot be null. (Parameter 'networkId')");
		}

		[TestMethod]
		public void AddingNetworkWithSameIdFails()
		{
			AddJsonTestNetwork(id: "id1");

			TestUtils.AssertException(() => AddJsonTestNetwork(id: "id1"), "The server already has a power network with ID 'id1'.");
			TestUtils.AssertException(() => AddCimTestNetwork(id: "id1"), "The server already has a power network with ID 'id1'.");
			TestUtils.AssertException(() => AddCimJsonLdTestNetwork(id: "id1"), "The server already has a power network with ID 'id1'.");
		}

		[TestMethod]
		public void RemovingNonexistentNetworkHasNoEffect()
		{
			AddJsonTestNetwork();

			Assert.AreEqual(_jsonNetworkId, NetworkIds);

			RemoveNetwork("id1");

			Assert.AreEqual(_jsonNetworkId, NetworkIds);
		}

		[TestMethod]
		public void ErrorsInJsonNetworkAreReported()
		{
			TestUtils.AssertException(() =>
				AddJsonTestNetwork(id: "id1", modify: n => n.Nodes = null),
				requiredMessage: "An error occurred while parsing the network data: The node list is null");

			// This was just a spot check -- see also:
			Action _ = new InputConsistencyTests_DotNetApi().ErrorsInJsonNetworkDefinitionAreReported;
		}

		[TestMethod]
		public void ErrorsInCimNetworkAreReported()
		{
			TestUtils.AssertException(() =>
				AddCimTestNetwork(id: "id1", modify: n => n.Network.ACLineSegments.Add(null)),
				requiredMessage: "An error occurred while parsing the network data: ACLineSegments contains null");

			// This was just a spot check -- see also:
			Action _ = new CimInputConsistencyTests().ErrorsInNetworkDefinitionAreReported;
		}

		[TestMethod]
		public void AnalyzeNetworkWorks()
		{
			AddJsonTestNetwork();

			List<string> analysis = _server.AnalyzeNetwork(_jsonNetworkId, false)
				.Split('\n')
				.Select(x => x.Trim())
				.ToList();

			CollectionAssert.Contains(analysis, "Number of nodes = 2");
			Assert.IsTrue(analysis.Count > 10);


			List<string> verboseAnalysis = _server.AnalyzeNetwork(_jsonNetworkId, true)
				.Split('\n')
				.Select(x => x.Trim())
				.ToList();

			Console.WriteLine(verboseAnalysis.Join("\n"));
			CollectionAssert.Contains(verboseAnalysis, "Potential for aggregation:");
			Assert.IsTrue(verboseAnalysis.Count > 30);
		}

		[TestMethod]
		public void AnalyzeNonexistentNetworkFails()
		{
			TestUtils.AssertException(() => _server.AnalyzeNetwork(_jsonNetworkId, false),
				requiredMessage: "There is no power network with ID 'jsonNetworkId'");
		}

		[TestMethod]
		public void ConnectivityCheckForNonexistentNetworkFails()
		{
			TestUtils.AssertException(() => _server.AnalyzeNetworkConnectivity(_jsonNetworkId),
				requiredMessage: "There is no power network with ID 'jsonNetworkId'");
		}

		[TestMethod]
		public void ConnectivityCheckDetectsDisconnectedNodes()
		{
			AddNetwork("G1[generatorVoltage=1000]",
				"C[consumption=(1,0)]",
				"Node1");

			NetworkConnectivityStatus connectivity = _server.AnalyzeNetworkConnectivity(_jsonNetworkId);

			Assert.IsTrue(connectivity.Ok);
			CollectionAssert.AreEquivalent(connectivity.UnconnectableNodes, new[] { "Node1", "C" });
		}

		[TestMethod]
		public void ConnectivityCheckDetectsUnbreakableCycle()
		{
			AddNetwork(
				"Gen1[generatorVoltage=100] -- Line1 -- Node -- Line2 -- Gen2[generatorVoltage=100]"
			);

			NetworkConnectivityStatus connectivity = _server.AnalyzeNetworkConnectivity(_jsonNetworkId);

			Assert.IsFalse(connectivity.Ok);
			CollectionAssert.AreEquivalent(connectivity.UnbreakableCycle, new[] { "Line1", "Line2" });
		}

		[TestMethod]
		public void ConnectivityCheckAcceptsRemovableCycle()
		{
			AddNetwork(
				"Gen[generatorVoltage=1000] -- L -- C[consumption=(1,0)]",
				"Gen -- Line0 -- Node1 -- Line1 -- Node2 -- Line2 -- Node3 -- Line3 -- Node4",
				"Node2 -- Line4 -- Node4"
			);

			NetworkConnectivityStatus connectivity = _server.AnalyzeNetworkConnectivity(_jsonNetworkId);

			Assert.IsTrue(connectivity.Ok);
		}

		[TestMethod]
		public void CycleReportIsCorrectWithParallelLines()
		{
			AddNetwork(
				"Gen1[generatorVoltage=100] -- Line1 -- Node -- Line2 -- Gen2[generatorVoltage=100]",
				"Gen1 -- Line1b -- Node"
			);

			NetworkConnectivityStatus connectivity = _server.AnalyzeNetworkConnectivity(_jsonNetworkId);

			Assert.IsFalse(connectivity.Ok);
			CollectionAssert.AreEquivalent(connectivity.UnbreakableCycle, new[] { "Line1", "Line2" });
		}

		[TestMethod]
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
					t2.AddMode("Node3", "Node4", TransformerOperationType.FixedRatio);
				else
					t2.AddMode("Node4", "Node3", TransformerOperationType.FixedRatio);

				AddNetwork(builder);

				NetworkConnectivityStatus connectivity = _server.AnalyzeNetworkConnectivity(_jsonNetworkId);

				if (consistent)
				{
					Assert.IsTrue(connectivity.Ok);
					Assert.IsNull(connectivity.UnbreakableCycle);
					Assert.IsNull(connectivity.InvalidTransformers);
				}
				else
				{
					Assert.IsFalse(connectivity.Ok);
					Assert.IsNull(connectivity.UnbreakableCycle);
					CollectionAssert.AreEquivalent(connectivity.InvalidTransformers, new[] { "T2" });
				}

				RemoveNetwork(_jsonNetworkId);
			}
		}

		[TestMethod]
		public void DeletingNetworkRemovesItsSessions()
		{
			AddJsonTestNetwork();
			AddCimTestNetwork();

			AddJsonTestSession();
			AddJsonTestSession("json2");
			AddCimTestSession();
			AddCimTestSession("cim2");

			Assert.AreEqual("jsonSessionId:jsonNetworkId json2:jsonNetworkId cimSessionId:cimNetworkId cim2:cimNetworkId", SessionIds);

			RemoveNetwork(_jsonNetworkId);

			Assert.AreEqual("cimSessionId:cimNetworkId cim2:cimNetworkId", SessionIds);

			RemoveNetwork(_cimNetworkId);

			Assert.AreEqual("", SessionIds);
		}

		[TestMethod, Ignore]
		public void MoreTestsFromNetworkHandingTestsEtc()
		{
		}
	}
}