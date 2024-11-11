using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Api
{
	/// <summary>
	/// A PGO session.
	/// 
	/// A session allows you to optimize the network configuration for a situation described by the following data:
	///  - a power network
	///  - a collection of time periods, and consumer demands in each period
	///  - optionally, the network configuration before the first period
	///  
	/// The session contains a collection of solutions to the configuration problem, each identified by a string.
	/// The solution ID 'best' is reserved to refer to the best solution found by optimization.
	/// 
	/// A session has JSON and CIM versions of several methods. The JSON versions may be used only in sessions
	/// created for a JSON network, and the CIM versions may only be used in sessions created for a CIM network.
	/// </summary>
	public interface ISession
	{
		/// <summary>
		/// The ID of the session
		/// </summary>
		string Id { get; }

		/// <summary>
		/// The ID of the network that the session is based on
		/// </summary>
		string NetworkId { get; }

		/// <summary>
		/// Returns the session's status
		/// </summary>
		SessionStatus Status { get; }

		/// <summary>
		/// Returns the best known solution in PGO's JSON format
		/// </summary>
		/// <returns>The best known solution</returns>
		Solution BestJsonSolution { get; }

		/// <summary>
		/// Returns the best known solution in CIM format
		/// </summary>
		/// <returns>The best known solution</returns>
		CimSolution BestCimSolution { get; }

		/// <summary>
		/// Returns the best known solution in CIM JSON-LD format
		/// </summary>
		/// <returns>The best known solution</returns>
		CimJsonLdSolution BestCimJsonLdSolution { get; }

		/// <summary>
		/// Returns summary information about the best known solution
		/// </summary>
		/// <returns>Summary information about the best known solution</returns>
		SolutionInfo BestSolutionInfo { get; }

		/// <summary>
		/// This event is raised when optimization finds a new best solution
		/// </summary>
		event EventHandler<EventArgs> BestSolutionFound;

		/// <summary>
		/// Adds a JSON solution to the session's collection of solutions.
		/// </summary>
		/// <param name="id">The ID of the new solution</param>
		/// <param name="solution">The solution data, in PGO's JSON format</param>
		void AddSolution(string id, Solution solution);

		/// <summary>
		/// Adds a CIM solution to the session's collection of solutions.
		/// </summary>
		/// <param name="id">The ID of the new solution</param>
		/// <param name="solution">The solution data, in CIM format</param>
		void AddSolution(string id, CimSolution solution);

		/// <summary>
		/// Adds a CIM solution on JSON-LD format to the session's collection of solutions.
		/// </summary>
		/// <param name="id">The ID of the new solution</param>
		/// <param name="solution">The solution data, in CIM JSON-LD format</param>
		void AddSolution(string id, CimJsonLdSolution solution);

		/// <summary>
		/// Updates (replaces) an existing JSON solution
		/// </summary>
		/// <param name="id">The ID of the solution to update</param>
		/// <param name="solution">The solution data to replace the solution with, in PGO's JSON format</param>
		void UpdateSolution(string id, Solution solution);

		/// <summary>
		/// Updates (replaces) an existing CIM solution
		/// </summary>
		/// <param name="id">The ID of the solution to update</param>
		/// <param name="solution">The solution data to replace the solution with, in CIM format</param>
		void UpdateSolution(string id, CimSolution solution);

		/// <summary>
		/// Updates (replaces) an existing CIM solution on JSON-LD format
		/// </summary>
		/// <param name="id">The ID of the solution to update</param>
		/// <param name="solution">The solution data to replace the solution with, in CIM JSON-LD format</param>
		void UpdateSolution(string id, CimJsonLdSolution solution);

		/// <summary>
		/// Repairs an existing solution that does not allow radial flow because it is unconnected,
		/// has cycles, or uses transformers in invalid modes. The repaired solution is
		/// added to the session collection under the given new solution ID.
		/// </summary>
		/// <param name="id">The ID of the solution to repair</param>
		/// <param name="newId">the ID to give to the repaired solution</param>
		/// <returns>A message describing the repair</returns>
		string RepairSolution(string id, string newId);

		/// <summary>
		/// Removes a solution from the session's collection
		/// </summary>
		/// <param name="id">The ID of the solution to remove</param>
		void RemoveSolution(string id);

		/// <summary>
		/// Returns a solution in PGO's JSON format
		/// </summary>
		/// <param name="id">The ID of the solution to return</param>
		/// <returns>The requested solution</returns>
		Solution GetJsonSolution(string id);

		/// <summary>
		/// Returns a solution in CIM format
		/// </summary>
		/// <param name="id">The ID of the solution to return</param>
		/// <returns>The requested solution</returns>
		CimSolution GetCimSolution(string id);

		/// <summary>
		/// Returns a solution in CIM JSON-LD format
		/// </summary>
		/// <param name="id">The ID of the solution to return</param>
		/// <returns>The requested solution</returns>
		CimJsonLdSolution GetCimJsonLdSolution(string id);

		/// <summary>
		/// Returns summary information about a solution
		/// </summary>
		/// <param name="id">The ID of the solution</param>
		/// <returns>Summary information about the requsted solution</returns>
		SolutionInfo GetSolutionInfo(string id);

		/// <summary>
		/// Starts optimization in the session, if it is not already running
		/// </summary>
		void StartOptimization();

		/// <summary>
		/// Stops optimization in the session, if it is running
		/// </summary>
		void StopOptimization();
	}
}