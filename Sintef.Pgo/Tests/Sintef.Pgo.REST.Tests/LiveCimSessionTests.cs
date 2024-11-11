using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Linq;
using System.IO;
using System;
using System.Threading.Tasks;
using Moq;
using Sintef.Pgo.Cim;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.Core.Test;
using Sintef.Pgo.Server;
using Sintef.Pgo.REST.Client;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for CIM session management (using the real service).
	/// 
	/// Most tests are CIM variants of existing tests in 
	/// <see cref="LiveSessionTests"/> and <see cref="SolutionPoolTests"/>.
	/// </summary>
	public class LiveCimSessionTests : LiveServerFixture
	{
		public LiveCimSessionTests(ITestOutputHelper output) : base(output)
		{
			DefaultSessionType = SessionType.Cim;
		}

		[Fact]
		public void CreateSessionFailsForUnknownNetwork()
		{
			CreateDefaultClient();

			AssertException(() => CreateStandardSession(), $"No network with ID '{NetworkId}' exists.");
		}

		[Fact]
		public void CreateSessionSucceeds()
		{
			SetupStandardSession();

			Assert.Equal(SessionId, Client.GetServerStatus().Sessions.Single().Id);
		}

		[Fact]
		public void CreateSessionWithInvalidDataFails()
		{
			CreateDefaultClient();
			LoadDiginNetwork();

			string demandsText = File.ReadAllText(TestUtils.DiginCombinedSshFile)
				.Replace("\"cim:EnergyConsumer.p\": 0.005", "\"cim:EnergyConsumer.p\": -0.005");

			AssertException(() => Client.CreateCimSession(SessionId, NetworkId, new[] { demandsText }, TestUtils.DiginCombinedSshFile),
				requiredMessage: "An error occurred while parsing the session data: " +
					"Error in the demands: Period 'Period 1': " +
					"ConformLoad with MRID 'eba80fde-c5f8-49fc-8465-0329fdeefda9': " +
					"The active power for a consumer must be positive");
		}

		[Fact]
		public void CreateDuplicateSessionFails()
		{
			SetupStandardSession();

			AssertException(() => CreateStandardSession(), "A session with ID 'mySession' already exists");
		}

		[Fact]
		public void DeleteSessionSucceeds()
		{
			SetupStandardSession();

			Client.DeleteSession(SessionId);

			Assert.Empty(Client.GetServerStatus().Sessions);
		}

		[Fact]
		public void AccessingNonexistentSessionFails()
		{
			CreateDefaultClient();

			AssertException(() => Client.DeleteSession(SessionId), "Not found");
			AssertException(() => Client.StartOptimizing(SessionId), "Not found");
			AssertException(() => Client.StopOptimizing(SessionId), "Not found");
			AssertException(() => Client.GetSolution(SessionId, ""), "Not found");
			AssertException(() => Client.GetSolutionInfo(SessionId, ""), "Not found");
			AssertException(() => Client.GetBestSolution(SessionId), "Not found");
			AssertException(() => Client.GetBestSolutionInfo(SessionId), "Not found");
			AssertException(() => Client.SetObjectiveWeights(SessionId, new()), "Not found");
			AssertException(() => Client.GetObjectiveWeights(SessionId), "Not found");
			AssertException(() => Client.AddSolution(SessionId, "", (Solution)null), "Not found");
			AssertException(() => Client.RepairSolution(SessionId, "", ""), "Not found");
			AssertException(() => Client.RemoveSolution(SessionId, ""), "Not found");
		}

		[Fact]
		public void CimSessionIsNotVisibleInJsonAPI()
		{
			SetupStandardSession(includeStartConfig: false);
			var jsonClient = CreateClient(Client.UserId, SessionType.Json);

			Assert.Single(Client.GetServerStatus().Sessions);
			Assert.Empty(jsonClient.GetServerStatus().Sessions);
		}

		[Fact]
		public void JsonSessionIsNotVisibleInCimAPI()
		{
			CreateDefaultClient();

			var jsonClient = CreateClient(Client.UserId, SessionType.Json);
			jsonClient.LoadNetworkFromFile(NetworkId, BaranWuModifiedNetworkFile);
			jsonClient.CreateJsonSession(SessionId, NetworkId, BaranWuModifiedDemandsFile);

			Assert.Single(jsonClient.GetServerStatus().Sessions);
			Assert.Empty(Client.GetServerStatus().Sessions);
		}

		[Fact]
		public void DeletingASessionAlsoDeletesItsSolutions()
		{
			SetupStandardSession(includeStartConfig: false);

			var status = Client.GetSessionStatus(SessionId);
			var initialValue = status.BestSolutionValue;
			Assert.Null(initialValue);

			Optimize(Client, 1);

			status = Client.GetSessionStatus(SessionId);
			Assert.Equal(SessionId, status.Id);
			Assert.False(status.OptimizationIsRunning);
			Assert.NotNull(status.BestSolutionValue);
			Assert.True(status.BestSolutionValue != initialValue);

			// Delete the session
			Client.DeleteSession(SessionId);

			// Create a new session with the same Id
			CreateStandardSession(includeStartConfig: false);

			status = Client.GetSessionStatus(SessionId);
			Assert.NotNull(status);
			Assert.True(status.BestSolutionValue == initialValue);
		}

		[Fact]
		public void DeletingASessionStopsTheOptimizer()
		{
			SetupStandardSession(includeStartConfig: false);

			// Hook up tasks that will complete when optimization starts/stops
			var session = (Session)ServerFor(Client).GetSession(SessionId);

			var started = new TaskCompletionSource<object>();
			var stopped = new TaskCompletionSource<object>();
			session.OptimizationStarted += (s, e) => { started.SetResult(null); };
			session.OptimizationStopped += (s, e) => { stopped.SetResult(null); };

			Client.StartOptimizing(SessionId);

			// Verify that optimization started
			Assert.True(started.Task.Wait(TimeSpan.FromSeconds(5)), "Optimization did not start");

			var status = Client.GetSessionStatus(SessionId);
			Assert.True(status.OptimizationIsRunning);

			Client.DeleteSession(SessionId);

			// Verify that optimization stopped
			Assert.True(stopped.Task.Wait(TimeSpan.FromSeconds(5)), "Optimization did not stop");
		}

		[Fact]
		public void NewSessionHasStartConfigurationAsBestSolution()
		{
			SetupStandardSession();
			var status = Client.GetSessionStatus(SessionId);
			Assert.Equal(SessionId, status.Id);
			Assert.False(status.OptimizationIsRunning);
			Assert.True(status.BestSolutionValue.HasValue);
		}

		[Fact]
		public void OptimizationProducesASolution()
		{
			SetupStandardSession(includeStartConfig: false);

			// Since we gave no configuration, there is no initial feasible best solution
			var status = Client.GetSessionStatus(SessionId);
			Assert.False(status.BestSolutionValue.HasValue);

			Optimize();

			status = Client.GetSessionStatus(SessionId);
			Assert.True(status.BestSolutionValue.HasValue);

			// Also verify that the solution is exported OK

			var solution = Client.GetBestCimSolution(SessionId).PeriodSolutions.Single();

			CimJsonParser parser = new CimJsonParser(new Mock<ICimUnitsProfile>().Object);
			parser.Parse(solution);
			parser.CreateCimObjects();
			var switches = parser.CreatedObjects<Switch>();

			Assert.Equal(60, switches.Count());
			Assert.Equal(2, switches.Count(s => s.Open == true));
		}

		[Fact]
		public void MultiplePeriodSessionWorks()
		{
			CreateDefaultClient();
			LoadDiginNetwork();

			// Create session using the same demands in two periods
			var singlePeriodDemands = File.ReadAllText(TestUtils.DiginCombinedSshFile);
			var demands = new[] { singlePeriodDemands, singlePeriodDemands };

			Client.CreateCimSession(SessionId, NetworkId, demands);

			// Verify that we get a solution with two periods
			Optimize();

			var status = Client.GetSessionStatus(SessionId);
			Assert.True(status.BestSolutionValue.HasValue);

			var solution = Client.GetBestCimSolution(SessionId);
			Assert.Equal(2, solution.PeriodSolutions.Count);
		}
	}
}
