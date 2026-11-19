using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for <see cref="MultiPeriodCandidateScheduler"/>.
	///
	/// One consumer can be fed by either of two providers, in three periods with low, high and low load.
	/// The near feeder has the lower losses, but violates the consumer's minimum voltage at high load.
	/// The far feeder meets the limits in every period, and is the start configuration.
	/// </summary>
	[TestClass]
	public class MultiPeriodCandidateSchedulerTests
	{
		[TestMethod]
		public void ScheduleUsesTheFeasibleConfigurationOnlyWhereNeeded()
		{
			var problem = Problem(switchingCost: 0);

			var result = MultiPeriodCandidateScheduler.Schedule(problem, new[] { FedBy(problem, "NearFeeder"), FedBy(problem, "FarFeeder") });

			Assert.AreEqual("NearFeeder FarFeeder NearFeeder", Feeders(result));
			Assert.IsTrue(result.IsFeasible(problem.CriteriaSet));
		}

		[TestMethod]
		public void HighSwitchingCostKeepsTheStartConfiguration()
		{
			var problem = Problem(switchingCost: 10000);

			var result = MultiPeriodCandidateScheduler.Schedule(problem, new[] { FedBy(problem, "NearFeeder"), FedBy(problem, "FarFeeder") });

			Assert.AreEqual("FarFeeder FarFeeder FarFeeder", Feeders(result));
		}

		[TestMethod]
		public void ScheduleIsTheBestFeasibleScheduleFromTheCandidates()
		{
			var problem = Problem(switchingCost: 500);
			var candidates = new[] { FedBy(problem, "NearFeeder"), FedBy(problem, "FarFeeder") };

			var result = MultiPeriodCandidateScheduler.Schedule(problem, candidates);

			// Every schedule from the candidates
			var periods = problem.Periods.ToList();
			var all = Enumerable.Range(0, 1 << periods.Count)
				.Select(k => new PgoSolution(problem, periods.ToDictionary(p => p, p => candidates[(k >> p.Index) & 1])));
			double bestValue = all.Where(s => s.IsFeasible(problem.CriteriaSet)).Min(s => problem.CriteriaSet.Objective.Value(s));

			Assert.IsTrue(result.IsFeasible(problem.CriteriaSet));
			Assert.AreEqual(bestValue, problem.CriteriaSet.Objective.Value(result), 1e-6);
		}

		[TestMethod]
		public void ScheduleIsReturnedWhenNoConfigurationMeetsTheConstraints()
		{
			var problem = Problem(switchingCost: 0);

			var result = MultiPeriodCandidateScheduler.Schedule(problem, new[] { FedBy(problem, "NearFeeder") });

			Assert.AreEqual("NearFeeder NearFeeder NearFeeder", Feeders(result));
			Assert.IsFalse(result.IsFeasible(problem.CriteriaSet));
		}

		[TestMethod]
		public void InvalidCandidatesAreRejected()
		{
			var problem = Problem(switchingCost: 0);
			var other = Problem(switchingCost: 0);
			var bothClosed = new NetworkConfiguration(problem.Network, new SwitchSettings(problem.Network, line => false));

			TestUtils.AssertException(() => MultiPeriodCandidateScheduler.Schedule(problem, new NetworkConfiguration[0]), requiredInMessage: "no candidate");
			TestUtils.AssertException(() => MultiPeriodCandidateScheduler.Schedule(problem, new[] { FedBy(other, "NearFeeder") }), requiredInMessage: "another network");
			TestUtils.AssertException(() => MultiPeriodCandidateScheduler.Schedule(problem, new[] { bothClosed }), requiredInMessage: "not radial");
		}

		#region Utilities

		/// <summary>
		/// Creates the problem, with the given switching cost on both feeders and the far feeder as start configuration.
		/// The consumer's voltage on the near feeder at high load is 935.9 V by Iterated DistFlow, below its limit of 938 V.
		/// </summary>
		private static PgoProblem Problem(int switchingCost)
		{
			var builder = new NetworkBuilder();
			builder.Add("Consumer[consumption=(6000, 0); vMinV=938]");
			builder.Add($"Near[generatorVoltage=1000] -- NearFeeder[r=1; iMax=1000; open; switchingCost={switchingCost}] -- Consumer");
			builder.Add($"Far[generatorVoltage=1050] -- FarFeeder[r=1.2; iMax=1000; closed; switchingCost={switchingCost}] -- Consumer");

			var network = builder.Network;
			var consumer = network.GetBus("Consumer");
			var start = new DateTime(2024, 1, 1);
			var periods = new[] { 6000.0, 60000.0, 6000.0 }.Select((load, i) =>
			{
				var demands = new PowerDemands(network);
				demands.SetPowerDemand(consumer, new Complex(load, 0));
				return new PeriodData(network, demands, new Period(start.AddHours(i), start.AddHours(i + 1), i));
			});

			return new PgoProblem(periods, new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions), "Candidates", builder.Configuration);
		}

		/// <summary>
		/// The configuration where the consumer is fed through the given feeder
		/// </summary>
		private static NetworkConfiguration FedBy(PgoProblem problem, string feeder)
			=> new NetworkConfiguration(problem.Network, new SwitchSettings(problem.Network, line => line.Name != feeder));

		/// <summary>
		/// The feeder that feeds the consumer in each period
		/// </summary>
		private static string Feeders(PgoSolution solution)
			=> string.Join(" ", solution.Problem.Periods.Select(p => solution.GetPeriodSolution(p).NetConfig.SwitchSettings.ClosedSwitches.Single().Name));

		#endregion
	}
}
