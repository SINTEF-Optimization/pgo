using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sintef.Scoop.Utilities;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;

namespace Sintef.Pgo.Server
{
	/// <summary>
	/// The PGO server. The user can create one or more <see cref="ISession"/>'s, each of which 
	/// can be used to configure and run algorithms to solve a network configuration problem.
	/// These problems can be single- or multi-period.
	/// </summary>
	public class Server : IServer
	{
		#region Public properties 

		/// <summary>
		/// Enumerates the IDs of the existing sessions.
		/// </summary>
		public IEnumerable<string> SessionIds
		{
			get
			{
				lock (Sessions)
					return Sessions.Items.Select(s => s.Id).ToList();
			}
		}

		/// <summary>
		/// Enumerates the IDs of the existing networks.
		/// </summary>
		public IEnumerable<string> NetworkIds
		{
			get
			{
				lock (PowerNetworks)
					return PowerNetworks.Keys.ToList();
			}
		}

		#endregion

		#region Private properties and data members

		/// <summary>
		/// The created sessions.
		/// </summary>
		private SessionCollection Sessions { get; }

		/// <summary>
		/// The networks that have been added.
		/// </summary>
		private Dictionary<string, NetworkManager> PowerNetworks { get; } = new();

		/// <summary>
		/// For each CIM network, contains the converter that created
		/// the network.
		/// For non-CIM networks, contains null.
		/// </summary>
		private Dictionary<PowerNetwork, CimNetworkConverter> _cimNetworkConverters = new();

		#endregion

		#region Events

		public event EventHandler<SessionIdEventArgs> SessionDeleted;

		#endregion

		/// <summary>
		/// Deletes the session with the given <paramref name="id"/>, if it exists. 
		/// If not, the function does nothing.
		/// Stops optimization in the session if running.
		/// </summary>
		public void DeleteSession(string id)
		{
			lock (Sessions)
			{
				_ = GetSession(id)?.StopOptimization(); // Don't wait for completion

				if (Sessions.Delete(id))
					SessionDeleted?.Invoke(this, new SessionIdEventArgs(id));
			}
		}

		/// <summary>
		/// The networks that have been added.
		/// </summary>

		#region Construction

		/// <summary>
		/// Constructor, which is private. Use <see cref="Create"/> to create a <see cref="Server"/> object.
		/// </summary>
		private Server()
		{
			Sessions = new SessionCollection();
		}

		/// <summary>
		/// Creates and returns a new server
		/// </summary>
		public static Server Create()
		{
			return new Server();
		}

		#endregion

		#region Network management

		/// <inheritdoc/>
		public void LoadNetworkFromJson(string id, Stream inputFile, Action<PowerNetwork> action = null)
		{
			PowerNetwork network = PgoJsonParser.ParseNetworkFromJson(inputFile);
			action?.Invoke(network);
			AddNetwork(id, network);
		}

		/// <inheritdoc/>
		public void LoadCimNetworkFromJsonLd(string id, CimJsonLdNetworkData networkData, Action<PowerNetwork> action = null)
		{
			ICimUnitsProfile unitsProfile = CimUnitProfileFactory.CreateProfile(networkData.ParsingOptions.UnitsProfile);

			var parser = new CimJsonParser(unitsProfile);
			parser.Parse(networkData.Network);
			parser.CreateCimObjects();

			var converter = CimNetworkConverter.FromParser(parser, networkData.ConversionOptions ?? new());
			var network = converter.CreateNetwork();

			action?.Invoke(network);
			AddNetwork(id, network, converter);
		}

		/// <summary>
		/// Stores the given power network in the server, under the given ID.
		/// </summary>
		public void AddNetwork(string id, PowerNetwork network, CimNetworkConverter converter = null)
		{
			lock (PowerNetworks)
			{
				if (PowerNetworks.ContainsKey(id))
					throw new InvalidOperationException($"The server already has a power network with ID '{id}'.");

				var networkManager = new NetworkManager(network);

				PowerNetworks.Add(id, networkManager);

				_cimNetworkConverters[networkManager.OriginalNetwork] = converter;
			}
		}

