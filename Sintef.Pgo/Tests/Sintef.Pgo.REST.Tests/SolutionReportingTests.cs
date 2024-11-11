using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Linq;
using System;
using System.Threading;
using System.Collections.Generic;
using Sintef.Pgo.DataContracts;
using API = Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests mainly for getting information about a solution (from the session's solution pool)
	/// </summary>
	public class SolutionReportingTests
	{
		private const string _solutionId = "new solution";

		private Sintef.Pgo.Server.ISession _session;

		[Fact]
		public void ReportedFlowIsExact()
		{
			// Because it's computed by IteratedDistFlow

			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1 -- Consumer[consumption=(1,0)]"
			);

			SetupSession(builder);
			var (solution, info) = SolutionAndInfo();

			// The objective value reflects the line power loss, which should
			// be correct for iterated dist flow
			Assert.InRange(info.ObjectiveValue, 0.00010002, 0.00010003);

			// The injected power should be slightly larger than 1W due
			// to the line power loss for iterated dist flow
			var injected = solution.Flows[0].Powers.Single().Value.Single();
			double activePowerW = injected.ActivePower * 1e6;
			Assert.InRange(activePowerW, 1.00001, 1.001);

			// The reported flow should be exact.
			Assert.Equal(API.FlowStatus.Exact, solution.Flows[0].Status);
		}

		[Fact]
		public void FlowIsComputedForSolutionWithParallelLines()
		{
			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1a -- Node -- Line2a -- Consumer[consumption=(1,0)]",
				"Gen1 -- Line1b -- Node -- Line2b -- Consumer"
			);

			SetupSession(builder);
			var (solution, info) = SolutionAndInfo();

			Assert.NotEqual(0, solution.Flows.Single().Currents.First().Value.First().Current);
		}

		[Fact]
		public void ZeroFlowIsReportedAsFlowingFromProducers()
		{
			// The 'natural' direction for Line is from Consumer to Gen1
			var builder = NetworkBuilder.Create("Consumer[consumption=(0,0)] -- Line -- Gen1[generatorVoltage=100]");

			SetupSession(builder);
			var (solution, info) = SolutionAndInfo();

			// but even though it carries zero current,
			var flow = solution.Flows.Single().Currents.Single();
			Assert.Equal(0, flow.Value.Single().Current);

			// it should be reported in direction from Gen1
			Assert.Equal("Gen1", flow.Key);
		}

		[Fact]
		public void CycleSolutionIsReportedWithNoFlow()
		{
			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1[closed] -- Consumer[consumption=(1,0)]",
				"Gen2[generatorVoltage=100] -- Line2 -- Consumer"
			);

			SetupSession(builder);
			var (solution, info) = SolutionAndInfo();

			Assert.False(info.IsFeasible);

			Assert.Contains(info.ViolatedConstraints,
				c => c.Description?.Contains("The configuration is not radial in periods: 0") ?? false);
			Assert.Empty(solution.Flows);
		}

		[Fact]
		public void DisconnectedSolutionIsReportedWithNoFlow()
		{
			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1[open] -- ConsumerX[consumption=(1,0)]"
			);

			SetupSession(builder);
			var (solution, info) = SolutionAndInfo();

			Assert.False(info.IsFeasible);
			Assert.Contains(info.ViolatedConstraints, c => c.Description.Contains("These consumers are unconnected and have a demand of 1.00 VA active power (100.00 % of total active power demand) in period 0: ConsumerX"));
		}

		[Fact]
		public void SolutionDoesNotNeedToBeStrictlyConnected()
		{
			var builder = NetworkBuilder.Create("Gen1[generatorVoltage=100] -- Line1[closed] -- X1 -- Line2[closed] -- Consumer[consumption=(1,0)]");
			builder.Add("Gen1 -- Line3[open] -- X2 -- Line4[open] -- Consumer");

			SetupSession(builder);
			var (solution, info) = SolutionAndInfo();

			Assert.True(info.IsFeasible);
		}

		[Fact]
		public void UnconnectedSolutionCanBeRepaired()
		{
			var builder = NetworkBuilder.Create("Gen1[generatorVoltage=100] -- Line1[open] -- X1 -- Line2[closed] -- Consumer[consumption=(1,0)]");
			builder.Add("Gen1 -- Line3[open] -- X2 -- Line4[closed] -- Consumer");

			SetupSession(builder);
			var (solution, info) = SolutionAndInfo();
			Assert.False(info.IsFeasible);

			var newId = "repaired";
			var repairMessage = _session.Repair(_session.GetSolution(_solutionId), newId);

			Assert.Equal("Repair was successful after opening 0 and closing 1 switches.", repairMessage);

			(solution, info) = SolutionAndInfo(newId);

			Assert.True(info.IsFeasible);
		}

		[Fact]
		public void SolutionNeedsDemandsToBeFulfilled()
		{
			var builder = NetworkBuilder.Create("Gen1[generatorVoltage=100] -- Line1[closed] -- X1 -- Line2[closed] -- Consumer[consumption=(1,0)]");
			builder.Add("Gen1 -- Line3[open] -- X2[consumption=(1,0)] -- Line4[open] -- Consumer");

			SetupSession(builder);
			var (solution, info) = SolutionAndInfo();

			Assert.False(info.IsFeasible);
			Assert.Contains(info.ViolatedConstraints, c => c.Description.Contains("These consumers are unconnected and have a demand of 1.00 VA active power (50.00 % of total active power demand) in period 0: X2"));
		}

		[Fact]
		public void SolutionCanHaveInvalidTransformerModeInDisconnectedComponent()
		{
			var builder = NetworkBuilder.Create("Gen1[generatorVoltage=100] -- Line1[closed] -- X1 -- Line2[closed] -- Consumer[consumption=(1,0)]");
			builder.Add("Gen1 -- Line3[open] -- B1 -- Line4[closed] -- B2");
			builder.Add("B3 -- Line6[closed] -- B4");
			var network = builder.Network;
			// B2->B3
			var t1 = network.AddTransformer(new[] { ("B2", 1.0), ("B3", 1.0) }, name: "T1");
			t1.AddMode("B2", "B3", TransformerOperationType.FixedRatio);

			// B2->B4
			var t2 = network.AddTransformer(new[] { ("B2", 1.0), ("B4", 1.0) }, name: "T2");
			t2.AddMode("B2", "B4", TransformerOperationType.FixedRatio);

			foreach (var invalidTransformerCycleIsDisconnected in new[] { true, false })
			{
				SetupSession(builder, isOpen: id => id == "Line3" && invalidTransformerCycleIsDisconnected);
				var (solution, info) = SolutionAndInfo();

				Assert.True(info.IsFeasible == invalidTransformerCycleIsDisconnected);
				if (!invalidTransformerCycleIsDisconnected)
				{
					Assert.Contains(info.ViolatedConstraints, c => c.Description?.Contains("The configuration uses one or more missing transformer modes in periods: 0") ?? false);
					Assert.Contains(info.ViolatedConstraints, c => c.Description?.Contains("These transformers are used in invalid modes in period 0: transformer connecting B2/B3") ?? false);
				}
			}
		}

		[Fact]
		public void SolutionCanHaveCycleInDisconnectedComponent()
		{
			var builder = NetworkBuilder.Create("Gen1[generatorVoltage=100] -- Line1[closed] -- X1 -- Line2[closed] -- Consumer[consumption=(1,0)]");
			builder.Add("Gen1 -- Line3[open] -- B1 -- Line4[closed] -- B2 -- Line5[closed] -- B3");
			builder.Add("B3 -- Line6[closed] -- B1");

			foreach (var cycleIsDisconnected in new[] { true, false })
			{
				SetupSession(builder, isOpen: id => id == "Line3" && cycleIsDisconnected);
				var (solution, info) = SolutionAndInfo();

				Assert.Equal(cycleIsDisconnected, info.IsFeasible);
				if (!cycleIsDisconnected)
				{
					Assert.Contains(info.ViolatedConstraints, c => c.Description?.Contains("The configuration is not radial in periods: 0") ?? false);
					Assert.Contains(info.ViolatedConstraints, c => c.Description?.Contains("There are 1 cycles in period 0. The first cycle consists of these lines: Line4, Line5, Line6") ?? false);
				}

				Optimize(500);
			}
		}

		[Fact]
		public void SolutionWithDisconnectedComponentCanBeUsedAsStartConfiguration()
		{
			var builder = NetworkBuilder.Create("Gen1[generatorVoltage=100] -- Line1[closed] -- X1 -- Line2[closed] -- Consumer[consumption=(1,0)]");
			builder.Add("Gen1 -- Line3[open] -- X2 -- Line4[open] -- Consumer");

			SetupSession(builder);

			// TODO How can we see that the correct solution was picked?
			Optimize(1000);
		}

		[Fact]
		public void PerBusKileCostIsReported()
		{
			var builder = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1a[breaker] -o- Line2a[closed] -o- Line3a[faultsPerYear=1;sectioningTime=PT1H;repairTime=PT1H] -- ConsumerA[consumption=(100,0);type=Domestic]",
				"Gen1                       -- Line1b[breaker] -o- Line2b[closed] -o- Line3b[faultsPerYear=5;sectioningTime=PT5H;repairTime=PT5H] -- ConsumerB[consumption=(100,0);type=Domestic]"
			);

			SetupSession(builder);
			var (solution, info) = SolutionAndInfo();

			// KILE cost is reported for the consumer nodes.
			Assert.True(solution.KileCosts[0].ExpectedCosts.Keys.ToHashSet().SetEquals(new[] { "ConsumerA", "ConsumerB" }));

			Assert.Equal(0.0002363013698630137, solution.KileCosts[0].ExpectedCosts["ConsumerA"], 10);
			Assert.Equal(0.0056563926940639275, solution.KileCosts[0].ExpectedCosts["ConsumerB"], 10);
		}

		[Fact]
		public void ReportAboutSolutionsNotUsingTransformersConsistently()
		{
			foreach (var (transformerCanBeUsedWithN1Upstream, transformerCanBeUsedWithN2Upstream) in new[] { (true, true), (false, true), (true, false) })
			{
				var upstreamNodes = new List<string>();
				if (transformerCanBeUsedWithN1Upstream) upstreamNodes.Add("N1");
				if (transformerCanBeUsedWithN2Upstream) upstreamNodes.Add("N2");
				var upstreamString = String.Join(",", upstreamNodes);

				var builder = new NetworkBuilder();
				builder.Add($"G1[generatorVoltage=1] -- Line1[open] -- N1 -- Line2 -- Transformer1[transformer;ends=(N1,N2);voltages=(1,1);upstream=({upstreamString})] -- Line3 -- N2 -- Line4[open] -- G2[generatorVoltage=1]");
				var network = builder.Network;

				foreach (var openLine in new[] { "Line1", "Line4" })
				{
					SetupSession(builder, isOpen: id => id == openLine);
					var (solution, info) = SolutionAndInfo();

					var valid = !(openLine == "Line4" && !transformerCanBeUsedWithN1Upstream) &&
								!(openLine == "Line1" && !transformerCanBeUsedWithN2Upstream);

					if (valid)
					{
						Assert.True(info.IsFeasible);
						Assert.True(!(info.ViolatedConstraints?.Count > 0));
					}
					else
					{
						Assert.True(!info.IsFeasible);
						Assert.Contains(info.ViolatedConstraints, c => c.Description?.Contains("The configuration uses one or more missing transformer modes in periods: 0") ?? false);
					}
				}
			}
		}

		private void SetupSession(NetworkBuilder builder, Func<string, bool> isOpen = null)
		{
			var networkManager = new NetworkManager(builder.Network);
			var periodData = builder.PeriodData;

			_session = Sintef.Pgo.Server.Session.Create("id", "networkId", networkManager, new() { periodData }, null);

			var pgoSolution = builder.Solution(_session.Problem);

			if (isOpen != null)
			{
				foreach (var p in pgoSolution.SinglePeriodSolutions)
					foreach (var sw in _session.Problem.Network.SwitchableLines)
						p.NetConfig.SetSwitch(sw, isOpen(sw.Name));
			}

			_session.ComputeFlow(pgoSolution);
			_session.AddSolution(pgoSolution, _solutionId);
		}

		private (Solution solution, SolutionInfo info) SolutionAndInfo(string solutionId = _solutionId)
		{
			PgoSolution pgoSolution = (PgoSolution)_session.GetSolution(solutionId);

			Solution solution;

			var flowProvider = (pgoSolution.Encoding as PgoProblem)?.FlowProvider;
			if (pgoSolution.IsComplete(flowProvider))
				solution = PgoJsonParser.ConvertToJson(pgoSolution, flowProvider);
			else
				solution = PgoJsonParser.ConvertToJson(pgoSolution, null);

			var info = _session.Summarize(pgoSolution);

			return (solution, info);
		}

		private void Optimize(int milliseconds)
		{
			_session.StartOptimization(new());
			
			Thread.Sleep(milliseconds);
			
			_session.StopOptimization().GetAwaiter().GetResult();
		}
	}
}
