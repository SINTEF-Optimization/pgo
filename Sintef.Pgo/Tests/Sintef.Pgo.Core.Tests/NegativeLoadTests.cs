using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for consumers with negative net active load (local generation exceeds local load).
	/// </summary>
	[TestClass]
	public class NegativeLoadTests
	{
		#region Input

		[TestMethod]
		public void PowerDemandsAcceptNegativeActiveDemand()
		{
			var network = NetworkBuilder.Create("Producer[generatorVoltage=1000.0] -- Line1[r=1] -- Consumer[consumption=(100, 0)]").Network;
			var consumer = network.GetBus("Consumer");

			var demands = new PowerDemands(network);
			demands.SetPowerDemand(consumer, new Complex(-1000, 200));
			Assert.AreEqual(new Complex(-1000, 200), demands.PowerDemand(consumer));

			demands.SetActivePowerDemand(consumer, -5);
			Assert.AreEqual(-5, demands.ActivePowerDemand(consumer));
		}

		[TestMethod]
		public void NetworkBuilderAcceptsNegativeConsumption()
		{
			var builder = NetworkBuilder.Create("Producer[generatorVoltage=1000.0] -- Line1[r=1] -- Consumer[consumption=(-100, 10)]");

			Assert.AreEqual(new Complex(-100, 10), builder.Demands.PowerDemand(builder.Network.GetBus("Consumer")));
		}

		#endregion

		#region Iterated DistFlow with export

		/// <summary>
		/// Iterated DistFlow solves Baran-Wu with exporting consumers, and the flow is consistent.
		/// "some": every third consumer exports what it consumed. The total demand is still positive,
		///   but power flows towards the provider on some lines.
		/// "all": every consumer exports what it consumed, so the provider absorbs power.
		/// </summary>
		[DataTestMethod]
		[DataRow("some")]
		[DataRow("all")]
		public void IteratedDistFlowHandlesExport(string exporters)
		{
			var (network, demands) = BaranWuWithExport(k => exporters == "all" || k % 3 == 0);

			var configuration = new NetworkConfiguration(network, new SwitchSettings(network));
			var flow = new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions).ComputeFlow(new FlowProblem(configuration, demands));

			Assert.AreEqual(FlowStatus.Exact, flow.Status, flow.StatusDetails);
			Assert.AreEqual(
				"NoCurrent: ok; Current: ok; PowerLoss: ok; LineBalance: ok; NodeBalance: ok; " +
				"PowerLossTransformer: ok; Transformer voltage: ok; TransformerLineBalance: ok",
				flow.FlowConsistencyReportWithTransformers());

			// In baran-wu.json, each line's source is its upstream end
			Assert.IsTrue(network.Lines.Any(l => flow.PowerFlow(l.Node1, l).Real < 0), "some line has reverse flow");
			double generation = flow.PowerInjection(network.Providers.Single()).Real;
			if (exporters == "all")
				Assert.IsTrue(generation < 0, $"the provider absorbs power, generation {generation}");
			else
				Assert.IsTrue(generation > 0, $"the provider supplies power, generation {generation}");
		}

		#endregion

		#region Utilities

		/// <summary>
		/// Returns Baran-Wu (baran-wu.json) and its demands, where each consumer number k (counting from 0)
		/// for which <paramref name="exports"/> is true exports the active power it consumed.
		/// </summary>
		internal static (PowerNetwork network, PowerDemands demands) BaranWuWithExport(Func<int, bool> exports)
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("baran-wu.json"));
			var demands = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, TestUtils.TestDataFile("baran-wu_forecast.json"))[0].Demands;

			foreach (var (bus, k) in network.Consumers.Select((b, k) => (b, k)).Where(x => exports(x.k)))
				demands.SetActivePowerDemand(bus, -demands.ActivePowerDemand(bus));

			return (network, demands);
		}

		#endregion
	}
}
