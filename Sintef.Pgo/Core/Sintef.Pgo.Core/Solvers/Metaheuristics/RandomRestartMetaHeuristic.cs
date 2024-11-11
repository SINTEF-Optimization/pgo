using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// An optimizer that is based on a repeated (random) construction of a 
	/// feasible switching solution, followed by a local search to the closest local optimum.
	/// Uses the branch and bound search to first leaf node for the construction.
	/// </summary>
	public class RandomRestartMetaHeuristic : MetaHeuristicBase
	{
		/// <summary>
		/// Constructor
		/// </summary>
		/// /// <param name="solution">The solution to be solved.</param>
		/// <param name="parameters">The basic parameters of the meta-heuristic.</param>
		/// <param name="environment">The shared services of the optimization environment.</param>
		public RandomRestartMetaHeuristic(PgoSolution	solution, MetaHeuristicParameters parameters, OptimizerEnvironment environment)
			: base(parameters, solution, environment)
		{
		}

		/// <summary>
		/// Optimize function.
		/// </summary>
		/// <param name="inputSol"></param>
		/// <param name="criteriaSet"></param>
		/// <param name="stopCrit"></param>
		/// <returns>The best found solution</returns>
		public override ISolution Optimize(ISolution inputSol, ICriteriaSet criteriaSet, StopCriterion stopCrit)
		{
			FireOptimizationStarted(inputSol, criteriaSet, stopCrit);

			PgoSolution inputSpSol = inputSol as PgoSolution;
			PgoProblem enc = inputSpSol.Problem;

			//Create search visualizer
			SetUpSearchProfiler(inputSol);

			InitializeBestSolution(inputSol, criteriaSet);

			HiPerfTimer timer = new HiPerfTimer();

			int i = 0;
			while (!stopCrit.IsTriggered)
			{
				timer.Reset();
				timer.Start();
				PgoSolution initSol = CreateInitialSolution(enc, criteriaSet, stopCrit);
				timer.Stop();
				if (Verbose)
					Console.WriteLine($"RRMH: Initial solution construction took {string.Format("{0:0.00}", timer.Duration)} seconds.");


				if (stopCrit.IsTriggered)
					break;

				if (initSol == null) //infeasible
				{
					//This can happen if the construction algorithm is not exact. In this case, we simply try again
					continue;
				}
				else
				{
					//TODO remove
					Debug.Assert(initSol.IsFeasible(criteriaSet), "Initial solution with flow is not feasible according to problem constraints");

					// Do local search
					PgoSolution sol = RunLocalSearch(initSol, criteriaSet, stopCrit);

					UpdateBestSolution(sol, criteriaSet);

					if (Verbose)
						Console.WriteLine($"RandomRestartMetaHeur: New local optimum {sol.ObjectiveValue}");


					sol.Name = (++i).ToString();
					FireLocalOptimumFound(sol);

					FireIteration(sol);
				}
			}

			FireOptimizationStopped(_bestSolution, criteriaSet, stopCrit);

			return _bestSolution;
		}
	}
}
