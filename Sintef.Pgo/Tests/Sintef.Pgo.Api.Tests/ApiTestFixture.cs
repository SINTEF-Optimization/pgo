using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Sintef.Pgo.Cim;
using Sintef.Scoop.Utilities;
using System.Diagnostics;
using System.Globalization;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.Core.Test;

using ActivePower = UnitsNet.Power;
using ReactivePower = UnitsNet.ReactivePower;

namespace Sintef.Pgo.Api.Tests
{
	/// <summary>
	/// A fixture for tests using the .NET API.
	/// 
	/// Contains a server and helper methods for e.g. adding sessions
	/// </summary>
	public class ApiTestFixture
	{
		/// <summary>
		/// Returns the server's status
		/// </summary>
		protected ServerStatus Status => _server.Status;

		/// <summary>
		/// Returns the IDs of all loaded network, sorted and space-separated
		/// </summary>
		protected string NetworkIds => Status.Networks.OrderBy(x => x).Join(" ");

		/// <summary>
		/// Returns the IDs of all loaded sessions and corresponding network, sorted and space-separated
		/// </summary>
		public string SessionIds => _server.Sessions
			.Select(s => $"{s.Id}:{s.NetworkId}")
			.Join(" ");

		/// <summary>
		/// The server
		/// </summary>
		protected IServer _server = ServerFactory.CreateServer();

		/// <summary>
		/// The default JSON network ID
		/// </summary>
		protected const string _jsonNetworkId = "jsonNetworkId";

		/// <summary>
		/// The default CIM network ID
		/// </summary>
		protected const string _cimNetworkId = "cimNetworkId";

		/// <summary>
		/// The default CIM JSON-LD network ID
		/// </summary>
		protected const string _cimJsonLdNetworkId = "cimJsonLdNetworkId";

		/// <summary>
		/// The default JSON session ID
		/// </summary>
		protected const string _jsonSessionId = "jsonSessionId";

		/// <summary>
		/// The default CIM session ID
		/// </summary>
		protected const string _cimSessionId = "cimSessionId";

		/// <summary>
		/// The default CIM JSON-LD session ID
		/// </summary>
		protected const string _cimJsonLdSessionId = "cimJsonLdSessionId";

		protected static JObject _diginNetworkJsonLd = null!;

		protected static JObject _diginSshJsonLd = null!;

		[TestInitialize]
		public void Setup()
		{
			_diginNetworkJsonLd ??= JsonConvert.DeserializeObject<JObject>(File.ReadAllText(TestUtils.DiginCombinedNetworkFile))!;
			_diginSshJsonLd ??= JsonConvert.DeserializeObject<JObject>(File.ReadAllText(TestUtils.DiginCombinedSshFile))!;

			// Ensure we're not dependent on a specific locale
			Thread.CurrentThread.CurrentCulture = new[] { 
				new CultureInfo("nb-NO"), 
				new CultureInfo("en-GB") 
			}.RandomElement(new Random());
		}

		/// <summary>
		/// Adds a small network based on <see cref="JsonTestData"/> to the server in PGO's JSON format
		/// </summary>
		/// <param name="id">The network id</param>
		/// <param name="modify">If not null, this action is applied to the network data before
		///   giving it to the server</param>
		protected void AddJsonTestNetwork(string id = _jsonNetworkId, Action<PowerGrid>? modify = null)
		{
			PowerGrid jsonNetwork = JsonTestData().PowerGrid;

			modify?.Invoke(jsonNetwork);

			_server.AddNetwork(id, jsonNetwork);
		}

