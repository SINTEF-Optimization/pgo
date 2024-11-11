using Sintef.Pgo.DataContracts;
using Sintef.Scoop.Kernel;
using System.Collections.Generic;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Interface for a PGO solution.
	/// </summary>
	public interface IPgoSolution : ISolution
	{
		/// <summary>
		/// True when all period problems have complete period solutions and flow computation
		/// succeeds in each for the given flow provider
		/// </summary>
		bool IsComplete(IFlowProvider flowProvider);

		/// <summary>
		/// An enumeration of the single period solutions that are computed so far
		/// </summary>
		IEnumerable<PeriodSolution> SinglePeriodSolutions { get; }

		/// <summary>
		/// Return summary information about this solution, for the given criteria set
		/// </summary>
		/// <returns></returns>
		SolutionInfo Summarize(CriteriaSet criteriaSet);

		/// <summary>
		/// The number of single-period solutions that have been computed for this multi-period solution.
		/// </summary>
		int PeriodCount { get; }

		/// <summary>
		/// Returns a JSON-formatted string representing the solution.
		/// </summary>
		/// <param name="flowProvider">The flow provider to report flows for. If null,
		///   no flows are reported</param>
		/// <param name="prettify">Whether to pretty-print the json</param>
		string ToJson(IFlowProvider flowProvider, bool prettify = false);
	}

	/// <summary>
	/// Extension methods for <see cref="IPgoSolution"/>
	/// </summary>
	public static class IPgoSolutionExtensions
	{
		/// <summary>
		/// The problem that the solution is for.
		/// </summary>
		public static PgoProblem Problem(this IPgoSolution solution) => solution.Encoding as PgoProblem;
	}
}