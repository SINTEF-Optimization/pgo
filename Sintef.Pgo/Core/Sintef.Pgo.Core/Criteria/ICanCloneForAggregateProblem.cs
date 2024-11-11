using System;
using System.Collections.Generic;
using System.Text;
using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Interface used for criteria that can be cloned to work on an aggregate of the
	/// original problem, to produce the same results as for the original (or approximately the same,
	/// depending on the exactness of the aggregation).
	/// </summary>
	public interface ICanCloneForAggregateProblem : ICriterion
	{
		/// <summary>
		/// Clones the criterion to be valid for an aggregate of the given
		/// <paramref name="originalProblem"/>.
		/// </summary>
		/// <param name="originalProblem">The original problem</param>
		/// <param name="aggregateProblem">The aggregate problem</param>
		ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem);
	}
}