		/// <summary>
		/// Adds a small network to the server in CIM format
		/// </summary>
		/// <param name="id">The network id</param>
		/// <param name="modify">If not null, this action is applied to the network data before
		///   giving it to the server</param>
		protected void AddCimTestNetwork(string id = _cimNetworkId, Action<CimNetworkData>? modify = null)
		{
			CimBuilder b = new();

			var generatingUnit = b.AddGeneratingUnit("generator", 0, 100_000);
			var machine = b.AddSynchronousMachine("machine", generatingUnit, -10_000, 10_000, 100);
			var consumer = b.AddConsumer("consumer");
			var theSwitch = b.AddSwitch<Breaker>("switch", consumer.Terminals[0]);
			b.AddLine("line", 0.1, 0.1, machine.Terminals[0], theSwitch.Terminals[1]);

			CimNetworkData cimNetworkData = new() { Network = b.Network, ConversionOptions = new() };

			modify?.Invoke(cimNetworkData);

			_server.AddNetwork(id, cimNetworkData);
		}

		/// <summary>
		/// Adds a test network to the server in CIM JSON-LD format.
		/// 
		/// The network could be made smaller, but for now, it's the DIGIN network
		/// </summary>
		/// <param name="id">The network id</param>
		protected void AddCimJsonLdTestNetwork(string id = _cimJsonLdNetworkId)
		{
			AddDiginNetwork(id);
		}

		/// <summary>
		/// Adds the network in the given builder to the server, using JSON format
		/// </summary>
		/// <param name="builder">The builder containing the network to add</param>
		/// <param name="id">The network id</param>
		protected void AddNetwork(NetworkBuilder builder, string id = _jsonNetworkId)
		{
			PowerGrid jsonNetwork = PgoJsonParser.ConvertToJson((PowerNetwork?)builder.Network);
			_server.AddNetwork(id, jsonNetwork);
		}

		/// <summary>
		/// Adds a network to the server, using JSON format, by giving the lines in <paramref name="specs"/>
		/// to a network builder
		/// </summary>
		protected void AddNetwork(params string[] specs)
		{
			var builder = new NetworkBuilder();

			foreach (var spec in specs)
				builder.Add(spec);

			AddNetwork(builder);
		}

		/// <summary>
		/// Adds the DIGIN network to the server, from JSON-LD data
		/// </summary>
		/// <param name="id">The network id</param>
		/// <param name="modify">If not null, this action is applied to the network data before
		///   giving it to the server</param>
		protected void AddDiginNetwork(string id = "digin", Action<JObject>? modify = null)
		{
			JObject networkJson = _diginNetworkJsonLd;
			if (modify != null)
			{
				networkJson = (JObject)_diginNetworkJsonLd.DeepClone();
				modify(networkJson);
			}

			var networkData = new CimJsonLdNetworkData
			{
				Network = networkJson,
				ParsingOptions = new() { UnitsProfile = CimUnitsProfile.Digin },
				ConversionOptions = new() { LineImpedanceScaleFactor = 1e-3 }
			};

			_server.AddNetwork(id, networkData);
		}

		/// <summary>
		/// Removes the network with the given ID
		/// </summary>
		protected void RemoveNetwork(string id)
		{
			_server.RemoveNetwork(id);
		}

		/// <summary>
		/// Adds a JSON session to the server, suitable for the <see cref="AddJsonTestNetwork"/> network.
		/// </summary>
		/// <param name="id">The session ID</param>
		/// <param name="networkId">The ID of the network to use</param>
		/// <param name="modify">If not null, this action is applied to the session data before
		///   giving it to the server</param>
		/// <param name="omitStartConfig">If true, no start configuration is used in the session</param>
		/// <returns>The new session</returns>
		protected ISession AddJsonTestSession(string id = _jsonSessionId, string networkId = _jsonNetworkId, 
			Action<Demand, SinglePeriodSettings?>? modify = null, bool omitStartConfig = false)
		{
			var (_, demand, singlePeriodSettings) = JsonTestData();

			SinglePeriodSettings? startConfig = null;
			if (!omitStartConfig)
			{
				startConfig = singlePeriodSettings;
			}

			modify?.Invoke(demand, startConfig);

			return AddJsonSession(id, networkId, demand, startConfig);
		}

