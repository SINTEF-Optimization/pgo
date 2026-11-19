using System;
using System.Collections.Generic;
using System.Linq;
using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Schedules a multi-period problem from a pool of candidate configurations, with one of the candidates in each period.
	///
	/// The schedule is found by dynamic programming over the periods. It optimizes for the fewest periods that violate a
	/// constraint of the problem's criteria set and then, among those schedules, for the lowest objective value.
	/// The objective value of a period is the value of the configuration in the period's single-period problem
	/// (see <see cref="PgoProblem.CreateSinglePeriodCopy"/>). Each change of configuration between periods, from the
	/// problem's start configuration and to its target configuration, costs the weighted switching cost of the
	/// <see cref="ConfigChangeCost"/> components of the objective. The schedule is therefore the best one from the candidates
	/// when the objective is a sum over the periods plus the switching cost, as for the default criteria set.
	/// </summary>
	public static class MultiPeriodCandidateScheduler
	{
		/// <summary>
		/// Returns the best schedule for the problem that uses only the candidate configurations
		/// </summary>
		/// <param name="problem">The problem to schedule</param>
		/// <param name="candidates">The configurations to choose from. Each must be a configuration of the problem's network
		///   that allows radial flow.</param>
		public static PgoSolution Schedule(PgoProblem problem, IEnumerable<NetworkConfiguration> candidates)
		{
			var configs = candidates.ToList();
			if (configs.Count == 0)
				throw new ArgumentException("There are no candidate configurations", nameof(candidates));
			if (configs.Any(c => c.Network != problem.Network))
				throw new ArgumentException("A candidate configuration is of another network than the problem's", nameof(candidates));
			if (configs.Any(c => !c.AllowsRadialFlow(requireConnected: false)))
				throw new ArgumentException("A candidate configuration is not radial or is missing a transformer mode", nameof(candidates));

			var periods = problem.Periods.ToList();

			// The violation (0 or 1) and the objective value of each configuration in each period
			var cost = new (int violations, double value)[periods.Count, configs.Count];
			for (int t = 0; t < periods.Count; t++)
			{
				var periodProblem = problem.CreateSinglePeriodCopy(periods[t]);
				for (int c = 0; c < configs.Count; c++)
				{
					var solution = new PgoSolution(periodProblem, new Dictionary<Period, NetworkConfiguration> { [periods[t]] = configs[c] });
					cost[t, c] = (solution.IsFeasible(periodProblem.CriteriaSet) ? 0 : 1, periodProblem.CriteriaSet.Objective.Value(solution));
				}
			}

			// The weighted switching cost of changing between two switch settings
			var objective = problem.CriteriaSet.Objective;
			double weight = objective is AggregateObjective aggregate
				? aggregate.Components.OfType<ConfigChangeCost>().Sum(component => aggregate.GetWeight(component))
				: (objective is ConfigChangeCost ? 1 : 0);
			double Switching(NetworkConfiguration config, NetworkConfiguration other)
				=> other == null ? 0 : weight * ConfigChangeCost.SwitchingCost(config.SwitchSettings, other.SwitchSettings);

			// Dynamic programming: best[c] is the best cost of the periods so far, ending with configuration c
			var best = new (int violations, double value)[configs.Count];
			var previous = new int[periods.Count, configs.Count];
			for (int c = 0; c < configs.Count; c++)
				best[c] = Plus(cost[0, c], Switching(configs[c], problem.StartConfiguration));
			for (int t = 1; t < periods.Count; t++)
			{
				var next = new (int violations, double value)[configs.Count];
				for (int c = 0; c < configs.Count; c++)
				{
					for (int p = 0; p < configs.Count; p++)
					{
						var candidate = Plus(best[p], Switching(configs[c], configs[p]));
						if (p == 0 || Less(candidate, next[c]))
						{
							next[c] = candidate;
							previous[t, c] = p;
						}
					}
					next[c] = Plus(next[c], cost[t, c]);
				}
				best = next;
			}

			// The best last configuration, including the change to the target configuration, then back through the periods
			int last = 0;
			for (int c = 1; c < configs.Count; c++)
				if (Less(Plus(best[c], Switching(configs[c], problem.TargetPostConfiguration)),
					Plus(best[last], Switching(configs[last], problem.TargetPostConfiguration))))
					last = c;

			var schedule = new Dictionary<Period, NetworkConfiguration>();
			for (int t = periods.Count - 1; t >= 0; t--)
			{
				schedule[periods[t]] = configs[last];
				last = previous[t, last];
			}

			return new PgoSolution(problem, schedule);
		}

		/// <summary>
		/// Adds a value to a cost
		/// </summary>
		private static (int violations, double value) Plus((int violations, double value) cost, double value)
			=> (cost.violations, cost.value + value);

		/// <summary>
		/// Adds two costs
		/// </summary>
		private static (int violations, double value) Plus((int violations, double value) cost, (int violations, double value) other)
			=> (cost.violations + other.violations, cost.value + other.value);

		/// <summary>
		/// True if cost <paramref name="a"/> is lower than <paramref name="b"/>: fewer violations, or as many and a lower value
		/// </summary>
		private static bool Less((int violations, double value) a, (int violations, double value) b)
			=> a.violations < b.violations || (a.violations == b.violations && a.value < b.value);
	}
}
