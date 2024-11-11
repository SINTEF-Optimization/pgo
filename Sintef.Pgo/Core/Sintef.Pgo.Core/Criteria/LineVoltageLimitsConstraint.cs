using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{


	/// <summary>
	/// Constraint representing maximum voltage limits by the voltage magnitude at each line.
	/// In relaxed form, the penalty of violating this constraint is proportional to the violation.
	/// </summary>
	public class LineVoltageLimitsConstraint : FlowDependentCriterion, ISolutionAnnotator, ICanCloneForAggregateProblem
	{
		/// <summary>
		/// The voltage checker using a max voltage loss assumption.
		/// </summary>
		public BusVoltageChecker VoltageCheck { get; private set; }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="flowProvider"> The flow provider that is used to compute flows and delta flows.</param>
		/// <param name="assumedMaxLossRatio">Set the AssumedMaxLossRatio property</param>
		public LineVoltageLimitsConstraint(IFlowProvider flowProvider, double? assumedMaxLossRatio)
			:base(flowProvider)
		{
			Name = "Line voltage limits";
			VoltageCheck = new BusVoltageChecker(assumedMaxLossRatio);
		}

		/// <summary>
		/// Clones the criterion to be valid for an aggregate of the given
		/// <paramref name="originalProblem"/>.
		/// </summary>
		/// <param name="originalProblem">The original problem</param>
		/// <param name="aggregateProblem">The aggregate problem</param>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new LineVoltageLimitsConstraint(FlowProvider, VoltageCheck.AssumedMaxLossRatio);
		}

		/// <summary>
		/// Creates a new criterion equivalent to this one, except that it uses the given 
		/// flow provider instead
		/// </summary>
		public override FlowDependentCriterion WithProvider(IFlowProvider flowProvider)
		{
			return new LineVoltageLimitsConstraint(flowProvider, VoltageCheck.AssumedMaxLossRatio);
		}

		/// <summary>
		/// True only if no constraints in the criteria set of the associated Problem are violated.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override bool IsSatisfied(ISolution solution)
		{
			IPgoSolution sol = solution as IPgoSolution;
			return sol.IsComplete(FlowProvider) && !sol.SinglePeriodSolutions.Any(s => VoltageCheck.LinePenalties(FlowProvider, s).Any(i => i.penalty > 0.0));
		}

		/// <summary>
		/// This is only implemented for single period solutions, since that is what we currently need in the search.
		/// Checks that the new flow will not violate any line voltage constraints.
		/// </summary>
		/// <param name="move"></param>
		/// <returns></returns>
		public override bool LegalMove(Move move)
		{
			if (!(move is SwapSwitchStatusMove swapMove))
				throw new NotImplementedException("Can only provide delta value for SwapSwitchStatusMove");

			var sol = move.Solution as PgoSolution;
			PeriodSolution perSol = sol.GetPeriodSolution(swapMove.Period);
			PowerFlowDelta flowDelta = swapMove.GetCachedPowerFlowDelta(FlowProvider);

			return !VoltageCheck.LineDeltaPenalties(perSol, FlowProvider, flowDelta).Any((l) => l.deltaPenalty > 0.0);
		}


		/// <summary>
		/// Given an infeasible solution, this function returns a string that
		/// describes why the solution is infeasible. 
		/// The precision of the diagnostic may vary with the constraint subclass. If the
		/// constraint is satisfied wrt. the solution, no significance is attached 
		/// to the returned string.
		/// </summary>
		public override string Reason(ISolution solution)
		{
			StringBuilder stringBuilder = new StringBuilder();
			PgoSolution sol = solution as PgoSolution;
			foreach (PeriodSolution periodSolution in sol.SinglePeriodSolutions)
			{
				foreach (var item in VoltageCheck.LinePenalties(FlowProvider, periodSolution))
				{
					stringBuilder.AppendLine($"Period {periodSolution.Period.StartTime}: Voltage of Line {item.line.Name} exceeds {item.line.VMax}");
				}
			}
			return stringBuilder.ToString();
		}


		/// <summary>
		/// The sum of penalties for each line.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override double Value(ISolution solution)
		{
			IPgoSolution sol = solution as IPgoSolution;
			return sol.SinglePeriodSolutions.SelectMany(s => VoltageCheck.LinePenalties(FlowProvider, s).Select(i => i.penalty)).Sum();
		}

		/// <summary>
		/// Returns the difference 
		/// in objective value between the new solution and the move's base solution, 
		/// where "new solution" is the solution that results from performing the
		/// move on its base solution. 
		/// </summary>
		public override double DeltaValue(Move move)
		{
			if (!(move is SwapSwitchStatusMove swapMove))
				throw new NotImplementedException("Can only provide delta value for SwapSwitchStatusMove");

			var sol = move.Solution as PgoSolution;
			PeriodSolution perSol = sol.GetPeriodSolution(swapMove.Period);
			PowerFlowDelta flowDelta = swapMove.GetCachedPowerFlowDelta(FlowProvider);

			return VoltageCheck.LineDeltaPenalties(perSol, FlowProvider, flowDelta).Select(i => i.deltaPenalty).Sum();
		}

		/// <summary>
		/// Returns annotations for the given solution
		/// </summary>
		public IEnumerable<Annotation> Annotate(ISolution solution)
		{
			var mySolution = (PgoSolution)solution;
			var network = mySolution.PowerNetwork;

			foreach (var periodSolution in mySolution.SinglePeriodSolutions)
			{
				IPowerFlow flow = periodSolution.Flow(FlowProvider);

				foreach (var item in VoltageCheck.LinePenalties(FlowProvider, periodSolution))
				{
					if (item.penalty == 0) continue;
					yield return new LineAnnotation(item.line, periodSolution.Period,
						$"Line {item.line.Name}: Voltage exceeds {item.line.VMax} (penalty {item.penalty})");
				}
			}
		}
	}
}
