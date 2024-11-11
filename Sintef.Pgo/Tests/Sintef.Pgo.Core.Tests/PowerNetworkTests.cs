using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sintef.Scoop.Utilities;
using System.IO;
using Sintef.Pgo.Core.IO;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class PowerNetworkTests
	{
		[TestMethod]
		public void BasicCheckIEEE34Network()
		{
			PeriodData perDat = IEEE34NetworkMaker.IEEE34();
			PowerNetwork ieee34 = perDat.Network;

			Assert.AreEqual(ieee34.Providers.Count(), 2);
			Assert.AreEqual(ieee34.SwitchableLines.Count(), 3);
			Assert.AreEqual(ieee34.Buses.Count(), 34);

			Bus b1 = ieee34.GetBus("814");
			Bus b2 = ieee34.GetBus("850");
			Line l = ieee34.GetLine(b1, b2);
			Assert.IsTrue(l.IsSwitchable);
		}

		[TestMethod]
		public void ClosestSwitchesIsCorrect()
		{
			var network = TestUtils.SmallTestNetwork().Network;

			AssertClosestSwitches("L2", "L1 L3 La Lb Ly Lz");
			AssertClosestSwitches("Lc", "L11 L3 L4 Lz");


			void AssertClosestSwitches(string lineName, string expectedSwitches)
			{
				var line = network.GetLine(lineName);
				var switches = network.ClosestSwitches(line);

				Assert.AreEqual(expectedSwitches, switches.Select(s => s.Name).OrderBy(n => n).Concatenate(" "));
			}
		}

		/// <summary>
		/// Creates a small case with a composite consumer, writes this to file, reads it back in, and checks
		/// that this gave the same case as the original.
		/// </summary>
		[TestMethod]
		public void ParsingAndWritingOfCompositeConsumerCategoriesWorks()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("N1[generator] -- L1[breaker] -o- L2[breaker] -o- L3[closed] -o- L4[faultsPerYear=1;sectioningTime=PT1H;repairTime=PT1H] -- N2[consumption=(1,0);type=Domestic/0.1,Agriculture/0.2,Industry/0.7] -- L5[faultsPerYear=1;sectioningTime=PT1H;repairTime=PT1H] -- N3[consumption=(1,0);type=Agriculture]");

			var problem = b.SinglePeriodProblem;
			string filename = Directory.GetCurrentDirectory() + "\\"+ problem.Name;
			PgoJsonParser.SaveToFiles(filename, problem);

			var problemParsed = PgoJsonParser.CreateProblemFromFiles(filename, problem.FlowProvider);

			Bus parsedCompositeBus = problemParsed.Network.GetBus("N2");
			IEnumerable<ConsumerCategory> cats = problemParsed.Network.CategoryProvider.Categories(parsedCompositeBus);
			Assert.AreEqual(3, cats.Count());
			Assert.IsTrue(cats.Contains(ConsumerCategory.Agriculture));
			Assert.IsTrue(cats.Contains(ConsumerCategory.Industry));
			Assert.IsTrue(cats.Contains(ConsumerCategory.Domestic));
			Assert.AreEqual(0.1, problemParsed.Network.CategoryProvider.ConsumptionFraction(parsedCompositeBus, ConsumerCategory.Domestic));
			Assert.AreEqual(0.2, problemParsed.Network.CategoryProvider.ConsumptionFraction(parsedCompositeBus, ConsumerCategory.Agriculture));
			Assert.AreEqual(0.7, problemParsed.Network.CategoryProvider.ConsumptionFraction(parsedCompositeBus, ConsumerCategory.Industry));
		}

		[TestMethod]
		public void DisconnectedNetworkIsDetected()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("N1[generator] -- L1 -- N2");
			b.Add("N3 -- L3 -- N4");

			Assert.AreEqual(PowerNetwork.ConnectivityType.HasDisconnectedComponent, b.Network.Connectivity);
			(string descr, NetworkConfiguration conf) = b.Network.ReportCyclesOrDisconnectedComponents();
			Assert.IsTrue(descr.Contains("N3"));
			Assert.IsTrue(descr.Contains("N4"));
			Assert.IsFalse(descr.Contains("N1"));
			Assert.IsFalse(descr.Contains("N2"));
		}

		[TestMethod]
		public void UnbreakableCycleIsDetected()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("N1[generator] -- L1 -- N2 -- L2 -- N1");

			Assert.AreEqual(PowerNetwork.ConnectivityType.HasUnbreakableCycle, b.Network.Connectivity);
			(string descr, NetworkConfiguration conf) = b.Network.ReportCyclesOrDisconnectedComponents();
			Assert.IsTrue(descr.Contains("N1"));
			Assert.IsTrue(descr.Contains("N2"));

		}

		[TestMethod]
		public void OkNetworkIsDetected()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("N1[generator] -- L1 -- N2 -- L2 -- N3");

			Assert.AreEqual(PowerNetwork.ConnectivityType.Ok, b.Network.Connectivity);
		}

		[TestMethod]
		public void AnalysisReportsLeafComponents()
		{
			var network = TestUtils.ExampleNetworkWithLeafComponents().Network;
			var analysis = network.AnalyseNetwork(verbose: true);
			var nodes = "Number of buses in leaf components (sum/avg/max): 9 / 4,5 / 6";
			var consumers = "Number of consumer buses in leaf components (sum/avg/max): 6 / 3 / 3";
			var switches = "Number of switches in leaf components that must be closed (sum/avg/max): 2 / 1 / 2";
			Assert.IsTrue(analysis.Contains(nodes) || analysis.Contains(nodes.Replace(",", ".")));

			Assert.IsTrue(analysis.Contains(consumers));
			Assert.IsTrue(analysis.Contains(switches));
		}
	}
}
