using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Scoop.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class ValidTransformerModesTests
	{
		public PowerNetwork TwoWindingTestNetwork()
		{
			var builder = new NetworkBuilder();
			builder.Add("G1[generatorVoltage=1] -- Line1 -- N1");
			builder.Add("N2 -- Line2 -- C1[consumption=(1, 0.2)]");
			var network = builder.Network;

			network.AddTransformer(new[] { ("N1", 0.1), ("N2", 0.5) }, modes: null, "trafo");
			return network;
		}

		[TestMethod]
		public void TwoWindingTransformerModesMustMatchFlowDirection()
		{
			{
				// Without any modes, the network does not allow radial flow.
				var net = TwoWindingTestNetwork();
				var conf = new NetworkConfiguration(net, new SwitchSettings(net));
				Assert.IsTrue(conf.IsRadial);
				Assert.IsTrue(conf.HasTransformersUsingMissingModes);
				Assert.IsTrue(!conf.AllowsRadialFlow(requireConnected: true));
			}

			{
				// With only one mode in the opposite of the flow direction, the network does not allow radial flow.
				var net = TwoWindingTestNetwork();
				net.GetBus("trafo").Transformer.AddMode("N2", "N1", TransformerOperationType.FixedRatio, 0.5);
				var conf = new NetworkConfiguration(net, new SwitchSettings(net));
				Assert.IsTrue(conf.IsRadial);
				Assert.IsTrue(conf.HasTransformersUsingMissingModes);
				Assert.IsTrue(!conf.AllowsRadialFlow(requireConnected: true));
			}

			{
				// With one mode in the flow direction, the network does allows radial flow.
				var net = TwoWindingTestNetwork();
				net.GetBus("trafo").Transformer.AddMode("N1", "N2", TransformerOperationType.FixedRatio, 2);
				var conf = new NetworkConfiguration(net, new SwitchSettings(net));
				Assert.IsTrue(conf.IsRadial);
				Assert.IsTrue(!conf.HasTransformersUsingMissingModes);
				Assert.IsTrue(conf.AllowsRadialFlow(requireConnected: true));
			}

			{
				// With both possible modes, the network does allows radial flow.
				var net = TwoWindingTestNetwork();
				net.GetBus("trafo").Transformer.AddMode("N1", "N2", TransformerOperationType.FixedRatio, 2);
				net.GetBus("trafo").Transformer.AddMode("N2", "N1", TransformerOperationType.FixedRatio, 0.5);
				var conf = new NetworkConfiguration(net, new SwitchSettings(net));
				Assert.IsTrue(conf.IsRadial);
				Assert.IsTrue(!conf.HasTransformersUsingMissingModes);
				Assert.IsTrue(conf.AllowsRadialFlow(requireConnected: true));
			}
		}

		public PowerNetwork ThreeWindingTestNetwork()
		{
			var builder = new NetworkBuilder();
			builder.Add("G1[generatorVoltage=1] -- Line1 -- N1");
			builder.Add("N2 -- Line2 -- C1[consumption=(1, 0.2)]");
			builder.Add("N3 -- Line3 -- C2[consumption=(1, 0.2)]");
			var network = builder.Network;

			network.AddTransformer(
				new[] { ("N1", 1.0), ("N2", 0.5), ("N3", 0.5) },
				modes: null,
				"trafo");
			return network;
		}

		[TestMethod]
		public void ThreeWindingTransformerModesMustMatchFlowDirection()
		{
			var modes = new[]
			{
				("N1","N2",5.0),
				("N1","N3",10.0),
				("N2","N1",0.2),
				("N2","N3",2.0),
				("N3","N1",0.1),
				("N3","N2",0.5),
			};

			// all subsets of possible modes
			foreach (var m in Enumerable.Range(0, 1 << modes.Length))
			{
				var subset = Enumerable.Range(0, modes.Length).Where(i => (m & (1 << i)) != 0).ToList();

				// we need n1->n2 and n1->n3 to both be present, the other modes cannot be used so are irrelevant.
				var valid = subset.Contains(0) && subset.Contains(1);

				var net = ThreeWindingTestNetwork();
				foreach (var modeIdx in subset)
				{
					var (n1, n2, ratio) = modes[modeIdx];
					net.GetBus("trafo").Transformer.AddMode(n1, n2, TransformerOperationType.FixedRatio, ratio);
				}
				var conf = new NetworkConfiguration(net, new SwitchSettings(net));
				Assert.IsTrue(conf.IsRadial);
				Assert.IsTrue(conf.HasTransformersUsingMissingModes == !valid);
				Assert.IsTrue(conf.AllowsRadialFlow(requireConnected: true) == valid);
			}
		}

		[TestMethod]
		public void TestNeighborhoodGeneratesMovesWithFeasibleTransformerModes()
		{
			int NumberOfMoves(bool allTransformerModes)
			{
				var (network, perDat) = TwoThreewindingTransformersTestNetwork();

				var t1 = network.GetBus("T1");
				var t2 = network.GetBus("T2");

				// all modes for t1
				t1.Transformer.AddMode("T1TOP", "T1LEFT", TransformerOperationType.FixedRatio);
				t1.Transformer.AddMode("T1TOP", "T1RIGHT", TransformerOperationType.FixedRatio);
				t1.Transformer.AddMode("T1LEFT", "T1TOP", TransformerOperationType.FixedRatio);
				t1.Transformer.AddMode("T1LEFT", "T1RIGHT", TransformerOperationType.FixedRatio);
				t1.Transformer.AddMode("T1RIGHT", "T1TOP", TransformerOperationType.FixedRatio);
				t1.Transformer.AddMode("T1RIGHT", "T1LEFT", TransformerOperationType.FixedRatio);

				// just one config available for t2
				t2.Transformer.AddMode("T2LEFT", "T2TOP", TransformerOperationType.FixedRatio);
				t2.Transformer.AddMode("T2LEFT", "T2RIGHT", TransformerOperationType.FixedRatio);

				if (allTransformerModes)
				{
					t2.Transformer.AddMode("T2TOP", "T2LEFT", TransformerOperationType.FixedRatio);
					t2.Transformer.AddMode("T2TOP", "T2RIGHT", TransformerOperationType.FixedRatio);
					t2.Transformer.AddMode("T2RIGHT", "T2TOP", TransformerOperationType.FixedRatio);
					t2.Transformer.AddMode("T2RIGHT", "T2LEFT", TransformerOperationType.FixedRatio);
				}

				var switches = new SwitchSettings(network);
				switches.SetSwitch(network.GetLine("L2"), open: true);
				switches.SetSwitch(network.GetLine("L3"), open: true);
				switches.SetSwitch(network.GetLine("L4"), open: true);

				var config = new NetworkConfiguration(network, switches);
				var pSols = new Dictionary<Period, NetworkConfiguration>();
				pSols.Add(perDat.Period, config);

				var solution = new PgoSolution(new PgoProblem(perDat, new SimplifiedDistFlowProvider()), pSols);
				var spSol = solution.SinglePeriodSolutions.First();

				Assert.IsTrue(spSol.AllowsRadialFlow(requireConnected: true));

				//Create a neighbourhood for each open switch, and make an aggregated list of all moves
				List<Move> moves = new List<Move>();
				foreach (Line switchToClose in spSol.OpenSwitches)
				{
					CloseSwitchAndOpenOtherNeighbourhood nh = new CloseSwitchAndOpenOtherNeighbourhood(switchToClose, perDat.Period);
					nh.Init(solution);

					foreach (var move in nh)
					{
						// The generated moves should respect transformer modes.
						if (move is SwapSwitchStatusMove swapSwitch)
						{
							Assert.IsTrue(config.SwappingSwitchesUsesValidTransformerModes(
								switchToClose: swapSwitch.SwitchToClose,
								switchToOpen: swapSwitch.SwitchToOpen));
						}
						moves.Add(move);
					}
				}

				return moves.Count;
			}

			// If we have more transformer modes, we will have more moves in the neighborhood.

			var lowNumber = NumberOfMoves(false);
			var highNumber = NumberOfMoves(true);
			Assert.IsTrue(0 < lowNumber);
			Assert.IsTrue(lowNumber < highNumber);

		}

		private static bool AreTransformerModesFeasible(PowerNetwork network, NetworkConfiguration radialConfig = null)
		{
			if (radialConfig == null)
			{
				radialConfig = new NetworkConfiguration(network, new SwitchSettings(network));
				radialConfig.MakeRadial(null, false);
			}
			Assert.IsTrue(radialConfig.IsRadial);
			radialConfig.MakeRadialFlowPossible(null, false);
			Assert.IsTrue(radialConfig.IsRadial);
			return radialConfig.AllowsRadialFlow(requireConnected: true);
		}

		[TestMethod]
		public void CannotTurnTransformersDownstreamFromUnturnableTransformer()
		{
			var b1 = new NetworkBuilder();
			b1.Add("G1[generatorVoltage=1] -- Sw1[open] -- N0 -- Line1 -- Transformer1[transformer;ends=(N0,N1);voltages=(1,1);upstream=(N0)] -- Line2 -- N1 -- Line3 -- Transformer2[transformer;ends=(N1,N2);voltages=(1,1);upstream=(N2)] -- Line4 -- N2 -- Sw2[open] -- G2[generatorVoltage=1]");
			Assert.IsFalse(AreTransformerModesFeasible(b1.Network));

			// Adding the mode fixes it:
			var b2 = new NetworkBuilder();
			b2.Add("G1[generatorVoltage=1] -- Sw1[open] -- N0 -- Line1 -- Transformer1[transformer;ends=(N0,N1);voltages=(1,1);upstream=(N0,N1)] -- Line2 -- N1 -- Line3 -- Transformer2[transformer;ends=(N1,N2);voltages=(1,1);upstream=(N2)] -- Line4 -- N2 -- Sw2[open] -- G2[generatorVoltage=1]");
			Assert.IsTrue(AreTransformerModesFeasible(b2.Network));

			// Adding a switch between the transformers also fixes it
			var b3 = new NetworkBuilder();
			b3.Add("G1[generatorVoltage=1] -- Sw1[open] -- N0 -- Line1 -- Transformer1[transformer;ends=(N0,N1);voltages=(1,1);upstream=(N0)] -- Line2 -- N1 -- S[open] -- NX -- Line3 -- Transformer2[transformer;ends=(NX,N2);voltages=(1,1);upstream=(N2)] -- Line4 -- N2 -- Sw2[open] -- G2[generatorVoltage=1]");
			Assert.IsTrue(AreTransformerModesFeasible(b3.Network));
		}

		[TestMethod]
		public void CannotTurnTransformerIfNoClosedUpstreamSwitch()
		{
			var b1 = new NetworkBuilder();
			b1.Add("G1[generatorVoltage=1] -- NotSwitch -- N0 -- Line1 -- Transformer1[transformer;ends=(N0,N1);voltages=(1,1);upstream=(N1)] -- Line2 -- N1 -- Sw2[open] -- G2[generatorVoltage=1]");
			b1.Add("G1 -- Sw1[open] -- NX[consumption=(1,0)]");
			Assert.IsFalse(AreTransformerModesFeasible(b1.Network));

			// Adding a switch fixes it
			var b2 = new NetworkBuilder();
			b2.Add("G1[generatorVoltage=1] -- Sw[open] -- N0 -- Line1 -- Transformer1[transformer;ends=(N0,N1);voltages=(1,1);upstream=(N1)] -- Line2 -- N1 -- Sw2[open] -- G2[generatorVoltage=1]");
			b2.Add("G1 -- Sw1[open] -- NX[consumption=(1,0)]");
			Assert.IsTrue(AreTransformerModesFeasible(b2.Network));
		}

		[TestMethod]
		public void CannotTurnTransformerIfNoOpenSwitchDownstream()
		{
			var b1 = new NetworkBuilder();
			b1.Add("G1[generatorVoltage=1] -- Sw[open] -- N0 -- Line1 -- Transformer1[transformer;ends=(N0,N1);voltages=(1,1);upstream=(N1)] -- Line2 -- N1");
			Assert.IsFalse(AreTransformerModesFeasible(b1.Network));

			// Adding a switch fixes it
			var b2 = new NetworkBuilder();
			b2.Add("G1[generatorVoltage=1] -- Sw[open] -- N0 -- Line1 -- Transformer1[transformer;ends=(N0,N1);voltages=(1,1);upstream=(N1)] -- Line2 -- N1 -- Sw2[open] -- G2[generatorVoltage=1]");
			Assert.IsTrue(AreTransformerModesFeasible(b2.Network));
		}


		[TestMethod]
		public void CannotTurnTransformerIfAllDownstreamPathsCrossUnturnableTransformers()
		{
			var b1 = new NetworkBuilder();
			b1.Add("G1[generatorVoltage=1] -- Sw1[open] -- N1");
			b1.Add("G2[generatorVoltage=1] -- Sw2[open] -- N2");
			b1.Add("G3[generatorVoltage=1] -- Sw3[open] -- N3");
			b1.Add("NMid");
			b1.AddTransformer("T1[transformer;ends=(N1,NMid);voltages=(1,1);upstream=(NMid)]");
			b1.AddTransformer("T2[transformer;ends=(N2,N3,NMid);voltages=(1,1,1);upstream=(NMid)]");
			var radialConfig1 = new NetworkConfiguration(b1.Network, new SwitchSettings(b1.Network));
			radialConfig1.SetSwitch(b1.Network.GetLine("Sw2"), open: true);
			radialConfig1.SetSwitch(b1.Network.GetLine("Sw3"), open: true);
			Assert.IsFalse(AreTransformerModesFeasible(b1.Network, radialConfig1));

			// adding any additional possible upstream bus for T2 is sufficient:
			foreach (var t2upstream in new[] { "(NMid,N2)", "(NMid,N3)" })
			{
				var b2 = new NetworkBuilder();
				b2.Add("G1[generatorVoltage=1] -- Sw1[open] -- N1");
				b2.Add("G2[generatorVoltage=1] -- Sw2[open] -- N2");
				b2.Add("G3[generatorVoltage=1] -- Sw3[open] -- N3");
				b2.Add("NMid");
				b2.AddTransformer("T1[transformer;ends=(N1,NMid);voltages=(1,1);upstream=(NMid)]");
				b2.AddTransformer($"T2[transformer;ends=(N2,N3,NMid);voltages=(1,1,1);upstream={t2upstream}]");
				var radialConfig2 = new NetworkConfiguration(b2.Network, new SwitchSettings(b2.Network));
				radialConfig2.SetSwitch(b2.Network.GetLine("Sw2"), open: true);
				radialConfig2.SetSwitch(b2.Network.GetLine("Sw3"), open: true);
				Assert.IsTrue(AreTransformerModesFeasible(b2.Network, radialConfig2));
			}
		}

		[TestMethod]
		public void CannotTurnTransformerUsingOpenSwitchConnectedToTheSameTree()
		{
			var b = new NetworkBuilder();
			b.Add("G[generatorVoltage=1] -- Sw0[open] -- N0 -- L0 -- T[transformer;ends=(N0,N1);voltages=(1,1);upstream=(N1)] -- L1 -- N1");

			// Cannot use this switch:
			b.Add("N1 -- Sw1[open] -- N0");
			var radialConfig1 = new NetworkConfiguration(b.Network, new SwitchSettings(b.Network));
			radialConfig1.SetSwitch(b.Network.GetLine("Sw1"), open: true);
			Assert.IsFalse(AreTransformerModesFeasible(b.Network, radialConfig1));

			// But we can use this switch:
			b.Add("N1 -- Sw2[open] -- G");
			var radialConfig2 = new NetworkConfiguration(b.Network, new SwitchSettings(b.Network));
			radialConfig2.SetSwitch(b.Network.GetLine("Sw1"), open: true);
			radialConfig2.SetSwitch(b.Network.GetLine("Sw2"), open: true);
			Assert.IsTrue(AreTransformerModesFeasible(b.Network, radialConfig2));
		}

		// Test the algorithm for MakeRadialFlowPossible
		[TestMethod]
		public void MakeRadialFlowPossibleWorksForTransformerNetworkCompatibility()
		{
			// Network with two 3-winding transformers where the available modes on the transformers need to be compatible:
			//
			//               G2                   G3
			//                |/switch             |/switch
			//                |                    |
			//                o 3Wnd.transf.       o 3Wnd.transf.
			//        sw     oo                   oo           sw
			//  G1  --/---o-/  \--------o--------/  \-----o----/----- G4

			var t1Terminals = new[] { "T1LEFT", "T1RIGHT", "T1TOP" };
			var t2Terminals = new[] { "T2LEFT", "T2RIGHT", "T2TOP" };

			foreach (var t1Input in t1Terminals)
			{
				foreach (var t2Input in t2Terminals)
				{
					var (network, _perDat) = TwoThreewindingTransformersTestNetwork();
					var t1 = network.GetBus("T1");
					var t2 = network.GetBus("T2");

					// add modes
					foreach (var t1Output in t1Terminals.Where(t => t != t1Input))
					{
						t1.Transformer.AddMode(t1Input, t1Output, TransformerOperationType.FixedRatio);
					}
					foreach (var t2Output in t2Terminals.Where(t => t != t2Input))
					{
						t2.Transformer.AddMode(t2Input, t2Output, TransformerOperationType.FixedRatio);
					}

					// make radial flow possible
					var config = new NetworkConfiguration(network, new SwitchSettings(network));
					Assert.IsTrue(!config.AllowsRadialFlow(requireConnected: true));


					var invalid = false;
					invalid |= t1Input == "T1TOP" && t2Input == "T2TOP";
					invalid |= t1Input == "T1LEFT" && t2Input != "T2LEFT";
					invalid |= t2Input == "T2RIGHT" && t1Input != "T1RIGHT";
					invalid |= t1Input == "T1RIGHT" && t2Input == "T2LEFT";

					var valid = !invalid;

					if (valid)
					{
						config.MakeRadialFlowPossible();
						Assert.IsTrue(config.AllowsRadialFlow(requireConnected: true));
					}
					else
					{
						try
						{
							config.MakeRadialFlowPossible();
							Assert.Fail();
						}
						catch (Exception e)
						{
							Assert.IsTrue(e.Message.Contains("cannot be connected with a valid mode"));
						}
						Assert.IsTrue(!config.AllowsRadialFlow(requireConnected: true));
					}
				}
			}
		}

		private static (PowerNetwork, PeriodData) TwoThreewindingTransformersTestNetwork()
		{
			var builder = new NetworkBuilder();
			builder.Add("G1[generatorVoltage=1] -- L1[open] -- C1[consumption=(1, 0.2)] -- lx1 -- T1LEFT");
			builder.Add("G2[generatorVoltage=1] -- L2[open] -- T1TOP");
			builder.Add("G3[generatorVoltage=1] -- L3[open] -- T2TOP");
			builder.Add("G4[generatorVoltage=1] -- L4[open] -- C3[consumption=(1,0.2)] -- lx2 -- T2RIGHT");
			builder.Add("T1RIGHT -- lx3 -- C2[consumption=(1, 0.2)] -- lx4 -- T2LEFT");

			var network = builder.Network;
			var periodData = builder.PeriodData;
			network.AddTransformer(new[] { ("T1TOP", 1.0), ("T1LEFT", 1.0), ("T1RIGHT", 1.0) }, null, "T1");
			network.AddTransformer(new[] { ("T2TOP", 1.0), ("T2LEFT", 1.0), ("T2RIGHT", 1.0) }, null, "T2");
			return (network, periodData);
		}

		public static PeriodData TwoThreeWindingTransformersTestNetworkPeriodData()
		{
			var (network, perDat) = TwoThreewindingTransformersTestNetwork();
			var t1 = network.GetBus("T1");
			var t2 = network.GetBus("T2");

			// all modes for t1
			t1.Transformer.AddMode("T1TOP", "T1LEFT", TransformerOperationType.FixedRatio);
			t1.Transformer.AddMode("T1TOP", "T1RIGHT", TransformerOperationType.FixedRatio);
			t1.Transformer.AddMode("T1LEFT", "T1TOP", TransformerOperationType.FixedRatio);
			t1.Transformer.AddMode("T1LEFT", "T1RIGHT", TransformerOperationType.FixedRatio);
			t1.Transformer.AddMode("T1RIGHT", "T1TOP", TransformerOperationType.FixedRatio);
			t1.Transformer.AddMode("T1RIGHT", "T1LEFT", TransformerOperationType.FixedRatio);

			// just one config available for t2
			t2.Transformer.AddMode("T2LEFT", "T2TOP", TransformerOperationType.FixedRatio);
			t2.Transformer.AddMode("T2LEFT", "T2RIGHT", TransformerOperationType.FixedRatio);

			return perDat;
		}
	}
}
