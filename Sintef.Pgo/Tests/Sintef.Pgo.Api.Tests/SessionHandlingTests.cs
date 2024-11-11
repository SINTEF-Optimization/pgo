using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.Test;

namespace Sintef.Pgo.Api.Tests
{
	/// <summary>
	/// Tests for session handling functionality in the .NET API
	/// </summary>
	[TestClass]
	public class SessionHandlingTests : ApiTestFixture
	{
		[TestInitialize]
		public new void Setup()
		{
			base.Setup();

			AddJsonTestNetwork();
			AddCimTestNetwork(); 
			AddCimJsonLdTestNetwork();
		}

		[TestMethod]
		public void SessionsCanBeAddedAndRemoved()
		{
			var session1 = AddJsonTestSession(id: "id1");

			Assert.AreEqual("id1:jsonNetworkId", SessionIds);

			var session2 = AddCimTestSession(id: "id2");

			Assert.AreEqual("id1:jsonNetworkId id2:cimNetworkId", SessionIds);

			RemoveSession(session1);

			Assert.AreEqual("id2:cimNetworkId", SessionIds);

			RemoveSession(session2);

			Assert.AreEqual("", SessionIds);
		}

		[TestMethod]
		public void StartConfigIsOptional()
		{
			AddJsonTestSession(omitStartConfig: true);
			AddCimTestSession(omitStartConfig: true);
		}

		[TestMethod]
		public void CreateSessionWithUnknownNetworkFails()
		{
			TestUtils.AssertException(() => AddJsonTestSession("id", networkId: "???"), "There is no power network with ID '???'");
			TestUtils.AssertException(() => AddCimTestSession("id", networkId: "???"), "There is no power network with ID '???'");
			TestUtils.AssertException(() => AddCimJsonLdTestSession("id", networkId: "???"), "There is no power network with ID '???'");
		}

		[TestMethod]
		public void CreateSessionWithWrongNetworkTypeFails()
		{
			TestUtils.AssertException(() => AddJsonTestSession("id", _cimNetworkId), "Cannot create a non-CIM session for a CIM network");
			TestUtils.AssertException(() => AddCimTestSession("id", _jsonNetworkId), "Cannot create a CIM session for a non-CIM network");
			TestUtils.AssertException(() => AddCimJsonLdTestSession("id", _jsonNetworkId), "Cannot create a CIM session for a non-CIM network");
			TestUtils.AssertException(() => AddCimJsonLdTestSession("id", _cimNetworkId), "Cannot create a session from JSON-LD: URIs are unknown since the network was not created from JSON-LD data.");
		}

		[TestMethod]
		public void ErrorsInJsonDataAreReported()
		{
			TestUtils.AssertException(() => AddJsonTestSession(
				modify: (demand, startConfig) => { demand.Periods.Add(null); }),
				"Error in the demands: The list contains a null period");

			TestUtils.AssertException(() => AddJsonTestSession(
				modify: (demand, startConfig) => { startConfig!.SwitchSettings.Add(null); }),
				"Error in the start configuration: The list of switch settings contains null");

			// These are just spot checks -- see also:
			Action _ = new InputConsistencyTests_DotNetApi().ErrorsInJsonSessionDefinitionAreReported;
		}

		[TestMethod]
		public void ErrorsInCimDataAreReported()
		{
			TestUtils.AssertException(() => AddCimTestSession(
				modify: (parameters) => { parameters.PeriodsAndDemands[0].Period.Id = null; }),
				requiredMessage: "Error in the demands: A period ID (at index 0) is null or empty");

			TestUtils.AssertException(() => AddCimTestSession(
				modify: (parameters) => { parameters.PeriodsAndDemands[0].Demands.EquivalentInjections.Add(null); }),
				requiredMessage: "Error in the demands: Period 'Period 1': The list of equivalent injections contains null");

			TestUtils.AssertException(() => AddCimTestSession(
				modify: (parameters) => { parameters.StartConfiguration.Switches.Add(null); }),
				"Error in the start configuration: The list of switches contains null");

			// These are just spot checks -- see also:
			Action _ = () =>
			{
				new InputConsistencyTests_DotNetApi().ErrorsInCimSessionDefinitionAreReported();
				new CimInputConsistencyTests().ErrorsInDemandsDefinitionAreReported();
				new CimInputConsistencyTests().ErrorsInConfigurationDefinitionAreReported();
			};
		}