		/// <summary>
		/// Returns a network connectivity status report for the network with the given `id`.
		/// </summary>
		/// <param name="id"></param>
		/// <returns></returns>
		public NetworkConnectivityStatus GetNetworkConnectivityStatus(string id)
		{
			var connectivityStatus = new NetworkConnectivityStatus();
			NetworkManager networkManager = GetNetworkManager(id);

			PowerNetwork acyclicNetwork = networkManager.AcyclicNetwork;
			var connectivity = acyclicNetwork.Connectivity;
			if (connectivity.HasFlag(PowerNetwork.ConnectivityType.HasUnbreakableCycle))
			{
				var cycle = acyclicNetwork.ShortestCycleWithoutSwitches ?? acyclicNetwork.ShortestCycle;
				var disaggregated = networkManager.AcyclicAggregator.OneDisaggregatedPath(cycle);
				connectivityStatus.UnbreakableCycle = disaggregated.Select(dl => dl.Line.Name).ToList();
			}

			//if (connectivity.HasFlag(PowerNetwork.ConnectivityType.HasDisconnectedComponent))
			{
				if (networkManager.AcyclicAggregator.UnconnectedBuses.Any())
				{
					connectivityStatus.UnconnectableNodes = networkManager.AcyclicAggregator.UnconnectedBuses.Select(s => s.Name).ToList();
				}
			}

			// Reporting inconsistent transformers might be misleading if the network has no radial configuration.
			var checkInconsistentTransformers = connectivityStatus.UnbreakableCycle == null && connectivityStatus.UnconnectableNodes == null;

			if (checkInconsistentTransformers && connectivity.HasFlag(PowerNetwork.ConnectivityType.HasInconsistentTransformerModes))
			{
				NetworkConfiguration config = NetworkConfiguration.AllClosed(acyclicNetwork);
				config.MakeRadialFlowPossible(throwOnFail: false);
				connectivityStatus.InvalidTransformers = config.TransformersUsingMissingModes.Select(tr => tr.Name).ToList();
			}

			connectivityStatus.Ok = connectivityStatus.UnbreakableCycle == null &&
				//connectivityStatus.UnconnectableBuses == null &&
				connectivityStatus.InvalidTransformers == null;

			return connectivityStatus;
		}

		/// <summary>
		/// Analyses the aggregated network (i.e. a network where some simple cycles may have been removed, and
		/// some sequences of connection nodes may have been aggregated to a single line), 
		/// and returns a human-readable text report.
		/// This is useful during integration development, to get a sense of how compact the 
		/// input network is, if it can in any way be configured radially and connected 
		/// (in the sense that all consumers can be connected to a provider).
		/// </summary>
		/// <returns></returns>
		public string AnalyseNetwork(string id, bool verbose)
		{
			NetworkManager networkManager = GetNetworkManager(id);

			PowerNetwork network = networkManager.AcyclicNetwork;

			var connectivityStatus = GetNetworkConnectivityStatus(id);
			(string cyclesText, NetworkConfiguration config) = network.ReportCycles();

			if (config.HasCycles)
			{
				var connectivityStatusText = PgoJsonParser.FormatNetworkConnectivityStatus(connectivityStatus);
				return $"{connectivityStatusText}" +
					$"\n\nDetailed cycles report follows." +
					$"\n${cyclesText}" +
					$"\n\nBecause the network has cycles, further analysis is not possible.\n";

			}

			var text = "";
			if (!connectivityStatus.Ok) text += PgoJsonParser.FormatNetworkConnectivityStatus(connectivityStatus);
			if (connectivityStatus.UnconnectableNodes != null) text += "\nAfter ignoring all un-connectable buses, we can analyse the remaining network:\n";

			text += "Input network summary:\n" + networkManager.OriginalNetwork.AnalyseNetworkProperties(verbose);
			text += "\n\nInternally, PGO uses an aggregated version of this network, with the following properties:\n" + network.AnalyseNetwork(verbose);

			return text;
		}

		/// <summary>
		/// Returns the network manager for the network with the given ID
		/// </summary>
		public NetworkManager GetNetworkManager(string id)
		{
			NetworkManager networkManager;
			lock (PowerNetworks)
			{
				if (!PowerNetworks.TryGetValue(id, out networkManager))
					throw new ArgumentException($"There is no power network with ID '{id}'");
			}

			return networkManager;
		}

		/// <summary>
		/// Returns an already loaded network.
		/// </summary>
		/// <returns></returns>
		public PowerGrid GetNetwork(string id)
		{
			return PgoJsonParser.ConvertToJson(GetNetworkManager(id).OriginalNetwork);
		}

		/// <summary>
		/// Deletes an already loaded network with the given ID, if it exists. 
		/// If not, the function does nothing.
		/// Also deletes all sessions associated with the network.
		/// </summary>
		public void DeleteNetwork(string id)
		{
			foreach (var session in Sessions.Items.ToList())
			{
				if (session.NetworkId == id)
					DeleteSession(session.Id);
			}

			lock (PowerNetworks)
			{
				if (PowerNetworks.TryGetValue(id, out var network))
					_cimNetworkConverters.Remove(network.OriginalNetwork);
				PowerNetworks.Remove(id);
			}
		}

