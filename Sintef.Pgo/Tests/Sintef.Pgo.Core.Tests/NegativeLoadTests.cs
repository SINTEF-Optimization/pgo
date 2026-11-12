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

			var flow = AssertExactAndConsistentFlow(FlowProblem(network, demands));

			// In baran-wu.json, each line's source is its upstream end
			Assert.IsTrue(network.Lines.Any(l => flow.PowerFlow(l.Node1, l).Real < 0), "some line has reverse flow");
			double generation = flow.PowerInjection(network.Providers.Single()).Real;
			if (exporters == "all")
				Assert.IsTrue(generation < 0, $"the provider absorbs power, generation {generation}");
			else
				Assert.IsTrue(generation > 0, $"the provider supplies power, generation {generation}");
		}


		/// <summary>
		/// The export at B exactly balances the load at A plus the loss in L2, so L1 carries no current
		/// and the generator power is zero. The convergence test must not depend on the generator power being large.
		/// </summary>
		[TestMethod]
		[Timeout(30000)]
		public void IteratedDistFlowConvergesWhenExportBalancesLoad()
		{
			var builder = NetworkBuilder.Create("P[generatorVoltage=20000] -- L1[z=(0.3, 0.2)] -- A[consumption=(1e6, 3e5)] -- L2[z=(0.5, 0.4)] -- B[consumer]");
			var network = builder.Network;

			// With no current in L1, A has the generator voltage V, and S_B = -S_A - z2 |S_A|^2 / |V|^2
			Complex sA = new(1e6, 3e5);
			Complex z2 = new(0.5, 0.4);
			builder.Demands.SetPowerDemand(network.GetBus("B"), -sA - z2 * sA.Magnitude * sA.Magnitude / (20000.0 * 20000.0));

			var flow = AssertExactAndConsistentFlow(builder.FlowProblem);

			Assert.AreEqual(0, flow.PowerInjection(network.GetBus("P")).Magnitude, 1e-3);
		}

		/// <summary>
		/// The demands sum to exactly zero, so the generator power is zero after the first iteration,
		/// before any line loss is known. The solver must not take that as convergence.
		/// The generator then supplies just the line losses.
		/// </summary>
		[TestMethod]
		[Timeout(30000)]
		public void IteratedDistFlowConvergesWhenDemandsSumToZero()
		{
			var builder = NetworkBuilder.Create("P[generatorVoltage=20000] -- L1[z=(0.3, 0.2)] -- A[consumption=(1e6, 3e5)] -- L2[z=(0.5, 0.4)] -- B[consumption=(-1e6, -3e5)]");
			var network = builder.Network;

			var flow = AssertExactAndConsistentFlow(builder.FlowProblem);

			double lineLoss = network.Lines.Sum(l => flow.PowerLoss(l));
			Assert.IsTrue(lineLoss > 0);
			AssertRelative(lineLoss, flow.PowerInjection(network.GetBus("P")).Real, 1e-9, "generation equals the line loss");
		}

		/// <summary>
		/// The solver gives up, and reports failure, when it has not converged within its iteration limit
		/// </summary>
		[TestMethod]
		public void IteratedDistFlowFailsWhenIterationLimitIsReached()
		{
			var (network, demands) = BaranWuWithExport(k => true);
			var options = new IteratedDistFlowProvider.Options { IterationLimit = 3 };

			var flow = new IteratedDistFlowProvider(options).ComputeFlow(FlowProblem(network, demands));

			Assert.AreEqual(FlowStatus.Failed, flow.Status);
			Assert.AreEqual("IteratedDistFlow did not converge within the iteration limit (3 iterations)", flow.StatusDetails);
		}

		/// <summary>
		/// The iteration limit is finite by default
		/// </summary>
		[TestMethod]
		public void IteratedDistFlowHasAFiniteDefaultIterationLimit()
		{
			Assert.IsTrue(IteratedDistFlowProvider.DefaultOptions.IterationLimit <= 100_000);
		}

		#endregion

		#region Utilities

		/// <summary>
		/// Computes the iterated DistFlow with the default options, and checks that it is exact and consistent
		/// </summary>
		internal static IPowerFlow AssertExactAndConsistentFlow(FlowProblem problem)
		{
			var flow = new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions).ComputeFlow(problem);

			Assert.AreEqual(FlowStatus.Exact, flow.Status, flow.StatusDetails);
			Assert.AreEqual(
				"NoCurrent: ok; Current: ok; PowerLoss: ok; LineBalance: ok; NodeBalance: ok; " +
				"PowerLossTransformer: ok; Transformer voltage: ok; TransformerLineBalance: ok",
				flow.FlowConsistencyReportWithTransformers());

			return flow;
		}

		/// <summary>
		/// Returns the flow problem for the network with all switches closed, and the given demands
		/// </summary>
		internal static FlowProblem FlowProblem(PowerNetwork network, PowerDemands demands)
		{
			return new FlowProblem(new NetworkConfiguration(network, new SwitchSettings(network)), demands);
		}

		/// <summary>
		/// Asserts that <paramref name="actual"/> equals <paramref name="expected"/> within the given relative tolerance
		/// </summary>
		internal static void AssertRelative(double expected, double actual, double relativeTolerance, string message)
		{
			Assert.AreEqual(expected, actual, relativeTolerance * Math.Abs(expected), message);
		}

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
