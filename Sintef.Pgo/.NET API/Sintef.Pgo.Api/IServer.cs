using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Api
{
	/// <summary>
	/// The .NET interface to a PGO server.
	/// 
	/// A server contains a collection of power networks and a collection of sessions.
	/// You can add/remove networks, perform network analysis, and add/remove sessions.
	/// </summary>
	public interface IServer
	{
		/// <summary>
		/// Returns the server's status
		/// </summary>
		ServerStatus Status { get; }

		/// <summary>
		/// Enumerates the sessions in the server
		/// </summary>
		IEnumerable<ISession> Sessions { get; }

		/// <summary>
		/// Adds a network to the server, from data in PGO's JSON format
		/// </summary>
		/// <param name="networkId">The ID of the new network</param>
		/// <param name="network">The network data</param>
		void AddNetwork(string networkId, PowerGrid network);

		/// <summary>
		/// Adds a network to the server, from CIM data
		/// </summary>
		/// <param name="networkId">The ID of the new network</param>
		/// <param name="cimNetworkData">The network data</param>
		void AddNetwork(string networkId, CimNetworkData cimNetworkData);

		/// <summary>
		/// Adds a network to the server, from CIM data on JSON-LD format
		/// </summary>
		/// <param name="networkId">The ID of the new network</param>
		/// <param name="cimNetworkData">The network data</param>
		void AddNetwork(string networkId, CimJsonLdNetworkData cimNetworkData);

		/// <summary>
		/// Removes the network with the given ID.
		/// Does nothing if there is no network with the given ID.
		/// </summary>
		/// <param name="networkId">The ID of the network to remove</param>
		void RemoveNetwork(string networkId);

		/// <summary>
		/// Analyzes a network and returns a string describing the results.
		/// 
		/// The result is a human-readable report that has no fixed specified format, but
		/// contains information that may be useful for integration testing or checking
		/// how PGO interprets a network data set.
		/// </summary>
		/// <param name="networkId">The ID of the network to analyze</param>
		/// <param name="verbose">If true, the report contains more information</param>
		/// <returns>The analysis result</returns>
		string AnalyzeNetwork(string networkId, bool verbose);

		/// <summary>
		/// Analyzes the connectivity of a network.
		/// </summary>
		/// <param name="networkId">The ID of the network to analyze</param>
		/// <returns>The analysis result</returns>
		NetworkConnectivityStatus AnalyzeNetworkConnectivity(string networkId);

		/// <summary>
		/// Adds a new session to the server, from data in PGO's JSON format
		/// </summary>
		/// <param name="sessionId">The ID of the new session</param>
		/// <param name="parameters">Parameters for creating the session</param>
		/// <returns>The new session</returns>
		ISession AddSession(string sessionId, SessionParameters parameters);

		/// <summary>
		/// Adds a new session to the server, from CIM data
		/// </summary>
		/// <param name="sessionId">The ID of the new session</param>
		/// <param name="parameters">Parameters for creating the session</param>
		/// <returns>The new session</returns>
		ISession AddSession(string sessionId, CimSessionParameters parameters);

		/// <summary>
		/// Adds a new session to the server, from CIM data on JSON-LD format
		/// </summary>
		/// <param name="sessionId">The ID of the new session</param>
		/// <param name="parameters">Parameters for creating the session</param>
		/// <returns>The new session</returns>
		ISession AddSession(string sessionId, CimJsonLdSessionParameters parameters);

		/// <summary>
		/// Removes an existing session from the server.
		/// 
		/// If optimization is running in the session, it is stopped.
		/// </summary>
		/// <remarks>
		/// Using any method or property in the session object after calling this
		/// function will result in an exception.
		/// </remarks>
		/// <param name="session">The session to remove</param>
		void RemoveSession(ISession session);
	}
}