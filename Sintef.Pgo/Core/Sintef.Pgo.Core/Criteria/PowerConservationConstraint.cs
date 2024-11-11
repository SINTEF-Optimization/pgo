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
	/// Checks power conservation (complex) in each bus.
	/// </summary>
	public class PowerConservationConstraint : FlowDependentCriterion, ICanCloneForAggregateProblem
	{
		/// <summary>
		/// The problem the constraint is for
		/// </summary>
		public PgoProblem Problem { get; }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="flowProvider">The flow provider to check for</param>
		public PowerConservationConstraint(IFlowProvider flowProvider)
			: base(flowProvider)
		{
			Name = "Power conservation";
		}

		/// <summary>
		/// Clones the criterion to be valid for an aggregate of the given
		/// <paramref name="originalProblem"/>.
		/// </summary>
		/// <param name="originalProblem">The original problem</param>
		/// <param name="aggregateProblem">The aggregate problem</param>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new PowerConservationConstraint(FlowProvider);
		}

		/// <summary>
		/// Creates a new criterion equivalent to this one, except that it uses the given 
		/// flow provider instead
		/// </summary>
		public override FlowDependentCriterion WithProvider(IFlowProvider flowProvider)
		{
			return new PowerConservationConstraint(flowProvider);
		}

		/// <summary>
		/// Returns true if power conservation is satisfied in every bus in the given solution.
		/// </summary>
		public override bool IsSatisfied(ISolution solution)
		{
			return (solution as IPgoSolution).SinglePeriodSolutions.All(FeasibleForSinglePeriodSolution);
		}

		/// <summary>
		/// Returns the penalty for violating power conservation in the given solution.
		/// </summary>
		public override double Value(ISolution solution)
		{
			return (solution as IPgoSolution).SinglePeriodSolutions.Sum(ValueForSinglePeriodSolution);
		}
		
		/// <summary>
		/// Returns true if the move leads to a solution satisfying this constraint
		/// </summary>
		public override bool LegalMove(Move move)
		{
			if (move is not SwapSwitchStatusMove swapMove)
				throw new NotImplementedException();

			var solution = swapMove.Solution;
			PeriodSolution periodSolution = solution.GetPeriodSolution(swapMove.Period);
			IPowerFlow flow = periodSolution.Flow(FlowProvider);
			PowerFlowDelta flowDelta = swapMove.GetCachedPowerFlowDelta(FlowProvider);

			foreach (var (bus, imbalanceDelta) in ImbalanceDeltas(flowDelta))
			{
				if (bus.IsProvider)
					// Injected power at providers is automatically adjusted
					continue;

				Complex newImbalance = PowerImbalance(periodSolution, flow, bus) + imbalanceDelta;

				if (!IsWithinTolerance(newImbalance))
					return false;
			}

			return true;
		}

		/// <summary>
		/// Returns true if power conservation is satisfied for the given <paramref name="periodSolution"/>
		/// </summary>
		private bool FeasibleForSinglePeriodSolution(PeriodSolution periodSolution)
		{
			foreach (var imbalance in PowerImbalances(periodSolution))
			{
				if (!IsWithinTolerance(imbalance))
					return false;
			}
			return true;
		}

		/// <summary>
		/// Returns the penalty for violating power in the given <paramref name="periodSolution"/>
		/// </summary>
		private double ValueForSinglePeriodSolution(PeriodSolution periodSolution)
		{
			double penalty = 0;

			foreach (var imbalance in PowerImbalances(periodSolution))
			{
				if (IsWithinTolerance(imbalance))
					continue;

				penalty += imbalance.Magnitude;
			}

			return penalty;
		}

		/// <summary>
		/// Enumerates the nonzero power imbalances in the given period solution
		/// </summary>
		private IEnumerable<Complex> PowerImbalances(PeriodSolution sol)
		{
			if (!sol.AllowsRadialFlow(requireConnected: true))
				yield break;

			IPowerFlow flow = sol.Flow(FlowProvider);
			if (flow == null)
				yield break;

			foreach (var bus in sol.Network.Buses)
			{
				Complex imbalance = PowerImbalance(sol, flow, bus);

				yield return imbalance;
			}
		}

		/// <summary>
		/// Returns the power imbalance (i.e. violation of power conservation) in the given bus,
		/// for the given solution and flow
		/// </summary>
		private static Complex PowerImbalance(PeriodSolution sol, IPowerFlow flow, Bus bus)
		{
			var presentLines = sol.NetConfig.PresentLinesAt(bus);

			Complex injected = flow.PowerInjection(bus);

			Complex netOutflow = presentLines.Select(line => flow.PowerFlow(bus, line)).ComplexSum();

			Complex imbalance = injected - netOutflow;

			return imbalance;
		}

		/// <summary>
		/// Creates a dictionary with the changes to power balances implied by the given flow delta
		/// </summary>
		private Dictionary<Bus, Complex> ImbalanceDeltas(PowerFlowDelta flowDelta)
		{
			Dictionary<Bus, Complex> deltas = new();

			foreach (var (line, delta) in flowDelta.LineDeltas)
			{
				deltas.AddOrNew(line.Node1, delta.DeltaPowerFlowFrom(line.Node1));
				deltas.AddOrNew(line.Node2, delta.DeltaPowerFlowFrom(line.Node2));
			}

			return deltas;
		}

		/// <summary>
		/// Returns true if the given imbalance is almost zero
		/// </summary>
		private bool IsWithinTolerance(Complex imbalance)
		{
			// Checking real power conservation
			if (Math.Abs(imbalance.Real) > 1e-3) // To what extent will accuracy be an issue here?
				return false; ;

			// Checking reactive power conservation
			if (Math.Abs(imbalance.Imaginary) > 1e-3) // To what extent will accuracy be an issue here?
				return false;

			return true;
		}
	}
}
