using System;
using System.Collections.Generic;
using System.IO;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Server
{
	/// <summary>
	/// Interface for a PGO server. 
	/// The user can create one or more <see cref="ISession"/>'s, each of which 
	/// can be used to configure and run algorithms to solve a network configuration problem.
	/// These problems can be single- or multi-period.
	/// </summary>
	public interface IServer
	{
		/// <summary>	
		/// Enumerates the IDs of the existing sessions.
		/// </summary>
		IEnumerable<string> SessionIds { get; }

		/// <summary>
		/// Enumerates the IDs of the existing loaded networks.
		/// </summary>
		IEnumerable<string> NetworkIds { get; }

		/// <summary>
		/// Creates a session with the given <paramref name="sessionId"/> for solving a configuration problem 
		/// on the power network with the given <paramref name="networkId"/>.
		/// Throws an exception if a session with that <paramref name="sessionId"/> already exists.
		/// </summary>
		/// <param name="sessionId">The id of the new session.</param>
		/// <param name="networkId">The id of the power network to use in the session.</param>
		/// <param name="demandsStream">A stream containing the demands per time period, as a JSON encoded <see cref="Demand"/>.</param>
		/// <param name="startConfigurationStream">A stream containing the network configuration at the beginning of the planning period,
		///   as a JSON encoded <see cref="SinglePeriodSettings"/>.</param>
		/// <param name="allowUnspecifiedConsumerDemands">If true, consumers that are not specified in the forcast will be assumed to have zero demand. If false, all consumers' demands must be specified in the forecast.</param>
		/// <returns>The created session.</returns>
		ISession CreateJsonSession(string sessionId, string networkId, Stream demandsStream,
									Stream startConfigurationStream = null, bool allowUnspecifiedConsumerDemands = false);

		/// <summary>
		/// Creates a session with the given <paramref name="sessionId"/> for solving a configuration problem.
		/// Throws an exception if a session with that <paramref name="sessionId"/> already exists.
		/// </summary>
		/// <param name="sessionId">The id of the new session.</param>
		/// <param name="parameters">Parameters for the session</param>
		/// <returns>The created session.</returns>
		ISession CreateCimSession(string sessionId, CimJsonLdSessionParameters parameters);

		/// <summary>
		/// Deletes the session with the given <paramref name="id"/>, if it exists. 
		/// If not, the function does nothing.
		/// Stops optimization in the session if running.
		/// </summary>
		void DeleteSession(string id);

		/// <summary>
		/// Deletes all sessions whose timeout has expired
		/// </summary>
		void CleanUpSessions();

		/// <summary>
		/// Returns the session with the given <paramref name="id"/>.
		/// </summary>
		/// <returns>The identified session, or null if no session with that <paramref name="id"/> exists.</returns>
		ISession GetSession(string id);

		/// <summary>
		/// Creates a power network from the given json data and adds it to the network list.
		/// </summary>
		/// <param name="id">The ID of the network</param>
		/// <param name="networkData">The json data, containing a serialized <see cref="PowerGrid"/></param>
		/// <param name="action">If not null, an action to be invoked on the parsed network, before it is stored. The action may
		///   abort the function by throwing an exception.</param>
		void LoadNetworkFromJson(string id, Stream networkData, Action<PowerNetwork> action = null);

		/// <summary>
		/// Creates a power network from the given data and adds it to the network list.
		/// 
		/// For information on how the CIM model is converted to the PGO internal model,
		/// see the documentation of <see cref="CimNetwork"/>.
		/// </summary>
		/// <param name="id">The ID of the network</param>
		/// <param name="networkData">The network data</param>
		/// <param name="action">If not null, an action to be invoked on the parsed network, before it is stored. The action may
		///   abort the function by throwing an exception.</param>
		void LoadCimNetworkFromJsonLd(string id, CimJsonLdNetworkData networkData, Action<PowerNetwork> action = null);

		/// <summary>
		/// Analyses the network, and returns a human-readable text report.
		/// This is useful during integration development, to get a sense of how compact the 
		/// input network is, if it can in any way be configured radially and connected 
		/// (in the sense that all consumers can be connected to a provider).
		/// </summary>
		/// <returns></returns>
		string AnalyseNetwork(string id, bool verbose);

		/// <summary>
		/// Returns a network connectivity status report for the network with the given `id`.
		/// </summary>
		/// <param name="id"></param>
		/// <returns></returns>
		NetworkConnectivityStatus GetNetworkConnectivityStatus(string id);

		/// <summary>
		/// Returns an already loaded network with the given ID.
		/// </summary>
		/// <returns></returns>
		PowerGrid GetNetwork(string id);

		/// <summary>
		/// Deletes an already loaded network with the given ID, if it exists. 
		/// If not, the function does nothing.
		/// Also deletes all sessions associated with the network.
		/// </summary>
		void DeleteNetwork(string id);

		/// <summary>
		/// Returns the converter that created the network with the given ID, 
		/// if this server contains CIM networks.
		/// </summary>
		CimNetworkConverter GetCimNetworkConverter(string networkId);
	}
}