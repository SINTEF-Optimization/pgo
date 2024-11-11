using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A local search neighbourhood, associated with one open switch, of all SwapSwitchStatusMove's that involves
	/// closing this switch and opening another to break the resulting cycle.
	/// </summary>
	/// <seealso cref="SwapSwitchStatusMove"/>
	public class CloseSwitchAndOpenOtherNeighbourhood : ResettableNeighborhood
	{
		#region Public properties 

		/// <summary>
		/// The Line/Switch to close
		/// </summary>
		public Line SwitchToClose { get; private set; }

		/// <summary>
		/// If true, maximum two switches to open will be considered, one on each side of the switch to close (if there is one on both sides 
		/// along the cycle). If false, all closed switches in the cycle will be considered.
		/// Set by the constructor, but can be changed during the search.
		/// </summary>
		bool UseSmallNeighbourhood { get; set; } = true;

		/// <summary>
		/// Enumerates all moves that open a switch and close another, in all periods,
		/// in the given solution
		/// </summary>
		internal static IEnumerable<Move> AllMoves(PgoSolution solution)
		{
			return solution.SinglePeriodSolutions.SelectMany(periodSolution =>
				periodSolution.NetConfig.OpenLines.SelectMany(openSwitch =>
				new CloseSwitchAndOpenOtherNeighbourhood(solution, openSwitch, periodSolution.Period, false)));
		}

		#endregion

		#region Private data members

		/// <summary>
		/// The period that the move should act in
		/// </summary>
		private readonly Period _period;

		/// <summary>
		/// The solution we are operating on.
		/// </summary>
		private PgoSolution Solution => Center as PgoSolution;

		/// <summary>
		/// The single period solution data related to _period
		/// </summary>
		private PeriodSolution PeriodSolutionData => Solution.GetPeriodSolution(_period);

		private List<Line> _switchesToOpen = null;

		#endregion

		#region Construction

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="period">The period that the move should act in. For
		/// single period problems, set to zero or ignore (optional).</param>
		/// <param name="switchToClose">The Line/Switch to close (assumed to be open)</param>
		/// <param name="useSmallNeighbourhood">If true, maximum two switches to open will be considered, one on each side of the switch to close (if there is one on both sides 
		/// along the cycle. If false, all closed switches in the cycle will be considered.
		/// Optional, default value = true;
		/// </param>
		public CloseSwitchAndOpenOtherNeighbourhood(Line switchToClose, Period period = null, bool useSmallNeighbourhood = true)
		{
			_period = period;
			SwitchToClose = switchToClose;
			UseSmallNeighbourhood = useSmallNeighbourhood;
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="solution">The solution to modify</param>
		/// <param name="switchToClose">The Line/Switch to close (assumed to be open)</param>
		/// <param name="period">The period that the move should act in. For
		/// single period problems, set to zero or ignore (optional).</param>
		/// <param name="useSmallNeighbourhood">If true, maximum two switches to open will be considered, one on each side of the switch to close (if there is one on both sides 
		/// along the cycle. If false, all closed switches in the cycle will be considered.
		/// Optional, default value = true;
		/// </param>
		public CloseSwitchAndOpenOtherNeighbourhood(PgoSolution solution, Line switchToClose, Period period = null, bool useSmallNeighbourhood = true)
		{
			_period = period;
			SwitchToClose = switchToClose;
			UseSmallNeighbourhood = useSmallNeighbourhood;

			Init(solution);
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Reset the neighborhood to work on the given solution.
		/// </summary>
		/// <param name="newCenter"></param>
		public override void Init(ISolution newCenter)
		{
			base.Init(newCenter);

			FindSwitchesToOpen();
		}

		/// <summary>
		/// The number of switches to open.
		/// </summary>
		public override int Count => _switchesToOpen.Count;

		/// <summary>
		/// Enumerates the moves in the neighbourhood. If the SwitchToClose is already closed,
		/// the function returns an empty enumeration.
		/// </summary>
		/// <returns></returns>
		public override IEnumerator<Move> GetEnumerator()
		{
			foreach (Line switchToOpen in _switchesToOpen)
			{
				// If the move is not feasible because of missing transformer modes, skip it:
				if (!PeriodSolutionData.NetConfig.SwappingSwitchesUsesValidTransformerModes(
						switchToClose: SwitchToClose,
						switchToOpen: switchToOpen))
					continue;

				yield return new SwapSwitchStatusMove(Solution, _period, switchToOpen, SwitchToClose);
			}
		}

		/// <summary>
		/// Generates all possible neighbourhoods for a multi-period problem.
		/// </summary>
		/// <param name="problem"></param>
		/// <param name="useSmallNeighbourhoods">If true, we use small neighbourhoods.</param>
		/// <param name="randomizeOrder">If true, the neighborhoods are returned in a random order</param>
		/// <returns>The neighbourhoods</returns>
		public static IEnumerable<CloseSwitchAndOpenOtherNeighbourhood>
			GenerateNeighbourhoodsFromNetworkPerTimePeriod(PgoProblem problem, bool useSmallNeighbourhoods, bool randomizeOrder)
		{
			PowerNetwork network = problem.Network;

			List<CloseSwitchAndOpenOtherNeighbourhood> nhs = new List<CloseSwitchAndOpenOtherNeighbourhood>();
			foreach (var period in problem.Periods)
			{
				nhs.AddRange(network.SwitchableLines.Select(line =>
					new CloseSwitchAndOpenOtherNeighbourhood(line, period, useSmallNeighbourhood: useSmallNeighbourhoods)));
			}

			if (randomizeOrder)
				return nhs.Shuffled();
			else
				return nhs;
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Fills _switchesToOpen with the switches that can be opened to break the cycle
		/// when <see cref="SwitchToClose"/> is closed
		/// </summary>
		private void FindSwitchesToOpen()
		{
			_switchesToOpen = new List<Line>();

			if (!PeriodSolutionData.IsOpen(SwitchToClose))
				// The switch cannot be closed
				return;

			// Find the cycle than contains the switch when closed
			IEnumerable<Line> cycle = PeriodSolutionData.FindCycleWith(SwitchToClose);
			List<Line> switchesInCycle = cycle.Where(l => l.IsSwitchable).ToList();

			if (switchesInCycle.Count == 1)
				// There are no other switchable lines in the cycle
				return;


			if (UseSmallNeighbourhood)
			{
				// Try a small neighbourhood instead, just considering neighbour switches.
				int pathLength = switchesInCycle.Count;

				int indexOfSwitchToClose = switchesInCycle.IndexOf(SwitchToClose);
				int indexBefore = (indexOfSwitchToClose - 1 + pathLength) % pathLength;
				int indexAfter = (indexOfSwitchToClose + 1) % pathLength;

				_switchesToOpen.Add(switchesInCycle[indexBefore]);
				if (indexBefore != indexAfter)
					_switchesToOpen.Add(switchesInCycle[indexAfter]);
			}
			else
				// Try to open every other switch in the cycle
				_switchesToOpen = switchesInCycle.Where(l => !PeriodSolutionData.IsOpen(l)).ToList();
		}

		#endregion
	}
}
