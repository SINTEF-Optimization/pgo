using System;
using System.Collections.Generic;
using System.Linq;
using Roslyn.Utilities;
using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A move that closes one switch and opens another
	/// </summary>
	internal class SwapSwitchStatusMove : ChangeSwitchesMove, IDiscardable
	{
		#region Public properties 

		/// <summary>
		/// The Line/Switch to open
		/// </summary>
		public Line SwitchToOpen { get; }

		/// <summary>
		/// The Line/Switch to close
		/// </summary>
		public Line SwitchToClose { get; }

		#endregion

		#region Private data members

		/// <summary>
		/// The cached power flow delta, computed by the first call to <see cref="GetCachedPowerFlowDelta"/>
		/// </summary>
		Dictionary<IFlowProvider, PowerFlowDelta> _powerFlowDeltaCache = null;

		#endregion

		/// <summary>
		/// Constructor, taking a solution.
		/// </summary>
		public SwapSwitchStatusMove(PgoSolution solution, Period period, Line switchToOpen, Line switchToClose)
			: base(solution, period, new[] { switchToOpen }, new[] { switchToClose })
		{
			SwitchToOpen = switchToOpen;
			SwitchToClose = switchToClose;
		}

		/// <summary>
		/// Called by neighborhood explorers and optimizers when this move is no longer 
		/// needed. Releases the power flow delta(s) back to the pool.
		/// </summary>
		public void Discard()
		{
			foreach (var flow in _powerFlowDeltaCache.Values)
				flow.Free();

			_powerFlowDeltaCache.Clear();
		}

		#region Private methods

		/// <summary>
		/// If a power flow delta has been previously computed for the move, and
		/// the given flow provider, this is returned.
		/// Otherwise, it is computed, cached and returned.
		/// </summary>
		/// <returns></returns>
		internal PowerFlowDelta GetCachedPowerFlowDelta(IFlowProvider flowProvider)
		{
			if (_powerFlowDeltaCache == null)
				_powerFlowDeltaCache = new Dictionary<IFlowProvider, PowerFlowDelta>();

			if (_powerFlowDeltaCache.TryGetValue(flowProvider, out var deltaFlow))
				return deltaFlow;

			deltaFlow = flowProvider.ComputePowerFlowDelta(this);
			_powerFlowDeltaCache.Add(flowProvider, deltaFlow);

			return deltaFlow;
		}

		/// <summary>
		/// Clears any power flow deltas computed for the move
		/// </summary>
		internal void ClearCachedPowerFlowDelta()
		{
			_powerFlowDeltaCache?.Clear();
		}

		/// <summary>
		/// Returns the line that is upstream of the given bus after the move has been applied.
		/// </summary>
		internal Line NewUpstreamLine(Bus bus)
		{
			var oldUpstream = Configuration.UpstreamLine(bus);

			if (!Configuration.IsAncestor(SwitchToOpen, bus))
				// The bus is not supplied through the switch to be opened; 
				// it keeps the same upstream line
				return oldUpstream;


			if (Configuration.IsAncestorOrSame(bus, SwitchToClose.Node1))
				// The bus lies between the opening switch and Node1 of the closing switch;
				// its new upstream line will be toward the closing switch
				return Configuration.DownstreamLineToward(bus, SwitchToClose.Node1);

			if (Configuration.IsAncestorOrSame(bus, SwitchToClose.Node2))
				// The bus lies between the opening switch and Node2 of the closing switch;
				// its new upstream line will be toward the closing switch
				return Configuration.DownstreamLineToward(bus, SwitchToClose.Node2);

			// The bus belongs to subtree hanging from between the switches
			return oldUpstream;
		}

		#endregion
	}
}