		/// <summary>
		/// Adds a JSON session to the server
		/// </summary>
		/// <param name="id">The session ID</param>
		/// <param name="networkId">The ID of the network to use</param>
		/// <param name="demand">The demands to use</param>
		/// <param name="startConfig">The start configuration to use</param>
		/// <param name="allowUnspecifiedConsumerDemands">If true, consumer demands may be omitted</param>
		/// <returns>The new session</returns>
		protected ISession AddJsonSession(string id, string networkId, Demand demand, SinglePeriodSettings? startConfig = null,
			bool allowUnspecifiedConsumerDemands = false)
		{
			var parameters = new SessionParameters
			{
				StartConfiguration = startConfig,
				Demand = demand,
				AllowUnspecifiedConsumerDemands = allowUnspecifiedConsumerDemands,
				NetworkId = networkId
			};

			return _server.AddSession(id, parameters);
		}

		/// <summary>
		/// Adds a CIM session to the server, suitable for the <see cref="AddCimTestNetwork"/> network.
		/// </summary>
		/// <param name="id">The session ID</param>
		/// <param name="networkId">The ID of the network to use</param>
		/// <param name="modify">If not null, this action is applied to the session data before
		///   giving it to the server</param>
		/// <param name="omitStartConfig">If true, no start configuration is used in the session</param>
		/// <returns>The new session</returns>
		protected ISession AddCimTestSession(string id = _cimSessionId, string networkId = _cimNetworkId, 
			Action<CimSessionParameters>? modify = null, bool omitStartConfig = false)
		{
			var cimData = CimTestData();

			CimPeriodAndDemands demands = new()
			{
				Period = TestUtils.DefaultPeriod,
				Demands = cimData.Demands
			};

			CimSessionParameters parameters = new()
			{
				NetworkId = networkId,
				PeriodsAndDemands = new() { demands }
			};

			if (!omitStartConfig)
			{
				parameters.StartConfiguration = cimData.Configuration;
			}

			modify?.Invoke(parameters);

			return _server.AddSession(id, parameters);
		}

		/// <summary>
		/// Adds a CIM JSON-LD session to the server, suitable for the <see cref="AddCimJsonLdTestNetwork"/> network.
		/// </summary>
		/// <param name="id">The session ID</param>
		/// <param name="networkId">The ID of the network to use</param>
		/// <param name="omitStartConfig">If true, no start configuration is used in the session</param>
		/// <returns>The new session</returns>
		protected ISession AddCimJsonLdTestSession(string id = _cimJsonLdSessionId, string networkId = _cimJsonLdNetworkId,
			bool omitStartConfig = false)
		{
			return AddDiginSession(id, networkId, omitStartConfig);
		}

		/// <summary>
		/// Adds a CIM JSON-LD session to the server, based on the DIGIN data
		/// </summary>
		/// <param name="id">The session ID</param>
		/// <param name="networkId">The ID of the network to use</param>
		/// <param name="omitStartConfig">If true, no start configuration is used in the session</param>
		/// <returns>The new session</returns>
		protected ISession AddDiginSession(string sessionId = "digin", string networkId = "digin", bool omitStartConfig = false)
		{
			var sessionParameters = new CimJsonLdSessionParameters
			{
				NetworkId = networkId,
				PeriodsAndDemands = new() {
					new() {
						Demands= _diginSshJsonLd,
						Period = TestUtils.DefaultPeriod
					}
				}
			};

			if (!omitStartConfig)
			{
				sessionParameters.StartConfiguration = _diginSshJsonLd;
			}


			return _server.AddSession(sessionId, sessionParameters);
		}

		/// <summary>
		/// Removes the given session from the server
		/// </summary>
		protected void RemoveSession(ISession session)
		{
			_server.RemoveSession(session);
		}

		/// <summary>
		/// Returns demands and a configuration suitable for <see cref="AddCimTestNetwork"/>
		/// </summary>
		protected static (CimDemands Demands, CimConfiguration Configuration) CimTestData()
		{
			CimBuilder b = new();

			var consumer = b.AddConsumer("consumer");
			consumer.P = ActivePower.FromWatts(100);
			consumer.Q = ReactivePower.FromVoltamperesReactive(10);

			var theSwitch = b.AddSwitch<Breaker>("switch");
			theSwitch.Open = true;

			return (b.Demands, b.Configuration);
		}

