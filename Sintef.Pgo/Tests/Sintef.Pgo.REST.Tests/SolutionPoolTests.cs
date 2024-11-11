using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Linq;
using System;
using System.Threading;
using Sintef.Pgo.DataContracts;
using API = Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for the session's solution pool
	/// </summary>
	public class SolutionPoolTests : LiveServerFixture
	{
		private const string _solutionId = "new solution";

		public SolutionPoolTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void SolutionPoolStartsWithOnlyBest()
		{
			SetupStandardSession();

			AssertSolutionIdsAre("best");
		}

		[Fact]
		public void ASolutionCanBeAddedToThePool()
		{
			SetupStandardSession();

			Client.AddSolution(SessionId, _solutionId, CreateASolution());

			AssertSolutionIdsAre("best", _solutionId);
		}

		[Fact]
		public void BestSolutionCannotBeAdded()
		{
			SetupStandardSession();
			Solution newSolution = CreateASolution();

			AssertException(() => Client.AddSolution(SessionId, "best", newSolution), "The solution ID 'best' is reserved for the best known solution");
			AssertException(() => Client.RepairSolution(SessionId, "best", "best"), "The solution ID 'best' is reserved for the best known solution");
		}

		[Fact]
		public void ASolutionWithTheSameIdCannotBeAdded()
		{
			SetupStandardSession();

			Client.AddSolution(SessionId, _solutionId, CreateASolution());

			AssertException(() => Client.AddSolution(SessionId, _solutionId, CreateASolution()), "There is already a solution with ID 'new solution'");
			AssertException(() => Client.RepairSolution(SessionId, _solutionId, _solutionId), "There is already a solution with ID 'new solution'");
		}

		[Fact]
		public void SolutionInPoolCanBeRetrievedAfterAdding()
		{
			SetupStandardSession();
			AddASolution();

			Solution solution = Client.GetSolution(SessionId, _solutionId);

			AssertPlausibleSolution(solution);
		}

		[Fact]
		public void SolutionInPoolCanBeRemoved()
		{
			SetupStandardSession();
			AddASolution();

			AssertSolutionIdsAre("best", _solutionId);

			Client.RemoveSolution(SessionId, _solutionId);

			AssertSolutionIdsAre("best");
		}

		[Fact]
		public void SolutionInPoolCanBeUpdated()
		{
			SetupStandardSession();

			// Add a solution with all switches closed
			var solution = CreateASolution();
			solution.PeriodSettings
				.First()
				.SwitchSettings
				.ForEach(s => s.Open = false);
			Client.AddSolution(SessionId, _solutionId, solution);

			// Open all switches and update
			solution.PeriodSettings
				.First()
				.SwitchSettings
				.ForEach(s => s.Open = true);
			Client.UpdateSolution(SessionId, _solutionId, solution);

			var updatedSolution = Client.GetSolution(SessionId, _solutionId);
			var switchSettings = updatedSolution.PeriodSettings
				.First()
				.SwitchSettings;
			
			Assert.All(switchSettings, s => Assert.True(s.Open));
		}

		[Fact]
		public void ErrorInUpdatingSolutionIsReported()
		{
			SetupStandardSession();

			// Add a solution with all switches closed
			var solution = CreateASolution();
			solution.PeriodSettings[0].SwitchSettings
				.ForEach(s => s.Open = false);
			Client.AddSolution(SessionId, _solutionId, solution);

			// Make an invalid solution and update
			solution.PeriodSettings[0].SwitchSettings[0].Id = "???";

			AssertException(() => Client.UpdateSolution(SessionId, _solutionId, solution),
				requiredMessage: "An error occurred while evaluating the solution: Period 'Day 1': Settings are given for unknown line IDs: ???",
				requiredCode: System.Net.HttpStatusCode.BadRequest);
		}

		[Fact]
		public void BestSolutionCannotBeRemoved()
		{
			SetupStandardSession();
			Optimize();

			AssertException(() => Client.RemoveSolution(SessionId, "best"), "Cannot remove the 'best' solution");
		}

		[Fact]
		public void AccessingNonexistentSolutionFails()
		{
			SetupStandardSession();

			AssertException(() => Client.GetSolution(SessionId, _solutionId), "Not found");
			AssertException(() => Client.GetSolutionInfo(SessionId, _solutionId), "Not found");
			AssertException(() => Client.RepairSolution(SessionId, _solutionId, ""), "Not found");
			AssertException(() => Client.RemoveSolution(SessionId, _solutionId), "Not found");
		}

		[Fact]
		public void BestSolutionCannotBeUpdated()
		{
			SetupStandardSession();

			var bestSolution = Client.GetSolution(SessionId, "best");

			AssertException(() => Client.UpdateSolution(SessionId, "best", bestSolution), "can not be updated");
		}

		[Fact]
		public void BestSolutionCanBeRetrievedAfterOptimization()
		{
			SetupStandardSession(includeStartConfig: false);

			// When starting a session without a default configuration, the 
			// initial solution should have all-closed switch settings.
			Solution initialSolution = Client.GetSolution(SessionId, "best");
			Assert.True(initialSolution.PeriodSettings.All(
				p => p.SwitchSettings.All(s => !s.Open)));

			Optimize();

			Solution solution = Client.GetSolution(SessionId, "best");

			AssertPlausibleSolution(solution);
		}

		[Fact]
		public void InfoForSolutionInPoolCanBeRetrievedAfterAdding()
		{
			SetupStandardSession();
			AddASolution();

			SolutionInfo info = Client.GetSolutionInfo(SessionId, _solutionId);

			Assert.True(info.IsFeasible);
			Assert.Single(info.PeriodInformation);
		}

		/// <summary>
		/// Verifies that the given solution seems to contain a full single period solution
		/// for the standard (Modified Baran Wu) problem
		/// </summary>
		private static void AssertPlausibleSolution(Solution solution)
		{
			Assert.Single(solution.PeriodSettings);
			Assert.Equal("Day 1", solution.PeriodSettings.Single().Period);
			Assert.Equal(15, solution.PeriodSettings.Single().SwitchSettings.Count);

			Assert.Single(solution.Flows);
			Assert.Equal(API.FlowStatus.Exact, solution.Flows.Single().Status);
			Assert.True(solution.Flows.Single().Voltages.ContainsKey("22"));
		}

		private Solution CreateASolution()
		{
			Optimize();

			var bestSolution = Client.GetBestSolution(SessionId);
			return new Solution { PeriodSettings = bestSolution.PeriodSettings };
		}

		private void AddASolution()
		{
			Solution newSolution = CreateASolution();

			Client.AddSolution(SessionId, _solutionId, newSolution);
		}
	}
}
