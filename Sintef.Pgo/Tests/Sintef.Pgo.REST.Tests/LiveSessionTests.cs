using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Linq;
using System;
using System.Threading.Tasks;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Server;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for session management (using the real service)
	/// </summary>
	public class LiveSessionTests : LiveServerFixture
	{
		public LiveSessionTests(ITestOutputHelper output) : base(output) { }

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
	}
}
