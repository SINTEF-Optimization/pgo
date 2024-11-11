using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Sintef.Pgo.Core;
using Sintef.Pgo.Server;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for optimizing
	/// </summary>
	public class OptimizationTests : LiveServerFixture
	{
		public OptimizationTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void OptimizationProducesASolution()
		{
			SetupStandardSession();

			Optimize();

			var status = Client.GetSessionStatus(SessionId);

			Assert.Equal(SessionId, status.Id);
			Assert.False(status.OptimizationIsRunning);
			Assert.NotNull(status.BestSolutionValue);
		}

		[Fact]
		public void StartingOrStoppingOptimizationTwiceIsOk()
		{
			SetupStandardSession();

			Client.StartOptimizing(SessionId);
			Assert.True(Client.GetSessionStatus(SessionId).OptimizationIsRunning);
			Client.StartOptimizing(SessionId);
			Assert.True(Client.GetSessionStatus(SessionId).OptimizationIsRunning);
			Client.StopOptimizing(SessionId);
			Assert.False(Client.GetSessionStatus(SessionId).OptimizationIsRunning);
			Client.StopOptimizing(SessionId);
			Assert.False(Client.GetSessionStatus(SessionId).OptimizationIsRunning);
		}


		[Fact]
		public void BestSolutionIsReturned()
		{
			SetupStandardSession();

			Optimize();

			var solution = Client.GetBestSolution(SessionId);

			// Any radial solution has exactly 6 open switches
			Assert.Equal(6, solution.PeriodSettings[0].SwitchSettings.Count(s => s.Open));
		}

		[Fact]
		public void OptimizationProducesAcceptableFlow()
		{
			SetupStandardSession();

			Optimize();

			var flow = Client.GetBestSolution(SessionId).Flows[0];

			// We were able to get the flow, now we do some basic checks

			Assert.Equal(12.66, flow.Voltages["1"]); // Root has a specific voltage
																							 // Solution should not have insane voltage drops, though stochasticity means we don't know which we get
			foreach (var voltage in flow.Voltages.Values)
			{
				Assert.InRange(voltage, 11.4, 12.66); // 12.66 * 0.9 = 11.394
			}
			// Total demand in this case is 3.715 MW and 2.3 MVAr. The generators are 
			// 1, connected to 2, and 34, connected to 17. Using Simplified DistFlow the power is conserved
			Assert.Single(flow.Powers["1"]);
			Assert.Single(flow.Powers["34"]);
			Assert.Equal(3.715, flow.Powers["1"][0].ActivePower + flow.Powers["34"][0].ActivePower, 0);
			Assert.Equal(2.3, flow.Powers["1"][0].ReactivePower + flow.Powers["34"][0].ReactivePower, 0);

			// More thorough tests elsewhere, but we can check that the current is also correct (nominal voltage is 12.66) for those two lines
			Assert.Single(flow.Currents["1"]);
			Assert.Single(flow.Currents["34"]);
			// We compute expected current from the above trustworthy numbers and verify that it has migrated correctly through the API
			// Low precision, this has been through a number of uncertainty sources
			var gen1Current = (new Complex(flow.Powers["1"][0].ActivePower, flow.Powers["1"][0].ReactivePower) / 12.66).Magnitude;
			Assert.Equal(gen1Current, flow.Currents["1"][0].Current, 2);

			var gen2Current = (new Complex(flow.Powers["34"][0].ActivePower, flow.Powers["34"][0].ReactivePower) / 12.66).Magnitude;
			Assert.Equal(gen2Current, flow.Currents["34"][0].Current, 2);
		}

		[Fact]
		public void OptimizationStartsFromBestSolution_IncludingInSolutionPool()
		{
			// First, run optimizer to get a solution
			SetupStandardSession(includeStartConfig: false);
			Optimize();
			var solution = Client.GetBestSolution(SessionId);
			double solutionValue = Client.GetSessionStatus(SessionId).BestSolutionValue.Value;


			// Reinitialize session
			Reinitialize();
			SetupStandardSession(includeStartConfig: false);

			// Add the solution to solution pool
			Client.AddSolution(SessionId, "solution", solution);

			// Verify that optimizer starts from this solution (by value)
			double startingValue = 0;
			var session = (Session)ServerFor(Client).GetSession(SessionId);
			session.OptimizationStarted += (s, e) => { startingValue = e.CriteriaSet.Objective.Value(e.Solution); };

			Optimize();

			Assert.Equal(solutionValue, startingValue);
		}

		[Fact]
		public void OptimizationReportsNewBestObjectiveValues()
		{
			SetupStandardSession(includeStartConfig: false); // Causes an infeasible initial best solution

			List<double> values = new List<double>();

			using (Client.ReceiveBestSolutionUpdates(SessionId,
				solutionStatus => values.Add(solutionStatus.ObjectiveValue)))
			{
				Optimize();

				// Wait to be sure the SignalR message has arrived
				Thread.Sleep(500);
			}

			Assert.NotEmpty(values);
			WriteLine($"Objective value: {values[0]}");
		}

		[Fact]
		public void OptimizingWithParallelLinesWorks()
		{
			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1a -- Node -- Line2a -- Consumer[consumption=(1,0)]",
				"Gen1 -- Line1b -- Node -- Line2b -- Consumer"
			);

			SetupOptimizeAndReport(builder);
		}
	}
}
