using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sintef.Scoop.Utilities;
using Sintef.Scoop.Kernel;
using System.Numerics;
using System.IO;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class AcyclicAggregationTests
	{

		[TestMethod]
		public void CycleAndEdgeAggregationWorks()
		{
			var network = TestUtils.SmallTestNetworkThatCanBeAggregated().Network;
			var aggNetwork = MakeAcyclicAndConnected(network).AggregateNetwork;

			//Check that all breakable and switchable lines exist in both networks
			foreach (var line in network.SwitchableLines)
			{
				Assert.IsTrue(aggNetwork.GetLine(line.Name).IsSwitchable);
			}

			foreach (var line in network.Breakers)
			{
				Assert.IsTrue(aggNetwork.GetLine(line.Name).IsBreaker);
			}

			//Test sequential edge aggregation
			Assert.AreEqual(1, aggNetwork.LinesBetween(aggNetwork.GetBus("N3"), aggNetwork.GetBus("N4")).Count());

			//Test bot sequential and parallel edge aggregation
			Assert.AreEqual(3, aggNetwork.Lines.Count(l => !l.IsSwitchable));
			Assert.AreEqual(1, aggNetwork.LinesBetween(aggNetwork.GetBus("N5"), aggNetwork.GetBus("N6")).Count());
		}

		[TestMethod]
		public void AggregationRemovesDanglingLines()
		{
			NetworkBuilder builder = new NetworkBuilder();

			// These lines are not dangling and should survive:
			builder.Add("G[generator] -- L10 -- N10 -- L11 -- C[consumer]");
			builder.Add("G -- L20 -- T_in -- L21 -- T[transformer;voltages=(1,1);operation=fixed;factor=0.98] -- L22 -- T_out ");

			// These lines are dangling and should be removed:
			builder.Add("G -- L30 -- N30 -- L31 -- N31");

			// Here's a dangling loop:
			builder.Add("C -- L40 -- N40 -- L41 -- C");

			// All these lines are dangling after parallel aggregation:
			builder.Add("N10 -- L50 -- N50 -- L51 -- N51");
			builder.Add("N10 -- L52 -- N50");

			// Here's a special case: After aggregating two parallel lines, we get a loop, which
			// used to fail in serial aggregation:
			builder.Add("N10 -- L60 -- N60 -- L61 -- N61 -- L62 -- N10");
			builder.Add("N10 -- L63 -- N60");


			// Aggregate

			var aggregation = MakeAcyclicAndConnected(builder.Network);

			// Verify that the correct lines are preserved

			var keptLines = aggregation.AggregateNetwork.Lines.Select(l => l.Name).OrderBy(n => n).Concatenate(" ");
			var removedLines = aggregation.DanglingLines.Select(l => l.Name).OrderBy(n => n).Concatenate(" "); ;

			Assert.AreEqual("L10+L11 L20 L21 L22", keptLines);
			Assert.AreEqual("L30 L31 L40 L41 L50 L51 L52 L60 L61 L62 L63", removedLines);
		}

		[DataTestMethod]
		[DataRow(true)]
		[DataRow(false)]
		public void ObjectiveValueSurvivesAggregation(bool useIteratedDistFlow)
		{
			var random = new Random();
			for (int i = 0; i < 10; ++i)
			{
				PgoProblem origProb = TestUtils.SetupProblemWithRandomData(random, sizeCategory: 2);

				if (useIteratedDistFlow)
					origProb.UseCriteria(origProb.CriteriaSet.WithProvider(new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions)));

				PgoSolution origSol = origProb.RadialSolution();
				Period per = origProb.GetPeriod(0);
				SwitchSettings origSettings = origSol.GetPeriodSolution(per).SwitchSettings;

				NetworkAggregation aggregation = MakeAcyclicAndConnected(origProb.Network);
				PgoProblem aggProblem = origProb.CreateAggregatedProblem(aggregation, true);
				PgoSolution aggSol = new PgoSolution(aggProblem);
				aggSol.GetPeriodSolution(per).SwitchSettings.CopyFrom(origSettings, aggProblem.Network, aggregation, _ => false);

				AggregateObjective origTotalObjective = origSol.Encoding.CriteriaSet.Objective as AggregateObjective;
				AggregateObjective aggTotalObjective = aggSol.Encoding.CriteriaSet.Objective as AggregateObjective;

				Console.WriteLine("Original:");
				origTotalObjective.WriteToConsole(origSol);

				Console.WriteLine("Aggregated:");
				aggTotalObjective.WriteToConsole(aggSol);

				// Verify that each objective component has the same value in both solutions
				foreach (var (origObj, aggObj) in origTotalObjective.Components().Zip(aggTotalObjective.Components()))
				{
					double tolerance = 1e-12;
					if (origObj is KileCostObjective)
						// Kile cost is not aggregated exactly, due to averaging sectioning and repair time
						// over the aggregated lines
						tolerance = origObj.Value(origSol) * 0.08;

					Assert.AreEqual(origObj.Value(origSol), aggObj.Value(aggSol), tolerance);
				}
			}

		}

		[TestMethod]
		public void AggregationCanReportHowLinesAreAggregated()
		{
			var network = TestUtils.SmallTestNetworkThatCanBeAggregated().Network;
			var aggregation = MakeAcyclicAndConnected(network);

			// This line is a parallel aggregate
			var line = aggregation.AggregateNetwork.GetBus("N1").IncidentLines.Single();
			Assert.AreEqual("[La1 || La2 || La3]", line.Name);

			// Extract data on aggregation
			var merge = aggregation.MergeInfoFor(line).InDirectionFrom("N1");
			Assert.AreEqual(NetworkAggregation.AggregateType.Parallel, merge.Type);
			Assert.AreEqual("La1 La2 La3", LinesIn(merge));
			Assert.AreEqual("La1 La3", ForwardLinesIn(merge));
			Assert.AreEqual("La1", OnePathIn(merge));
			Assert.AreEqual(new Complex(1 / 3.0, 0), merge.Impedance);


			// This line is a serial aggregate
			line = aggregation.AggregateNetwork.GetBus("N3").IncidentLines.Single();
			Assert.AreEqual("Lb1+Lb2+Lb4+Lb3", line.Name);

			// Extract data on aggregation
			merge = aggregation.MergeInfoFor(line).InDirectionFrom("N3");
			Assert.AreEqual(NetworkAggregation.AggregateType.Serial, merge.Type);
			Assert.AreEqual("Lb1 Lb2 Lb4 Lb3", LinesIn(merge));
			Assert.AreEqual("Lb1 Lb2", ForwardLinesIn(merge));
			Assert.AreEqual("Lb1 Lb2 Lb4 Lb3", OnePathIn(merge));
			Assert.AreEqual(new Complex(4, 0), merge.Impedance);


			// This line is a parallel aggregate of line 'Lc' plus a (complex) serial aggregate
			line = aggregation.AggregateNetwork.GetBus("N5").IncidentLines.Single();

			// Extract data on aggregation
			merge = aggregation.MergeInfoFor(line).InDirectionFrom("N5");
			Assert.AreEqual(NetworkAggregation.AggregateType.Parallel, merge.Type);
			Assert.AreEqual(2, merge.Parts.Count());
			Assert.AreEqual("Lc1 Lc2 lc3 Lc6 Lc7 lc8 lc9 lc10 lc4 lc5 Lc99", ForwardLinesIn(merge));
			Assert.AreEqual("Lc1 Lc2 lc3 lc9 lc10", OnePathIn(merge));

			line = merge.Parts.Single(p => p.Type == NetworkAggregation.AggregateType.SingleLine).SingleLine;
			Assert.AreEqual("Lc99", line.Name);

			var serialPart = merge.Parts.Single(p => p.Type == NetworkAggregation.AggregateType.Serial);
			Assert.AreEqual(NetworkAggregation.AggregateType.Serial, serialPart.Type);
			Assert.AreEqual(2, serialPart.Parts.Count());



			string LinesIn(DirectedMergedLine m)
			{
				return m.DirectedLines
					.Select(x => x.Line.Name)
					.Concatenate(" ");
			}

			string ForwardLinesIn(DirectedMergedLine m)
			{
				return m.DirectedLines
					.Where(x => x.Direction == LineDirection.Forward)
					.Select(x => x.Line.Name)
					.Concatenate(" ");
			}

			string OnePathIn(DirectedMergedLine m)
			{
				return m.OneDirectedPath
					.Select(x => x.Line.Name)
					.Concatenate(" ");
			}
		}

		[TestMethod]
		public void BreakersAreNotAggregated()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("N1[generator] -- L1 -o- L2[breaker] -o- L3[breaker] -o- L4[closed] -- N2[consumer]");

			var connected = MakeAcyclicAndConnected(b.Network);

			Assert.AreEqual(4, connected.AggregateNetwork.LineCount);
		}

		[TestMethod]
		public void UnconnectedTransformersAreRemoved()
		{
			var builder = new NetworkBuilder();
			builder.Add("Node1 -- Line1 -- Transformer1[transformer;ends=(Node1,Node2);voltages=(1000,500);operation=fixed;factor=0.98] -- Line2 -- Node2");
			builder.Add("Gen[generatorVoltage=1000.0] -- Line3 -- Con[consumption=(1000,0)]");

			var connected = MakeAcyclicAndConnected(builder.Network);
			var shouldBeRemoved = new HashSet<string> { "Node1", "Transformer1", "Node2" };
			var removed = new HashSet<string>(connected.UnconnectedBuses.Select(x => x.Name));
			Assert.IsTrue(shouldBeRemoved.SetEquals(removed));
		}

		[TestMethod]
		public void CyclicNetworkConnectivityReportReportsCycle()
		{
			// Cyclic network should give error message about an actual unbreakable cycle.
			var builder = new NetworkBuilder();
			builder.Add("G1[generatorVoltage=1] -- L11 -- N11 -- L12 -- N12 -- L13 -- N13[consumption=(1,0)]");

			// add breakable cycle
			builder.Add("N11 -- L51[open] -- N51");
			builder.Add("N11 -- L61[open] -- N51");

			// add unbreakable cycle
			builder.Add("N11 -- L21 -- N21[consumption=(1,0)]");
			builder.Add("N12 -- L31 -- N31[consumption=(1,0)]");
			builder.Add("N21 -- L41 -- N31");

			var acyclicNetwork = MakeAcyclicAndConnected(builder.Network).AggregateNetwork;

			// Check that the network has an unbreakable cycle.
			Assert.IsTrue(acyclicNetwork.Connectivity.HasFlag(PowerNetwork.ConnectivityType.HasUnbreakableCycle));

			// If we inspect the shortest cycle in the network, we expect this to be an actual
			// unbreakable cycle. This expectation is used in Server.LoadNetworkFromJsonFile.
			// NOTE that the result depends on the internal randomisation of the MakeRadial algorithm.
			// Adding the cycles to the network builder in reverse order may change the outcome.
			var errorCycle = acyclicNetwork.ShortestCycleWithoutSwitches ?? acyclicNetwork.ShortestCycle;
			var cycle = new HashSet<string>(errorCycle.Select(x => x.Line.Name));
			Assert.IsTrue(cycle.SetEquals(new HashSet<string>() { "L12", "L21", "L31", "L41" }));

		}

		[TestMethod]
		public void DisconnectedNetworkComponentIsRemoved()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("N1[generator] -- L1 -- N2[consumer]");
			b.Add("N3 -- L3 -- N4");
			var agg = MakeAcyclicAndConnected(b.Network);

			Assert.IsTrue(agg.AggregateNetwork.HasBus("N1"));
			Assert.IsTrue(agg.AggregateNetwork.HasBus("N2"));
			Assert.IsFalse(agg.AggregateNetwork.HasBus("N3"));
			Assert.IsFalse(agg.AggregateNetwork.HasBus("N4"));

			Assert.IsFalse(agg.UnconnectedBuses.Any(bus => bus.Name == "N1"));
			Assert.IsFalse(agg.UnconnectedBuses.Any(bus => bus.Name == "N2"));
			Assert.IsTrue(agg.UnconnectedBuses.Any(bus => bus.Name == "N3"));
			Assert.IsTrue(agg.UnconnectedBuses.Any(bus => bus.Name == "N4"));
		}

		[TestMethod]
		public void ZeroImpedanceIsAggregatedCorrectly()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("N1[generator] -- L1[z=(0,0)] -o- L2[z=(1,0)] -- N2[consumer]");
			b.Add("N3[generator] -- L3[z=(0,0)] -- N4[consumer]");
			b.Add("N3 -- L4[z=(1,0)] -- N4");
			var agg = MakeAcyclicAndConnected(b.Network);

			Assert.AreEqual(new Complex(1, 0), agg.AggregateNetwork.GetLine("L1+L2").Impedance);
			Assert.AreEqual(Complex.Zero, agg.AggregateNetwork.GetLine("[L3 || L4]").Impedance);
		}

		private NetworkAggregation MakeAcyclicAndConnected(PowerNetwork network)
		{
			return NetworkAggregation.MakeAcyclicAndConnected(network);
		}
	}
}
