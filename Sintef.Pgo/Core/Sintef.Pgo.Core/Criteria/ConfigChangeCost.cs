using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// An objective measuring the difference in switch settings between a solution and a given 
	/// reference solution.
	/// For multi-period problems, the cost is summed over all period solutions.
	/// </summary>
	public class ConfigChangeCost : Criterion, ICanCloneForAggregateProblem
	{
		#region Public properties 

		/// <summary>
		/// No propagation of anything necessary.
		/// </summary>
		public override bool RequiresPropagation => false;

		#endregion

		#region Private data members

		/// <summary>
		/// Caches the change cost per period calculated by <see cref="PeriodValue"/>
		/// </summary>
		PerPeriodValueCacheClient _periodValues;

		#endregion

		#region Construction

		/// <summary>
		/// Constructor
		/// </summary>
		public ConfigChangeCost()
		{
			Name = "Switching cost";

			_periodValues = new PerPeriodValueCacheClient(PeriodValue, InvalidPeriods);
		}

		/// <summary>
		/// Clones the criterion to be valid for an aggregate of the given
		/// <paramref name="originalProblem"/>.
		/// </summary>
		/// <param name="originalProblem">The original problem</param>
		/// <param name="aggregateProblem">The aggregate problem</param>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new ConfigChangeCost();
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Returns the sum of the cost of changing switch settings. The implementation depends on the solution type.
		/// 1 - For single period solutions that are sub-problems of a multi-period solution,
		///			we compare switch settings with the period solutions before or after, in such a way that changes in the period
		///			solution's value are identical to those that will result in the parent multi-period solution.
		///	2 - For single period solutions that are stand alone (top level) solutions, we return 0.
		/// 2 - For multi-period solutions, we calculate differences directly between the period solutions.
		/// </summary>
		public override double Value(ISolution solution)
		{
			if (solution is PgoSolution mySolution)
			{
				return _periodValues.Values(mySolution).Values.Sum();
			}
			else
				throw new Exception($"ConfigChangeCost.Value: Unknown solution class given: {solution.GetType().ToString()}");
		}

		/// <summary>
		/// Returns the change in switching cost associated with the given move (after - before)
		/// Assumes the lines involved in the <paramref name="move"/> are switchable.
		/// Assumes that the move's solution is an <see cref="PgoSolution"/>.
		/// </summary>
		/// <param name="move"></param>
		/// <returns></returns>
		public override double DeltaValue(Move move)
		{
			if (move is SwapSwitchStatusMove ssm)
			{
				if (move.Solution is PgoSolution solution)
				{
					List<SwitchSettings> refSettings = new List<SwitchSettings>();
					SwitchSettings prev = solution.GetPreviousPeriodSwitchSettings(ssm.Period);
					if (prev != null)
						refSettings.Add(prev);
					SwitchSettings next = solution.GetNextPeriodSwitchSettings(ssm.Period);
					if (next != null)
						refSettings.Add(next);

					double stcCost = ssm.SwitchToClose.SwitchingCost;
					double valueStcBefore = refSettings.Sum(rc => rc.IsOpen(ssm.SwitchToClose) ? 0 : stcCost);
					double valueStcAfter = refSettings.Sum(rc => rc.IsOpen(ssm.SwitchToClose) ? stcCost : 0);

					double stoCost = ssm.SwitchToOpen.SwitchingCost;
					double valueStoBefore = refSettings.Sum(rc => rc.IsOpen(ssm.SwitchToOpen) ? stoCost : 0);
					double valueStoAfter = refSettings.Sum(rc => rc.IsOpen(ssm.SwitchToOpen) ? 0 : stoCost);

					return (valueStcAfter + valueStoAfter) - (valueStcBefore + valueStoBefore);
				}
				else
					throw new NotImplementedException("ConfigChangeCost.DeltaValue: unexpected solution type");
			}
			else
				throw new NotImplementedException("ConfigChangeCostDeltaValue: unexpected move type");
		}

		/// <summary>
		/// Returns the value of the parts of the move's solution that are affected by the move.
		/// I.e., the difference between the current switching states of the switches affected
		/// by the move, in the move's solution, compared with the reference solution
		/// Assumes that the move's solution is an <see cref="PgoSolution"/>.
		/// </summary>
		/// <param name="move"></param>
		/// <returns></returns>
		public override double PartialValue(Move move)
		{
			if (move is SwapSwitchStatusMove ssm)
			{
				if (move.Solution is PgoSolution mpSol)
				{
					PeriodSolution sol = mpSol.GetPeriodSolution(ssm.Period);
					List<Line> moveSwitches = new List<Line> { ssm.SwitchToClose, ssm.SwitchToOpen };
					SwitchSettings previousSettings = mpSol.GetPreviousPeriodSwitchSettings(ssm.Period);
					double value = moveSwitches.Sum(sw => (previousSettings != null && sol.IsOpen(sw) != previousSettings.IsOpen(sw)) ? sw.SwitchingCost : 0);
					SwitchSettings nextSettings = mpSol.GetNextPeriodSwitchSettings(ssm.Period);
					value += moveSwitches.Sum(sw => (nextSettings != null && sol.IsOpen(sw) != nextSettings.IsOpen(sw)) ? sw.SwitchingCost : 0);
					return value;
				}
				else
					throw new NotImplementedException("ConfigChangeCost.PartialValue: unexpected solution type");
			}
			else
				throw new NotImplementedException("ConfigChangeCost.PartialValue: unexpected move type");

		}

		#endregion

		#region Private methods

		/// <summary>
		/// Returns the change cost for the given solution and period.
		/// This is the cost for switch changes between the given period and the next period (or
		/// against the TargetPostConfiguration, if it exists, for the solution's last period).
		/// In addition, the cost for switch changes against StartConfiguration, if it exists, 
		/// is added to the cost of the first period.
		/// </summary>
		private double PeriodValue(PgoSolution solution, Period period)
		{
			// Find the period's switch settings
			var settings = solution.GetPeriodSolution(period).SwitchSettings;

			// Find the next switch settings (next period or target)
			var nextSettings = solution.GetNextPeriodSwitchSettings(period);

			// Calculate the cost
			double value = SwitchingCost(settings, nextSettings);

			if (solution.Problem.PreviousPeriod(period) == null)
				// For the first period, add switch cost from the start configuration
				value += SwitchingCost(settings, solution.Problem.StartConfiguration?.SwitchSettings);

			return value;
		}

		/// <summary>
		/// Returns the total switching cost between the two given switch settings.
		/// If <paramref name="otherSettings"/> is null, returns 0.
		/// </summary>
		private double SwitchingCost(SwitchSettings settings, SwitchSettings otherSettings)
		{
			if (otherSettings == null)
				return 0;

			return settings.DifferentSwitches(otherSettings).Sum(line => line.SwitchingCost);
		}

		/// <summary>
		/// Enumerates the periods whose value is affected by the given move;
		/// i.e. the move's period and the previous one.
		/// </summary>
		/// <returns></returns>
		private IEnumerable<Period> InvalidPeriods(ChangeSwitchesMove move)
		{
			yield return move.Period;

			var previousPeriod = move.Solution.Problem.PreviousPeriod(move.Period);

			if (previousPeriod != null)
				yield return previousPeriod;
		}

		#endregion
	}
}
