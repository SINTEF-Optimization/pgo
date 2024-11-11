using Sintef.Scoop.Kernel;
using System;
using System.Collections.Generic;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A neighborhood selector that, after an improving move is found, suggests neighborhoods
	/// with moves for the same switches, in the adjacent periods.
	/// This is expected to improve performance, since it is likely that doing the same
	/// thing in the adjacent period is improving as well.
	/// 
	/// Apart from this mechanism, the selector merely relays the neighborhoods selected
	/// by the 'main' selector.
	/// </summary>
	internal class RetryImprovingMoveInAdjacentPeriodsSelector : INeighborhoodSelector
	{
		/// <summary>
		/// Event raised when a selection result is registered.
		/// Created primarily for testing
		/// </summary>
		public event EventHandler<ResultEventArgs> RegisteringResult;

		/// <summary>
		/// The main selector
		/// </summary>
		private INeighborhoodSelector _mainSelector;

		/// <summary>
		/// Info about the moves we are going to retry
		/// </summary>
		private List<MoveToTry> _toRetry = new();

		/// <summary>
		/// The number of retry neighborhoods we have generated, for which the result
		/// has not yet been registered
		/// </summary>
		private int _pendingNhCount = 0;

		/// <summary>
		/// Creates the retrying selector
		/// </summary>
		/// <param name="mainSelector">The main selector. Generates the stream of neighborhoods
		///   that this selector modifies with retries.</param>
		public RetryImprovingMoveInAdjacentPeriodsSelector(INeighborhoodSelector mainSelector)
		{
			_mainSelector = mainSelector;
		}

		/// <summary>
		/// (Re)initializes the selector
		/// </summary>
		public void Init()
		{
			_toRetry.Clear();
			_pendingNhCount = 0;
			_mainSelector.Init();
		}

		/// <inheritdoc/>
		public INeighborhood SelectNeighborhood(ISolution solution, ICriteriaSet criteriaSet)
		{
			PgoSolution mySolution = (PgoSolution)solution;

			while (_toRetry.Count != 0)
			{
				// Get the first move to retry

				var moveSpec = _toRetry[0];
				_toRetry.RemoveAt(0);

				// See if the move can be applied (it must lead to a new radial configuration)

				var configuration = mySolution.GetPeriodSolution(moveSpec.Period).NetConfig;

				if (configuration.IsOpen(moveSpec.SwitchToOpen))
					continue;
				if (! configuration.IsOpen(moveSpec.SwitchToClose))
					continue;
				if (!configuration.IsAncestorOfOneEnd(moveSpec.SwitchToOpen, moveSpec.SwitchToClose))
					continue;

				// Yes. Create and return a neiborhood with just that move

				var move = new SwapSwitchStatusMove(mySolution, moveSpec.Period, moveSpec.SwitchToOpen, moveSpec.SwitchToClose);
				var nh = new RetryNeighborhood(move);

				++_pendingNhCount;

				return nh;
			}

			// There are no moves to try. Use main selector

			INeighborhood innerResult = _mainSelector.SelectNeighborhood(solution, criteriaSet);

			if (innerResult == null && _pendingNhCount > 0)
				// Main selector has ended, but there are retry neighborhoods whose result we still don't know.
				// They could lead to further repeats. We need the results registered before we know.
				return SequentialSelector.Stalled;

			return innerResult;
		}

		/// <inheritdoc/>
		public void RegisterResult(INeighborhood neighborhood, MoveInfo moveInfo)
		{
			RegisteringResult?.Invoke(this, new ResultEventArgs(neighborhood, moveInfo));

			if (neighborhood is RetryNeighborhood)
				// This was one of ours
				--_pendingNhCount;
			else
				// Forward to main selector
				_mainSelector.RegisterResult(neighborhood, moveInfo);

			if (moveInfo.DeltaValue >= 0)
				// We only retry improving moves
				return;

			if (moveInfo.Move is not SwapSwitchStatusMove move)
				return;

			// Record that we want to retry this move

			PgoProblem problem = move.Solution.Problem;

			if (problem.PreviousPeriod(move.Period) is Period previousPeriod)
				// ... in the previous period
				_toRetry.Add(new MoveToTry(move.SwitchToClose, move.SwitchToOpen, previousPeriod));

			if (problem.NextPeriod(move.Period) is Period nextPeriod)
				// ... and in the next period
				_toRetry.Add(new MoveToTry(move.SwitchToClose, move.SwitchToOpen, nextPeriod));
		}

		/// <summary>
		/// Information about a move to retry
		/// </summary>
		private class MoveToTry
		{
			/// <summary>
			/// The period in which to generate the move
			/// </summary>
			public Period Period { get; }

			/// <summary>
			/// The swtich to close
			/// </summary>
			public Line SwitchToClose { get; }

			/// <summary>
			/// The switch to open
			/// </summary>
			public Line SwitchToOpen { get; }

			public MoveToTry(Line switchToClose, Line switchToOpen, Period period)
			{
				SwitchToClose = switchToClose;
				SwitchToOpen = switchToOpen;
				Period = period;
			}
		}

		/// <summary>
		/// The type of neighborhood used for retry moves
		/// </summary>
		private class RetryNeighborhood : MoveList
		{
			public RetryNeighborhood(SwapSwitchStatusMove move)
				: base(move)
			{
			}
		}

		/// <summary>
		/// Event arguments containing a selected neighborhood and the info
		/// for the selected move.
		/// </summary>
		public class ResultEventArgs
		{
			public INeighborhood Neighborhood { get; }
			public MoveInfo MoveInfo { get; }

			public ResultEventArgs(INeighborhood neighborhood, MoveInfo moveInfo)
			{
				Neighborhood = neighborhood;
				MoveInfo = moveInfo;
			}
		}
	}
}