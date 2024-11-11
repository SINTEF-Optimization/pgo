using System.IO;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Sintef.Pgo.REST.Tests.Utils;
using System;
using Xunit.Abstractions;
using Sintef.Pgo.REST.Client;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using Sintef.Scoop.Utilities;
using System.Threading.Tasks;
using System.Net;
using Newtonsoft.Json;
using System.Collections.Generic;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.Test;
using Sintef.Pgo.Server;

// xUnits method of running tests in parallel freezes. When a test makes a call to the service, the current
// thread blocks until the call returns. If enough tests do this in parallel, no free threads remain for the server to service
// the request.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Fixture for testing the REST API backed by a real service
	/// </summary>
	public class LiveServerFixture : IClassFixture<WebApplicationFactory<Startup>>
	{
		/// <summary>
		/// A default session ID
		/// </summary>
		public string SessionId { get; } = "mySession";

		/// <summary>
		/// A default network ID
		/// </summary>
		public string NetworkId { get; } = "myNetwork";

		/// <summary>
		/// The default client created by <see cref="CreateDefaultClient"/>.
		/// </summary>
		public PgoRestClient Client { get; private set; }

		/// <summary>
		/// The default session type for <see cref="CreateClient"/>
		/// </summary>
		protected SessionType DefaultSessionType { get; set; } = SessionType.Json;

		/// <summary>
		/// The web application (and client) factory
		/// </summary>
		private WebApplicationFactory<Startup> _factory;

		/// <summary>
		/// Sends a string to the test output
		/// </summary>
		protected Action<string> WriteLine { get; set; }

		public LiveServerFixture(ITestOutputHelper output)
		{
			_factory = new();

			WriteLine = s => output.WriteLine(s);
		}

		/// <summary>
		/// An example network file, for the modified Baran-Wu case
		/// </summary>
		protected static string BaranWuModifiedNetworkFile => TestUtils.TestDataFile("baran-wu-modified.json");

		/// <summary>
		/// Demands for the modified Baran-Wu case
		/// </summary>
		protected static string BaranWuModifiedDemandsFile => TestUtils.TestDataFile("baran-wu-modified_forecast.json");

		/// <summary>
		/// Initial network configuration for the modified Baran-Wu case
		/// </summary>
		protected static string BaranWuModifiedConfigurationFile => TestUtils.TestDataFile("baran-wu-modified_startconfig.json");

		/// <summary>
		/// Reinitializes the web application
		/// </summary>
		/// <param name="multiUser">If true, creates a multi-user server.
		///   If false, there is one common user.</param>
		/// <param name="demoQuotas">If true, default user quotas are for the demo web app.
		///   If false, default user quotas are unlimited.</param>
		protected void Reinitialize(bool multiUser = false, bool demoQuotas = false)
		{
			_factory = new();

			if (multiUser)
				_factory = _factory.WithWebHostBuilder(b => b.UseSetting("PgoMultiUser", "True"));

			if (demoQuotas)
				_factory = _factory.WithWebHostBuilder(b => b.UseSetting("PgoDefaultQuotas", "ForDemo"));

			Client = null;
		}

		/// <summary>
		/// Creates the default client.
		/// </summary>
		/// <param name="networkToLoad">If not null, loads a network from this filename</param>
		protected void CreateDefaultClient(string networkToLoad = null)
		{
			Client = CreateClient();

			if (networkToLoad != null)
				Client.LoadNetworkFromFile(NetworkId, networkToLoad);
		}

		/// <summary>
		/// Adds the DIGIN network to the server
		/// </summary>
		protected void LoadDiginNetwork()
		{
			var parsingOptions = new CimParsingOptions { UnitsProfile = CimUnitsProfile.Digin };

			// The impedance scale seems wrong -- correct for this.
			var conversionOptions = new CimNetworkConversionOptions
			{
				LineImpedanceScaleFactor = 0.001
			};

			Client.LoadNetworkFromFile(NetworkId, TestUtils.DiginCombinedNetworkFile, parsingOptions, conversionOptions);
		}

		/// <summary>
		/// Creates a client for interacting with the server
		/// </summary>
		/// <param name="userId">The ID of the user the client represents.
		///   If null, use the ID assigned by the server at the first request</param>
		/// <param name="type">The type of sessions to use (<see cref="DefaultSessionType"/> if null)</param>
		/// <param name="overrideDefaultQuotas">If not null, this function is used to override the user's default quotas</param>
		protected PgoRestClient CreateClient(string userId = null, SessionType? type = null, Func<UserQuotas, UserQuotas> overrideDefaultQuotas = null)
		{
			if (overrideDefaultQuotas != null)
			{
				var quotaProvider = _factory.Services.GetService<IUserQoutaProvider>();

				var quotas = quotaProvider.QuotasFor(userId);

				quotas = overrideDefaultQuotas(quotas);

				quotaProvider.SetQuotasFor(userId, quotas);
			}

			var httpClient = _factory.CreateClient();
			return new PgoRestClient(type ?? DefaultSessionType, httpClient, userId);
		}

		/// <summary>
		/// Adds a standard session based on Modified Baran Wu (for a json client)
		/// or DIGIN (for a CIM client)
		/// </summary>
		protected void CreateStandardSession(bool includeStartConfig = true)
		{
			switch (Client.SessionType)
			{
				case SessionType.Json:
					Client.CreateJsonSession(SessionId, NetworkId, BaranWuModifiedDemandsFile,
						includeStartConfig ? BaranWuModifiedConfigurationFile : null);
					break;

				case SessionType.Cim:
					Client.CreateCimSession(SessionId, NetworkId, TestUtils.DiginCombinedSshFile,
						includeStartConfig ? TestUtils.DiginCombinedSshFile : null);
					break;

				default:
					throw new NotImplementedException();
			}
		}

		/// <summary>
		/// Sets up the server with standard network and session based on Modified Baran Wu (for a json client)
		/// or DIGIN (for a CIM client)
		/// </summary>
		protected void SetupStandardSession(bool includeStartConfig = true)
		{
			switch (DefaultSessionType)
			{
				case SessionType.Json:
					CreateDefaultClient(BaranWuModifiedNetworkFile);
					break;

				case SessionType.Cim:
					CreateDefaultClient();
					LoadDiginNetwork();
					break;

				default:
					throw new NotImplementedException();
			}
				
			CreateStandardSession(includeStartConfig);
		}

		/// <summary>
		/// Sets up the server with the network from the given builder, and
		/// adds a single-period session with demands and configuration from the builder.
		/// </summary>
		/// <param name="client">The client to create the session for. If null, uses the default client.</param>
		protected void SetupSession(NetworkBuilder builder, PgoRestClient client = null)
		{
			CreateDefaultClient();

			client ??= Client;

			client.LoadNetworkFromBuilder(NetworkId, builder);

			client.CreateJsonSession(SessionId, NetworkId, builder);
		}

		/// <summary>
		/// Asserts that invoking the Action throws an exception
		/// </summary>
		/// <param name="action">The action to execute</param>
		/// <param name="requiredInMessage">If not null, this string must be contained in the exception message</param>
		/// <param name="requiredMessage">If not null, this string must equal exception message</param>
		/// <param name="requiredType">If not null, the exception thrown must be of this type</param>
		/// <param name="requiredCode">If not null, the exception must be of type <see cref="HttpCodeException"/>,
		///   with this status code</param>
		public static void AssertException(Action action, string requiredInMessage = null, 
			string requiredMessage = null, Type requiredType = null, 
			HttpStatusCode? requiredCode = null)
		{
			try
			{
				action.Invoke();
			}
			catch (Exception ex)
			{
				if (requiredCode != null) {
					Assert.IsType<HttpCodeException>(ex);
					Assert.Equal(requiredCode.Value, ((HttpCodeException)ex).StatusCode);
				}

				if (requiredType != null)
					Assert.Equal(requiredType, ex.GetType());
				if (requiredMessage != null)
					Assert.Equal(requiredMessage, ex.Message);
				if (requiredInMessage != null)
					Assert.Contains(requiredInMessage, ex.Message);

				Console.WriteLine($"Exception message: {ex.Message}");

				// Success
				return;
			}
			Assert.Fail("Expected an exception but did not get one");
		}

		/// <summary>
		/// Verifies that the default session has a feasible solution
		/// </summary>
		protected void AssertHasFeasibleSolution()
		{
			SessionStatus sessionStatus = SessionStatus();

			Assert.NotNull(sessionStatus.BestSolutionValue);
			Assert.NotEqual(0, sessionStatus.BestSolutionValue);

			var solutionInfo = Client.GetBestSolutionInfo(SessionId);
			Assert.True(solutionInfo.IsFeasible);
			Assert.True((solutionInfo.ViolatedConstraints?.Count ?? 0) == 0);
		}

		/// <summary>
		/// Verifies that the service contains a specific set of sessions, and their networks.
		/// </summary>
		/// <param name="expected">A string in format "sessionID:networkId sessionId2:networkId2 ..."</param>
		protected void AssertSessions(string expected)
		{
			var elements = Client.GetServerStatus().Sessions
				.Select(s => $"{s.Id}:{s.NetworkId}")
				.OrderBy(s => s);

			var found = String.Join(" ", elements);

			Assert.Equal(expected, found);
		}

		/// <summary>
		/// Writes information on the session's best solution
		/// </summary>
		/// <param name="sessionId"></param>
		public void ReportSolution(string sessionId)
		{
			SessionStatus sessionStatus = SessionStatus(sessionId);

			WriteLine($"Optimizing: {sessionStatus.OptimizationIsRunning}");
			WriteLine($"Solution value: {sessionStatus.BestSolutionValue}");

			var bestSolution = Client.GetBestSolution(sessionId);

			ReportFlows(bestSolution.Flows[0]);
		}

		/// <summary>
		/// Returns the status of the given (or standard) session
		/// </summary>
		protected SessionStatus SessionStatus(string sessionId = null) => Client.GetSessionStatus(sessionId ?? SessionId);

		/// <summary>
		/// Writes the given flow
		/// </summary>
		private void ReportFlows(PowerFlow powerFlow)
		{
			WriteLine($"Period: {powerFlow.PeriodId}");

			foreach (var (bus, voltage) in powerFlow.Voltages)
			{
				WriteLine($"  {bus}: {Format(voltage * 1000, "V")}");
			}

			foreach (var (fromNode, currents) in powerFlow.Currents)
			{
				foreach (var current in currents)
				{
					WriteLine($"  {fromNode} -> {current.Target}: {Format(current.Current * 1000, "A")}");
				}
			}

			foreach (var (fromNode, powers) in powerFlow.Powers)
			{
				foreach (var power in powers)
				{
					WriteLine($"  {fromNode} -> {power.Target}: {Format(power.ActivePower * 1e6, "W")}+{Format(power.ReactivePower * 1e6, "VAR")}");
				}
			}

			string Format(double value, string unit)
			{
				string prefix = "";
				var absval = Math.Abs(value);
				if (absval > 1e6)
				{
					prefix = "M";
					value /= 1e6;
				}
				else if (absval > 1e3)
				{
					prefix = "k";
					value /= 1e3;
				}
				else if (value == 0)
				{
					prefix = "";
				}
				else if (absval < 1e-4)
				{
					prefix = "u";
					value *= 1e6;
				}
				else if (absval < 1e-1)
				{
					prefix = "m";
					value *= 1000;
				}
				return $"{value:G10} {prefix}{unit}";
			}
		}

		/// <summary>
		/// Runs optimization in the default session until a good enough solution is found or
		/// the timeout expires
		/// </summary>
		/// <param name="client">The client to use. If null, uses <see cref="Client"/></param>
		/// <param name="timeoutSeconds">The timeout, in seconds</param>
		/// <param name="requiredObjectiveValue">If not null, optimization stops when the
		///   objective value is not greater than this.
		///   If null, optimization stops when a feasible solution is found.
		/// </param>
		protected void Optimize(PgoRestClient client = null, double timeoutSeconds = 1000,
			double? requiredObjectiveValue = null)
		{
			client ??= Client;

			client.StartOptimizing(SessionId);

			Stopwatch sw = new Stopwatch();
			sw.Start();

			while (true)
			{
				var status = client.GetSessionStatus(SessionId);
				if (requiredObjectiveValue == null && status.BestSolutionValue != null)
					break;
				if (status.BestSolutionValue <= requiredObjectiveValue)
					break;

				if (sw.Elapsed.TotalSeconds > timeoutSeconds)
					break;

				Thread.Sleep(10);
			}

			client.StopOptimizing(SessionId);
		}

		/// <summary>
		/// Updates the interval with which the <see cref="SessionCleanerUpper"/>
		/// performs its cleanup
		/// </summary>
		/// <param name="interval">The new interval</param>
		protected void SetCleanupInterval(TimeSpan interval)
		{
			var cleanerUpper = _factory.Services.GetService<SessionCleanerUpper>();
			cleanerUpper.Interval = interval;
		}

		/// <summary>
		/// Returns a task that completes when optimization stops for the given client and session
		/// </summary>
		protected Task<object> Stopped(PgoRestClient client, string sessionId)
		{
			var stopped = new TaskCompletionSource<object>();
			var session = (Session)ServerFor(client).GetSession(sessionId);
			session.OptimizationStopped += (s, e) => { stopped.SetResult(null); };
			return stopped.Task;
		}

		/// <summary>
		/// Creates a solution with all switches either open or all swiches closed,
		/// for the given builder's problem
		/// </summary>
		protected Solution AllEqualSettings(NetworkBuilder builder, bool open)
		{
			var switchSettings = builder.Network.SwitchableLines
				.Select(l => new SwitchState { Id = l.Name, Open = open })
				.ToList();

			return new Solution
			{
				PeriodSettings = new[]{ new SinglePeriodSettings {
					Period =  builder.PeriodData.Name,
					SwitchSettings = switchSettings
				} }.ToList()
			};
		}

		/// <summary>
		/// Returns a JSON Stream containing an object loaded from the given JSON file and modified by the given function.
		/// </summary>
		protected MemoryStream FileToModifiedJsonStream<T>(string filename, Action<T> modify)
		{
			var obj = JsonConvert.DeserializeObject<T>(File.ReadAllText(filename));
			modify(obj);
			return JsonConvert.SerializeObject(obj).ToMemoryStream();
		}

		/// <summary>
		/// Asserts that the sequences contain the same elements (in any order)
		/// </summary>
		public static void AssertEquivalent<T>(IEnumerable<T> expected, IEnumerable<T> actual)
			=> AssertEquivalent(expected.ToList(), actual.ToList());

		/// <summary>
		/// Asserts that the collections contain the same elements (in any order)
		/// </summary>
		public static void AssertEquivalent<T>(ICollection<T> expected, ICollection<T> actual)
		{
			HashSet<T> expectedSet = expected.ToHashSet();
			HashSet<T> actualSet = actual.ToHashSet();
			Assert.True(expectedSet.SetEquals(actualSet));

			if (expected.Count != expectedSet.Count || actual.Count != actualSet.Count)
			{
				var expectedCounts = expected.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
				var actualCounts = actual.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
				Assert.True(expectedCounts.SetEquals(actualCounts));
			}
		}

		protected void SetupOptimizeAndReport(NetworkBuilder builder)
		{
			SetupSession(builder);

			Optimize();

			AssertHasFeasibleSolution();
			ReportSolution(SessionId);
		}

		/// <summary>
		/// Returns the (internal) server that services the given client
		/// </summary>
		protected IServer ServerFor(PgoRestClient client)
		{
			var sessionType = client.SessionType switch
			{
				SessionType.Cim => Server.Server.SessionType.Cim,
				SessionType.Json => Server.Server.SessionType.Json,
				_ => throw new NotImplementedException()
			};

			var server = _factory.Services.GetService<IMultiUserServer>();
			return server.ServerFor(client.UserId, sessionType);
		}

		/// <summary>
		/// Verifies that the default sessions contains the given solution IDs
		/// ('best' included).
		/// </summary>
		protected void AssertSolutionIdsAre(params string[] ids)
		{
			var status = SessionStatus();
			Assert.Equal(ids.OrderBy(x => x), status.SolutionIds.OrderBy(x => x));
		}
	}
}
