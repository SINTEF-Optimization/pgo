using Sintef.Scoop.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A constraint that requires that all transformers are used in valid modes, i.e. modes that exist.
	/// 
	/// As an objective, it counts the number of transformers (in each period) that have invalid configurations,
	/// and adds a penalty for each of them.
	/// </summary>
	public class TransformerModesConstraint : Criterion, ISolutionAnnotator, ICanCloneForAggregateProblem
	{
		/// <summary>
		/// Clones the criterion for an aggregated problem.
		/// </summary>
		/// <param name="originalProblem"></param>
		/// <param name="aggregateProblem"></param>
		/// <returns></returns>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new TransformerModesConstraint() { Name = Name };
		}

		/// <summary>
		/// Constructor
		/// </summary>
		public TransformerModesConstraint()
		{
			Name = "Transformer input/output terminals";
		}

		/// <summary>
		/// Returns true if the solution has no invalid transformer modes in any period.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override bool IsSatisfied(ISolution solution)
		{
			return Value(solution) == 0;
		}

		/// <summary>
		/// Returns the number of invalid transformer configurations (summed over all periods).
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override double Value(ISolution solution)
		{
			if (solution is PgoSolution mpSol)
				return mpSol.SinglePeriodSolutions.Select(p => p.NetConfig.TransformersUsingMissingModes.Count()).Sum();
			else
				throw new Exception("Unknown solution type");
		}

		/// <summary>
		/// Print a string that explains why the criterion is not satisfied.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override string Reason(ISolution solution)
		{
			if (solution is PgoSolution mpSol)
			{
				return string.Join("", mpSol.SinglePeriodSolutions.Select(p =>
				   p.NetConfig.HasTransformersUsingMissingModes ? $"(Transformers using invalid modes in period {p}: {string.Join(", ", p.NetConfig.TransformersUsingMissingModes)}) " : ""
				));
			}
			else
				throw new Exception("Unknown solution type.");
		}

		/// <summary>
		/// Returns whether the solution implied by the move is a feasible one, 
		/// that is, whether the move is legal.
		/// </summary>
		/// <returns></returns>
		public override bool LegalMove(Move move)
		{
			if (move is SwapSwitchStatusMove swapmove)
			{
				return swapmove.Configuration.SwappingSwitchesUsesValidTransformerModes(
					switchToClose: swapmove.SwitchToClose, 
					switchToOpen: swapmove.SwitchToOpen);
			}
			else
				throw new NotImplementedException(string.Format("The criterion '{0}' does not implement LegalMove() for the given type of move {1}", this, move.GetType()));
		}

		/// <summary>
		/// Annotations for invalid transformer modes (not implemented).
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public IEnumerable<Annotation> Annotate(ISolution solution)
		{
			throw new NotImplementedException();
		}
	}
}
