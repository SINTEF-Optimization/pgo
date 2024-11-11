using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Representing hard capacity limits on the network lines. Satisfied only if all line flows
	/// are in fact below the max. capacity of the line.
	/// Works both as a constraint and as an objective.
	/// For multi-period problems, the objective value is summed over all period solutions.
	/// </summary>
	public class LineCapacityCriterion : FlowDependentCriterion, ISolutionAnnotator, ICanCloneForAggregateProblem
	{
		#region Private members

		/// <summary>
		/// The penalty is proportional, by this factor, with the excess of current above the IMax limit.
		/// </summary>
		double _penaltyFactor = 1;

		/// <summary>
		/// The fraction of total capacity at which the cost starts (when used as an objective). In [0,1].
		/// If not set, the default is 1, which means that the cost only starts when the capacity limit is exceeded.
		/// </summary>
		double _painThreshold = 1;

		/// <summary>
		/// Caches the total capacity violation penalty per period
		/// </summary>
		PerPeriodValueCacheClient _penaltyCache;

		#endregion

		/// <summary>
		/// Constructor.
		/// </summary>
		/// <param name="provider"></param>
		/// <param name="painThreshold">The fraction of total capacity at which the cost starts (when used as an objective). In [0,1].
		/// If not set, the default is 1, which means that the cost only starts when the capacity limit is exceeded.</param>
		public LineCapacityCriterion(IFlowProvider provider, double painThreshold = 1)
			: base(provider)
		{
			if (painThreshold < 0 || painThreshold > 1)
				throw new Exception("LineCapacityCriterion.ctor: Invalid capacity limit fraction given.Must be in [0, 1].");
			Name = painThreshold == 1 ? "IMax violation (Ah)" : string.Format("IMax({0:0.0}%) violation (Ah)", painThreshold * 100.0);
			_painThreshold = painThreshold;

			_penaltyCache = new PerPeriodValueCacheClient(PeriodPenalty);
		}

		/// <summary>
		/// Clones the criterion to be valid for an aggregate of the given
		/// <paramref name="originalProblem"/>.
		/// </summary>
		/// <param name="originalProblem">The original problem</param>
		/// <param name="aggregateProblem">The aggregate problem</param>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new LineCapacityCriterion(FlowProvider, _painThreshold);
		}

		/// <summary>
		/// Creates a new criterion equivalent to this one, except that it uses the given 
		/// flow provider instead
		/// </summary>
		public override FlowDependentCriterion WithProvider(IFlowProvider flowProvider)
		{
			return new LineCapacityCriterion(flowProvider, _painThreshold);
		}


		#region Constraint implementation

		/// <summary>
		/// True if there are no violations of the criterion in the given solution.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override bool IsSatisfied(ISolution solution)
		{
			IPgoSolution mySolution = solution as IPgoSolution;

			return mySolution.IsComplete(FlowProvider) && _penaltyCache.Values(solution).Values.All(v => v == 0);
		}

		/// <summary>
		/// Returns a string representation of the violations of the criterion in the given solution.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override string Reason(ISolution solution)
		{
			var violatedLines = new List<(Period, Line, double)>();
			PgoSolution sol = solution as PgoSolution;

			foreach (PeriodSolution perSol in sol.SinglePeriodSolutions)
			{
				IPowerFlow flow = perSol.Flow(FlowProvider);

				foreach (var line in perSol.PresentLines)
				{
					if (flow.Current(line).Magnitude > line.IMax * _painThreshold)
					{
						violatedLines.Add((perSol.Period, line, flow.Current(line).Magnitude));
					}
				}
			}

			if (violatedLines.Count == 0)
			{
				return "No line capacities violated.";
			}

			// Otherwise build a reason string
			StringBuilder sb = new StringBuilder();
			sb.Append("The following line names have their capacity violated").AppendLine();
			foreach ((Period per, Line line, double magnitude) in violatedLines)
			{
				sb.Append($"Period {per.Index}, Line {line.Name}: {magnitude}>{line.IMax}").AppendLine();
			}
			return sb.ToString();
		}

		/// <summary>
		/// Checks the resulting flows approximated current magnitudes against line limits.
		/// </summary>
		/// <param name="move"></param>
		/// <returns></returns>
		public override bool LegalMove(Move move)
		{
			if (!(move is SwapSwitchStatusMove swapMove))
				throw new NotImplementedException("Can only provide delta value for SwapSwitchStatusMove");

			PowerFlowDelta pfd = swapMove.GetCachedPowerFlowDelta(FlowProvider);

			return pfd.LineDeltas.All(kvp => kvp.Value.NewCurrent.Magnitude <= kvp.Key.IMax * _painThreshold);
		}

		#endregion

		#region Objective implementation

		/// <summary>
		/// Conversion factor from SI to the output unit Ah.
		/// </summary>
		private const double UnitAh = 1.0 / 3600.0;

		/// <summary>
		/// The change in line capacity violation cost.
		/// </summary>
		/// <param name="move"></param>
		/// <returns></returns>
		public override double DeltaValue(Move move)
		{
			if (!(move is SwapSwitchStatusMove swapMove))
				throw new NotImplementedException("Can only provide delta value for SwapSwitchStatusMove");

			PowerFlowDelta flowDelta = swapMove.GetCachedPowerFlowDelta(FlowProvider);
			double instantaneousPenaltyDelta = flowDelta.LineDeltas.Sum(kvp => LinePenaltyDelta(kvp.Key, kvp.Value));
			var periodLength = swapMove.Period.Length.TotalSeconds;
			return instantaneousPenaltyDelta * periodLength * UnitAh;
		}

		/// <summary>
		/// Line capacity violation cost. If the flow of the given solution is not yet set, the function returns 0.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override double Value(ISolution solution)
		{
			return _penaltyCache.Values(solution).Values.Sum();
		}

		/// <summary>
		/// Returns the total capacity violation penalty for the given period solution
		/// </summary>
		private double PeriodPenalty(PeriodSolution sol)
		{
			IPowerFlow flow = sol.Flow(FlowProvider);
			if (flow == null)
				return 0;

			var instantaneousValue = sol.PresentLines.Sum(l => LinePenalty(l, flow));
			var periodLength = sol.Period.Length.TotalSeconds;
			return instantaneousValue * periodLength * UnitAh;
		}

		/// <summary>
		/// Computes the delta in penalty based on the given physical changes for the given line.
		/// </summary>
		/// <param name="line"></param>
		/// <param name="delta"></param>
		/// <returns></returns>
		private double LinePenaltyDelta(Line line, PowerFlowDelta.LinePowerFlowDelta delta)
		{
			return LinePenalty(line, delta.NewCurrent) - LinePenalty(line, delta.OldCurrent);
		}

		/// <summary>
		/// Returns the penalty for the given line under the given flow
		/// </summary>
		private double LinePenalty(Line l, IPowerFlow flow)
		{
			return LinePenalty(l, flow.Current(l));
		}

		/// <summary>
		/// Returns the penalty for the given line carrying the given current
		/// </summary>
		private double LinePenalty(Line l, Complex current)
		{
			return Math.Max(current.Magnitude - (l.IMax * _painThreshold), 0.0) * _penaltyFactor;
		}

		#endregion

		#region Solution annotation

		/// <summary>
		/// Returns annotations for the given solution
		/// </summary>
		public IEnumerable<Annotation> Annotate(ISolution solution)
		{
			var mySolution = (PgoSolution)solution;

			foreach (var periodSolution in mySolution.SinglePeriodSolutions)
			{
				IPowerFlow flow = periodSolution.Flow(FlowProvider);

				foreach (var line in periodSolution.PresentLines)
				{
					double penalty = LinePenalty(line, flow);
					if (penalty == 0)
						continue;

					yield return new LineAnnotation(line, periodSolution.Period, 
						$"Line {line}: Capacity {line.IMax}, current {flow.Current(line).Magnitude}, penalty {penalty}");
				}
			}
		}

		#endregion

	}
}
