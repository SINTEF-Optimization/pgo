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
	/// Constraint representing minimum and maximum voltage limits by the voltage magnitude at all
	/// consumer buses.
	/// In relaxed form, the penalty of violating this constraint is proportional to the violation.
	/// </summary>
	public class ConsumerVoltageLimitsConstraint : FlowDependentCriterion, ISolutionAnnotator, ICanCloneForAggregateProblem
	{
		/// <summary>
		/// The voltage checker using a max voltage loss assumption.
		/// </summary>
		public BusVoltageChecker VoltageCheck { get; private set; }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="flowProvider"> The flow provider that is used to compute flows and delta flows.</param>
		/// <param name="assumedMaxLossRatio"></param>
		public ConsumerVoltageLimitsConstraint(IFlowProvider flowProvider, double? assumedMaxLossRatio)
			: base(flowProvider)
		{
			VoltageCheck = new BusVoltageChecker(assumedMaxLossRatio);
			Name = "Bus voltage limits";
		}

		/// <summary>
		/// Clones the criterion to be valid for an aggregate of the given
		/// <paramref name="originalProblem"/>.
		/// </summary>
		/// <param name="originalProblem">The original problem</param>
		/// <param name="aggregateProblem">The aggregate problem</param>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new ConsumerVoltageLimitsConstraint(FlowProvider, VoltageCheck.AssumedMaxLossRatio);
		}

		/// <summary>
		/// Creates a new criterion equivalent to this one, except that it uses the given 
		/// flow provider instead
		/// </summary>
		public override FlowDependentCriterion WithProvider(IFlowProvider flowProvider)
		{
			return new ConsumerVoltageLimitsConstraint(flowProvider, VoltageCheck.AssumedMaxLossRatio);
		}

		/// <summary>
		/// True only if no constraints in the criteria set of the associated Problem are violated.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override bool IsSatisfied(ISolution solution)
		{
			IPgoSolution sol = solution as IPgoSolution;
			return sol.IsComplete(FlowProvider) && !sol.SinglePeriodSolutions.Any(s => VoltageCheck.ConsumerBusPenalties(FlowProvider, s).Any(x => x.penalty > 0.0));
		}
		
		/// <summary>
		/// This is only implemented for single period solutions, since that is what we currently need in the search.
		/// Checks that the new flow will not violate any consumer voltage constraints.
		/// </summary>
		/// <param name="move"></param>
		/// <returns></returns>
		public override bool LegalMove(Move move)
		{
			if (!(move is SwapSwitchStatusMove swapMove)) 
				throw new NotImplementedException("VoltageLimitsConstraint. LegalMove: Invalid move. Only SwapSwitchStatusMove is implemented.");

			var sol = move.Solution as PgoSolution;
			PeriodSolution perSol = sol.GetPeriodSolution(swapMove.Period);
			PowerFlowDelta flowDelta = swapMove.GetCachedPowerFlowDelta(FlowProvider);

			return !VoltageCheck.ConsumerBusDeltaPenalties(perSol, FlowProvider, flowDelta).Any((l) => l.penaltyDelta > 0.0);
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
				foreach (var item in VoltageCheck.ConsumerBusPenalties(FlowProvider, periodSolution))
				{
					stringBuilder.Append($"Period {periodSolution.Period.StartTime}: Voltage of Bus {item.bus.Name} is outside bounds [{item.bus.VMin}, {item.bus.VMax}]");
				}
			}
			return stringBuilder.ToString();
		}

		/// <summary>
		/// The value is the sum of excess injection at providers, squared for each provider,
		/// and then summed over all period solutions.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override double Value(ISolution solution)
		{
			IPgoSolution sol = solution as IPgoSolution;
			return sol.SinglePeriodSolutions.Sum(s => VoltageCheck.ConsumerBusPenalties(FlowProvider, s).Select(p => p.penalty).Sum());
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

			return VoltageCheck.ConsumerBusDeltaPenalties(perSol, FlowProvider, flowDelta).Select(i => i.penaltyDelta).Sum();
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

				foreach (var item in VoltageCheck.ConsumerBusPenalties(FlowProvider, periodSolution))
				{
					if (item.penalty == 0) continue;
					yield return new BusAnnotation(item.bus, periodSolution.Period,
						$"Consumer {item.bus}: Voltage penalty {item.penalty}");
				}
			}
		}

	}
}
