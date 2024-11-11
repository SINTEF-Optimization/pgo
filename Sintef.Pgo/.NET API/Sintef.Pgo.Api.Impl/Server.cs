using Newtonsoft.Json.Linq;
using Sintef.Pgo.Cim;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Server;
using Sintef.Scoop.Utilities;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Sintef.Pgo.Api.Tests")]

namespace Sintef.Pgo.Api.Impl
{
	/// <summary>
	/// The implementation of <see cref="IServer"/>.
	/// 
	/// This is mostly a thin adaptation layer on top of <see cref="Pgo.Server.Server"/>.
	/// </summary>
	public class Server : IServer
	{
		/// <summary>
		/// The internal server that we use
		/// </summary>
		private Pgo.Server.Server _server;

		/// <summary>
		/// The server's sessions
		/// </summary>
		private List<Session> _sessions = new();

		/// <summary>
		/// Lock protecting updates to _sessions
		/// </summary>
		private object _lock = new object();

		/// <summary>
		/// Initializes the server
		/// </summary>
		public Server()
		{
			_server = Pgo.Server.Server.Create();
		}

		/// <inheritdoc/>
		public ServerStatus Status => new ServerStatus()
		{
			Networks = _server.NetworkIds.ToList(),
			Sessions = _server.SessionIds.Select(id => _server.GetSession(id).GetStatus()).ToList(),
		};

		/// <inheritdoc/>
		public IEnumerable<ISession> Sessions => _sessions;

		/// <inheritdoc/>
		public void AddNetwork(string networkId, PowerGrid network)
		{
			if (networkId == null)
				throw new ArgumentNullException(nameof(networkId));
			if (network == null)
				throw new ArgumentNullException(nameof(network));

			PowerNetwork pgoNetwork;
			try
			{
				pgoNetwork = PgoJsonParser.ParseNetwork(network);
			}
			catch (Exception ex)
			{
				throw new ArgumentException($"An error occurred while parsing the network data: {ex.Message}");
			}

			_server.AddNetwork(networkId, pgoNetwork);
		}

		/// <inheritdoc/>
		public void AddNetwork(string networkId, CimNetworkData cimNetworkData)
		{
			AddNetwork(networkId, cimNetworkData, null);
		}

		/// <inheritdoc/>
		public void AddNetwork(string networkId, CimJsonLdNetworkData networkData)
		{
			if (networkId == null)
				throw new ArgumentNullException(nameof(networkId));
			if (networkData == null)
				throw new ArgumentNullException(nameof(networkData));

			if (networkData.Network == null)
				throw new ArgumentException($"Bad network data: {nameof(networkData.Network)} is null");
			if (networkData.ConversionOptions == null)
				throw new ArgumentException($"Bad network data: {nameof(networkData.ConversionOptions)} is null");

			var unitsProfile = CimUnitProfileFactory.CreateProfile(networkData.ParsingOptions.UnitsProfile);
			var networkParser = new CimJsonParser(unitsProfile);

			networkParser.Parse(networkData.Network);
			networkParser.CreateCimObjects();

			var cimNetwork = CimNetwork.FromObjects(networkParser.CreatedObjects<IdentifiedObject>());

			CimNetworkData cimNetworkData = new()
			{
				Network = cimNetwork,
				ConversionOptions = networkData.ConversionOptions
			};

			AddNetwork(networkId, cimNetworkData, networkParser);
		}

		/// <inheritdoc/>
		public void RemoveNetwork(string networkId)
		{
			if (networkId == null)
				throw new ArgumentNullException(nameof(networkId));

			_server.DeleteNetwork(networkId);

			UpdateSessions();
		}

		/// <inheritdoc/>
		public string AnalyzeNetwork(string networkId, bool verbose)
		{
			if (networkId == null)
				throw new ArgumentNullException(nameof(networkId));

			return _server.AnalyseNetwork(networkId, verbose);
		}

		/// <inheritdoc/>
		public NetworkConnectivityStatus AnalyzeNetworkConnectivity(string networkId)
		{
			if (networkId == null)
				throw new ArgumentNullException(nameof(networkId));

			return _server.GetNetworkConnectivityStatus(networkId);
		}

		/// <inheritdoc/>
		public ISession AddSession(string sessionId, SessionParameters parameters)
		{
			_ = parameters ?? throw new ArgumentNullException(nameof(parameters));

			string networkId = parameters.NetworkId ?? throw new ArgumentException("No network ID is given");
			var demand = parameters.Demand ?? throw new ArgumentException("No demand is given");

			_server.CreateJsonSession(sessionId ?? throw new ArgumentNullException(nameof(sessionId)),
				networkId, demand, parameters.StartConfiguration, parameters.AllowUnspecifiedConsumerDemands);

			Session session = new Session(_server, sessionId, networkId);

			UpdateSessions(newSession: session);

			return session;
		}

