using Sintef.Pgo.Core;
using Sintef.Pgo.DataContracts;
using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sintef.Pgo.Server
{
	/// <summary>
	/// Basic interface for all sessions, with session ID and basic state information. This is the basic interface that is exposed for
	/// the external user.
	/// </summary>
	public interface ISession
	{
		/// <summary>
		/// The Id of this session
		/// </summary>
		string Id { get; }

		/// <summary>
		/// The ID of the network that this session is using.
		/// </summary>
		string NetworkId { get; }

		/// <summary>
		/// Solver running state, true if the solver is running, false if not.
		/// </summary>
		bool OptimizationIsRunning { get; }


		/// <summary>
		/// The network configuration problem that this session is for.
		/// </summary>
		PgoProblem Problem { get; }

		/// <summary>
		/// The objective value of the best solution
		/// </summary>
		double BestSolutionValue { get; }

		/// <summary>
		/// True if the best solution is feasible, false if not
		/// </summary>
		bool BestSolutionIsFeasible { get; }

		/// <summary>
		/// Returns the currently best known solution to the problem.
		/// Returns null if none has been found.
		/// </summary>
		IPgoSolution BestSolution { get; }

		/// <summary>
		/// Returns the IDs of all solutions held by the session
		/// </summary>
		IEnumerable<string> SolutionIds { get; }

		/// <summary>
		/// True if the session is not optimizing, and the timeout interval has elapsed since
		/// the last time any of these events took place
		///		- The session was created
		///		- Optimization stopped
		///		- The timeout interval was changed
		///		- A solution was added or removed
		/// </summary>
		bool HasTimedOut { get; }

		/// <summary>
		/// Sets the interval after which the session times out
		/// </summary>
		void SetTimesOutAfter(TimeSpan time);

		/// <summary>
		/// Event raised each time a new best solution is found, communicating the full solution.
		/// </summary>
		event EventHandler<SolutionEventArgs> BestSolutionFound;

		/// <summary>
		/// Event raised each time a new best solution is found
		/// </summary>
		event EventHandler<ValueEventArgs> BestSolutionValueFound;

		/// <summary>
		/// Raised when optimization has stopped, whether by user action or because the algorithm was done.
		/// </summary>
		event EventHandler OptimizationStopped;

		/// <summary>
		/// Runs optimization of the problem defined for the session. The best known solution at any time during
		/// the run is communicated by the <see cref="BestSolutionFound"/> event. To check whether
		/// the solver is running, see <see cref="OptimizationIsRunning"/>.
		/// The task completes when optimization has stopped.
		/// </summary>
		/// <param name="stopCriterion">If not null, the optimization will stop when this stop
		///   criterion is triggered</param>
		Task StartOptimization(StopCriterion stopCriterion);

		/// <summary>
		/// Stops any running optimization. If SolverIsRunning = false,
		/// nothing happens.  the OptimizationStopped event will be raised when the algorithm has terminated.
		/// The SolverIsRunning flag remains true until the 
		/// optimization has actually stopped.
		/// The task completes when optimization has stopped.
		/// </summary>
		Task StopOptimization();

		/// <summary>
		/// Get thread-safe copy of best solution. Necessary if returning the solution while optimization is running.
		/// </summary>
		/// <returns>A copy of the best solution, or null if no solution has been found yet.</returns>
		IPgoSolution GetBestSolutionClone();

		/// <summary>
		/// Gets the session's solution with the given ID. The solution was added earlier using
		/// <see cref="AddSolution"/>, or, if the ID is 'best', a copy of the best solution.
		/// Returns null if the solution does not exist.
		/// </summary>
		IPgoSolution GetSolution(string solutionId);

		/// <summary>
		/// Sets objective weights in the current <see cref="Problem"/>.
		/// </summary>
		/// <param name="objectiveComponentWeights"></param>
		void SetObjectiveWeights(IEnumerable<ObjectiveWeight> objectiveComponentWeights);

		/// <summary>
		/// Returns the currently used objective weights.
		/// </summary>
		/// <returns></returns>
		IEnumerable<ObjectiveWeight> GetObjectiveWeights();

		/// <summary>
		/// Get the demands that were used to create the session.
		/// </summary>
		/// <returns></returns>
		Demand GetDemands();

		/// <summary>
		/// Repair a solution (connect components, remove cycles, make transformers use valid modes).
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		IPgoSolution RepairSolution(IPgoSolution solution);

		/// <summary>
		/// Repairs the given solution and adds the result to the solution pool.
		/// </summary>
		/// <param name="oldSolution">The solution to repair</param>
		/// <param name="newSolutionId">The ID to give the repaired solution</param>
		/// <returns>A message describing the repair</returns>
		string Repair(IPgoSolution oldSolution, string newSolutionId);

		/// <summary>
		/// Return summary information about the given solution
		/// </summary>
		SolutionInfo Summarize(IPgoSolution solution);

		/// <summary>
		/// Ensures that the given solution has a flow for all periods where it is radial
		/// (on the aggregated network)
		/// </summary>
		void ComputeFlow(PgoSolution pgoSolution);

		/// <summary>
		/// Adds a solution to the session
		/// </summary>
		/// <param name="solution">The solution</param>
		/// <param name="solutionId">The ID to reference the solution by</param>
		void AddSolution(IPgoSolution solution, string solutionId);

		/// <summary>
		/// Updates an existing solution
		/// </summary>
		/// <param name="solution">The solution</param>
		/// <param name="solutionId">The solution ID</param>
		void UpdateSolution(PgoSolution solution, string solutionId);

		/// <summary>
		/// Removes a solution from the session
		/// </summary>
		/// <param name="solutionId">The ID of the solution to remove</param>
		void RemoveSolution(string solutionId);
	}
}