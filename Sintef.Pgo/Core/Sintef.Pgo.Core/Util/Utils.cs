using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A collection of helpful functions that have not yet found a more natural home
	/// </summary>
	public static class Utils
	{
		/// <summary>
		/// The factory used to create flow providers
		/// </summary>
		public static IFlowProviderFactory Factory = new DefaultFlowProviderFactory();

		/// <summary>
		/// Builds a solution that is radial and feasible, for the given period data.
		/// The solution is not optimized.
		/// </summary>
		/// <param name="periodData">The single-period data</param>
		/// <param name="approximation">The flow approximation to use</param>
		/// <param name="startConfig">If given, we attempt for the returned solution to be "close" to this configuration, although the function
		/// makes no guaranties regarding the degree of closeness. Optional.</param>
		/// <param name="endConfig">The configuration that we would like to see after the end of 
		/// the period (used by the ChangeCost objective).</param>
		/// <param name="criteria">The criteria defining feasibility. If null, default criteria are used.</param>
		/// <returns>A feasible solution, or null if no such solution could be found.</returns>
		public static PeriodSolution ConstructFeasibleSolution(PeriodData periodData, FlowApproximation approximation, 
			NetworkConfiguration startConfig = null, NetworkConfiguration endConfig = null, CriteriaSet criteria = null)
		{
			var flowProvider = criteria?.FlowProvider() ?? CreateFlowProvider(approximation);

			// Make a temporary single period encoding
			PgoProblem problem = new PgoProblem(periodData, flowProvider, startConfig, endConfig, criteriaSet: criteria);

			// Construct a radial solution.

			return ConstructFeasibleSolution(problem)?.GetPeriodSolution(periodData.Period);
		}

		/// <summary>
		/// Builds a solution that is radial and feasible, for each period.
		/// The solution is not optimized.
		/// </summary>
		/// <param name="problem">The multiperiod problem</param>
		/// <param name="random">The random generator to use, or null for a default generator</param>
		/// <returns>A feasible solution, or null if no such solution could be found.</returns>
		public static PgoSolution ConstructFeasibleSolution(PgoProblem problem, Random random = null)
		{
			var searchParams = MetaHeuristicParameters.Standard();
			searchParams.Random = random ?? searchParams.Random;

			PgoSolution solution = new PgoSolution(problem);
			var solver = OptimizerFactory.CreateFeasibilitySolver(searchParams, solution, new OptimizerEnvironment());
			solution = solver.Optimize(solution) as PgoSolution;

			if (solution.IsFeasible)
				return solution;
			else
				return null;
		}

		/// <summary>
		/// Builds a solution that is radial and feasible, for each period.
		/// The solution is not optimized.
		/// </summary>
		/// <param name="problem">The multiperiod problem</param>
		/// <param name="searchParams">Search parameters</param>
		/// <param name="criteriaSet">The criteria set defining feasibility</param>
		/// <param name="stop">The stop criterion</param>
		/// <param name="environment">The optimizer environment</param>
		/// <returns>The resulting solution, or null if a feasible solution was not found</returns>
		public static PgoSolution ConstructFeasibleSolution(PgoProblem problem, MetaHeuristicParameters searchParams, ICriteriaSet criteriaSet,
			StopCriterion stop, OptimizerEnvironment environment)
		{
			PgoSolution solution = new PgoSolution(problem);
			var solver = OptimizerFactory.CreateFeasibilitySolver(searchParams, solution, environment);
			solution = solver.Optimize(solution, criteriaSet, stop) as PgoSolution;

			if (solution.IsFeasible(criteriaSet))
				return solution;
			else
				return null;
		}

		/// <summary>
		/// Creates a flow provider object of the indicated type.
		/// Throws an exception if the approximation is not supported
		/// </summary>
		public static IFlowProvider CreateFlowProvider(FlowApproximation approximation)
		{
			return Factory.CreateFlowProvider(approximation);
		}

		/// <summary>
		/// Returns a string that summarizes how well the given flow is a solution to the
		/// power equations, ignoring transformers.
		/// </summary>
		/// <param name="flow">The flow to check</param>
		public static string FlowConsistencyReport(this IPowerFlow flow)
		{
			return new FlowConsistencyChecker(flow).Summary;
		}

		/// <summary>
		/// Returns a string that summarizes how well the given flow is a solution to the
		/// power equations, with separate statistics for transformers.
		/// </summary>
		/// <param name="flow">The flow to check</param>
		public static string FlowConsistencyReportWithTransformers(this IPowerFlow flow)
		{
			return new FlowConsistencyChecker(flow).SummaryWithTransformers;
		}
	}
}