		/// <inheritdoc/>
		public CimNetworkConverter GetCimNetworkConverter(string networkId) => _cimNetworkConverters[GetNetworkManager(networkId).OriginalNetwork];

		#endregion

		#region Session managment

		/// <summary>
		/// Returns the session with the given <paramref name="id"/>.
		/// </summary>
		/// <returns>The identified session, or null if no session with that <paramref name="id"/> exists.</returns>
		public ISession GetSession(string id)
		{
			lock (Sessions)
				return Sessions.ItemOrDefault(id);
		}

		/// <summary>
		/// Deletes all sessions whose timeout has expired
		/// </summary>
		public void CleanUpSessions()
		{
			lock (Sessions)
			{
				foreach (var session in Sessions.Items.ToList())
				{
					if (session.HasTimedOut)
						DeleteSession(session.Id);
				}
			}
		}

		/// <inheritdoc/>
		public ISession CreateJsonSession(string sessionId, string networkId, Stream demandsStream,
			Stream startConfigurationStream = null, bool allowUnspecifiedConsumerDemands = false)
		{
			return CreateSession(SessionType.Json, sessionId, networkId, ReadJsonDemands, ReadJsonStartConfiguration);


			// Local functions

			List<PeriodData> ReadJsonDemands(PowerNetwork network)
			{
				return PgoJsonParser.ParseDemandsFromJson(network, demandsStream, allowUnspecifiedConsumerDemands);
			}

			NetworkConfiguration ReadJsonStartConfiguration(PowerNetwork network)
			{
				if (startConfigurationStream != null)
					return PgoJsonParser.ParseConfigurationFromStream(network, startConfigurationStream);
				else
					return null;
			}
		}

		/// <summary>
		/// Creates a session with the given <paramref name="sessionId"/> for solving a configuration problem 
		/// on the power network with the given <paramref name="networkId"/>.
		/// Throws an exception if a session with that <paramref name="sessionId"/> already exists.
		/// </summary>
		/// <param name="sessionId">The id of the new session.</param>
		/// <param name="networkId">The id of the power network to use in the session.</param>
		/// <param name="demands">The demands per time period</param>
		/// <param name="startConfiguration">The network configuration at the beginning of the planning period,
		///   or null.</param>
		/// <param name="allowUnspecifiedConsumerDemands">If true and a demand is not given for 
		///   a bus, we assume the demand is zero. However, if demands are given, they must be given for each period.
		///   If false, demands must be given for all consumers.
		/// </param>
		/// <returns>The created session.</returns>
		public ISession CreateJsonSession(string sessionId, string networkId, Demand demands, SinglePeriodSettings startConfiguration, 
			bool allowUnspecifiedConsumerDemands = false)
		{
			return CreateSession(SessionType.Json, sessionId, networkId, ConvertJsonDemands, ConvertJsonStartConfiguration);


			// Local functions

			List<PeriodData> ConvertJsonDemands(PowerNetwork network)
			{
				return PgoJsonParser.ParseDemands(network, demands, allowUnspecifiedConsumerDemands);
			}

			NetworkConfiguration ConvertJsonStartConfiguration(PowerNetwork network)
			{
				if (startConfiguration != null)
					return PgoJsonParser.ParseConfiguration(network, startConfiguration);
				else
					return null;
			}
		}

		/// <inheritdoc/>
		public ISession CreateCimSession(string sessionId, CimJsonLdSessionParameters parameters)
		{
			return CreateSession(SessionType.Cim, sessionId, parameters.NetworkId, ReadCimDemands, ReadCimStartConfiguration);


			// Local functions

			List<PeriodData> ReadCimDemands(PowerNetwork network)
			{
				CimNetworkConverter networkConverter = _cimNetworkConverters[network];
				var converter = new CimDemandsConverter(network, networkConverter.NetworkParser);

				return converter.ToPeriodData(parameters.PeriodsAndDemands);
			}

			NetworkConfiguration ReadCimStartConfiguration(PowerNetwork network)
			{
				if (parameters.StartConfiguration == null)
					return null;

				// Convert to network configuration
				var converter = new CimConfigurationConverter(_cimNetworkConverters[network]);
				return converter.ToNetworkConfiguration(parameters.StartConfiguration);
			}
		}

