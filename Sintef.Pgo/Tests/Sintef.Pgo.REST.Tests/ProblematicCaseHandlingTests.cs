using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;
using Xunit.Abstractions;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Sintef.Pgo.Core;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for how cases with varying problematic properties are handled by the service
	/// </summary>
	public class ProblematicCaseHandlingTests : LiveServerFixture
	{
		public ProblematicCaseHandlingTests(ITestOutputHelper output) : base(output) { }


		[Fact]
		public void FailedOptimizationIsReported_BusVoltageLimit()
		{
			// This problem has no feasible solution due to the (small) voltage drop
			// across Line1
			var builder = NetworkBuilder.Create(
				"Gen[generatorVoltage=100] -- Line1 -- Consumer[consumption=(1,0);vMinV=100]"
			);

			AssertInfeasibleSolution(builder, timeoutSeconds: 2, "Consumer bus voltage limits (Iterated DF)");
		}

		[Fact]
		public void FailedOptimizationIsReported_CurrentLimit()
		{
			// This problem has no feasible solution (under IteratedDistFlow) due to the current limit on Line2
			var builder = NetworkBuilder.Create(
				"Gen[generatorVoltage=100] -- Line1 -o- Line2[iMax=0.01] -- Consumer[consumption=(1,0)]"
			);

			var constraint = builder.SinglePeriodProblem.CriteriaSet.Constraints.OfType<LineCapacityCriterion>().SingleOrDefault();
			if (constraint == null)
				return; //So that the test still runs if we remove the constraint from the criteria set.


			AssertInfeasibleSolution(builder, timeoutSeconds: 2, "IMax violation (Iterated DF)");
		}

		[Fact]
		public void FailedOptimizationIsReported_LineVoltageLimit()
		{
			// This problem has no feasible solution due to the voltage limit on Line1
			var builder = NetworkBuilder.Create(
				"Gen[generatorVoltage=100] -- Line1[vMax=99] -- Consumer[consumption=(1,0)]"
			);

			AssertInfeasibleSolution(builder, timeoutSeconds: 2, "Line voltage limits (Iterated DF)");
		}

		[Fact]
		public void FailedOptimizationIsReported_ProviderCapacity()
		{
			// This problem has no feasible solution due to Gen1's capacity
			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100;generationCapacity=(1,0)] -- Line1 -- Consumer[consumption=(2,0)]",
				"Gen2[generatorVoltage=100;generationCapacity=(1,0)]"
			);

			AssertInfeasibleSolution(builder, timeoutSeconds: 2, "Provider capacity (Iterated DF)");
		}

		[Fact]
		public void FailedOptimizationIsReported_DivergingFlow()
		{
			// This problem has no feasible flow solution, and IteratedDistFlow diverges
			var builder = NetworkBuilder.Create(
				"Producer[generatorVoltage=1000.0] -- Line1[r=1350] -- Consumer1[consumption=(200, 0)]"
			);

			AssertInfeasibleSolution(builder, timeoutSeconds: 2, "Computing a consistent flow (Iterated DF)");
		}

		[Fact]
		public void SolutionWithParallelLinesIsFeasible()
		{
			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1a -- Node -- Line2a -- Consumer[consumption=(1,0)]",
				"Gen1 -- Line1b -- Node -- Line2b -- Consumer"
			);

			SetupSession(builder);

			Optimize();

			AssertHasFeasibleSolution();
		}

		[Fact]
		public void OptimizingWorksWithZeroDemandInUnconnectedConsumers()
		{
			// This network has disconnected consumers, with zero demand
			var builder = NetworkBuilder.Create(
				"Gen[generatorVoltage=100] -- Line1 -- Consumer[consumption=(1,0)]",
				"Consumer2[consumption=(0,0)] -- Line2 -- Consumer3[consumption=(0,0)]"
			);

			SetupOptimizeAndReport(builder);
		}

		[Fact]
		public void CreateSessionFailsWithDemandInUnconnectedConsumers()
		{
			CreateDefaultClient();

			// This network has disconnected consumers
			var builder = NetworkBuilder.Create(
				"Gen[generatorVoltage=100] -- Line1 -- Consumer[consumption=(1,0)]",
				"Consumer2[consumption=(1,0)] -- Line2 -- Consumer3[consumption=(1,0)]"
			);

			// Loading the network is fine
			Client.LoadNetworkFromBuilder(NetworkId, builder);

			// But adding a session with consumption for these consumers is illegal
			AssertException(() => Client.CreateJsonSession(SessionId, NetworkId, builder), "2 consumers have nonzero demand but cannot be connected to a provider:\n\t" +
				"Consumer2, Consumer3");
		}

		[Fact]
		public void CreateSessionFailsWithUnsatisfiableVoltageLimit()
		{
			var builder = NetworkBuilder.Create(
				"Gen[generatorVoltage=100] -- Line1 -- Consumer[consumption=(1,0);vMinV=151]"
			);

			AssertException(() => SetupSession(builder), "Some consumers have voltage limits far outside the range [100,100] for providers:\n\t" +
				"Consumer: [151,10000000000]");
		}

		[Fact]
		public void CreateSessionFailsWithUnsatisfiableDemand()
		{
			var builder = NetworkBuilder.Create(
				"Gen[generatorVoltage=100;generationCapacity=(100,0)] -- Line1 -- Consumer[consumption=(101,0)]"
			);

			AssertException(() => SetupSession(builder), "The total demand <101; 0> > total generation capacity <100; 0>");
		}

		[Fact]
		public void CreateSessionFailsWithUdeliverableDemand()
		{
			var builder = NetworkBuilder.Create(
				"Gen[generatorVoltage=100] -- Line1[iMax=0.9] -- Consumer[consumption=(100,0)]"
			);

			AssertException(() => SetupSession(builder), "The total demand 100 cannot be delivered through the output lines from the providers, which can carry a maximum total power of 90");
		}

		[Fact]
		public void CreateSessionFailsWhenConnectivityCheckFails()
		{
			CreateDefaultClient();

			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1 -- Node -- Line2 -- Gen2[generatorVoltage=100]"
			);

			AssertException(() => SetupSession(builder), "network connectivity check failed");
		}

		[Fact]
		public void ExcessiveResistanceDistflowConvergenceFailureMessage()
		{
			CreateDefaultClient();

			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1[r=1000] -- Consumer[consumption=(100,0)]"
			);

			Client.LoadNetworkFromBuilder(NetworkId, builder);
			Client.CreateJsonSession(SessionId, NetworkId, builder);
			Optimize(timeoutSeconds: 1);
			var solutionInfo = Client.GetBestSolutionInfo(SessionId);
			Assert.Contains(solutionInfo.ViolatedConstraints, c =>
				c.Name == "Computing a consistent flow (Iterated DF)" &&
				c.Description == "Period 0: IteratedDistFlow diverged; line voltage drop exceeds upstream voltage (1 iterations)."
			);
		}

		[Fact]
		public void BugInDisaggregationIsFixed()
		{
			// This test caused an error in r1182 due to a bug in selecting the upstream node when disaggregating flows

			string dataDir = Path.Combine(Utils.TestDataDir, "OPFS-354");

			CreateDefaultClient(Path.Combine(dataDir, "Network.json"));
			Client.CreateJsonSession(SessionId, NetworkId, Path.Combine(dataDir, "Forecast.json"));
			Client.LoadSolutionFromFile(SessionId, "solution", Path.Combine(dataDir, "Solution.json"));
			var solution = Client.GetSolution(SessionId, "solution");
		}


		[Fact]
		public void BugInSolutionIsFixed()
		{
			// This test caused an error r1188. When given a user-supplied solutions that were not connected,
			// the flow values were not set for the unconnected component, causing an exception in the serialization to JSON.

			foreach (var solutionFilename in new[] { "OPFS-368-crafted-solution.json", "OPFS-368-crafted-solution_allclosed.json", "OPFS-368-crafted-solution_allopen.json" })
			{
				string dataDir = Path.Combine(Utils.TestDataDir, "OPFS-368");
				CreateDefaultClient(Path.Combine(dataDir, "ieee34.json"));
				Client.CreateJsonSession(SessionId, NetworkId, Path.Combine(dataDir, "ieee34_forecast.json"));
				Client.LoadSolutionFromFile(SessionId, "solution", Path.Combine(dataDir, solutionFilename));
				var solution = Client.GetSolution(SessionId, "solution");
				Client.DeleteNetwork(NetworkId);
			}
		}


		/// <summary>
		/// Optimizes for the data in the builder and verifies that the best solution is infeasible,
		/// due to the specified constraint
		/// </summary>
		private void AssertInfeasibleSolution(NetworkBuilder builder, int timeoutSeconds, string infeasibleConstraint)
		{
			SetupSession(builder);

			Optimize(timeoutSeconds: timeoutSeconds);

			var status = Client.GetSessionStatus(SessionId);
			Assert.Null(status.BestSolutionValue);
			Assert.NotNull(status.BestInfeasibleSolutionValue);

			var info = Client.GetBestSolutionInfo(SessionId);

			Assert.False(info.IsFeasible);
			Assert.Equal(infeasibleConstraint, info.ViolatedConstraints.Single().Name);
		}
	}
}
