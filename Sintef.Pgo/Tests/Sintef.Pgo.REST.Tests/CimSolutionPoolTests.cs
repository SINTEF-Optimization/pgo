using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sintef.Pgo.Core.Test;
using Sintef.Pgo.REST.Client;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for the solution pool in a CIM session
	/// 
	/// These tests are CIM variants of existing tests in <see cref="SolutionPoolTests"/>.
	/// There are also tests in <see cref="SolutionPoolTests"/> that are not duplicated here,
	/// since the tested functionality does not depend on the session type.
	/// </summary>
	public class CimSolutionPoolTests : LiveServerFixture
	{
		private const string _solutionId = "new solution";

		public CimSolutionPoolTests(ITestOutputHelper output) : base(output)
		{
			DefaultSessionType = SessionType.Cim;
		}

		[Fact]
		public void BestSolutionInfoCanBeExtracted()
		{
			SetupStandardSession(includeStartConfig: false);

			var info = Client.GetSolutionInfo(SessionId, "best");

			// The default solution has all switches closed, leading to cycles
			Assert.False(info.IsFeasible);
			Assert.Contains(info.ViolatedConstraints, c => c.Description?.Contains("The configuration is not radial in periods: Period 1") ?? false);
		}

		[Fact]
		public void BestSolutionCanBeExtracted()
		{
			SetupStandardSession();

			var solution = Client.GetBestCimSolution(SessionId);

			TestUtils.AssertValidDiginSolution(solution);
		}

		[Fact]
		public void ASolutionCanBeAddedToThePool()
		{
			SetupStandardSession();

			var solution = Client.GetBestCimSolution(SessionId);

			Client.AddSolution(SessionId, _solutionId, solution);

			AssertSolutionIdsAre("best", _solutionId);
		}

		[Fact]
		public void SolutionInPoolCanBeRetrievedAfterAdding()
		{
			SetupStandardSession();
			var solution = Client.GetBestCimSolution(SessionId);
			Client.AddSolution(SessionId, _solutionId, solution);

			solution = Client.GetCimSolution(SessionId, _solutionId);

			TestUtils.AssertValidDiginSolution(solution);
		}

		[Fact]
		public void SolutionInPoolCanBeUpdatedAfterAdding()
		{
			SetupStandardSession();
			var solution = Client.GetBestCimSolution(SessionId);
			Client.AddSolution(SessionId, _solutionId, solution);
			
			Client.UpdateSolution(SessionId, _solutionId, solution);
		}

		[Fact]
		public void JsonErrorInAddedSolutionIsReported()
		{
			SetupStandardSession();

			string solution = Client.GetBestSolutionAsString(SessionId);

			// Introduce a Json syntax error in the added solution
			solution = solution.Replace("\"cim:Switch.open\":", "garbage");

			AssertException(() => Client.AddSolution(SessionId, _solutionId, solution),
				requiredInMessage: "Invalid JavaScript property identifier character",
				requiredCode: System.Net.HttpStatusCode.BadRequest);
		}

		[Fact]
		public void ErrorInAddedSolutionIsReported()
		{
			SetupStandardSession();

			string solution = Client.GetBestSolutionAsString(SessionId);

			// Introduce an error in the added solution: Change a switch UUID to an unknown value
			solution = solution.Replace("urn:uuid:81f3131f-baed-442d-bf7a-3606832fc3e0", "urn:uuid:81f3131f-baed-442d-bf7a-000000000000");

			AssertException(() => Client.AddSolution(SessionId, _solutionId, solution),
				requiredMessage: "An error occurred adding the solution: Period 'Period 1': Breaker with MRID '81f3131f-baed-442d-bf7a-000000000000': Not found in the network",
				requiredCode: System.Net.HttpStatusCode.BadRequest);
		}

	}
}