		/// <inheritdoc/>
		public ISession AddSession(string sessionId, CimSessionParameters parameters)
		{
			_server.CreateCimSession(
				sessionId ?? throw new ArgumentNullException(nameof(sessionId)),
				parameters ?? throw new ArgumentNullException(nameof(parameters)));

			Session session = new Session(_server, sessionId, parameters.NetworkId);

			UpdateSessions(newSession: session);

			return session;
		}

		/// <inheritdoc/>
		public ISession AddSession(string sessionId, CimJsonLdSessionParameters parameters)
		{
			if (sessionId == null)
				throw new ArgumentNullException(nameof(sessionId));
			if (parameters == null)
				throw new ArgumentNullException(nameof(parameters));

			var network = _server.GetNetworkManager(parameters.NetworkId).OriginalNetwork;
			var networkConverter = _server.GetCimNetworkConverter(parameters.NetworkId);

			if (networkConverter == null)
				throw new InvalidOperationException("Cannot create a CIM session for a non-CIM network");
			if (networkConverter.NetworkParser == null)
				throw new InvalidOperationException("Cannot create a session from JSON-LD: URIs are unknown since the network was not created from JSON-LD data.");

			var periodsAndDemands = parameters.PeriodsAndDemands
				.Select(item =>
					new CimPeriodAndDemands
					{
						Period = item.Period,
						Demands = ConvertDemands(item.Demands)
					})
				.ToList();

			var sessionParameters = new CimSessionParameters
			{
				NetworkId = parameters.NetworkId,
				PeriodsAndDemands = periodsAndDemands,
				StartConfiguration = ConvertConfiguration(parameters.StartConfiguration)
			};

			return AddSession(sessionId, sessionParameters);



			CimDemands ConvertDemands(JObject demands)
			{
				var parser = new CimJsonParser(networkConverter.NetworkParser.Units);

				parser.Parse(demands);
				parser.CreateCimObjects();

				return new CimDemandsConverter(network, networkConverter.NetworkParser).ToCimDemands(parser);
			}


			CimConfiguration ConvertConfiguration(JObject configuration)
			{
				var parser = new CimJsonParser(networkConverter.NetworkParser.Units);

				parser.Parse(configuration);
				parser.CreateCimObjects();

				return new CimConfigurationConverter(networkConverter).ToCimConfiguration(parser);
			}
		}

		/// <inheritdoc/>
		public void RemoveSession(ISession session)
		{
			if (session == null)
				throw new ArgumentNullException(nameof(session));

			if (!_sessions.Contains(session))
				throw new ArgumentException("The session does not belong to this server");

			_server.DeleteSession(session.Id);

			UpdateSessions();
		}

		/// <summary>
		/// Worker method for two public AddNetwork overloads
		/// </summary>
		private void AddNetwork(string networkId, CimNetworkData cimNetworkData, CimJsonParser? networkParser)
		{
			if (networkId == null)
				throw new ArgumentNullException(nameof(networkId));
			if (cimNetworkData == null)
				throw new ArgumentNullException(nameof(cimNetworkData));

			if (cimNetworkData.Network == null)
				throw new ArgumentException($"Bad network data: {nameof(cimNetworkData.Network)} is null");
			if (cimNetworkData.ConversionOptions == null)
				throw new ArgumentException($"Bad network data: {nameof(cimNetworkData.ConversionOptions)} is null");

			var converter = new CimNetworkConverter(cimNetworkData.Network, cimNetworkData.ConversionOptions, networkParser);
			var pgoNetwork = converter.CreateNetwork();

			_server.AddNetwork(networkId, pgoNetwork, converter);
		}

		/// <summary>
		/// Updates _sessions to reflect the internal server's live sessions.
		/// </summary>
		/// <param name="newSession">A new session to add to the list</param>
		private void UpdateSessions(Session? newSession = null)
		{
			// Work within a lock and repace atomically to avoid race conditions

			lock (_lock)
			{
				if (newSession != null)
					_sessions.Add(newSession);

				var liveSessionIds = _server.SessionIds.ToList();
				var deadSessions = _sessions.Where(s => !liveSessionIds.Contains(s.Id));

				foreach (var s in deadSessions)
					s.Die();

				_sessions = _sessions.Except(deadSessions).ToList();
			}
		}
	}
}