		[TestMethod]
		public void CreatingSessionForBadNetworkConnectivityFails()
		{
			NetworkBuilder b = new();
			b.Add("G1[generatorVoltage=100] -- L1 -- G2[generatorVoltage=100]");

			var (powerGrid, demand, _) = TestDataFrom(b);

			_server.AddNetwork("id", powerGrid);

			TestUtils.AssertException(() => _server.AddSession("id", new SessionParameters { NetworkId = "id", Demand = demand }),
				"Cannot create session: network connectivity check failed (see /api/networks/id/connectivityStatus or IServer.AnalyzeNetworkConnectivity())");
		}

		[TestMethod]
		public void AddingSessionWithSameIdFails()
		{
			AddJsonTestSession("id");

			TestUtils.AssertException(() => AddJsonTestSession("id"), "A session with ID 'id' already exists");
			TestUtils.AssertException(() => AddCimTestSession("id"), "A session with ID 'id' already exists");
			TestUtils.AssertException(() => AddCimJsonLdTestSession("id"), "A session with ID 'id' already exists");
		}

		[TestMethod]
		public void UsingARemovedSessionFails()
		{
			var session = AddJsonTestSession();
			var cimSession = AddCimTestSession();
			RemoveSession(session);
			RemoveSession(cimSession);

			TestUtils.AssertException(() => RemoveSession(session), "The session does not belong to this server");

			TestUtils.AssertException(() => _ = session.Status, "The session has been removed from the server and may not be used");
			TestUtils.AssertException(() => session.AddSolution("", new Solution()), "The session has been removed from the server and may not be used");
			TestUtils.AssertException(() => cimSession.AddSolution("", new CimSolution()), "The session has been removed from the server and may not be used");
			TestUtils.AssertException(() => session.UpdateSolution("", new Solution()), "The session has been removed from the server and may not be used");
			TestUtils.AssertException(() => cimSession.UpdateSolution("", new CimSolution()), "The session has been removed from the server and may not be used");
			TestUtils.AssertException(() => session.RepairSolution("", ""), "The session has been removed from the server and may not be used");
			TestUtils.AssertException(() => session.RemoveSolution(""), "The session has been removed from the server and may not be used");
			TestUtils.AssertException(() => session.GetJsonSolution(""), "The session has been removed from the server and may not be used");
			TestUtils.AssertException(() => cimSession.GetCimSolution(""), "The session has been removed from the server and may not be used");
			TestUtils.AssertException(() => session.GetSolutionInfo(""), "The session has been removed from the server and may not be used");

			//TestUtils.AssertException(() => session.StartOptimize(), "The session has been removed from the server and may not be used");

			// Also if we add a new session with the same ID
			var newSession = AddJsonTestSession();

			_ = newSession.Status;
			TestUtils.AssertException(() => _ = session.Status, "The session has been removed from the server and may not be used");
		}

		[TestMethod]
		public void SessionStatusWorks()
		{
			var session = AddJsonTestSession();
			var status = session.Status;

			Assert.AreEqual("jsonSessionId", status.Id);
			Assert.AreEqual("jsonNetworkId", status.NetworkId);
			Assert.AreEqual(1, status.SolutionIds.Count);
		}

		[TestMethod]
		public void NullArgumentsAreCaught_SessionFunctions()
		{
			// AddSession

			Demand demand = new();
			SinglePeriodSettings settings = new();

			TestUtils.AssertException(() => _server.AddSession("id", (SessionParameters)null!), "Value cannot be null. (Parameter 'parameters')");
			TestUtils.AssertException(() => AddJsonSession(null!, "id", demand, settings), "Value cannot be null. (Parameter 'sessionId')");
			TestUtils.AssertException(() => AddJsonSession("id", null!, demand, settings), "No network ID is given");
			TestUtils.AssertException(() => AddJsonSession("id", "id", null!, settings), "No demand is given");

			CimSessionParameters parameters = new();

			TestUtils.AssertException(() => _server.AddSession(null!, parameters), "Value cannot be null. (Parameter 'sessionId')");
			TestUtils.AssertException(() => _server.AddSession("id", (CimSessionParameters)null!), "Value cannot be null. (Parameter 'parameters')");

			CimJsonLdSessionParameters jsonLdParameters = new();

			TestUtils.AssertException(() => _server.AddSession(null!, jsonLdParameters), "Value cannot be null. (Parameter 'sessionId')");
			TestUtils.AssertException(() => _server.AddSession("id", (CimJsonLdSessionParameters)null!), "Value cannot be null. (Parameter 'parameters')");

			// RemoveSession

			TestUtils.AssertException(() => _server.RemoveSession(null!), "Value cannot be null. (Parameter 'session')");
		}
	}
}