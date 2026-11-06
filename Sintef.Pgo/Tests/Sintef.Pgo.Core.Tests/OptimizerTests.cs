using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

using System.Numerics;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class OptimizerTests
	{
		#region Voltage limits and the start configuration

		/// <summary>
		/// The start configuration violates the consumer's minimum voltage under Iterated DistFlow,
		/// but not under Simplified DistFlow, which ignores the line losses. The other configuration
		/// meets the limit, at higher losses. The optimizer should return the feasible configuration,
		/// not the start.
		/// </summary>
		[TestMethod]
		public void OptimizerLeavesStartThatViolatesVoltageLimitOnlyUnderIteratedDistFlow()
		{
			var builder = TwoFeederNetwork(vMinV: 938, startFeeder: "NearFeeder");
			var problem = builder.OnePeriodProblem;
			var start = PgoSolution.CreateUnchangingSolution(problem);
			var other = SolutionFedBy(problem, "FarFeeder");

			// Preconditions: this is the case where search and acceptance disagree
			Assert.IsFalse(start.IsFeasible(problem.CriteriaSet), "The start should violate the limit under Iterated DistFlow");
			Assert.IsTrue(start.IsFeasible(problem.CriteriaSet.WithProvider(new SimplifiedDistFlowProvider())),
				"The start should meet the limit under Simplified DistFlow");
			Assert.IsTrue(other.IsFeasible(problem.CriteriaSet), "The other configuration should meet the limit");
			Assert.IsTrue(Losses(other) > Losses(start), "The other configuration should have higher losses");

			var result = Optimize(problem, start);

			Assert.IsFalse(IsFedBy(result, "NearFeeder"), "The optimizer returned the start configuration");
			Assert.IsTrue(result.IsFeasible(problem.CriteriaSet), "The optimizer returned an infeasible solution:\n" +
				ViolationReasons(problem, result));
		}

		#endregion

		#region Utilities

		/// <summary>
		/// Creates a network where one consumer can be fed by either of two providers.
		/// The near provider has a feeder with less resistance and gives lower losses. The far provider
		/// has a higher voltage and gives a higher voltage at the consumer.
		/// The consumer's voltage on the near feeder is 940 V by Simplified DistFlow
		/// and 935.9 V by Iterated DistFlow. On the far feeder it is 981.4 V and 976.2 V.
		/// The problems created by the builder use Iterated DistFlow.
		/// </summary>
		/// <param name="vMinV">The consumer's minimum voltage</param>
		/// <param name="startFeeder">The feeder that is closed in the builder's configuration</param>
		/// <param name="load">The consumer's active load, in W</param>
		private static NetworkBuilder TwoFeederNetwork(double vMinV, string startFeeder, double load = 60000)
		{
			string Status(string feeder) => feeder == startFeeder ? "closed" : "open";

			var builder = new NetworkBuilder(new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions));
			builder.Add($"Consumer[consumption=({load}, 0); vMinV={vMinV}]");
			builder.Add($"Near[generatorVoltage=1000] -- NearFeeder[r=1; iMax=1000; {Status("NearFeeder")}] -- Consumer");
			builder.Add($"Far[generatorVoltage=1050] -- FarFeeder[r=1.2; iMax=1000; {Status("FarFeeder")}] -- Consumer");

			return builder;
		}

		/// <summary>
		/// Returns a solution to the problem where the consumer is fed through the given feeder in every period
		/// </summary>
		private static PgoSolution SolutionFedBy(PgoProblem problem, string feeder)
		{
			var network = problem.Network;
			var settings = new SwitchSettings(network, line => line.Name != feeder);
			var configuration = new NetworkConfiguration(network, settings);

			return new PgoSolution(problem, problem.Periods.ToDictionary(p => p, p => configuration));
		}

		/// <summary>
		/// Returns true if the consumer is fed through the given feeder in every period
		/// </summary>
		private static bool IsFedBy(PgoSolution solution, string feeder)
		{
			var line = solution.Problem.Network.GetLine(feeder);
			return solution.SinglePeriodSolutions.All(s => !s.IsOpen(line));
		}

		/// <summary>
		/// Returns the reasons given by the problem's violated constraints
		/// </summary>
		private static string ViolationReasons(PgoProblem problem, PgoSolution solution)
			=> problem.CriteriaSet.Constraints.Where(c => !c.IsSatisfied(solution)).Select(c => c.Reason(solution)).Join("\n");

		/// <summary>
		/// The total losses of the solution under Simplified DistFlow
		/// </summary>
		private static double Losses(PgoSolution solution)
			=> new TotalLossObjective(new SimplifiedDistFlowProvider()).Value(solution);

		/// <summary>
		/// Optimizes from the given start with the default criteria, as a session does,
		/// for at most one second
		/// </summary>
		private static PgoSolution Optimize(PgoProblem problem, PgoSolution start)
		{
			var parameters = new OptimizerParameters
			{
				Algorithm = problem.Periods.Count() == 1 ? AlgorithmType.RuinAndRecreate : AlgorithmType.SimpleMultiPeriodsolver,
				OptimizerType = OptimizerType.Descent,
				ExplorerType = ExplorerType.DeltaExplorer,
				SimpleMPSolverSolverTimeOut = TimeSpan.FromSeconds(1)
			};
			parameters.MetaHeuristicParams.Random = new Random(1);

			IOptimizer optimizer = new ConfigOptimizerFactory(new NetworkManager(problem.Network))
				.CreateOptimizer(parameters, start, problem.CriteriaSet, new OptimizerEnvironment());

			return (PgoSolution)new OptimizerRunner(optimizer, TimeSpan.FromSeconds(1)).Optimize(start.Clone());
		}

		#endregion
	}
}
