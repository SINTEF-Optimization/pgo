using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// Status information about a session
	/// </summary>
	public class SessionStatus
	{
		/// <summary>
		/// The ID of the session
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// The ID of the network that the session is using.
		/// </summary>
		public string NetworkId { get; set; }

		/// <summary>
		/// True if the solver is running, false if not.
		/// </summary>
		public bool OptimizationIsRunning { get; set; }

		/// <summary>
		/// The objective value of the best feasible (legal) solution.
		/// Null if no feasible solution has been found yet.
		/// </summary>
		public double? BestSolutionValue { get; set; }

		/// <summary>
		/// The objective value of the best infeasible (illegal) solution found.
		/// Null if no solution has been found yet or if a feasible solution has been found.
		/// </summary>
		public double? BestInfeasibleSolutionValue { get; set; }

		/// <summary>
		/// The IDs of the solutions stored in the session. This includes the ID 'best',
		/// which is reserved to refer to the best known solution (produced by optimization
		/// or added manually to the session).
		/// </summary>
		public List<string> SolutionIds { get; set; }
	}
}
