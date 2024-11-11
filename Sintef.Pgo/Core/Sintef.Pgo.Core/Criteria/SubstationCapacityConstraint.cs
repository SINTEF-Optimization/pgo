using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Constraint representing generation capacities (real power) of the substations. 
	/// In relaxed form, the penalty of violating this constraint is proportional to the violation
	/// squared (for "fairness" among sub stations).
	/// 
	/// WARNING: ONLY ACTIVE CAPACITIES DEALT WITH
	/// </summary>
	public class SubstationCapacityConstraint : FlowDependentCriterion, ISolutionAnnotator, ICanCloneForAggregateProblem
	{
		/// <summary>
		/// Create constraint with given flow provider.
		/// </summary>
		/// <param name="provider"></param>
		public SubstationCapacityConstraint(IFlowProvider provider)
			: base(provider)
		{
			Name = "Provider capacity";
		}

		/// <summary>
		/// Clones the criterion to be valid for an aggregate of the given
		/// <paramref name="originalProblem"/>.
		/// </summary>
		/// <param name="originalProblem">The original problem</param>
		/// <param name="aggregateProblem">The aggregate problem</param>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new SubstationCapacityConstraint(FlowProvider);
		}

		/// <summary>
		/// Creates a new criterion equivalent to this one, except that it uses the given 
		/// flow provider instead
		/// </summary>
		public override FlowDependentCriterion WithProvider(IFlowProvider flowProvider)
		{
			return new SubstationCapacityConstraint(flowProvider);
		}

		/// <summary>
		/// Checks if the substration capacity constraints are satisfied in the given solution.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override bool IsSatisfied(ISolution solution)
		{
			IPgoSolution sol = solution as IPgoSolution;
			return sol.IsComplete(FlowProvider) && sol.SinglePeriodSolutions.All(s => AllProvidersAreWithinCapacities(s));
		}

		/// <summary>
		/// Checks if all providers produce within their capacities.
		/// </summary>
		/// <param name="sol"></param>
		/// <returns></returns>
		private bool AllProvidersAreWithinCapacities(PeriodSolution sol)
		{
			IPowerFlow flow = sol.Flow(FlowProvider);

			foreach (var provider in sol.Network.Providers)
			{
				if (flow.PowerInjection(provider).Real > provider.ActiveGenerationCapacity)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// This is only implemented for single period solutions, since that is what we currently need in the search.
		/// Checks that the new flow will not violate any provider capacity constraints.
		/// </summary>
		/// <param name="move"></param>
		/// <returns></returns>
		public override bool LegalMove(Move move)
		{
			if (move is SwapSwitchStatusMove swapMove)
			{
				var sol = move.Solution as PgoSolution;
				foreach (var provider in sol.Problem.Network.Providers)
				{
					double newInjection = NewInjection(swapMove, provider);
					if (newInjection > provider.ActiveGenerationCapacity)
					{
						return false;
					}
				}
				return true;
			}
			else
			{
				throw new NotImplementedException("SubstationCapacityConstraint. LegalMove: Invalid move. Only SwapSwitchStatusMove is implemented.");
			}
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
				IPowerFlow powerFlow = periodSolution.Flow(FlowProvider);
				if (powerFlow == null)
				{
					stringBuilder.AppendLine($"Period {periodSolution.Period.StartTime}: Flow computation failed.");
					continue;
				}


				foreach (var provider in sol.Problem.Network.Providers)
				{
					if (powerFlow.PowerInjection(provider).Real > provider.ActiveGenerationCapacity)
					{
						stringBuilder.AppendLine($"Period {periodSolution.Period.StartTime}: Capacity of Provider {provider.Name} is exceeded");
					}
				}
			}
			return stringBuilder.ToString();
		}

		/// <summary>
		/// Returns the new injection at the given provider that will result from the given move.
		/// </summary>
		/// <param name="swapMove"></param>
		/// <param name="provider"></param>
		/// <returns></returns>
		private double NewInjection(SwapSwitchStatusMove swapMove, Bus provider)
		{
			var sol = swapMove.Solution;
			PowerFlowDelta pfd = swapMove.GetCachedPowerFlowDelta(FlowProvider);
			PeriodSolution perSol = sol.GetPeriodSolution(swapMove.Period);
			IPowerFlow flow = perSol.Flow(FlowProvider);

			IEnumerable<Line> outputLinesAfterMove = provider.IncidentLines.Where(l => (!l.IsSwitchable || perSol.NetConfig.SwitchSettings.IsClosed(l) || l == swapMove.SwitchToClose));
			return outputLinesAfterMove.Sum(l => NewPowerFlow(l));

			double NewPowerFlow(Line line)
			{
				if (pfd.LineDeltas.TryGetValue(line, out var lineDelta))
					return lineDelta.NewPowerFlowFrom(provider).Real;
				else
					return flow.PowerFlow(provider, line).Real;
			}
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
			return sol.SinglePeriodSolutions.Sum(s => TotalPenalty(s));
		}

		/// <summary>
		/// Returns the difference 
		/// in objective value between the new solution and the move's base solution, 
		/// where "new solution" is the soluion that results from performing the
		/// move on its base solution. 
		/// </summary>
		public override double DeltaValue(Move move)
		{
			if (move is SwapSwitchStatusMove swapMove)
			{
				var sol = move.Solution as PgoSolution;
				PeriodSolution perSol = sol.GetPeriodSolution(swapMove.Period);
				IPowerFlow oldFlow = perSol.Flow(FlowProvider);

				double penalty = 0;
				foreach (var provider in sol.Problem.Network.Providers)
				{
					double cap = provider.ActiveGenerationCapacity;
					double oldInjection = oldFlow.PowerInjection(provider).Real;
					double oldPenalty = Math.Pow(Math.Max(oldInjection - cap, 0.0), 2.0);

					double newInjection = NewInjection(swapMove, provider);
					double newPenalty = Math.Pow(Math.Max(newInjection - cap, 0.0), 2.0);

					penalty += newPenalty - oldPenalty;

				}
				return penalty;
			}
			else
			{
				throw new NotImplementedException("SubstationCapacityConstraint.DeltaValue: Invalid move. Only SwapSwitchStatusMove is implemented.");
			}

		}

		/// <summary>
		/// Sums the squares of any excess above provider capacity, summed over all providers.
		/// </summary>
		/// <param name="sol"></param>
		/// <returns></returns>
		private double TotalPenalty(PeriodSolution sol)
		{
			IPowerFlow flow = sol.Flow(FlowProvider);
			if (flow == null)
				return 0;

			return sol.Network.Providers.Sum(provider => Penalty(provider, flow));
		}

		/// <summary>
		/// Returns the penalty of the given <paramref name="provider"/> under the given <paramref name="flow"/>
		/// </summary>
		/// <returns></returns>
		private static double Penalty(Bus provider, IPowerFlow flow)
		{
			double powerInjection = flow.PowerInjection(provider).Real;
			double activeGenerationCapacity = provider.ActiveGenerationCapacity;

			if (powerInjection <= activeGenerationCapacity)
				return 0;
			else
				return Math.Pow(powerInjection - activeGenerationCapacity, 2.0);
		}

		#region Solution annotation

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

				foreach (var provider in network.Providers)
				{
					double penalty = Penalty(provider, flow);
					if (penalty == 0)
						continue;

					yield return new BusAnnotation(provider, periodSolution.Period,
						$"Provider {provider}: Capacity {provider.GenerationCapacity}, injection {flow.PowerInjection(provider)}, penalty {penalty}");
				}
			}
		}

		#endregion
	}
}
