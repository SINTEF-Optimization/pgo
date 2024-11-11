using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Checks for line and bus voltage limits, for use in LineVoltageLimitsConstraint and ConsumerVoltageLimitsConstraint.
	/// The check functions will in general make use of the distflow calculations for the voltages on the network,
	/// but will avoid using calling distflow calcs if the criterion is already satisfied or refuted based on the topology.
	/// The topological check uses the generator voltage at the top of the tree as a maximum voltage on all connected nodes,
	/// and optionally takes a maximum loss factor assumption to calculate a minimum voltage on all connected nodes.
	/// </summary>
	public class BusVoltageChecker
	{
		/// <summary>
		/// If a line is has a provider with voltage v, assume that the voltage on the 
		/// line will be at least v * AssumedMaxLossRatio. If the value is null then 
		/// no such assumption can be made.
		/// </summary>
		public double? AssumedMaxLossRatio { get; }

		/// <summary>
		/// The criterion has at least once been shown to be satisfied using only the voltage bound given by the generator which the line is attached to.
		/// </summary>
		public bool HaveUsedTopologyBasedSatisfaction { get; private set; } = false;
		/// <summary>
		/// The criterion has at least one been refuted using only the voltage bound given by the generator which the line is attached to,
		/// combined with the max loss ratio assumption (AssumedMaxLossRatio).
		/// </summary>
		public bool HaveUsedTopologyBasedRefutation { get; private set; } = false;
		/// <summary>
		/// The criterion has at least once been unable to prove satisfaction or refutation using only the voltage bound given by the generator the 
		/// line is attached to, and therefore needed to use the DistFlow calculations for comparing allowed and actual voltage on the line.
		/// </summary>
		public bool HaveUsedFlowCalculation { get; private set; } = false;

		/// <summary>
		/// Check voltage limits using the given assumed max loss ratio.
		/// </summary>
		/// <param name="assumedMaxLossRatio"></param>
		public BusVoltageChecker(double? assumedMaxLossRatio)
		{
			AssumedMaxLossRatio = assumedMaxLossRatio;
		}

		/// <summary>
		/// Calculate the penalty for voltages outside the allowed voltage range. 
		/// </summary>
		/// 
		/// Topological range check:
		/// The generator voltage and a maximum loss assumption is used to establish a topological voltage range, which 
		/// is a safe overapproximation of the possible voltage range at all buses connected to the generator. If the 
		/// voltage limits are satisfied at all points of the topological voltage range, there can be no violation, 
		/// and NULL is returned. If the voltage limits are violated for every point inside the topological voltage 
		/// range, the maximum penalty is returned. The maximum penalty is defined as the maximum generator voltage 
		/// for the whole network multiplied by the maximum loss factor (or 1.0 if no loss factor is given).
		/// 
		/// Full check:
		/// If the topological range check was inconclusive, the getVoltage function is called to get the actual voltage.
		/// The penalty is calculated as the distance from the voltage to the allowed voltage interval:
		///   max(max(0, voltage - vMax), max(0, vMin - voltage)),
		/// or NULL if the voltage is inside the allowed range.
		/// 
		/// <param name="maxNetworkVoltage"></param>
		/// <param name="vMin"></param>
		/// <param name="vMax"></param>
		/// <param name="nominalVoltage"></param>
		/// <param name="getVoltage"></param>
		/// <returns></returns>
		private double? MinMaxVoltagePenalty(double maxNetworkVoltage, double vMin, double vMax, double? nominalVoltage, Func<double?> getVoltage)
		{
			var topoVoltageRangeMax = nominalVoltage.GetValueOrDefault();
			var topoVoltageRangeMin = (AssumedMaxLossRatio is double loss) ? (nominalVoltage.GetValueOrDefault() * (1.0 - loss)) : -1.0;

			if (vMin < topoVoltageRangeMin && vMax > topoVoltageRangeMax)
			{
				// we are guaranteed to be within range, no need to check flow
				HaveUsedTopologyBasedSatisfaction = true;
				return null;
			}

			if (vMax < topoVoltageRangeMin || vMin > topoVoltageRangeMax)
			{
				HaveUsedTopologyBasedRefutation = true;
				return maxNetworkVoltage;
			}

			HaveUsedFlowCalculation = true;
			if (!(getVoltage() is double voltage))
				return null;
			if (voltage.IsNanOrInfinity())
				return double.PositiveInfinity;
			if (voltage > vMax)
				return voltage - vMax;
			if (voltage < vMin)
				return vMin - voltage;

			return null;
		}

		/// <summary>
		/// Use the PowerFlowDelta to get the bus voltage after a move. If the bus is not part of the
		/// PowerFlowDelta.NewVoltage set, the flow provides the voltage instead.
		/// </summary>
		/// <param name="bus">The bus to get the voltage for.</param>
		/// <param name="flowDelta">The flow overrides.</param>
		/// <param name="flow">The base flow, overridden by any entry in flowDelta.</param>
		/// <returns></returns>
		public double BusVoltageAfterDelta(Bus bus, PowerFlowDelta flowDelta, IPowerFlow flow)
		{
			if (flowDelta.BusDeltas.TryGetValue(bus, out var value))
				return value.NewVoltage.Magnitude;

			return flow.Voltage(bus).Magnitude;
		}

		/// <summary>
		/// Use the PowerFlowDelta to get the bus provider after a move. If the bus is not part of the
		/// PowerFlowDelta.NewProvider set, the PeriodSolution and FlowProvider representing the pre-move state
		/// is used to calculate the provider instead.
		/// </summary>
		/// <param name="sol"></param>
		/// <param name="flowDelta"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		public double? NominalVoltageAfterDelta(PeriodSolution sol, PowerFlowDelta flowDelta, Bus b)
		{
			if (flowDelta.BusDeltas.TryGetValue(b, out var value))
				return value.NewNominalVoltage;
			return sol.NetConfig.NominalVoltage(b);
		}


		/// <summary>
		/// For each bus in the network which is involved in the given  power flow delta, and has its penalty changed,
		/// return the bus and its penalty delta.
		/// </summary>
		/// <param name="sol"></param>
		/// <param name="flowProvider"></param>
		/// <param name="flowDelta"></param>
		/// <returns></returns>
		public IEnumerable<(Bus bus, double penaltyDelta)> ConsumerBusDeltaPenalties(PeriodSolution sol, IFlowProvider flowProvider, PowerFlowDelta flowDelta)
		{
			if (sol.Flow(flowProvider) is not IPowerFlow flow)
				yield break;

			var maxNetworkVoltage = sol.Network.MaxNetworkVoltage;

			// Create funcs outside loop to reduce memory allocations
			Bus currentBus = null;
			Func<double?> Voltage = () => flow.Voltage(currentBus).Magnitude;
			Func<double?> VoltageAfterDelta = () => BusVoltageAfterDelta(currentBus, flowDelta, flow);

			foreach (var bus in flowDelta.BusDeltas.Keys.Where(b => b.IsConsumer))
			{
				currentBus = bus;

				var oldNominalVoltage = sol.NetConfig.NominalVoltage(bus);
				var newNominalVoltage = NominalVoltageAfterDelta(sol, flowDelta, bus);

				var oldPenalty = MinMaxVoltagePenalty(maxNetworkVoltage, bus.VMin, bus.VMax, oldNominalVoltage, Voltage);
				var newPenalty = MinMaxVoltagePenalty(maxNetworkVoltage, bus.VMin, bus.VMax, newNominalVoltage, VoltageAfterDelta);

				var deltaPenalty = (newPenalty ?? 0.0) - (oldPenalty ?? 0.0);
				if (deltaPenalty != 0.0)
					yield return (bus, deltaPenalty);
			}
		}

		/// <summary>
		/// For each line in the network which is involved in the given power flow delta, and has its penalty changed,
		/// return the line and its penalty delta.
		/// </summary>
		/// <param name="sol"></param>
		/// <param name="flowProvider"></param>
		/// <param name="flowDelta"></param>
		/// <returns></returns>
		public IEnumerable<(Line line, double deltaPenalty)> LineDeltaPenalties(PeriodSolution sol, IFlowProvider flowProvider, PowerFlowDelta flowDelta)
		{
			if (sol.Flow(flowProvider) is not IPowerFlow flow)
				yield break;

			var maxNetworkVoltage = sol.Network.MaxNetworkVoltage;

			// Create funcs outside loop to reduce memory allocations
			Line currentLine = null;
			Func<double?> Voltage = () => currentLine.Endpoints.Select(b => flow.Voltage(b).Magnitude).Max();
			Func<double?> VoltageAfterDelta = () => currentLine.Endpoints.Select(b => BusVoltageAfterDelta(b, flowDelta, flow)).Max();

			foreach (var line in flowDelta.LineDeltas.Keys)
			{
				currentLine = line;

				if (line.IsSwitchable && sol.NetConfig.SwitchSettings.IsOpen(line)) 
					continue;

				var oldNominalVoltage = sol.NetConfig.NominalVoltage(line.Node1);
				var newNominalVoltage = NominalVoltageAfterDelta(sol, flowDelta, line.Node1);

				var oldPenalty = MinMaxVoltagePenalty(maxNetworkVoltage, double.NegativeInfinity, line.VMax, oldNominalVoltage, Voltage);
				var newPenalty = MinMaxVoltagePenalty(maxNetworkVoltage, double.NegativeInfinity, line.VMax, newNominalVoltage, VoltageAfterDelta);

				var deltaPenalty = (newPenalty ?? 0.0) - (oldPenalty ?? 0.0);
				if (deltaPenalty != 0.0)
					yield return (line, deltaPenalty);
			}

		}

		/// <summary>
		/// For each line in the network with a non-zero voltage limit violation penalty, return the line and its penalty.
		/// </summary>
		/// <param name="flowProvider"></param>
		/// <param name="sol"></param>
		/// <returns></returns>
		public IEnumerable<(Line line, double penalty)> LinePenalties(IFlowProvider flowProvider, PeriodSolution sol)
		{
			if (sol.Flow(flowProvider) is not IPowerFlow flow)
				yield break;

			var maxNetworkVoltage = sol.Network.MaxNetworkVoltage;

			// Create func outside loop to reduce memory allocations
			Line currentLine = null;
			Func<double?> Voltage = () => currentLine.Endpoints.Select(b => flow.Voltage(b).Magnitude).Max();

			foreach (var line in sol.Network.Lines)
			{
				currentLine = line;

				if (line.IsSwitchable && sol.NetConfig.SwitchSettings.IsOpen(line))
					continue;

				// Note that we arbitrarily choose the Node1 end of the line for nominal voltage.
				// The ends may have different nominal voltages in transformer output lines, but
				// we don't expect these lines to have voltage limits.
				var nominalVoltage = sol.NetConfig.NominalVoltage(line.Node1);
				var check = MinMaxVoltagePenalty(maxNetworkVoltage, double.NegativeInfinity, line.VMax, nominalVoltage, Voltage);

				if (check is double penalty)
					yield return (line, penalty);
			}
		}

		/// <summary>
		/// For each bus in the network with a non-zero voltage limit violation penalty, return the bus and its penalty.
		/// </summary>
		/// <param name="flowProvider"></param>
		/// <param name="sol"></param>
		/// <returns></returns>
		public IEnumerable<(Bus bus, double penalty)> ConsumerBusPenalties(IFlowProvider flowProvider, PeriodSolution sol)
		{
			if (sol.Flow(flowProvider) is not IPowerFlow flow)
				yield break;

			var maxNetworkVoltage = sol.Network.MaxNetworkVoltage;

			// Create func outside loop to reduce memory allocations
			Bus currentBus = null;
			Func<double?> Voltage = () => flow.Voltage(currentBus).Magnitude;

			foreach (var bus in sol.Network.Buses.Where(b => b.IsConsumer))
			{
				currentBus = bus;

				var nominalVoltage = sol.NetConfig.NominalVoltage(bus);
				var check = MinMaxVoltagePenalty(maxNetworkVoltage, bus.VMin, bus.VMax, nominalVoltage, Voltage);

				if (check is double penalty)
					yield return (bus, penalty);
			}
		}
	}

}
