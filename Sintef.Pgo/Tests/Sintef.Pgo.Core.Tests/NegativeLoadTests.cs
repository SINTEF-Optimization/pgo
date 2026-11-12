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

		/// <summary>
		/// The JSON input accepts a negative active load. Baran-Wu's demands are read, the active load of
		/// bus 2 is made negative, and the demands are parsed.
		/// </summary>
		[TestMethod]
		public void JsonDemandsWithNegativeActiveLoadAreParsed()
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("baran-wu.json"));
			var input = Newtonsoft.Json.JsonConvert.DeserializeObject<DataContracts.Demand>(
				System.IO.File.ReadAllText(TestUtils.TestDataFile("baran-wu_forecast.json")));
			input.Loads.Single(l => l.NodeId == "2").ActiveLoads = new List<double> { -0.1 };

			var demands = IO.PgoJsonParser.ParseDemands(network, input).Single().Demands;

			Assert.AreEqual(-0.1e6, demands.ActivePowerDemand(network.GetBus("2")), 1e-6);
			Assert.AreEqual(0.06e6, demands.PowerDemand(network.GetBus("2")).Imaginary, 1e-6);
			Assert.AreEqual(0.09e6, demands.ActivePowerDemand(network.GetBus("3")), 1e-6);
		}

		/// <summary>
		/// In the flows of a JSON solution, each line is presented from the end where active power enters it,
		/// and the reported power is the power injected there. Under reverse flow this is the downstream end.
		/// In Baran-Wu with every consumer exporting, every line has reverse flow.
		/// </summary>
		[TestMethod]
		public void JsonSolutionReportsPowerInjectedAtTheSendingEnd()
		{
			var (network, demands) = BaranWuWithExport(k => true);
			var flowProvider = new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions);
			var solution = new PgoSolution(new PgoProblem(new PeriodData(network, demands, Period.Default), flowProvider));
			var flow = solution.SinglePeriodSolutions.Single().Flow(flowProvider);

			var reported = IO.PgoJsonParser.ConvertToJson(solution, flowProvider).Flows.Single();

			var entries = reported.Powers.SelectMany(kv => kv.Value.Select(p => (from: network.GetBus(kv.Key), power: p))).ToList();
			Assert.AreEqual(network.Lines.Count(), entries.Count);
			foreach (var (from, power) in entries)
			{
				var line = network.GetLine(power.LineId);
				Assert.IsTrue(power.ActivePower > 0, $"line {line.Name}");
				Assert.AreEqual(flow.PowerFlow(from, line).Real / 1e6, power.ActivePower, 1e-12, $"line {line.Name}");
				Assert.AreEqual(flow.PowerLoss(line) / 1e6, power.PowerLoss, 1e-12, $"line {line.Name}");
			}

			// The line from the provider (bus 1) to bus 2 carries power from bus 2 back to the provider
			Assert.IsTrue(reported.Powers["2"].Any(p => p.Target == "1"));
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

		#region Automatic transformers

		/// <summary>
		/// Iterated DistFlow with exporters on either side of an automatic transformer, which holds its output
		/// voltage whatever the direction of flow.
		/// "downstream": exporters behind the transformer. Strong reverse flow, near zero active power
		///   with a large reactive flow, slightly reverse active power, and forward flow.
		/// "upstream": an exporter on the input side, behind a weak line. Strong export, demands summing to zero,
		///   and an export that raises the transformer's input voltage about 9 percent above nominal.
		/// The flow is exact and consistent, the output voltage stays at the output end voltage, the transformer
		/// loss is positive, the generator supplies the demands plus all losses, and the solver converges in few iterations.
		/// </summary>
		[DataTestMethod]
		[DataRow("downstream")]
		[DataRow("upstream")]
		[Timeout(30000)]
		public void IteratedDistFlowWithAutomaticTransformer(string exporters)
		{
			var (builder, outputLine, periods) = AutomaticTransformerCase(exporters);
			var network = builder.Network;

			foreach (var (demands, k) in periods.Select((d, k) => (d, k)))
			{
				SetDemands(builder, demands);
				string where = $"period {k}";

				var flow = AssertExactAndConsistentFlow(builder.FlowProblem);

				Assert.AreEqual(10500, flow.Voltage(network.GetBus("TS")).Magnitude, 1e-9, where);
				Assert.IsTrue(flow.PowerLoss(outputLine) > 0, $"{where}: transformer loss {flow.PowerLoss(outputLine)}");

				double demand = network.Consumers.Sum(b => builder.Demands.ActivePowerDemand(b));
				double loss = network.Lines.Sum(l => flow.PowerLoss(l));
				AssertRelative(demand + loss, flow.PowerInjection(network.GetBus("P")).Real, 1e-9, $"{where}: generation equals demand plus losses");

				int iterations = int.Parse(flow.StatusDetails.Split(' ')[0]);
				Assert.IsTrue(iterations < 50, $"{where}: {flow.StatusDetails}");
			}
		}

		/// <summary>
		/// SimplifiedDistFlow keeps the automatic transformer's output voltage under reverse flow and reports a
		/// non-negative transformer loss. Its voltages are within 2 percent of Iterated DistFlow's. The largest error
		/// is at the exporter on the weak line upstream of the transformer, with the largest export, and is the usual
		/// error of the simplified model under heavy loading, not an effect of the transformer.
		/// </summary>
		[DataTestMethod]
		[DataRow("downstream")]
		[DataRow("upstream")]
		public void SimplifiedDistFlowHandlesAutomaticTransformerWithExporters(string exporters)
		{
			var (builder, outputLine, periods) = AutomaticTransformerCase(exporters);
			var network = builder.Network;

			foreach (var (demands, k) in periods.Select((d, k) => (d, k)))
			{
				SetDemands(builder, demands);

				var flow = new SimplifiedDistFlowProvider().ComputeFlow(builder.FlowProblem);
				var exact = new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions).ComputeFlow(builder.FlowProblem);

				Assert.AreEqual(10500, flow.Voltage(network.GetBus("TS")).Magnitude, 1e-9);
				Assert.IsTrue(flow.PowerLoss(outputLine) >= 0);
				foreach (var bus in network.Buses.Where(b => !b.IsTransformer))
					AssertRelative(exact.Voltage(bus).Magnitude, flow.Voltage(bus).Magnitude, 2e-2, $"period {k}: voltage at {bus.Name}");
			}
		}

		/// <summary>
		/// Returns a network with a provider P at 20 kV, a consumer A, and an automatic transformer (pf 0.98)
		/// to 10.5 kV that feeds consumers E and F, and returns the transformer's connection line on the output side.
		/// Also returns, for each period, the demands of A, E and F, in VA.
		/// The output end voltage (10.5 kV) differs from what a fixed ratio would give, so the regulation is visible.
		/// With <paramref name="exporters"/> "downstream", E exports; with "upstream", A exports, and the line
		/// to A is weak.
		/// </summary>
		private static (NetworkBuilder builder, Line outputLine, Complex[][] periods) AutomaticTransformerCase(string exporters)
		{
			string z1 = exporters == "downstream" ? "(0.4, 0.3)" : "(4, 3)";
			var builder = new NetworkBuilder();
			builder.Add($"P[generatorVoltage=20000] -- L1[z={z1}] -- A[consumer] -- L4[z=(0.3, 0.2)] -- TP");
			builder.Add("TS -- L2[z=(0.2, 0.15)] -- E[consumer] -- L3[z=(0.1, 0.1)] -- F[consumer]");
			builder.AddBus("T[transformer; ends=(TP, TS); voltages=(20000, 10500); operation=auto; factor=0.98]");
			var outputLine = builder.Network.Lines.Single(l => l.IsTransformerConnection && l.Endpoints.Any(b => b.Name == "TS"));

			Complex[][] periodsMVA = exporters == "downstream"
				? new[]
				{
					new Complex[] { new(0.4, 0.1), new(-3.0, 0.2), new(0.3, 0.1) },
					new Complex[] { new(0.4, 0.1), new(-1.0, 0.0), new(1.0, 0.8) },
					new Complex[] { new(0.4, 0.1), new(-1.02, 0.0), new(1.0, 0.8) },
					new Complex[] { new(0.4, 0.1), new(0.5, 0.1), new(0.3, 0.1) },
				}
				: new[]
				{
					new Complex[] { new(-4.0, 0.1), new(0.5, 0.1), new(0.7, 0.2) },
					new Complex[] { new(-1.2, -0.3), new(0.5, 0.1), new(0.7, 0.2) },
					new Complex[] { new(-12.0, 0.0), new(0.5, 0.1), new(0.7, 0.2) },
				};

			return (builder, outputLine, periodsMVA.Select(p => p.Select(s => 1e6 * s).ToArray()).ToArray());
		}

		/// <summary>
		/// Sets the demands of consumers A, E and F in a network from <see cref="AutomaticTransformerCase"/>
		/// </summary>
		private static void SetDemands(NetworkBuilder builder, Complex[] demands)
		{
			foreach (var (name, demand) in new[] { "A", "E", "F" }.Zip(demands))
				builder.Demands.SetPowerDemand(builder.Network.GetBus(name), demand);
		}

		/// <summary>
		/// LegalMove and DeltaValue agree with the full evaluation for the loss, voltage, line capacity and provider
		/// capacity criteria when moves shift exporters between automatic transformers and reverse the flow
		/// through them
		/// </summary>
		[TestMethod]
		public void CriteriaDeltasAreCorrectWithAutomaticTransformers()
		{
			var flowProvider = new SimplifiedDistFlowProvider();
			var builder = AutomaticTransformerNetworkWithExporters(flowProvider);
			PgoSolution solution = builder.SinglePeriodSolution;
			var moves = TestUtils.AllMovesFor(solution).ToList();
			Assert.IsTrue(moves.Count > 3);

			TestUtils.VerifyDeltaValue(new TotalLossObjective(flowProvider), moves, 1e-9);

			var voltage = new ConsumerVoltageLimitsConstraint(flowProvider, 0.3);
			TestUtils.VerifyLegalMove(voltage, moves);
			TestUtils.VerifyDeltaValue(voltage, moves, 1e-9);

			var capacity = new SubstationCapacityConstraint(flowProvider);
			TestUtils.VerifyLegalMove(capacity, moves);
			TestUtils.VerifyDeltaValue(capacity, moves, 1e-9);

			TestUtils.VerifyDeltaValue(new LineCapacityCriterion(flowProvider), moves, 1e-9);
		}

		/// <summary>
		/// A short optimization on a network with automatic transformers, where the flow through the
		/// transformers changes direction between periods, completes. Iterated DistFlow converges for
		/// the best solution in every period, and every objective component is non-negative.
		/// </summary>
		[TestMethod]
		public void OptimizerRunsWithAutomaticTransformersAndExporters()
		{
			var builder = AutomaticTransformerNetworkWithExporters(new SimplifiedDistFlowProvider());
			var network = builder.Network;

			// Period 0 has the builder's demands. In period 1 all exports are three times larger,
			// in period 2 every consumer draws a load, and in period 3 the demands sum to zero.
			var periodData = builder.RepeatedPeriodData(4).ToList();
			var consumers = network.Consumers.ToList();
			for (int k = 1; k < 4; ++k)
			{
				var demands = new PowerDemands(periodData[k].Demands);
				foreach (var consumer in consumers)
				{
					var demand = demands.PowerDemand(consumer);
					if (k == 1 && demand.Real < 0)
						demands.SetPowerDemand(consumer, new Complex(3 * demand.Real, demand.Imaginary));
					if (k == 2)
						demands.SetPowerDemand(consumer, new Complex(Math.Abs(demand.Real), demand.Imaginary));
				}
				if (k == 3)
				{
					double sum = demands.Sum.Real;
					var last = consumers.Last();
					demands.SetPowerDemand(last, demands.PowerDemand(last) - sum);
					Assert.AreEqual(0, demands.Sum.Real, 1e-6);
				}
				periodData[k] = new PeriodData(network, demands, periodData[k].Period);
			}

			var problem = new PgoProblem(periodData, new SimplifiedDistFlowProvider(), "automatic");
			var parameters = new OptimizerParameters
			{
				Algorithm = AlgorithmType.RuinAndRecreate,
				SimpleMPSolverSolverTimeOut = TimeSpan.FromSeconds(1)
			};
			var solution = new PgoSolution(problem);
			IOptimizer optimizer = new ConfigOptimizerFactory(new NetworkManager(problem.Network))
				.CreateOptimizer(parameters, solution, problem.CriteriaSet, new OptimizerEnvironment());
			var best = (PgoSolution)new OptimizerRunner(optimizer, parameters.SimpleMPSolverSolverTimeOut).Optimize(solution);

			var objective = (AggregateObjective)problem.CriteriaSet.Objective;
			foreach (var component in objective.Components)
				Assert.IsTrue(component.Value(best) >= 0, $"{component} is negative: {component.Value(best)}");

			var iterated = new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions);
			foreach (var periodSolution in best.SinglePeriodSolutions)
			{
				var flow = periodSolution.Flow(iterated);
				Assert.AreEqual(FlowStatus.Exact, flow.Status, $"period {periodSolution.Period.Index}: {flow.StatusDetails}");
				Assert.IsTrue(network.Buses.Where(b => !b.IsTransformer).All(b => double.IsFinite(flow.Voltage(b).Magnitude)));
			}
		}

		/// <summary>
		/// Returns a builder with two automatic transformers that feed a meshed secondary network with
		/// exporters, and an exporter on the input side of transformer T1
		/// </summary>
		private static NetworkBuilder AutomaticTransformerNetworkWithExporters(IFlowProvider flowProvider)
		{
			var builder = new NetworkBuilder(flowProvider);
			builder.Add("G1[generatorVoltage=20000; generationCapacity=(150000,1e9)] -- line1[z=(2,1)] -- X[consumption=(-50000,10000); vMinV=19000; vMaxV=20100] -- line2 -- T1_in");
			builder.Add("T1_out -- line3[closed; iMax=5] -- C1[consumption=(30000,5000); vMinV=10000; vMaxV=11000] -- line4[closed] -- C2[consumption=(-80000,0); vMinV=10000; vMaxV=10600] -- line5[open] -- C3[consumption=(-60000,20000); vMinV=9500; vMaxV=10050]");
			builder.Add("C3 -- line6[closed; iMax=5] -- C4[consumption=(20000,0); vMinV=9500; vMaxV=10500] -- line7[closed; iMax=5] -- T2_out");
			builder.Add("C1 -- line8[open] -- C4");
			builder.Add("C2 -- line9[open] -- C4");
			builder.Add("T2_in -- line10 -- G2[generatorVoltage=20000; generationCapacity=(100000,1e9)]");
			builder.AddBus("T1[transformer; ends=(T1_in,T1_out); voltages=(20000,10500); operation=auto; factor=0.98]");
			builder.AddBus("T2[transformer; ends=(T2_in,T2_out); voltages=(20000,10000); operation=auto; factor=0.95]");
			return builder;
		}

		#endregion

		#region Overvoltage

		/// <summary>
		/// An exporter at the end of a weak line raises the voltage above its upper limit. The voltage is
		/// checked against v_max with the computed flow.
		/// </summary>
		[DataTestMethod]
		[DataRow(FlowApproximation.IteratedDF)]
		[DataRow(FlowApproximation.SimplifiedDF)]
		public void OvervoltageFromExportIsAViolation(FlowApproximation approximation)
		{
			var flowProvider = Utils.CreateFlowProvider(approximation);
			var builder = new NetworkBuilder(flowProvider);
			builder.Add("G[generatorVoltage=20000] -- l1[z=(8,4)] -- A[consumption=(-3000000,0); vMinV=19000; vMaxV=21000]");

			PgoSolution solution = builder.SinglePeriodSolution;
			var constraint = new ConsumerVoltageLimitsConstraint(flowProvider, 0.3);
			var flow = solution.SinglePeriodSolutions.Single().Flow(flowProvider);
			double voltage = flow.Voltage(builder.Network.GetBus("A")).Magnitude;

			Assert.IsTrue(voltage > 21000, $"Voltage {voltage}");
			Assert.IsFalse(constraint.IsSatisfied(solution));
			Assert.AreEqual(voltage - 21000, constraint.Value(solution), 1e-6);
			Assert.IsTrue(constraint.VoltageCheck.HaveUsedFlowCalculation);
		}

		/// <summary>
		/// LegalMove and DeltaValue of the consumer voltage constraint agree with the full check
		/// when a move connects an exporter through a weak line
		/// </summary>
		[TestMethod]
		public void OvervoltageDeltasAreCorrect()
		{
			var flowProvider = new SimplifiedDistFlowProvider();
			var builder = new NetworkBuilder(flowProvider);
			builder.Add("G1[generatorVoltage=20000] -- l1[z=(0.5,0.3); closed] -- A[consumption=(-3000000,0); vMinV=19000; vMaxV=21000]");
			builder.Add("A -- l2[z=(8,4); open] -- B[consumption=(100000,0); vMinV=19000; vMaxV=21000] -- l3[z=(0.5,0.3)] -- G2[generatorVoltage=20000]");
			builder.Add("A -- l5[z=(0.2,0.1); open] -- G2");

			PgoSolution solution = builder.SinglePeriodSolution;
			var periodSolution = solution.SinglePeriodSolutions.Single();
			var moves = new List<Move>();
			foreach (Line switchToClose in periodSolution.OpenSwitches.ToList())
			{
				var neighbourhood = new CloseSwitchAndOpenOtherNeighbourhood(switchToClose, periodSolution.Period);
				neighbourhood.Init(solution);
				moves.AddRange(neighbourhood);
			}

			var constraint = new ConsumerVoltageLimitsConstraint(flowProvider, 0.3);

			TestUtils.VerifyLegalMove(constraint, moves);
			TestUtils.VerifyDeltaValue(constraint, moves, 1e-9);
		}

		/// <summary>
		/// An overvoltage from export is a violation also when the consumer's lower voltage limit is very low
		/// (here 0, the default). BusVoltageChecker does not assume that the voltage stays below nominal.
		/// </summary>
		[TestMethod]
		public void OvervoltageIsAViolationAlsoWithALowLowerLimit()
		{
			var flowProvider = new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions);
			var builder = new NetworkBuilder(flowProvider);
			builder.Add("G[generatorVoltage=20000] -- l1[z=(8,4)] -- A[consumption=(-3000000,0); vMinV=0; vMaxV=21000]");

			PgoSolution solution = builder.SinglePeriodSolution;
			var constraint = new ConsumerVoltageLimitsConstraint(flowProvider, 0.3);

			Assert.IsFalse(constraint.IsSatisfied(solution));
		}

		#endregion

		#region KILE

		/// <summary>
		/// KILE uses the net load truncated at zero, per consumer and period. An exporting consumer
		/// costs nothing (rather than a negative cost, which would reward disconnecting it).
		/// </summary>
		[DataTestMethod]
		[DataRow(true)]
		[DataRow(false)]
		public void KileCostUsesNetLoadTruncatedAtZero(bool cacheExpectedConsumption)
		{
			Random random = new Random(3);
			var (problem, signedData, truncatedData) = RandomProblemWithExporters(random);
			var network = problem.Network;

			var faultProperties = LineFaultPropertiesProvider.RandomFor(network, new Random(4));
			var categories = ConsumerCategoryProvider.RandomFor(network, new Random(5));
			var periods = problem.Periods.ToList();
			var signedObjective = new KileCostObjective(network, periods, new ExpectedConsumptionFromDemand(signedData), faultProperties, categories, cacheExpectedConsumption);
			var truncatedObjective = new KileCostObjective(network, periods, new ExpectedConsumptionFromDemand(truncatedData), faultProperties, categories, cacheExpectedConsumption);

			for (int i = 0; i < 5; ++i)
			{
				var solution = problem.RadialSolution(random);

				double signedValue = signedObjective.Value(solution);
				double truncatedValue = truncatedObjective.Value(solution);

				Assert.IsTrue(signedValue >= 0, $"KILE cost {signedValue} is negative");
				Assert.AreEqual(truncatedValue, signedValue, 1e-9 * truncatedValue);
			}
		}

		/// <summary>
		/// The KILE delta value agrees with the full value when some consumers export
		/// </summary>
		[TestMethod]
		public void KileCostDeltaIsCorrectWithExporters()
		{
			Random random = new Random(6);
			var (problem, signedData, _) = RandomProblemWithExporters(random);
			var network = problem.Network;

			var objective = new KileCostObjective(network, problem.Periods.ToList(), new ExpectedConsumptionFromDemand(signedData),
				LineFaultPropertiesProvider.RandomFor(network, new Random(7)), ConsumerCategoryProvider.RandomFor(network, new Random(8)), true);

			var solution = problem.RadialSolution(random);
			var moves = TestUtils.AllMovesFor(solution);
			Assert.IsTrue(moves.Any());

			TestUtils.VerifyDeltaValue(objective, moves, tolerance: 1e-10);
		}

		/// <summary>
		/// Returns a random problem with 4 periods in which every second consumer exports in odd periods, and
		/// the period data with signed demands and with the demands truncated at zero.
		/// The problem uses the signed demands.
		/// </summary>
		private static (PgoProblem problem, List<PeriodData> signedData, List<PeriodData> truncatedData) RandomProblemWithExporters(Random random)
		{
			var network = TestUtils.SmallRandomNetwork(random);
			var problem = TestUtils.ProblemWithRandomDemands(network, random, periodCount: 4);

			var consumers = network.Consumers.ToList();
			var signedData = new List<PeriodData>();
			var truncatedData = new List<PeriodData>();
			foreach (var (data, k) in problem.AllPeriodData.Select((d, k) => (d, k)))
			{
				var signed = new PowerDemands(data.Demands);
				var truncated = new PowerDemands(data.Demands);
				for (int i = 0; i < consumers.Count; i += 2)
				{
					if (k % 2 == 1)
					{
						var demand = data.Demands.PowerDemand(consumers[i]);
						signed.SetPowerDemand(consumers[i], new Complex(-2 * demand.Real - 1000, demand.Imaginary));
						truncated.SetPowerDemand(consumers[i], new Complex(0, demand.Imaginary));
					}
				}
				signedData.Add(new PeriodData(network, signed, data.Period));
				truncatedData.Add(new PeriodData(network, truncated, data.Period));
			}
			Assert.IsTrue(signedData.Any(d => consumers.Any(c => d.Demands.ActivePowerDemand(c) < 0)), "No negative demand");

			var signedProblem = new PgoProblem(signedData, new SimplifiedDistFlowProvider(), "exporters");
			return (signedProblem, signedData, truncatedData);
		}

		#endregion

		#region Provider capacity

		/// <summary>
		/// The capacity limits the active power through the provider in both directions.
		/// In Baran-Wu with every consumer exporting, the provider absorbs the export less the losses.
		/// </summary>
		[TestMethod]
		public void ProviderCapacityLimitsAbsorbedPower()
		{
			var (network, demands) = BaranWuWithExport(k => true);
			var flowProvider = new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions);
			var problem = new PgoProblem(new PeriodData(network, demands, Period.Default), flowProvider);
			var solution = new PgoSolution(problem);
			var provider = network.Providers.Single();
			var constraint = new SubstationCapacityConstraint(flowProvider);

			double export = -network.Consumers.Sum(b => demands.ActivePowerDemand(b));
			double absorbed = -solution.SinglePeriodSolutions.Single().Flow(flowProvider).PowerInjection(provider).Real;
			Assert.IsTrue(0 < absorbed && absorbed < export, $"absorbed {absorbed}, export {export}");

			provider.ActiveGenerationCapacity = 1.1 * absorbed;
			Assert.IsTrue(constraint.IsSatisfied(solution));
			Assert.AreEqual(0, constraint.Value(solution));

			double capacity = 0.9 * absorbed;
			provider.ActiveGenerationCapacity = capacity;
			Assert.IsFalse(constraint.IsSatisfied(solution));
			Assert.AreEqual(Math.Pow(absorbed - capacity, 2), constraint.Value(solution), 1e-6 * Math.Pow(absorbed - capacity, 2));
			StringAssert.Contains(constraint.Reason(solution), "Capacity of Provider");
		}

		/// <summary>
		/// LegalMove and DeltaValue agree with the full check when moves change the power absorbed by providers
		/// </summary>
		[TestMethod]
		public void ProviderCapacityDeltasAreCorrectForAbsorbedPower()
		{
			var flowProvider = new SimplifiedDistFlowProvider();
			var builder = new NetworkBuilder(flowProvider);
			builder.Add("G1[generatorVoltage=1000; generationCapacity=(1000,1e9)] -- l1 -- A[consumption=(-300,0)] -- l2[closed] -- B[consumption=(-300,0)]");
			builder.Add("B -- l3[open] -- C[consumption=(-300,0)] -- l4[closed] -- D[consumption=(-300,0)] -- l5 -- G2[generatorVoltage=1000; generationCapacity=(700,1e9)]");

			PgoSolution solution = builder.SinglePeriodSolution;
			var periodSolution = solution.SinglePeriodSolutions.Single();
			var moves = new List<Move>();
			foreach (Line switchToClose in periodSolution.OpenSwitches.ToList())
			{
				var neighbourhood = new CloseSwitchAndOpenOtherNeighbourhood(switchToClose, periodSolution.Period);
				neighbourhood.Init(solution);
				moves.AddRange(neighbourhood);
			}

			var constraint = new SubstationCapacityConstraint(flowProvider);

			// Opening l2 makes G2 absorb 900 W > 700 W; opening l4 makes G1 absorb 900 W < 1000 W
			TestUtils.VerifyLegalMove(constraint, moves);
			TestUtils.VerifyDeltaValue(constraint, moves, 1e-9);
		}

		#endregion

		#region Session checks and optimization

		/// <summary>
		/// The input analysis at session creation compares the magnitude of the total net demand with the
		/// magnitude of the total generation capacity. A net export within the capacity passes. A net export
		/// larger than the capacity is reported, consistent with the capacity limiting power in both directions.
		/// </summary>
		[TestMethod]
		public void SessionAnalysisHandlesNetExport()
		{
			var (network, demands) = BaranWuWithExport(k => true);
			var problem = new PgoProblem(new PeriodData(network, demands, Period.Default), new SimplifiedDistFlowProvider());
			Assert.IsTrue(problem.AllPeriodData.Single().Demands.Sum.Real < 0);

			var (ok, description) = problem.AnalyseDemandsCurrentsAndVoltages(network);
			Assert.IsTrue(ok, description);

			network.Providers.Single().GenerationCapacity = new Complex(1e6, 1e6);
			(ok, description) = problem.AnalyseDemandsCurrentsAndVoltages(network);
			Assert.IsFalse(ok);
			StringAssert.Contains(description, "total generation capacity");
		}

		/// <summary>
		/// The optimizer assumes a lower bound of zero on the objective. With exporting consumers every
		/// objective component stays non-negative, and a short optimization runs without errors.
		/// </summary>
		[TestMethod]
		public void OptimizerRunsWithExportersAndObjectiveIsNonNegative()
		{
			Random random = new Random(9);
			var (problem, _, _) = RandomProblemWithExporters(random);

			var parameters = new OptimizerParameters
			{
				Algorithm = AlgorithmType.RuinAndRecreate,
				SimpleMPSolverSolverTimeOut = TimeSpan.FromSeconds(1)
			};
			var solution = new PgoSolution(problem);
			IOptimizer optimizer = new ConfigOptimizerFactory(new NetworkManager(problem.Network))
				.CreateOptimizer(parameters, solution, problem.CriteriaSet, new OptimizerEnvironment());
			var best = (PgoSolution)new OptimizerRunner(optimizer, parameters.SimpleMPSolverSolverTimeOut).Optimize(solution);

			var objective = (AggregateObjective)problem.CriteriaSet.Objective;
			foreach (var component in objective.Components)
				Assert.IsTrue(component.Value(best) >= 0, $"{component} is negative: {component.Value(best)}");
			Assert.IsTrue(objective.Value(best) >= 0);
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
