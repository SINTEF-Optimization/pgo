using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class SmallNetworkConfigurationTests
	{
		[TestMethod]
		public void NonRadialIEEE34()
		{
			PeriodData perDat = IEEE34NetworkMaker.IEEE34();
			PowerNetwork ieee34 = perDat.Network;
			SwitchSettings settings = new SwitchSettings(ieee34); // Default: All lines closed
			var config = new NetworkConfiguration(ieee34, settings);

			Assert.IsFalse(config.IsRadial);
		}

		[TestMethod]
		public void RadialityIEEE34Network()
		{
			PeriodData perDat = IEEE34NetworkMaker.IEEE34();
			PowerNetwork ieee34 = perDat.Network;

			SwitchSettings settings = new SwitchSettings(ieee34);

			Line lineToOpen = ieee34.GetLine("814", "850");
			settings.SetSwitch(lineToOpen, true);

			var config = new NetworkConfiguration(ieee34, settings);
			Assert.IsTrue(config.IsRadial);
		}

		[TestMethod]
		public void RadialNetworkIsIdentified()
		{
			NetworkBuilder builder = new NetworkBuilder();

			builder.Add("Node1[generator] -- Line1[closed] -- Node2");

			var config = builder.Configuration;

			Assert.IsTrue(config.IsRadial);
			Assert.IsTrue(config.IsConnected);
			Assert.IsFalse(config.HasCycles);
		}

		[TestMethod]
		public void DisconnectedNetworkIsIdentified()
		{
			NetworkBuilder builder = new NetworkBuilder();

			builder.Add("Node1[generator] -- Line1[open] -- Node2");

			var config = builder.Configuration;

			Assert.IsFalse(config.IsRadial);
			Assert.IsFalse(config.IsConnected);
			Assert.IsFalse(config.HasCycles);
		}

		[TestMethod]
		public void DisconnectedNetworkIsIdentified2()
		{
			NetworkBuilder builder = new NetworkBuilder();

			// Not connected to a generator:
			builder.Add("Node1 -- Line1 -- Node2");

			var config = builder.Configuration;

			Assert.IsFalse(config.IsRadial);
			Assert.IsFalse(config.IsConnected);
			Assert.IsFalse(config.HasCycles);
		}

		[TestMethod]
		public void CyclicNetworkIsIdentified()
		{
			NetworkBuilder builder = new NetworkBuilder();

			builder.Add("Node1[generator] -- Line1[closed] -- Node2 -- Line2 -- Node1");

			var config = builder.Configuration;

			Assert.IsFalse(config.IsRadial);
			Assert.IsTrue(config.IsConnected);
			Assert.IsTrue(config.HasCycles);
		}

		[TestMethod]
		public void CyclicAndDisconnectedNetworkIsIdentified()
		{
			NetworkBuilder builder = new NetworkBuilder();

			builder.Add("Node1[generator] -- Line1[closed] -- Node2 -- Line2 -- Node1"); // Cycle
			builder.Add("Node3"); // Disconnected

			var config = builder.Configuration;

			Assert.IsFalse(config.IsRadial);
			Assert.IsFalse(config.IsConnected);
			Assert.IsTrue(config.HasCycles);
		}

		[TestMethod]
		public void CycleInDisconnectedComponentDoesNotCount()
		{
			NetworkBuilder builder = new NetworkBuilder();

			// Cycle, but not connected to a generator:
			builder.Add("Node1 -- Line1[closed] -- Node2 -- Line2 -- Node1");

			var config = builder.Configuration;

			Assert.IsFalse(config.IsRadial);
			Assert.IsFalse(config.IsConnected);
			Assert.IsFalse(config.HasCycles);
		}

		[TestMethod]
		public void FindPathIsCorrect()
		{
			var builder = TestUtils.SmallTestNetwork();
			var solution = builder.SinglePeriodSolution;
			var perSol = solution.SinglePeriodSolutions.Single();

			VerifyPath("L6", "L1 L2 L3 L4 L5 L6");
			VerifyPath("Ly", "Ly L5 L4 L3");
			VerifyPath("Lb", "L1 L2 Lb L10");



			void VerifyPath(string nameOfSwitchToClose, string expectedCycle)
			{
				var switchToClose = builder.Network.GetLine(nameOfSwitchToClose);

				var directedPath = perSol.NetConfig.FindCycleWith(switchToClose);

				Assert.AreEqual(expectedCycle, directedPath.Select(l => l.Line.Name).Concatenate(" "));

				// Path must start and end at generators, or at the same node
				if (directedPath.First().StartNode.IsProvider)
					Assert.IsTrue(directedPath.Last().EndNode.IsProvider);
				else
					Assert.AreEqual(directedPath.First().StartNode, directedPath.Last().EndNode);

				// Adjacent path elements must connect to the same node
				foreach (var (first, second) in directedPath.AdjacentPairs())
					Assert.AreEqual(first.EndNode, second.StartNode);
			}
		}

		[TestMethod]
		public void UpstreamAndDownstreamLinesWorksWithParallelLines()
		{
			NetworkBuilder nb = new NetworkBuilder();
			nb.Add("G[generator] -- line1[open] -- C[consumption=(1,2)] -- line2[closed] -- G");

			NetworkConfiguration config = nb.Configuration;

			var generator = config.Network.GetBus("G");
			var consumer = config.Network.GetBus("C");
			var line2 = config.Network.GetLine("line2");

			Assert.AreEqual(line2, config.UpstreamLine(consumer));
			DirectedLine directedLine = config.UpstreamDirectedLine(consumer);
			Assert.AreEqual(line2, directedLine.Line);
			Assert.AreEqual(LineDirection.Forward, directedLine.Direction);

			Assert.AreEqual(line2, config.DownstreamLines(generator).Single());
		}

		[TestMethod]
		public void DisconnectedNetworkCannotGetRadialConfiguration()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("N1[generator] -- L1 -- N2");
			b.Add("N3 -- L3 -- N4");

			TestUtils.AssertException(() => b.Configuration.MakeRadial(), requiredMessage: "Failed to make the configuration radial");
		}

		[TestMethod]
		public void NetworkWithUnbreakableCycleCannotGetRadialConfiguration()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("N1[generator] -- L1 -- N2 -- L2 -- N1");

			TestUtils.AssertException(() => b.Configuration.MakeRadial(), requiredMessage: "The network has a cycle with no switchable line");
		}
	}
}
