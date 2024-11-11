using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Class for computing the flow using the simplified DistFlow method. The
	/// method is documented in documentation\DistFlow.tex.
	/// </summary>
	public class SimplifiedDistFlowProvider : IFlowProvider
	{
		#region Public properties 

		/// <summary>
		/// The flow approximation that the flow provider uses.
		/// </summary>
		public FlowApproximation FlowApproximation => FlowApproximation.SimplifiedDF;

		#endregion

		#region Construction

		/// <summary>
		/// Constructor
		/// </summary>
		public SimplifiedDistFlowProvider()
		{
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Compute flow for the given flow problem.
		/// </summary>
		/// <param name="flowProblem"></param>
		/// <returns></returns>
		public IPowerFlow ComputeFlow(FlowProblem flowProblem)
		{
			var flow = new RadialFlow(flowProblem.NetworkConfig, flowProblem.Demands);

			if (!flowProblem.NetworkConfig.AllowsRadialFlow(requireConnected: false))
			{
				throw new System.Exception("Cannot compute flow with simplified DistFlow because the network is not radial or is missing a transformer mode");
			}

			foreach (var generator in flowProblem.NetworkConfig.Network.Providers)
			{
				ComputePowerDepthFirst(flow, flowProblem.Demands, generator);
				ComputeVoltageAndCurrentTopDown(flowProblem.NetworkConfig, flow, generator);
			}

			flow.Status = FlowStatus.Approximate;
			flow.StatusDetails = "SimplifiedDistFlow is always approximate";

			return flow;
		}

		/// <summary>
		/// Computes voltage and current, from the given provider bus and down, using a bredth first
		/// traversal of the tree. Assumes that power has been computed for all lines in the tree.
		/// </summary>
		/// <param name="netConfig"></param>
		/// <param name="flow"></param>
		/// <param name="generator">The provider bus.</param>
		private void ComputeVoltageAndCurrentTopDown(NetworkConfiguration netConfig, RadialFlow flow, Bus generator)
		{
			// This is a generator node, with fixed voltage
			flow.SetVoltage(generator, generator.GeneratorVoltage);
			flow.SetNominalVoltage(generator, generator.GeneratorVoltage);

			Stack<Bus> stack = new Stack<Bus>();
			stack.Push(generator);

			while (stack.Any())
			{
				var bus = stack.Pop();
				var voltage = flow.Voltage(bus);
				var nominalVoltage = flow.NominalVoltage(bus);

				foreach (var line in netConfig.DownstreamLines(bus))
				{
					var child = line.OtherEnd(bus);

					if (netConfig.TransformerModeForOutputLine(line) is Transformer.Mode mode)
					{
						voltage = mode.OutputVoltage(flow.Voltage(bus));
						nominalVoltage = mode.Transformer.ExpectedVoltageFor(mode.OutputBus);
					}

					var impedance = line.Impedance;
					var power = flow.PowerFlow(bus, line);
					var current = Complex.Conjugate(power / nominalVoltage);  //This follows from the simplification of SimplifiedDistFlow.
					flow.SetVoltage(child, voltage - impedance * current);
					flow.SetNominalVoltage(child, nominalVoltage);
					flow.SetDownstreamCurrent(line, current);
					stack.Push(child);
				}
			}
		}

		/// <summary>
		/// Computes the changes in flow that will result from the given move.
		/// </summary>
		public PowerFlowDelta ComputePowerFlowDelta(Move move)
		{
			return ComputePowerFlowDelta(move, this);
		}

		/// <summary>
		/// The tolerance for the delta flow algorithm to consider voltage and current values to be equal.
		/// </summary>
		const double SUBTREE_EQUALITY_TOLERANCE = 1e-9;

		/// <summary>
		/// Computes the changes in flow that will result from the given move.
		/// </summary>
		public static PowerFlowDelta ComputePowerFlowDelta(Move move, IFlowProvider flowProvider)
		{
			if (!(move is SwapSwitchStatusMove swapmove))
				throw new NotImplementedException("SimplifiedDistFlow.ComputePowerFlowDelta not implemented for move " + move.GetType().ToString());

			PeriodSolution sol = swapmove.Solution.GetPeriodSolution(swapmove.Period);
			var oldFlow = (RadialFlow)sol.Flow(flowProvider);
			NetworkConfiguration config = sol.NetConfig;
			Debug.Assert(oldFlow != null);
			List<Line> buffer = new List<Line>();

			// We assume that the move is between two radial configurations.
			// We also assume that the switch that is to close is open, and
			// that the switch that is to open is closed.
			var belowSwitchToClose = swapmove.SwitchToClose.Endpoints.First(node =>
				sol.NetConfig.IsAncestor(swapmove.SwitchToOpen, node));
			var aboveSwitchToClose = swapmove.SwitchToClose.Endpoints.First(node =>
				!sol.NetConfig.IsAncestor(swapmove.SwitchToOpen, node));

			var haveCommonAncestor = sol.NetConfig.ProviderForBus(swapmove.SwitchToClose.Node1)
				== sol.NetConfig.ProviderForBus(swapmove.SwitchToClose.Node2);
			var commonAncestor = haveCommonAncestor ?
				sol.NetConfig.CommonAncestor(swapmove.SwitchToClose.Node1, swapmove.SwitchToClose.Node2) : null;

			// Topology changes
			var pathFromCloseToOpen = config.PathToProvider(belowSwitchToClose)
				.TakeWhile(l => l.Line != swapmove.SwitchToOpen).ToList();

			var deletedDownstreamEdges = pathFromCloseToOpen.ToDictionary(k => k.EndNode, k => k.Line);
			deletedDownstreamEdges[config.UpstreamEnd(swapmove.SwitchToOpen)] = swapmove.SwitchToOpen;
			var newDownstreamEdges = pathFromCloseToOpen.ToDictionary(k => k.StartNode, k => k.Line);
			newDownstreamEdges[aboveSwitchToClose] = swapmove.SwitchToClose;

			// Calculate new power in two steps:
			var swapCycleLinePower = new Dictionary<Line, Complex>();
			var deltaPower = -oldFlow.PowerFlow(config.UpstreamEnd(swapmove.SwitchToOpen), swapmove.SwitchToOpen);

			// Step 1: Retract power from switch-to-open.
			var pathRetract = config.PathToProvider(config.DownstreamEnd(swapmove.SwitchToOpen))
				.TakeWhile(l => config.DownstreamEnd(l.Line) != commonAncestor);
			foreach (var dirLine in pathRetract)
			{
				swapCycleLinePower[dirLine.Line] = oldFlow.PowerFlow(dirLine.Line.Node1, dirLine.Line)
					- deltaPower.AdjustForDirection(dirLine.Direction);
			}

			// Step 2: Inject power into switch-to-close.

			var pathInject = new List<DirectedLine>();
			pathInject.AddRange(pathFromCloseToOpen.ReverseDirectedPath());
			pathInject.Add(swapmove.SwitchToClose.InDirectionTo(aboveSwitchToClose));
			pathInject.AddRange(config.PathToProvider(aboveSwitchToClose).TakeWhile(l => config.DownstreamEnd(l.Line) != commonAncestor));

			foreach (var dirLine in pathInject)
			{
				swapCycleLinePower[dirLine.Line] = oldFlow.PowerFlow(dirLine.Line.Node1, dirLine.Line)
					+ deltaPower.AdjustForDirection(dirLine.Direction);
			}

			// Calculate new voltages and currents by working top-down from the common ancestor, 
			// or from each top-level provider.
			PowerFlowDelta delta = PowerFlowDelta.Allocate();

			// Add delta for the switch to open -- it is not connected after the move, so the update tree walk below will not add it.
			delta.AddLineDelta(swapmove.SwitchToOpen,
				/* power node1: */ oldFlow.PowerFlow(swapmove.SwitchToOpen.Node1, swapmove.SwitchToOpen), 0,
				/* power node2: */ oldFlow.PowerFlow(swapmove.SwitchToOpen.Node2, swapmove.SwitchToOpen), 0,
				/* current:     */ oldFlow.Current(swapmove.SwitchToOpen), 0);

			if (commonAncestor != null)
			{
				UpdateVoltageAndCurrent(commonAncestor);
			}
			else
			{
				UpdateVoltageAndCurrent(sol.NetConfig.ProviderForBus(swapmove.SwitchToClose.Node1));
				UpdateVoltageAndCurrent(sol.NetConfig.ProviderForBus(swapmove.SwitchToClose.Node2));
			}

			return delta;


			void UpdateVoltageAndCurrent(Bus top)
			{
				var provider = top.IsProvider ? top : config.ProviderForBus(top);
				var topBusDelta = delta.AddBusDelta(top, oldFlow.Voltage(top), oldFlow.NominalVoltage(top).Magnitude, provider);

				var stack = new Stack<(PowerFlowDelta.BusPowerFlowDelta, bool)>(new[] { (topBusDelta, true) });
				while (stack.Count > 0)
				{
					var (busDelta, busIsOnSwapCycle) = stack.Pop();
					var bus = busDelta.Bus;
					var voltage = busDelta.NewVoltage;
					var nominalVoltage = busDelta.NewNominalVoltage;

					// Get the downstream lines into a List. This allows foreach to use List's struct
					// enumerator and saves on memory allocation
					List<Line> downstreamLines;
					if (busIsOnSwapCycle)
					{
						FillDownstreamLinesAfterMove(bus, buffer);
						downstreamLines = buffer;
					}
					else
						downstreamLines = config.DownstreamLines(bus);

					foreach (var line in downstreamLines)
					{
						var oldCurrent = oldFlow.Current(line);
						var oldVoltage = oldFlow.Voltage(bus);
						var oldNominalVoltage = oldFlow.NominalVoltage(bus).Magnitude;
						var lineIsOnSwapCycle = busIsOnSwapCycle && swapCycleLinePower.ContainsKey(line);
						if (lineIsOnSwapCycle != swapCycleLinePower.ContainsKey(line))
							throw new Exception();

						var child = line.OtherEnd(bus);

						if (line.IsTransformerConnection && TransformerModeForOutputLineAfterMove(line) is Transformer.Mode mode)
						{
							voltage = mode.OutputVoltage(busDelta.NewVoltage);
							nominalVoltage = mode.Transformer.ExpectedVoltageFor(mode.OutputBus);
						}

						var impedance = line.Impedance;
						var lineDirection = line.InDirectionFrom(bus);
						var oldPower = oldFlow.PowerFlow(bus, line);

						var power = lineIsOnSwapCycle ?
							swapCycleLinePower[line].AdjustForDirection(lineDirection.Direction) : oldPower;
						var current = Complex.Conjugate(power / nominalVoltage);  //This follows from the simplification of SimplifiedDistFlow.

						var updatedBusVoltage = voltage - impedance * current;
						var updatedLineCurrent = current.AdjustForDirection(lineDirection.Direction);

						if (lineIsOnSwapCycle ||
							!updatedBusVoltage.ComplexEqualsWithTolerance(oldVoltage, SUBTREE_EQUALITY_TOLERANCE) ||
							!updatedLineCurrent.ComplexEqualsWithTolerance(oldCurrent, SUBTREE_EQUALITY_TOLERANCE) ||
							!nominalVoltage.EqualsWithTolerance(oldNominalVoltage, SUBTREE_EQUALITY_TOLERANCE) ||
							provider != config.ProviderForBus(child))
						{
							var childBusDelta = delta.AddBusDelta(child, updatedBusVoltage, nominalVoltage, provider);

							var newPower = power.AdjustForDirection(lineDirection.Direction);
							var oldPowerFromBus1 = oldPower.AdjustForDirection(lineDirection.Direction);
							var oldPowerFromBus2 = -oldPowerFromBus1;

							delta.AddLineDelta(line,
								/* power node1: */ oldPowerFromBus1, newPower,
								/* power node2: */ oldPowerFromBus2, -newPower,
								/* current:     */ oldCurrent, updatedLineCurrent);
							stack.Push((childBusDelta, lineIsOnSwapCycle));
						}
					}
				}
			}

			Transformer.Mode TransformerModeForOutputLineAfterMove(Line line)
			{
				if (!line.IsTransformerConnection)
					return null;

				var modes = line.Transformer.Modes.Where(
						m => GetDownstreamLinesAfterMove(m.InputBus).Any(l => l.Endpoints.Any(b => b == line.Transformer.Bus))
							 && line.Endpoints.Any(b => b == m.OutputBus));
				var mode = modes.SingleOrDefault();
				return mode;
			}

			IEnumerable<Line> GetDownstreamLinesAfterMove(Bus b)
			{
				List<Line> buffer = new();
				FillDownstreamLinesAfterMove(b, buffer);
				return buffer;
			}

			void FillDownstreamLinesAfterMove(Bus bus, List<Line> buffer)
			{
				buffer.Clear();

				deletedDownstreamEdges.TryGetValue(bus, out var deletedLine);

				foreach (var line in config.DownstreamLines(bus))
				{
					if (line != deletedLine)
						buffer.Add(line);
				}

				if (newDownstreamEdges.TryGetValue(bus, out var newLine))
					buffer.Add(newLine);
			}
		}

		/// <summary>
		/// Disaggregates a power flow on an aggregated network, to produce
		/// an equivalent flow on the original network
		/// </summary>
		/// <param name="aggregateFlow">The flow to disaggregate</param>
		/// <param name="aggregation">The network aggregation</param>
		/// <param name="originalConfiguration">The network configuration to make a flow for, on
		///   the original network</param>
		/// <param name="originalDemands">The power demands to make a flow for, on
		///   the original network</param>
		/// <returns>The disagreggated flow, on the original network</returns>
		public IPowerFlow DisaggregateFlow(IPowerFlow aggregateFlow, NetworkAggregation aggregation,
			NetworkConfiguration originalConfiguration, PowerDemands originalDemands)
		{
			// Check input

			if (!(aggregateFlow is RadialFlow flow))
				throw new ArgumentException("This is not a flow from this provider");

			return Disaggregate(flow, aggregation, originalConfiguration, originalDemands);
		}

		/// <summary>
		/// Disaggregates a power flow on an aggregated network, to produce
		/// an equivalent flow on the original network.
		/// 
		/// This method does the work for both <see cref="SimplifiedDistFlowProvider"/> and
		/// <see cref="IteratedDistFlowProvider"/>, with only slightly different settings.
		/// </summary>
		/// <param name="aggregateFlow">The flow to disaggregate</param>
		/// <param name="aggregation">The network aggregation</param>
		/// <param name="originalConfiguration">The network configuration to make a flow for, on
		///   the original network</param>
		/// <param name="originalDemands">The power demands to make a flow for, on
		///   the original network</param>
		/// <returns>The disagreggated flow, on the original network</returns>
		internal static IPowerFlow Disaggregate(RadialFlow aggregateFlow, NetworkAggregation aggregation,
			NetworkConfiguration originalConfiguration, PowerDemands originalDemands)
		{
			NetworkConfiguration configuration = aggregateFlow.NetworkConfig;
			if (configuration.Network != aggregation.AggregateNetwork)
				throw new ArgumentException("This flow was not computed on the aggregated network");

			var originalNetwork = originalConfiguration.Network;

			// Find the upstream end of each original line

			Dictionary<Line, Bus> upstreamEnd = new Dictionary<Line, Bus>();
			foreach (var line in configuration.PresentConnectedLines)
			{
				var downstream = line.InDirectionFrom(configuration.UpstreamEnd(line));
				foreach (var originalLine in aggregation.MergeInfoFor(downstream).DirectedLines)
					upstreamEnd.Add(originalLine.Line, originalLine.StartNode);
			}

			// Choose upstream end of dangling lines arbitrarily
			foreach (var line in aggregation.DanglingLines)
				upstreamEnd.Add(line, line.Node1);

			// Choose upstream end of unconnected lines arbitrarily.
			foreach (var aggregateLine in configuration.PresentLines.Where(l => !configuration.LineIsConnected(l)))
				foreach (var originalLine in aggregation.MergeInfoFor(aggregateLine).Lines)
					upstreamEnd.Add(originalLine, aggregateLine.Node1);

			// Initialize the flow

			var originalFlow = new RadialFlow(originalConfiguration, originalDemands,
				ignoreLinePowerLoss: aggregateFlow.IgnoreLinePowerLoss, upstreamEnd: upstreamEnd);

			originalFlow.Status = aggregateFlow.Status;
			originalFlow.StatusDetails = aggregateFlow.StatusDetails;

			// Copy voltage and generated power for not aggregated nodes

			foreach (var bus in configuration.Network.Buses)
			{
				var originalBus = originalNetwork.GetBus(bus.Name);

				if (!bus.IsTransformer)
					// Voltage at transformer bus is undefined
					originalFlow.SetVoltage(originalBus, aggregateFlow.Voltage(bus));

				if (bus.IsProvider)
					originalFlow.SetGeneratedPower(originalBus, aggregateFlow.PowerInjection(bus));
			}

			// Disaggregate data for each line

			foreach (var line in configuration.PresentConnectedLines)
			{
				Bus upstreamBus = configuration.UpstreamEnd(line);
				Bus downstreamBus = configuration.DownstreamEnd(line);

				var current = aggregateFlow.DownstreamCurrent(line);
				var injectedPower = aggregateFlow.PowerFlow(upstreamBus, line);

				if (line.IsTransformerConnection)
				{
					// Transformer connection lines are never aggregated
					var originalLine = aggregation.MergeInfoFor(line).SingleLine;
					originalFlow.SetDownstreamCurrent(originalLine, current);
					originalFlow.SetInjectedPower(originalLine, injectedPower);
				}
				else
				{
					var upstreamVoltage = aggregateFlow.Voltage(upstreamBus);
					var downstreamVoltage = aggregateFlow.Voltage(downstreamBus);

					var mergeInfo = aggregation.MergeInfoFor(line.InDirectionTo(downstreamBus));

					DistributeFlow(mergeInfo, originalFlow, upstreamVoltage, downstreamVoltage, current,
						injectedPower);
				}
			}

			// Set flow for lines and buses not currently connected.
			// (both for components that cannot be connected, and for components that could become connected by closing a switch)
			foreach (var line in originalConfiguration.PresentLines.Where(l => !originalConfiguration.LineIsConnected(l)))
			{
				// for unconnected lines, set a bus at an abitrary end of the line to upstream
				upstreamEnd[line] = originalNetwork.GetBus(line.Endpoints.First().Name);

				// set current to zero
				originalFlow.SetDownstreamCurrent(line, Complex.Zero);
				originalFlow.SetInjectedPower(line, Complex.Zero);

				foreach (var bus in line.Endpoints)
				{
					if (!originalConfiguration.BusIsConnected(bus))
					{
						originalFlow.SetVoltage(bus, Complex.Zero);
					}
				}
			}


			// Set flow for dangling lines

			var danglingLines = aggregation.DanglingLines.ToHashSetScoop();

			foreach (var danglingLine in danglingLines)
			{
				// A dangling line has zero current and power
				originalFlow.SetDownstreamCurrent(danglingLine, Complex.Zero);
				originalFlow.SetInjectedPower(danglingLine, Complex.Zero);
			}

			// Find roots, where dangling lines connect to the network proper
			var danglingBuses = danglingLines
				.SelectMany(l => l.Endpoints)
				.Distinct()
				.ToHashSetScoop();

			var dangleRoots = danglingBuses
				.Where(bus => bus.IncidentLines.Any(l => !danglingLines.Contains(l)))
				.ToHashSetScoop();

			// Find the first buses reached from a root through a danging line
			var firstDanglers = danglingBuses.Except(dangleRoots)
				.Where(b => dangleRoots.Contains(originalConfiguration.UpstreamBus(b)));

			foreach (var first in firstDanglers)
			{
				// Each of these, and the rest of the dangling subtree, has the
				// same voltage as the root

				var root = originalConfiguration.UpstreamBus(first);
				Complex rootVoltage = originalFlow.Voltage(root);

				foreach (var bus in originalConfiguration.BusesInSubtree(first))
				{
					originalFlow.SetVoltage(bus, rootVoltage);
				}
			}

			return originalFlow;
		}

		/// <summary>
		/// Returns a description of the flow provider
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			return "Simplified DF";
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Computes power for each bus in the tree under the given <paramref name="generator"/>,
		/// using a depth first search.
		/// </summary>
		/// <param name="flow">The flow to update</param>
		/// <param name="demands">The consumer demands</param>
		/// <param name="generator"></param>
		private void ComputePowerDepthFirst(RadialFlow flow, PowerDemands demands, Bus generator)
		{
			NetworkConfiguration configuration = flow.NetworkConfig;

			// Update in each bus, from leaves to root
			configuration.Traverse(generator, bottomUpAction: Update);


			void Update(Bus bus)
			{
				// Sum up the power injected to downstream lines
				var power = new Complex();
				foreach (var line in configuration.DownstreamLines(bus))
				{
					power += flow.PowerFlow(bus, line);
				}

				// Add the bus' own consumption
				if (bus.IsConsumer)
					power += demands.PowerDemand(bus);

				if (bus.IsProvider)
				{
					// Set the generated power
					flow.SetGeneratedPower(bus, power);
				}
				else
				{
					// Set the power injected into the upstream line
					var upstreamLine = configuration.UpstreamLine(bus);
					var transformerLossCompensationFactor = 1.0;

					// We don't consider power loss in transformers when using simplified dist flow.
					//if (configuration.TransformerModeForOutputLine(upstreamLine) is Transformer.Mode mode)
					//{
					//	transformerLossCompensationFactor = 1 / mode.PowerFactor;
					//}

					flow.SetInjectedPower(upstreamLine, transformerLossCompensationFactor * power);
				}
			}
		}

		/// <summary>
		/// Distributes flow for a possibly aggregated line onto the lines
		/// it was aggregated from.
		/// </summary>
		/// <param name="line">The line to disaggregate for, in direction
		///   from upstream to downstream</param>
		/// <param name="flow">The flow to update</param>
		/// <param name="voltageAtStart">The voltage at the line's start (upstream)</param>
		/// <param name="voltageAtEnd">The voltage at the line's end (downstream)</param>
		/// <param name="current">The line's current, in downstream direction</param>
		/// <param name="injectedPower">The power injected in to the upstream end
		///   of the line</param>
		private static void DistributeFlow(DirectedMergedLine line, RadialFlow flow,
			Complex voltageAtStart, Complex voltageAtEnd, Complex current, Complex injectedPower)
		{
			switch (line.Type)
			{
				case NetworkAggregation.AggregateType.SingleLine:

					// Line is not an aggregate: Just set the flow data

					flow.SetDownstreamCurrent(line.SingleLine, current);
					flow.SetInjectedPower(line.SingleLine, injectedPower);
					break;

				case NetworkAggregation.AggregateType.Parallel:

					// Split the current over parallel parts

					Dictionary<DirectedMergedLine, Complex> currents = SplitParallelCurrent(current, line);

					// Record the current and power over parallel parts, and recurse

					var voltageAtRoot = flow.Voltage(flow.NetworkConfig.ProviderForBus(line.StartNode));
					foreach (var part in line.Parts)
					{
						var currentInPart = currents[part];
						Complex injectedPowerInPart;
						if (flow.IgnoreLinePowerLoss)
							injectedPowerInPart = Complex.Conjugate(currentInPart) * voltageAtRoot;
						else
							injectedPowerInPart = Complex.Conjugate(currentInPart) * voltageAtStart;

						DistributeFlow(part, flow, voltageAtStart, voltageAtEnd, currentInPart, injectedPowerInPart);
					}
					break;

				case NetworkAggregation.AggregateType.Serial:

					// Split the voltage drop over serial parts, and recurse

					var upstreamVoltage = voltageAtStart;
					foreach (var part in line.Parts)
					{
						Complex voltageDrop = part.Impedance * current;
						var downstreamVoltage = upstreamVoltage - voltageDrop;

						flow.SetVoltage(part.StartNode, upstreamVoltage);
						DistributeFlow(part, flow, upstreamVoltage, downstreamVoltage, current, injectedPower);

						if (!flow.IgnoreLinePowerLoss)
							injectedPower -= Complex.Conjugate(current) * voltageDrop;

						upstreamVoltage = downstreamVoltage;
					}
					break;
			}
		}

		/// <summary>
		/// Decides how to split a current between parallel lines. This includes handling special cases where one
		/// or more impedances are zero.
		/// </summary>
		/// <param name="current">The total current to split</param>
		/// <param name="mergedLine">The merged line whose parallel parts to divide the current among</param>
		/// <returns></returns>
		private static Dictionary<DirectedMergedLine, Complex> SplitParallelCurrent(Complex current, DirectedMergedLine mergedLine)
		{
			var lines = mergedLine.Parts;

			if (lines.All(p => p.Impedance != 0))
			{
				// Normal case: all impedances nonzero
				return lines.ToDictionary(p => p, part => current * mergedLine.Impedance / part.Impedance);
			}

			// One or more parallel lines have zero impedance. Current flows in 
			// these lines only

			var linesWithCurrent = lines.Where(p => p.Impedance == 0);

			var currents = lines.ToDictionary(p => p, p => Complex.Zero);

			var lineWithNoIMax = linesWithCurrent.Where(p => p.Imax >= double.MaxValue).FirstOrDefault();
			if (lineWithNoIMax.MergedLine != null)
			{
				// One or more lines have effectively infinite IMax. Send all current through one of them
				currents[lineWithNoIMax] = current;
				return currents;
			}

			// All lines have finite IMax. 

			var sumIMax = linesWithCurrent.Sum(p => p.Imax);

			if (sumIMax == 0)
			{
				// All IMax are zero. Divide current equally
				foreach (var line in linesWithCurrent)
					currents[line] = current / linesWithCurrent.Count();

				return currents;
			}

			// Split current in proportion to IMax

			foreach (var line in linesWithCurrent)
				currents[line] = current * line.Imax / sumIMax;

			return currents;
		}

		#endregion
	}
}
