using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class NetworkConfigurationTests
	{
		/// <summary>
		/// A simple network with two trees and a connecting switch between them-
		/// </summary>
		NetworkConfiguration _simpleConfig;

		/// <summary>
		/// The connection between the two trees. Initially closed.
		/// </summary>
		Line _connectionSwitch;

		[TestInitialize]
		public void Setup()
		{
			//Create a small network, with two "binary trees"
			PowerNetwork n = new PowerNetwork();
			Bus p1 = n.AddProvider(1, 1, 1, "p1");
			AddChildren(n, p1, 0, 4);
			Bus p2 = n.AddProvider(1, 1, 1, "p2");
			AddChildren(n, p2, 0, 4);

			//Add connecting switch
			_connectionSwitch = n.AddLine("p1.[0,1].[1,1].[2,1]", "p2.[0,1].[1,1].[2,1]", Complex.Zero, 0, double.PositiveInfinity, switchable: true);

			SwitchSettings switchSettings = new SwitchSettings(n);

			_simpleConfig = new NetworkConfiguration(n, switchSettings);
		}

		/// <summary>
		/// Adds children to the given parent, to max depth maxLev to the given network, so that each bus has two children.
		/// Only at the leaf node consumers will be added. Before this, connections are added.
		/// </summary>
		/// <param name="n"></param>
		/// <param name="parent"></param>
		/// <param name="curLev">Current level (0 for provider)</param>
		/// <param name="maxLev">Maximum level</param>
		private void AddChildren(PowerNetwork n, Bus parent, int curLev, int maxLev)
		{
			List<Bus> children = new List<Bus>();
			if (curLev == maxLev)
			{
				Bus c1 = n.AddConsumer(1, 1, $"{parent.Name}.[{curLev},1]");
				n.AddLine(parent.Name, c1.Name, new Complex(0, 0), 0, double.PositiveInfinity);
				children.Add(c1);

				Bus c2 = n.AddConsumer(1, 1, $"{parent.Name}.[{curLev},2]");
				n.AddLine(parent.Name, c2.Name, new Complex(0, 0), 0, double.PositiveInfinity);
				children.Add(c2);
			}
			else
			{
				Bus c1 = n.AddTransition(1, 1, $"{parent.Name}.[{curLev},1]");
				n.AddLine(parent.Name, c1.Name, new Complex(0, 0), 0, double.PositiveInfinity);
				children.Add(c1);

				Bus c2 = n.AddTransition(1, 1, $"{parent.Name}.[{curLev},2]");
				n.AddLine(parent.Name, c2.Name, new Complex(0, 0), 0, double.PositiveInfinity);
				children.Add(c2);

				children.Do(c => AddChildren(n, c, curLev + 1, maxLev));
			}
		}

		/// <summary>
		/// Tests that the inter-bus configurations are calculated correctly
		/// </summary>
		[TestMethod]
		public void TestUpdateRelationsInBranchOf()
		{
			//This triggers a first inter-bus relation computation
			Assert.IsFalse(_simpleConfig.IsRadial, "Config should not be radial initially");

			(IEnumerable<Line> path, Line bridge) = _simpleConfig.FindRadialityConflict();

			//Open the switch
			_simpleConfig.OpenSwitchForBreakingCycleWithBridge(_connectionSwitch, bridge);

			//Check radiality again, which also trigger a re-evaluation of inter-bus relationships
			bool israd = _simpleConfig.IsRadial;
			Assert.IsTrue(israd, "Config should now be radial");

			//Now all realtions should be correnct.
			Assert.IsTrue(_simpleConfig.Network.Buses.All(b => _simpleConfig.DownstreamBuses(b).All(c => _simpleConfig.UpstreamBus(c) == b)), "Upstream/downstream relations are not consistent");
			Assert.IsTrue(_simpleConfig.Network.Buses.All(b => _simpleConfig.DownstreamBuses(b).All(c => _simpleConfig.ProviderForBus(c) == _simpleConfig.ProviderForBus(b) || _simpleConfig.ProviderForBus(b) == null)), "Provider refernces are not the same in the same tree");
		}

		[TestMethod]
		public void MakeRadialOpensCenterSwitchInLoop()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("G[generator] -- L0 -- Node -- L1[closed] -o- L2 -o- L3 -o- L4 -o- L5[closed] -o- L6 -o- L7 -o- L8 -o- L9[closed] -- Node");

			var network = b.Network;
			var configuration = b.Configuration;
			Assert.IsFalse(configuration.IsRadial);

			configuration.MakeRadial();

			Assert.IsTrue(configuration.IsOpen(network.GetLine("L5")));
		}

		[TestMethod]
		public void MakeRadialOpensCenterSwitchBetweenGenerators()
		{
			NetworkBuilder b = new NetworkBuilder();
			b.Add("G1[generator] -- L1[closed] -o- L2 -o- L3 -o- L4 -o- L5[closed] -o- L6 -o- L7 -o- L8 -o- L9[closed] -- G2[generator]");

			var network = b.Network;
			var configuration = b.Configuration;
			Assert.IsFalse(configuration.IsRadial);

			configuration.MakeRadial();

			Assert.IsTrue(configuration.IsOpen(network.GetLine("L5")));
		}
	}
}
