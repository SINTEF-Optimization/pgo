using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Newtonsoft.Json;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Kernel.ConflictbasedBranchAndBound;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// The collection of solution data associated with a single period of the problem.
	/// Contains a network configuration and possibly one or more computed flows.
	/// </summary>
	public class PeriodSolution
	{
		#region Public properties

		/// <summary>
		/// The data for the period that this solution is for
		/// </summary>
		public PeriodData PeriodData { get; private set; }

		/// <summary>
		/// The network configuration in the period.
		/// </summary>
		public NetworkConfiguration NetConfig { get; }

		/// <summary>
		/// The underlying <see cref="PowerNetwork"/>.
		/// </summary>
		public PowerNetwork Network => NetConfig.Network;

		/// <summary>
		/// The switch settings in the network configuration
		/// </summary>
		internal SwitchSettings SwitchSettings => NetConfig.SwitchSettings;

		/// <summary>
		/// The <see cref="Period"/> that this single period solution is for
		/// </summary>
		public Period Period => PeriodData.Period;

		/// <summary>
		/// Enumerates the switches that are closed in the solution.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<Line> ClosedSwitches => NetConfig.SwitchSettings.ClosedSwitches;

		/// <summary>
		/// Enumerates the switches that are open in the solution.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<Line> OpenSwitches => NetConfig.SwitchSettings.OpenSwitches;

		/// <summary>
		/// Enumerates the lines in the underlying network that are present,
		/// i.e. either not switchable or switchable but closed.
		/// </summary>
		public IEnumerable<Line> PresentLines => NetConfig.PresentLines;

		/// <summary>
		/// Returns true if the solution's <see cref="NetConfig"/> is radial
		/// (connected, with no cycles)
		/// </summary>
		public bool IsRadial => NetConfig.IsRadial;

		/// <summary>
		/// Returns true if the solution's <see cref="NetConfig"/> is connected
		/// </summary>
		public bool IsConnected => NetConfig.IsConnected;

		/// <summary>
		/// Returns true if the solution's <see cref="NetConfig"/> has cycles
		/// </summary>
		public bool HasCycles => NetConfig.HasCycles;

		/// <summary>
		/// True if the network configuration <see cref="NetConfig"/> has any transformers that are using missing modes.
		/// </summary>
		public bool HasTransformersUsingMissingModes => NetConfig.HasTransformersUsingMissingModes;

		/// <summary>
		/// True if the network configuration is radial and each transformer has a corresponding mode for its upstream line.
		/// </summary>
		public bool AllowsRadialFlow(bool requireConnected) => NetConfig.AllowsRadialFlow(requireConnected);

		/// <summary>
		/// Enumerates unconnected buses that have a non-zero demand in this period.
		/// </summary>
		public IEnumerable<Bus> UnconnectedConsumersWithDemand => NetConfig.UnconnectedConsumers.Where(c => PeriodData.Demands.PowerDemand(c).Magnitude > 0.0);

		/// <summary>
		/// Enumerates the flows that have been computed for this period solution
		/// </summary>
		public IEnumerable<(IFlowProvider, IPowerFlow)> Flows
		{
			get
			{
				if (_computedFlows == null)
					return new (IFlowProvider, IPowerFlow)[0];

				lock (_mutex)
				{
					return _computedFlows.Select(kv => (kv.Key, kv.Value)).ToList();
				}
			}
		}

		#endregion

		#region Private data members

		/// <summary>
		/// The flows that have been computed for this solution. 
		/// Cleared whenever switch settings are changed.
		/// </summary>
		private Dictionary<IFlowProvider, IPowerFlow> _computedFlows;

		/// <summary>
		/// The object that is locked to protect _computedFlows from concurrent
		/// access.
		/// </summary>
		private object _mutex = new();

		#endregion

		#region Constructors

		/// <summary>
		/// Creates a solution with all switches closed.
		/// </summary>
		/// <param name="periodData">The period data that the solution is for.</param>
		public PeriodSolution(PeriodData periodData)
		{
			PeriodData = periodData;
			NetConfig = new NetworkConfiguration(periodData.Network, new SwitchSettings(periodData.Network));
		}

		/// <summary>
		/// Initializes a copy of the given period solution
		/// </summary>
		/// <param name="other"></param>
		/// <param name="copyFlows">If true, flows are copied</param>
		public PeriodSolution(PeriodSolution other, bool copyFlows)
		{
			PeriodData = other.PeriodData;
			NetConfig = other.NetConfig?.Clone();

			if (copyFlows && other._computedFlows != null)
			{
				lock (other._mutex)
					_computedFlows = new Dictionary<IFlowProvider, IPowerFlow>(other._computedFlows);
			}
		}

		/// <summary>
		/// Constructs a solution from the given switch settings, for the given period problem data
		/// </summary>
		/// <param name="periodData"></param>
		/// <param name="switchSettings"></param>
		public PeriodSolution(PeriodData periodData, SwitchSettings switchSettings)
		{
			PeriodData = periodData;
			NetConfig = new NetworkConfiguration(periodData.Network, switchSettings);
		}

		/// <summary>
		/// Constructs a solution from the given network configuration.
		/// </summary>
		/// <param name="periodData"></param>
		/// <param name="config"></param>
		public PeriodSolution(PeriodData periodData, NetworkConfiguration config)
		{
			PeriodData = periodData;
			NetConfig = config;
		}

		/// <summary>
		/// Creates a clone of this period solution.
		/// </summary>
		/// <returns>The copy.</returns>
		/// <param name="copyFlows">If true, any computed flows are copied</param>
		public PeriodSolution Clone(bool copyFlows = false) => new PeriodSolution(this, copyFlows);

		#endregion

		#region Public methods

		/// <summary>
		/// Identifies the cycle that will arise if the given switch is closed.
		/// 
		/// If the cycle involves two generators, it starts at one generator and ends at the other.
		/// If not, it starts and ends at the bus in the cycle that is closest to a generator.
		/// </summary>
		/// <param name="switchToClose">The switch to close</param>
		internal IEnumerable<Line> FindCycleWith(Line switchToClose)
		{
			return NetConfig.FindCycleWith(switchToClose).Select(directedLine => directedLine.Line);
		}

		#region Switch settings

		/// <summary>
		/// Returns a close of the solution's switch settings
		/// </summary>
		/// <returns></returns>
		public SwitchSettings CloneSwitchSettings() => NetConfig.SwitchSettings.Clone();

		/// <summary>
		/// Returns the buses that are immediate neighbours of the given bus,
		/// based on the current switch settings.
		/// </summary>
		/// <param name="bus"></param>
		/// <returns></returns>
		internal IEnumerable<Bus> GetSwitchedNeighbours(Bus bus) => bus.IncidentLines.Where(l => ((!l.IsSwitchable) || !IsOpen(l))).Select(l => l.OtherEnd(bus));

		/// <summary>
		/// Sets the state of the switchable <paramref name="line"/>
		/// </summary>
		/// <param name="line"></param>
		/// <param name="open">true if line is to be opened, false if it is to be closed</param>
		public void SetSwitch(Line line, bool open)
		{
			bool changed = NetConfig.SetSwitch(line, open);

			if (changed)
				_computedFlows = null;
		}

		/// <summary>
		/// Opens a switch with the intent of breaking a cycle associated with the given <paramref name="cycleBridge"/>.
		/// Calling SetSwitch in in the contained <see cref="SwitchSettings"/>.
		/// Flags the need to compute new inter-bus-relationships, and notes which switch was opened.
		/// </summary>
		/// <param name="line">Line to switch</param>
		/// <param name="cycleBridge">The cycle bridge.</param>
		public void OpenSwitchForBreakingCycleWithBridge(Line line, Line cycleBridge)
		{
			NetConfig.OpenSwitchForBreakingCycleWithBridge(line, cycleBridge);
			_computedFlows = null;
		}

		/// <summary>
		/// Sets the state of the switchable line between two named nodes
		/// </summary>
		/// <param name="fromNodeName"></param>
		/// <param name="toNodeName"></param>
		/// <param name="open">true if line is to be opened, false if it is to be closed</param>
		public void SetSwitch(string fromNodeName, string toNodeName, bool open)
		{
			var line = Network.GetLine(fromNodeName, toNodeName);
			SetSwitch(line, open);
		}

		/// <summary>
		/// Returns true if l is open, false if not.
		/// </summary>
		/// <param name="l"></param>
		/// <returns></returns>
		public bool IsOpen(Line l) => NetConfig.IsOpen(l);

		/// <summary>
		/// Returns the number of switches that are different in the given solution 
		/// from this one, assuming both solutions are for the same encoding.
		/// </summary>
		/// <param name="other"></param>
		/// <returns></returns>
		public int NumberOfDifferentSwitches(PeriodSolution other)
		{
			return NetConfig.SwitchSettings.NumberOfDifferentSwitches(other.NetConfig.SwitchSettings);
		}

		#endregion

		/// <summary>
		/// Clears all stored flows
		/// </summary>
		public void ClearFlows()
		{
			_computedFlows = null;
		}

		/// <summary>
		/// Returns the flow computed by the given provider. If a flow cannot be
		/// computed, usually because the network is not radial, returns null.
		/// Note that the flow may have status <see cref="FlowStatus.Failed"/>.
		/// </summary>
		public IPowerFlow Flow(IFlowProvider provider)
		{
			// Return flow if cached
			if (_computedFlows != null)
			{
				lock (_mutex)
				{
					if (_computedFlows.TryGetValue(provider, out var flow))
						return flow;
				}
			}

			// Nope. Compute and return
			ComputeFlow(provider);

			return _computedFlows[provider];
		}

		/// <summary>
		/// Sets the flow for the given provider to the given value
		/// </summary>
		public void SetFlow(IFlowProvider provider, IPowerFlow flow)
		{
			lock (_mutex)
			{
				if (_computedFlows == null)
					_computedFlows = new Dictionary<IFlowProvider, IPowerFlow>();

				_computedFlows[provider] = flow;
			}
		}

		/// <summary>
		/// Computes the power flow arising from the current switch settings.
		/// If the flow already exists, it is not recomputed.
		/// </summary>
		/// <param name="flowProvider">The provider that is used to calculate the physical flow</param>
		/// <returns>true if computation succeeded, false otherwise</returns>
		public bool ComputeFlow(IFlowProvider flowProvider)
		{
			lock (_mutex)
			{
				if (_computedFlows == null)
					_computedFlows = new Dictionary<IFlowProvider, IPowerFlow>();

				if (_computedFlows.TryGetValue(flowProvider, out var flow))
					return flow.Status != FlowStatus.Failed;

				if (!NetConfig.AllowsRadialFlow(requireConnected: false))
				{
					_computedFlows[flowProvider] = null;
					return false;
				}

				FlowProblem flowProblem = new FlowProblem(NetConfig, PeriodData.Demands);

				flow = flowProvider.ComputeFlow(flowProblem);
				if (flow.Status == FlowStatus.None)
					throw new Exception("The flow provider must set a flow status");

				_computedFlows[flowProvider] = flow;

				if (_computedFlows == null)
					throw new Exception("Solution was changed while computing flow. This is bad.");

				return flow.Status != FlowStatus.Failed;
			}
		}

		#endregion
	}
}
