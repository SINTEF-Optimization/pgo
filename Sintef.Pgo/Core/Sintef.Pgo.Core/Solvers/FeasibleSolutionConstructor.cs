using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{

	/// <summary>
	/// Implements a heuristic solution constructor, that can 
	/// make radial (and optionally also feasible) solutions.
	/// Modifies the solution by opening and closing switches, by applying 
	/// local search Move's.
	/// Inherits from <see cref="Optimizer"/>, and calling Optimize will return the first found feasible solution.
	/// </summary>
	public class FeasibleSolutionConstructor : Optimizer
	{
		/// <summary>
		/// Parameters for the optimizer
		/// </summary>
		private MetaHeuristicParameters _parameters;

		/// <summary>
		/// The optimizer environment
		/// </summary>
		private OptimizerEnvironment _environment;

		/// <summary>
		/// The critera set that is used during a repair. Set each time a repair search is initiated.
		/// </summary>
		CriteriaSet _criteraSetForRepair;

		/// <summary>
		/// Constructor
		/// </summary>
		public FeasibleSolutionConstructor(MetaHeuristicParameters parameters, OptimizerEnvironment environment)
		{
			_environment = environment;
			_parameters = parameters;
		}

		#region Public methods

		/// <summary>
		/// Optimizes until a feasible solution is found, in a random way.
		/// </summary>
		/// <param name="solution">The start solution, assumed to be an <see cref="PgoSolution"/>.</param>
		/// <param name="criteriaSet"></param>
		/// <param name="stopCriterion"></param>
		/// <returns>A feasible solution, or null if no such is found.</returns>
		public override ISolution Optimize(ISolution solution, ICriteriaSet criteriaSet, StopCriterion stopCriterion)
		{
			FireOptimizationStarted(solution, criteriaSet, stopCriterion);

			var mySolution = solution as PgoSolution;
			mySolution.MakeRadialFlowPossible(_parameters.Random, stopCriterion);

			if (!mySolution.IsFeasible(criteriaSet))
			{
				_criteraSetForRepair = criteriaSet.GetRelaxedFor(mySolution);

				var localSearcher = OptimizerFactory.CreateLocalSearchSolver(_parameters.FeasibilitySearchParameters, mySolution, _criteraSetForRepair, _environment);

				//TODO remove. this is for debugging
				//descentImprover.BestSolutionFound += DescentImprover_BestSolutionFound_DuringRepair;

				FireStartingSubOptimizer(localSearcher);

				mySolution = localSearcher.Optimize(mySolution, _criteraSetForRepair, stopCriterion) as PgoSolution;
			}

			FireOptimizationStopped(mySolution, criteriaSet, stopCriterion);

			return mySolution;
		}

		#endregion
	}
}
