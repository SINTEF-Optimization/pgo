using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class SimplifiedDistflowCorrectnessTests
	{
		readonly NetworkBuilder _builder = new NetworkBuilder(new SimplifiedDistFlowProvider());

		[TestMethod]
		public void VerifyTrivialCase()
		{
			var line1 = _builder.AddLine("Producer[generatorVoltage=1000.0] -- Line1[r=1] -- Consumer1[consumption=(100, 0)]");
			var line2 = _builder.AddLine("Consumer1 -- Line2[r=0] -- Consumer2[consumption=(100, 0)]");

			FlowProblem flowProblem = _builder.FlowProblem;
			var flow = new SimplifiedDistFlowProvider().ComputeFlow(flowProblem);

			Assert.AreEqual(0.2, flow.Current(line1));
			Assert.AreEqual(0.1, flow.Current(line2));

			Assert.AreEqual(
				"NoCurrent: ok; " +
				"Current: ok; " +
				"PowerLoss: ok; " +
				"LineBalance: rms 0.02828, max 0.04 at Line1; " +
				"NodeBalance: ok",
				flow.FlowConsistencyReport());

			flow.Write();
		}


		[TestMethod]
		public void VerifyBaranWu()
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("baran-wu.json"));
			var periodData = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, TestUtils.TestDataFile("baran-wu_forecast.json"));

			var config = new NetworkConfiguration(network, new SwitchSettings(network));
			var problem = new FlowProblem(config, periodData[0].Demands); //Only one period here

			var flow = new SimplifiedDistFlowProvider().ComputeFlow(problem);

			Bus start = network.GetBus("6");
			Bus end = network.GetBus("26");
			Line line = network.GetLine(start, end);

			Complex powerFlow = flow.PowerFlow(start, line);
			AssertAreEqual(920000, 950000, powerFlow, 1e-10);

			start = network.GetBus("2");
			end = network.GetBus("3");
			line = network.GetLine(start, end);

			powerFlow = flow.PowerFlow(start, line);
			AssertAreEqual(3255000, 2080000, powerFlow, 1e-10);

			Assert.AreEqual(
				"NoCurrent: ok; " +
				"Current: ok; " +
				"PowerLoss: ok; " +
				"LineBalance: rms 1.346E+04, max 5.151E+04 at line1; " +
				"NodeBalance: ok",
				flow.FlowConsistencyReport());

		}

		[TestMethod]
		public void VerifyIEEE34()
		{
			var flowProvider = new SimplifiedDistFlowProvider();
			PgoProblem problem = new PgoProblem(IEEE34NetworkMaker.IEEE34(), flowProvider);
			PgoSolution solution = new PgoSolution(problem);
			PeriodSolution perSol = solution.SinglePeriodSolutions.Single();
			var network = problem.Network;

			perSol.SetSwitch("830", "854", true);
			Assert.IsTrue(perSol.IsRadial);


			Assert.IsTrue(solution.ComputeFlow(perSol.Period, flowProvider));
			IPowerFlow flow = perSol.Flow(flowProvider);

			Bus start = network.GetBus("806");
			Bus end = network.GetBus("808");
			Line line = network.GetLine(start, end);

			Complex powerFlow = flow.PowerFlow(start, line);
			AssertAreEqual(360000000, 40000000, powerFlow, 1e-3);

			start = network.GetBus("812");
			end = network.GetBus("814");
			line = network.GetLine(start, end);

			powerFlow = flow.PowerFlow(start, line);
			AssertAreEqual(270000000, 30000000, powerFlow, 1e-3);
		}

		[TestMethod]
		public void FlowDeltasAreCorrectForIEEE34()
		{
			var flowProvider = new SimplifiedDistFlowProvider();

			// Set up a problem
			PeriodData perDat = IEEE34NetworkMaker.IEEE34();
			PgoProblem problem = new PgoProblem(perDat, flowProvider);
			PgoSolution mpSol = new PgoSolution(problem);
			var network = problem.Network;

			// Create a single period solution data
			PeriodSolution solution = mpSol.GetPeriodSolution(perDat.Period);

			solution.SetSwitch("830", "854", true);
			Assert.IsTrue(solution.IsRadial);

			Assert.IsTrue(solution.ComputeFlow(flowProvider));

			// Create a move
			SwapSwitchStatusMove move = new SwapSwitchStatusMove(mpSol, perDat.Period, network.GetLine("814", "850"), network.GetLine("830", "854"));

			VerifyFlowDelta(new[] { move });
		}

		/// <summary>
		/// Tests delta flow computation for a simple case with slightly different generator voltages.
		/// </summary>
		[TestMethod]
		public void FlowDeltasAreCorrectForIEEE34DifferentGenV()
		{
			var flowProvider = new SimplifiedDistFlowProvider();

			// Set up a problem
			PeriodData perDat = IEEE34NetworkMaker.IEEE34WithDifferentGenVoltages();
			PgoProblem problem = new PgoProblem(perDat, flowProvider);
			PgoSolution mpSol = new PgoSolution(problem);
			var network = problem.Network;

			// Create a single period solution data
			PeriodSolution solution = mpSol.GetPeriodSolution(perDat.Period);

			solution.SetSwitch("830", "854", true);
			Assert.IsTrue(solution.IsRadial);

			Assert.IsTrue(solution.ComputeFlow(flowProvider));

			// Create a move
			SwapSwitchStatusMove move = new SwapSwitchStatusMove(mpSol, perDat.Period, network.GetLine("814", "850"), network.GetLine("830", "854"));

			VerifyFlowDelta(new[] { move });
		}


		[TestMethod]
		public void FlowDeltasAreCorrectForTestNetwork()
		{
			var flowProvider = new SimplifiedDistFlowProvider();

			NetworkBuilder builder = TestUtils.SmallTestNetwork(flowProvider);

			PgoSolution solution = builder.SinglePeriodSolution;
			PeriodSolution perSol = solution.SinglePeriodSolutions.Single();
			Assert.IsTrue(perSol.IsRadial);

			Assert.IsTrue(perSol.ComputeFlow(flowProvider));

			foreach (var lineToClose in perSol.OpenSwitches.ToList())
			{
				CloseSwitchAndOpenOtherNeighbourhood nh = new CloseSwitchAndOpenOtherNeighbourhood(lineToClose, perSol.Period);
				nh.Init(solution);

				VerifyFlowDelta(nh.Cast<SwapSwitchStatusMove>());
			}
		}

		[TestMethod]
		public void TestTopologicalRelationsInFlowDelta()
		{
			Random random = new Random(42); ;

			// Use a random period length, to avoid coincidences
			PgoProblem problem = TestUtils.SetupProblemWithRandomData(random, periodCount: 7, sizeCategory: 1);

			var solution = problem.RadialSolution(random);

			for (int i = 0; i < 20; ++i)
			{
				var moves = solution.GetAllRadialSwapMoves().Shuffled(random).Take(100).ToList();
				TestUtils.VerifyDeltaTopology(moves);
			}
		}

		[TestMethod]
		public void ProviderCanTransferAggregatedFlowToOriginalNetwork()
		{
			// Create a network with parallel and serial lines

			_builder.Add("Producer[generatorVoltage=1000.0] -- Line1[r=1] -- Consumer1[consumption=(100, 0)]");
			_builder.Add("Producer -- Line2[r=1] -o- Line3[z=(1,1)] -- Consumer1");
			// Add also an unconnected component. Unconnected components should be removed by aggregation,
			// and added back with zero voltage and current into the original problem.
			// Note that if we had a consumer demand at any of these buses, the node balance in the
			// FlowConsistencyReport would not be ok (and this would be correct).
			_builder.Add("BusUnconnected1 -- LineUnconnected -- BusUnconnected2");

			// Build aggregated problem

			var aggregation = NetworkAggregation.MakeAcyclicAndConnected(_builder.Network);
			var originalProblem = _builder.SinglePeriodProblem;
			var aggregatedProblem = originalProblem.CreateAggregatedProblem(aggregation, false);

			// The two unconnected buses should be added to the aggregation's UnconnectedBuses list.
			Assert.AreEqual(aggregation.UnconnectedBuses.Where(b => b.Name == "BusUnconnected1").Count(), 1);
			Assert.AreEqual(aggregation.UnconnectedBuses.Where(b => b.Name == "BusUnconnected2").Count(), 1);

			// Solve flow problem

			FlowProblem originalFlowProblem = new FlowProblem(
				NetworkConfiguration.AllClosed(originalProblem.Network),
				originalProblem.AllPeriodData.Single().Demands);
			FlowProblem aggregateFlowProblem = new FlowProblem(
				NetworkConfiguration.AllClosed(aggregatedProblem.Network),
				aggregatedProblem.AllPeriodData.Single().Demands);

			SimplifiedDistFlowProvider provider = new SimplifiedDistFlowProvider();
			var aggregateFlow = provider.ComputeFlow(aggregateFlowProblem);

			// For simplified DistFlow, the flow is consistent except for line balance

			Assert.AreEqual(
				"NoCurrent: ok; " +
				"Current: ok; " +
				"PowerLoss: ok; " +
				"LineBalance: rms 0.007071, max 0.007071 at [Line1 || Line2+Line3]; " +
				"NodeBalance: ok",
				aggregateFlow.FlowConsistencyReport());

			aggregateFlow.Write();

			// Disaggregate flow

			var originalFlow = provider.DisaggregateFlow(aggregateFlow, aggregation, NetworkConfiguration.AllClosed(originalProblem.Network), originalProblem.AllPeriodData.Single().Demands);

			// The disaggregated flow is equally consistent
			// (the rms/max error is smaller due to being distributed over more lines)

			Assert.AreEqual(
				"NoCurrent: ok; " +
				"Current: ok; " +
				"PowerLoss: ok; " +
				"LineBalance: rms 0.002646, max 0.005 at Line1; " +
				"NodeBalance: ok",
				originalFlow.FlowConsistencyReport());

			// The disaggregated flow has zero voltage and current at all unconnected buses.
			var maxUnconnectedVoltage = aggregation.UnconnectedBuses
				.Select(b => originalFlow.Voltage(aggregation.OriginalNetwork.GetBus(b.Name)).Magnitude).Max();
			var maxUnconnectedCurrent = aggregation.UnconnectedBuses.SelectMany(b => b.IncidentLines)
				.Select(l => originalFlow.Current(aggregation.OriginalNetwork.GetLine(l.Name)).Magnitude).Max();
			Assert.AreEqual(maxUnconnectedVoltage, 0.0);
			Assert.AreEqual(maxUnconnectedCurrent, 0.0);

			originalFlow.Write();
		}


		/// <summary>
		/// For each of the given moves, verifies that the power flows and currents reported by
		/// <see cref="SwapSwitchStatusMove.GetCachedPowerFlowDelta()"/> are correct.
		/// </summary>
		/// <param name="moves">The moves to check</param>
		private void VerifyFlowDelta(IEnumerable<SwapSwitchStatusMove> moves)
		{
			PgoSolution mpSol = moves.First().Solution as PgoSolution;
			Period per = mpSol.Problem.Periods.Single(); //Assuming thie solution is single period.
			var solution = mpSol.GetPeriodSolution(per);
			var solution2 = (mpSol.Clone() as PgoSolution).GetPeriodSolution(per);

			var network = mpSol.Problem.Network;
			IFlowProvider flowProvider = mpSol.Problem.FlowProvider;

			foreach (var move in moves)
			{
				// Compute the delta, then apply the move

				var delta = move.GetCachedPowerFlowDelta(flowProvider);

				move.Apply(true);


				// Verify that lines not in the delta have unchanged current and power

				IPowerFlow oldFlow = solution2.Flow(flowProvider);
				IPowerFlow newFlow = solution.Flow(flowProvider);

				foreach (var line in network.Lines)
				{
					if (!delta.LineDeltas.ContainsKey(line))
					{
						Assert.AreEqual(oldFlow.Current(line), newFlow.Current(line));
						Assert.AreEqual(oldFlow.PowerFlow(line.Node1, line), newFlow.PowerFlow(line.Node1, line));
						Assert.AreEqual(oldFlow.PowerFlow(line.Node2, line), newFlow.PowerFlow(line.Node2, line));
					}
				}

				// Verify the currents and power before and after in the delta

				foreach (var (line, lineDelta) in delta.LineDeltas)
				{
					Console.WriteLine($"{line} {lineDelta.OldCurrent} {lineDelta.NewCurrent}");

					AssertAreEqual(oldFlow.Current(line), lineDelta.OldCurrent, 1e-5);
					AssertAreEqual(newFlow.Current(line), lineDelta.NewCurrent, 1e-5);

					AssertAreEqual(oldFlow.PowerFlow(line.Node1, line), lineDelta.OldPowerFlowFrom(line.Node1), 1e-5);
					AssertAreEqual(newFlow.PowerFlow(line.Node1, line), lineDelta.NewPowerFlowFrom(line.Node1), 1e-5);
					AssertAreEqual(oldFlow.PowerFlow(line.Node2, line), lineDelta.OldPowerFlowFrom(line.Node2), 1e-5);
					AssertAreEqual(newFlow.PowerFlow(line.Node2, line), lineDelta.NewPowerFlowFrom(line.Node2), 1e-5);
				}

				// Verify the modified voltages in the delta
				foreach (var bus in network.Consumers)
				{
					if (delta.BusDeltas.ContainsKey(bus))
					{
						Complex newVoltage = delta.BusDeltas[bus].NewVoltage;
						AssertAreEqual(newFlow.Voltage(bus), newVoltage, 1e-5);
					}
					else
						AssertAreEqual(newFlow.Voltage(bus), oldFlow.Voltage(bus), 1e-5);
				}

				// Restore the solution

				move.GetReverse().Apply(true);
			}
		}



		[TestMethod]
		public void TransformerThreeWindingFlowIsCorrect()
		{
			_builder.Add("N1[generatorVoltage=22000] -- l1 -- N_T_in");
			_builder.Add("N_T_out1 -- l2 -- Consumer1[consumption=(100000,0)]");
			_builder.Add("N_T_out2 -- l3 -- Consumer2[consumption=(100000,0)]");
			_builder.AddBus("Transformer1[transformer; ends=(N_T_in, N_T_out1, N_T_out2); voltages=(22000, 11000,5500); operation=fixed; factor=0.98]");

			FlowProblem flowProblem = _builder.FlowProblem;
			var flow = new SimplifiedDistFlowProvider().ComputeFlow(flowProblem);

			var report = flow.FlowConsistencyReportWithTransformers();
			Assert.AreEqual(
				"NoCurrent: ok; " +
				"Current: ok; " +
				"PowerLoss: ok; " +
				"LineBalance: rms 202.4, max 330.6 at l3; " +
				"NodeBalance: ok; " +
				"PowerLossTransformer: ok; " +
				"Transformer voltage: ok; " +
				"TransformerLineBalance: rms 2000, max 2000 at Line 4: N_T_out1 -- Transformer1",
				report);

			flow.Write();

		}

		[TestMethod]
		public void TransformerFlowIsCorrect()
		{
			_builder.Add("N1[generatorVoltage=11000] -- l1 -- N_T_in");
			_builder.Add("N_T_out -- l2 -- Consumer[consumption=(100000,0)]");
			_builder.AddBus("Transformer1[transformer; ends=(N_T_in, N_T_out); voltages=(11000, 22000); operation=fixed; factor=0.98]");

			FlowProblem flowProblem = _builder.FlowProblem;
			var flow = new SimplifiedDistFlowProvider().ComputeFlow(flowProblem);

			var report = flow.FlowConsistencyReportWithTransformers();
			Assert.AreEqual(
				"NoCurrent: ok; " +
				"Current: ok; " +
				"PowerLoss: ok; " +
				"LineBalance: rms 60.24, max 82.64 at l1; " +
				"NodeBalance: ok; " +
				"PowerLossTransformer: ok; " +
				"Transformer voltage: ok; " +
				"TransformerLineBalance: rms 2000, max 2000 at Line 3: N_T_out -- Transformer1",
				report);

			flow.Write();

		}

		[TestMethod]
		public void ZeroImpedanceIsHandled()
		{
			_builder.Add("Producer[generatorVoltage=1000.0] -- Line[z=(0,0)] -- Consumer[consumption=(100, 0)]");

			var flow = new SimplifiedDistFlowProvider().ComputeFlow(_builder.FlowProblem);

			Assert.AreEqual(
				"NoCurrent: ok; " +
				"Current: ok; " +
				"PowerLoss: ok; " +
				"LineBalance: ok; " +
				"NodeBalance: ok",
				flow.FlowConsistencyReport());

			flow.Write();
		}


		[TestMethod]
		public void FlowDeltasAreCorrectForDownstreamCurrents()
		{
			_builder.Add("N1[generatorVoltage=100] -- l1[closed] -- NX");
			_builder.Add("N2[generatorVoltage=120] -- l2[open] -- NX");
			_builder.Add("NX -- l3 -o- l4 -- Consumer[consumption=(10000,0)]");

			VerifyFlowDelta("l1", "l2");
		}

		[TestMethod]
		public void FlowDeltasAreCorrectForThreeTransformersSwitch()
		{
			_builder.Add("G1[generatorVoltage=11000] -- line1 -- T1_in");
			_builder.Add("T1_out -- line2 -- C1[consumption=(7000,0)] -- line3[closed] -- C2[consumption=(8000,0)] -- line4 -- T2_in");
			_builder.Add("T2_out -- line5 -- C3[consumption=(9000,0)] -- line6[open] -- C4[consumption=(10000,0)] -- line7 -- T3_out");
			_builder.Add("T3_in -- line8 -- G2[generatorVoltage=11000]");
			_builder.AddBus("T1[transformer; ends=(T1_in,T1_out); voltages=(11000,22000); operation=fixed; factor=0.9]");
			_builder.AddBus("T2[transformer; ends=(T2_in,T2_out); voltages=(21000,22000); operation=fixed; factor=0.8]");
			_builder.AddBus("T3[transformer; ends=(T3_in,T3_out); voltages=(11000,22000); operation=fixed; factor=0.7]");

			VerifyFlowDelta("line3", "line6");
		}

		[TestMethod]
		public void FlowDeltasAreCorrectForTransformersSwitch()
		{
			_builder.Add("N1[generatorVoltage=11000] -- l1[closed] -- N_T1_in");
			_builder.Add("N_T1_out -- l3 -- Join");
			_builder.AddBus("Transformer1[transformer; ends=(N_T1_in, N_T1_out); voltages=(11000,220); operation=fixed; factor=0.9]");

			_builder.Add("N2[generatorVoltage=11000] -- l2[open] -- N_T2_in");
			_builder.Add("N_T2_out -- l4 -- Join");
			_builder.AddBus("Transformer2[transformer; ends=(N_T2_in, N_T2_out); voltages=(11000,220); operation=fixed; factor=0.8]");

			_builder.Add("Join -- l5 -- Consumer[consumption=(10000,0)]");

			VerifyFlowDelta("l1", "l2");
		}

		[TestMethod]
		public void FlowDeltasAreCorrectForConsumersOnCycle()
		{
			_builder.Add("N1[generatorVoltage=11000] -- l1[closed] -- NA[consumption=(50,0)]");
			_builder.Add("N2[generatorVoltage=22000] -- l2[open] -- NB[consumption=(50,0)]");
			_builder.Add("NA -- lx -- NC -- ly -- NB");
			_builder.Add("NC -- lz -- ND[consumption=(50,0)]");

			VerifyFlowDelta("l1", "l2");
		}

		[TestMethod]
		public void FlowDeltasAreCorrectForTransformer()
		{
			_builder.Add("N1[generatorVoltage=11000] -- l1[closed] -- N_T_in1");
			_builder.Add("N2[generatorVoltage=22000] -- l2[open] -- N_T_in2");
			_builder.Add("N_T_out -- l3 -- Consumer[consumption=(10000,0)]");
			_builder.AddBus("Transformer1[transformer; ends=(N_T_in1, N_T_in2, N_T_out); voltages=(11000,22000,220); operation=fixed; factor=0.98]");

			VerifyFlowDelta("l1", "l2");
		}

		[TestMethod]
		public void FlowDeltasAreCorrectForTransformer2()
		{
			_builder.Add("N1[generatorVoltage=22000] -- l1[closed] -- N_T_in1");
			_builder.Add("N_T_out1 -- l3[closed] -- Consumer1[consumption=(100,0)]");
			_builder.Add("N_T_out2 -- l4[open] -- Consumer2[consumption=(100,0)] -- l5[closed] -- Consumer1");
			_builder.AddBus("Transformer1[transformer; ends=(N_T_in1, N_T_out1, N_T_out2); voltages=(22000,11000,220); operation=fixed; factor=0.98]");

			VerifyFlowDelta("l3", "l4");
			VerifyFlowDelta("l5", "l4");
		}

		private void VerifyFlowDelta(string switchToOpen, string switchToClose)
		{
			PgoSolution mpSol = _builder.SinglePeriodSolution;
			var network = mpSol.Problem.Network;
			PeriodSolution solution = mpSol.SinglePeriodSolutions.Single();

			// Check the single period solution
			Assert.IsTrue(solution.IsRadial);
			Assert.IsTrue(solution.ComputeFlow(mpSol.Problem.FlowProvider));

			// Create a move
			SwapSwitchStatusMove move = mpSol.SwapMove(switchToOpen, switchToClose); 

			VerifyFlowDelta(new[] { move });
		}

		private void AssertAreEqual(double re, double im, Complex c, double tolerance = 0)
		{
			Assert.AreEqual(re, c.Real, tolerance);
			Assert.AreEqual(im, c.Imaginary, tolerance);
		}

		private void AssertAreEqual(Complex expected, Complex c, double tolerance = 0)
		{
			AssertAreEqual(expected.Real, expected.Imaginary, c, tolerance);
		}
	}
}