		/// <summary>
		/// Returns a set of JSON test data, based on <see cref="JsonTestNetworkBuilder"/>
		/// </summary>
		/// <returns></returns>
		protected static (PowerGrid PowerGrid, Demand Demand, SinglePeriodSettings SinglePeriodSettings) JsonTestData()
		{
			return TestDataFrom(JsonTestNetworkBuilder());
		}

		/// <summary>
		/// Extracts the grid, demadns, and settings from the given builder
		/// </summary>
		protected static (PowerGrid PowerGrid, Demand Demand, SinglePeriodSettings SinglePeriodSettings) TestDataFrom(NetworkBuilder builder)
		{
			var demand = PgoJsonParser.ConvertToJson(new[] { builder.PeriodData });
			var singlePeriodSettings = PgoJsonParser.CreateSinglePeriodSettingsContract(demand.Periods[0].Id, builder.Configuration.SwitchSettings);
			var powerGrid = PgoJsonParser.ConvertToJson((PowerNetwork?)builder.Network);

			return (powerGrid, demand, singlePeriodSettings);
		}

		/// <summary>
		/// Creates a builder with a simple test network
		/// </summary>
		protected static NetworkBuilder JsonTestNetworkBuilder()
		{
			NetworkBuilder b = new();
			b.Add("G[generatorVoltage=100] -- L1[open] -- C[consumption=(1,0)]");
			b.Add("G -- L2[closed] -- C");
			return b;
		}

		/// <summary>
		/// Adds a JSON network, session and solution from the data in the given builder
		/// </summary>
		/// <param name="solutionId">The ID to use for the solution added</param>
		/// <returns></returns>
		protected ISession AddNetworkAndSession(NetworkBuilder builder, string solutionId = "solution")
		{
			AddNetwork(builder, "net");

			var session = AddJsonSession(builder, "sessionId", "net");
			session.AddSolution(solutionId, SolutionFrom(builder));

			return session;
		}

		/// <summary>
		/// Adds a JSON session with demands from the given builder
		/// </summary>
		protected ISession AddJsonSession(NetworkBuilder builder, string sessionId, string networkId)
		{
			Demand demand = PgoJsonParser.ConvertToJson(new[] { builder.PeriodData });
			return AddJsonSession(sessionId, networkId, demand);
		}

		/// <summary>
		/// Creates a JSON single period solution from the switch settings of the given builder
		/// </summary>
		protected Solution SolutionFrom(NetworkBuilder builder)
		{
			var settings = TestDataFrom(builder).SinglePeriodSettings;
			return new Solution() { PeriodSettings = new() { settings } };
		}

		/// <summary>
		/// Runs optimization in the session until a good enough solution is found or
		/// the timeout expires
		/// </summary>
		/// <param name="session">The session to optimize in</param>
		/// <param name="timeoutSeconds">The timeout, in seconds</param>
		/// <param name="requiredObjectiveValue">If not null, optimization stops when the
		///   objective value is not greater than this.
		///   If null, optimization stops when a feasible solution is found.
		/// </param>
		protected void Optimize(ISession session, double timeoutSeconds = 1000,
			double? requiredObjectiveValue = null)
		{
			session.StartOptimization();

			Stopwatch sw = new Stopwatch();
			sw.Start();

			while (true)
			{
				var status = session.Status;
				if (requiredObjectiveValue == null && status.BestSolutionValue != null)
					break;
				if (status.BestSolutionValue <= requiredObjectiveValue)
					break;

				if (sw.Elapsed.TotalSeconds > timeoutSeconds)
					break;

				Thread.Sleep(10);
			}

			session.StopOptimization();
		}

		/// <summary>
		/// Verifies that the given session contains the given solution IDs
		/// </summary>
		protected void AssertSolutionIds(ISession session, string expected)
		{
			var status = session.Status;
			Assert.IsTrue(expected.Split(" ").SetEquals(status.SolutionIds));
		}
	}
}