		/// <summary>
		/// Creates a session with the given <paramref name="sessionId"/> for solving a configuration problem.
		/// Throws an exception if a session with that <paramref name="sessionId"/> already exists.
		/// </summary>
		/// <param name="sessionId">The id of the new session.</param>
		/// <param name="parameters">Parameters for the session</param>
		/// <returns>The created session.</returns>
		public ISession CreateCimSession(string sessionId, CimSessionParameters parameters)
		{
			if (parameters.NetworkId == null)
				throw new ArgumentException("The network ID is null");
			if (parameters.PeriodsAndDemands == null)
				throw new ArgumentException("The list of periods and demands is null");

			return CreateSession(SessionType.Cim, sessionId, parameters.NetworkId, ConvertCimDemands, ConvertCimStartConfiguration);


			// Local functions

			List<PeriodData> ConvertCimDemands(PowerNetwork network)
			{
				CimNetworkConverter networkConverter = _cimNetworkConverters[network];
				var converter = new CimDemandsConverter(network, networkConverter.NetworkParser);

				return converter.ToPeriodData(parameters.PeriodsAndDemands);
			}

			NetworkConfiguration ConvertCimStartConfiguration(PowerNetwork network)
			{
				if (parameters.StartConfiguration == null)
					return null;

				// Convert to network configuration
				var converter = new CimConfigurationConverter(_cimNetworkConverters[network]);
				return converter.ToNetworkConfiguration(parameters.StartConfiguration);
			}
		}

		/// <summary>
		/// Creates a new session, adding it to the server.
		/// </summary>
		/// <param name="sessionId">The id of the new session.</param>
		/// <param name="networkId">The id of the power network to use in the session.</param>
		/// <param name="createDemands">A function that produces the demands to use in the session</param>
		/// <param name="createStartConfiguration">A function that produces the start configuration to use in the session</param>
		/// <returns>The new session</returns>
		private ISession CreateSession(SessionType type, string id, string networkId, Func<PowerNetwork, List<PeriodData>> createDemands,
			Func<PowerNetwork, NetworkConfiguration> createStartConfiguration)
		{
			NetworkManager networkManager = GetNetworkManager(networkId);

			bool networkIsCim = _cimNetworkConverters[networkManager.OriginalNetwork] != null;
			if (type == SessionType.Cim && !networkIsCim)
				throw new InvalidOperationException($"Cannot create a CIM session for a non-CIM network");
			if (type == SessionType.Json && networkIsCim)
				throw new InvalidOperationException($"Cannot create a non-CIM session for a CIM network");

			lock (Sessions)
			{
				if (Sessions.Exists(id))
					throw new InvalidOperationException($"A session with ID '{id}' already exists");
			}

			if (!GetNetworkConnectivityStatus(networkId).Ok)
				throw new Exception($"Cannot create session: network connectivity check failed " +
					$"(see /api/networks/{id}/connectivityStatus or IServer.AnalyzeNetworkConnectivity())");

			List<PeriodData> demands;
			try
			{
				demands = createDemands(networkManager.OriginalNetwork);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error in the demands: {ex.Message}", ex);
			}

			// Parse current configuration
			NetworkConfiguration startConfiguration;
			try
			{
				startConfiguration = createStartConfiguration(networkManager.OriginalNetwork);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error in the start configuration: {ex.Message}", ex);
			}

			var newSession = Session.Create(id, networkId, networkManager, demands, startConfiguration);

			// Check for obvious inconsistencies in the setup
			(bool ok, string desc) = newSession.Problem.AnalyseDemandsCurrentsAndVoltages(networkManager.AcyclicNetwork);
			if (!ok)
				throw new Exception($"Problems with demand, currents, or voltages:\n{desc}");

			lock (Sessions)
			{
				if (Sessions.Exists(id))
					throw new InvalidOperationException($"A session with ID '{id}' already exists");
				Sessions.Add(newSession);
			}

			return newSession;
		}

		/// <summary>
		/// Creates a session based on the given problem. Used internally for debugging and testing, not to be considered a part of the official interface.
		/// </summary>
		public Session CreateSession(string id, string networkId, PgoProblem problem)
		{
			if (!PowerNetworks.TryGetValue(networkId, out var networkManager))
				throw new ArgumentException($"There is no power network with ID '{id}'");

			//TODO remove? This is here temporarily to help users.
			(bool ok, string desc) = problem.AnalyseDemandsCurrentsAndVoltages(problem.Network);
			if (!ok)
				throw new Exception($"Problems with demand, currents, or voltages:\n{desc}");

			Session session = new Session(id, problem, networkManager);

			lock (Sessions)
				Sessions.Add(session);

			return session;
		}


		#endregion

		/// <summary>
		/// A type of session
		/// </summary>
		public enum SessionType
		{
			/// <summary>
			/// A session based on data expressed in PGO's JSON format
			/// </summary>
			Json,

			/// <summary>
			/// A session based on CIM data
			/// </summary>
			Cim
		}
	}

	public class SessionIdEventArgs : EventArgs
	{
		public string Id { get; }

		public SessionIdEventArgs(string id)
		{
			Id = id;
		}
	}
}
