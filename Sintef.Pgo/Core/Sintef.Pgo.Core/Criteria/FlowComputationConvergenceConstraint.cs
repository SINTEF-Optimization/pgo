using Sintef.Scoop.Kernel;
using System.Linq;
using System;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A constraint that requires that flow computation succeeds in each period
	/// </summary>
	internal class FlowComputationSuccessConstraint : FlowDependentCriterion, ICanCloneForAggregateProblem
	{
		/// <summary>
		/// Initializes the constraint
		/// </summary>
		/// <param name="flowProvider">The flow provider whose computations should succeed</param>
		public FlowComputationSuccessConstraint(IFlowProvider flowProvider) : base(flowProvider)
		{
			Name = "Computing a consistent flow";
		}

		/// <summary>
		/// Returns true if a valid flow is present in each period
		/// </summary>
		public override bool IsSatisfied(ISolution solution)
		{
			var mySolution = solution as PgoSolution;

			return mySolution.SinglePeriodSolutions.All(PeriodSolutionIsOk);
		}

		/// <summary>
		/// Returns the number of periods where no valid flow is present
		/// </summary>
		public override double Value(ISolution solution)
		{
			var mySolution = solution as PgoSolution;

			return mySolution.SinglePeriodSolutions.Count(x => !PeriodSolutionIsOk(x));
		}

		/// <summary>
		/// Returns true if the move leads to a solution with valid flows
		/// </summary>
		public override bool LegalMove(Move move)
		{
			if (FlowProvider.FlowApproximation == FlowApproximation.SimplifiedDF)
				// Simplified DF always succeeds
				return true;

			throw new NotImplementedException();
		}

		/// <inheritdoc/>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new FlowComputationSuccessConstraint(FlowProvider);
		}

		/// <inheritdoc/>
		public override FlowDependentCriterion WithProvider(IFlowProvider flowProvider)
		{
			return new FlowComputationSuccessConstraint(flowProvider);
		}

		/// <summary>
		/// Returns true if the period solution has a valid flow
		/// </summary>
		private bool PeriodSolutionIsOk(PeriodSolution periodSolution)
		{
			IPowerFlow flow = periodSolution.Flow(FlowProvider);

			return flow != null && flow.Status >= FlowStatus.Approximate;
		}

		/// <summary>
		/// Return the error message for non-convergence in each period.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override string Reason(ISolution solution)
		{
			var mySolution = solution as PgoSolution;
			var reasons = mySolution.SinglePeriodSolutions.Select(periodSolution =>
				(id: periodSolution.Period.Id, flow: periodSolution.Flow(FlowProvider))
			).Where(p => p.flow == null || p.flow.Status < FlowStatus.Approximate)
			.Select(p => $"Period {p.id}: {p.flow?.StatusDetails ?? "Flow computation failed"}.").ToList();
			if (reasons.Count > 0)
			{
				return string.Join(" ", reasons);
			}
			else
			{
				return "Consistent flow computed OK.";
			}
		}
	}
}