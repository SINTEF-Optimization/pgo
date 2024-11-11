using System;
using System.Collections.Generic;
using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// An interface with function for setting up a local search/meta heuristic profiler.
	/// </summary>
	public interface ICanSetupSearchProfiler
	{

		/// <summary>
		/// Sets up a profiler for the local search, on the given problem encoding, <see cref="Descent"/> solver and meta heuristic.
		/// </summary>
		void SetUpSearchProfiler(Encoding encoding, LocalSearcher descentSolver, MetaHeuristicBase metaHeuristic);
		
	}
}
