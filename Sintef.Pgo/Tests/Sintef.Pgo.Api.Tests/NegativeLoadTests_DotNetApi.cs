using Newtonsoft.Json;
using Sintef.Pgo.Core.Test;

namespace Sintef.Pgo.Api.Tests
{
	/// <summary>
	/// Tests of consumers with negative active load (net export) through the .NET API.
	/// The network and demands are Baran-Wu (baran-wu.json), with the active loads of some consumers negated.
	/// </summary>
	[TestClass]
	public class NegativeLoadTests_DotNetApi : ApiTestFixture
	{
		[TestMethod]
		public void SessionWithNetExportReportsReverseFlow()
		{
			var session = AddSession(exports: id => true);

			Solution solution = session.BestJsonSolution;
			var flow = solution.Flows.Single();
			Assert.AreEqual(FlowStatus.Exact, flow.Status);

			// Each line is reported in the direction of active power flow, so the reverse flow in the line
			// from the provider (bus 1) to bus 2 is reported from bus 2 to the provider
			Assert.IsTrue(flow.Powers["2"].Any(p => p.Target == "1" && p.ActivePower > 0));

			// The provider absorbs the export less the losses, and the voltage rises toward the exporters
			double absorbed = PowerAbsorbedBy("1", flow);
			Assert.IsTrue(0 < absorbed && absorbed < 3.715, $"absorbed {absorbed} MW");
			Assert.IsTrue(flow.Voltages["18"] > flow.Voltages["2"] && flow.Voltages["2"] > 12.66);

			var info = session.GetSolutionInfo("best");
			Assert.IsTrue(info.IsFeasible);
			Assert.IsTrue(info.ObjectiveComponents.All(c => c.Value >= 0));
		}

		[TestMethod]
		public void KileCostOfAnExportingConsumerIsZero()
		{
			// The line at the provider is a breaker. Faults on every other line interrupt all consumers.
			var session = AddSession(exports: id => id == "18" || id == "33", network =>
			{
				foreach (var node in network.Nodes.Where(n => n.Type == NodeType.PowerConsumer))
					node.ConsumerType = ConsumerCategory.Household;
				foreach (var line in network.Lines)
				{
					if (line.SourceNode == "1")
					{
						line.IsBreaker = true;
						line.IsSwitchable = true;
						continue;
					}
					line.FaultsPerYear = 0.1;
					line.SectioningTime = TimeSpan.FromHours(1);
					line.RepairTime = TimeSpan.FromHours(2);
				}
			});

			var costs = session.BestJsonSolution.KileCosts.Single().ExpectedCosts;

			// 18 and 33 export and cost nothing. 17 consumes and is interrupted by the same faults.
			Assert.AreEqual(0, costs["18"]);
			Assert.AreEqual(0, costs["33"]);
			Assert.IsTrue(costs["17"] > 0);

			var kile = session.GetSolutionInfo("best").ObjectiveComponents.Single(c => c.Name == "KILE cost");
			Assert.AreEqual(costs.Values.Sum(), kile.Value, 1e-9 * kile.Value);
		}

		[TestMethod]
		public void ProviderAbsorbingMoreThanItsCapacityIsReported()
		{
			var session = AddSession(exports: id => true, network => network.Nodes.Single(n => n.Id == "1").MaximumActiveGeneration = 1.0);

			var info = session.GetSolutionInfo("best");

			Assert.IsFalse(info.IsFeasible);
			StringAssert.Contains(info.ViolatedConstraints.Single().Description, "Capacity of Provider 1 is exceeded");
		}

		/// <summary>
		/// Returns the active power, in MW, that flows into the given provider from its lines
		/// </summary>
		internal static double PowerAbsorbedBy(string provider, PowerFlow flow)
		{
			return flow.Powers.Sum(kv => kv.Value.Sum(p =>
				p.Target == provider ? p.ActivePower - p.PowerLoss :
				kv.Key == provider ? -p.ActivePower : 0));
		}

		/// <summary>
		/// Adds Baran-Wu as network and session. The active loads of the consumers for which <paramref name="exports"/>
		/// is true are negated, and <paramref name="modifyNetwork"/> is applied to the network.
		/// </summary>
		private ISession AddSession(Func<string, bool> exports, Action<PowerGrid>? modifyNetwork = null)
		{
			var network = JsonConvert.DeserializeObject<PowerGrid>(File.ReadAllText(TestUtils.TestDataFile("baran-wu.json")))!;
			var demand = JsonConvert.DeserializeObject<Demand>(File.ReadAllText(TestUtils.TestDataFile("baran-wu_forecast.json")))!;
			foreach (var load in demand.Loads.Where(l => exports(l.NodeId)))
				load.ActiveLoads = load.ActiveLoads.Select(p => -p).ToList();

			modifyNetwork?.Invoke(network);

			_server.AddNetwork("negload", network);
			return AddJsonSession("negload", "negload", demand);
		}
	}
}
