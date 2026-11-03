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

		#region Transformer loss under reverse flow

		/// <summary>
		/// An exporter behind a transformer. With E exporting, the flow through the transformer is reversed;
		/// with E consuming, it is forward. The transformer loss is (1 - pf) times the power that enters the
		/// transformer, in both directions, so it is positive, and the generator supplies the demands plus all losses.
		/// </summary>
		[DataTestMethod]
		[DataRow(-1.5e6, DisplayName = "reverse flow")]
		[DataRow(0.5e6, DisplayName = "forward flow")]
		public void IteratedDistFlowWithFlowThroughTransformer(double exporterDemand)
		{
			var (builder, outputLine) = ExporterBehindTransformer(exporterDemand);
			var network = builder.Network;

			var flow = AssertExactAndConsistentFlow(builder.FlowProblem);

			Assert.IsTrue(flow.PowerLoss(outputLine) > 0, $"transformer loss {flow.PowerLoss(outputLine)}");

			double demand = network.Consumers.Sum(b => builder.Demands.ActivePowerDemand(b));
			double loss = network.Lines.Sum(l => flow.PowerLoss(l));
			AssertRelative(demand + loss, flow.PowerInjection(network.GetBus("P")).Real, 1e-9, "generation equals demand plus losses");
		}

		/// <summary>
		/// SimplifiedDistFlow ignores the transformer loss in the flow, but reports it as
		/// (1 - pf) times the power through the transformer. That must be positive under reverse flow too.
		/// </summary>
		[DataTestMethod]
		[DataRow(-1.5e6, DisplayName = "reverse flow")]
		[DataRow(0.5e6, DisplayName = "forward flow")]
		public void SimplifiedDistFlowTransformerLossIsPositive(double exporterDemand)
		{
			var (builder, outputLine) = ExporterBehindTransformer(exporterDemand);
			var transformerBus = outputLine.Transformer.Bus;

			var flow = new SimplifiedDistFlowProvider().ComputeFlow(builder.FlowProblem);

			double throughput = flow.PowerFlow(transformerBus, outputLine).Real;
			Assert.AreEqual(0.02 * Math.Abs(throughput), flow.PowerLoss(outputLine), 1e-9 * Math.Abs(throughput));
			Assert.IsTrue(flow.PowerLoss(outputLine) > 0);
		}

		/// <summary>
		/// Returns a network with a provider P at 20 kV, a fixed ratio transformer (pf 0.98) to 10 kV, and two
		/// consumers E and F behind it. E has the given active demand and F consumes 0.3 MW. Also returns the
		/// transformer's connection line on the output side.
		/// </summary>
		private static (NetworkBuilder builder, Line outputLine) ExporterBehindTransformer(double exporterDemand)
		{
			var builder = new NetworkBuilder();
			builder.Add("P[generatorVoltage=20000] -- L1[z=(0.4, 0.3)] -- TP");
			builder.Add("TS -- L2[z=(0.2, 0.15)] -- E[consumer]");
			builder.Add("TS -- L3[z=(0.1, 0.1)] -- F[consumption=(3e5, 1e5)]");
			builder.AddBus("T[transformer; ends=(TP, TS); voltages=(20000, 10000); operation=fixed; factor=0.98]");
			builder.Demands.SetPowerDemand(builder.Network.GetBus("E"), new Complex(exporterDemand, 1e5));

			var outputLine = builder.Network.Lines.Single(l => l.IsTransformerConnection && l.Endpoints.Any(b => b.Name == "TS"));
			return (builder, outputLine);
		}

		/// <summary>
		/// A move that sends an export through a transformer with a lower power factor
		/// increases the total loss. The delta value agrees, and the loss is positive before and after.
		/// </summary>
		[TestMethod]
		public void TotalLossDeltaIsCorrectForExportThroughTransformers()
		{
			var flowProvider = new SimplifiedDistFlowProvider();
			var builder = new NetworkBuilder(flowProvider);
			builder.Add("N1[generatorVoltage=11000] -- l1[closed] -- N_T1_in");
			builder.Add("N_T1_out -- l3[closed] -- Join");
			builder.AddBus("Transformer1[transformer; ends=(N_T1_in, N_T1_out); voltages=(11000,220); operation=fixed; factor=0.9]");

			builder.Add("N2[generatorVoltage=11000] -- l2[closed] -- N_T2_in");
			builder.Add("N_T2_out -- l4[open] -- Join");
			builder.AddBus("Transformer2[transformer; ends=(N_T2_in, N_T2_out); voltages=(11000,220); operation=fixed; factor=0.8]");

			builder.Add("Join -- l5 -- Exporter[consumption=(-10000,0)]");

			PgoSolution solution = builder.SinglePeriodSolution;
			var objective = new TotalLossObjective(flowProvider);
			var move = solution.SwapMove("l3", "l4");

			double before = objective.Value(solution);
			double delta = objective.DeltaValue(move);
			move.Apply(true);
			double after = objective.Value(solution);

			// Losses in MWh over the period
			double hours = solution.SinglePeriodSolutions.Single().Period.Length.TotalHours;
			double lineLossMWh = builder.Network.Lines.Where(l => !l.IsTransformerConnection)
				.Sum(l => solution.SinglePeriodSolutions.Single().Flow(flowProvider).PowerLoss(l)) * hours / 1e6;

			Assert.IsTrue(before > 0);
			Assert.IsTrue(after > 0);
			Assert.AreEqual(after - before, delta, 1e-12);

			// Transformer loss goes from 10% to 20% of the 10 kW export
			Assert.AreEqual(0.2 * 10000 * hours / 1e6, after - lineLossMWh, 1e-12);
			Assert.AreEqual(1000 * hours / 1e6, delta, 1e-6 * 1000 * hours / 1e6 + Math.Abs(lineLossMWh));
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
