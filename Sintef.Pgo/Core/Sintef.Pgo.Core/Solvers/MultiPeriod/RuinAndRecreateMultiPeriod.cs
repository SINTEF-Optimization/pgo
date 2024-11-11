using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A ruin-and-recreate based meta-heuristic for multi-period problems.
	/// First solves all periods to local optimum for the period respective problems, same as the SimpleMultiPeriodSolver. 
	/// Then enters into an iterative improvement phase, that does "per period", but focused on issues with the total solution.
	/// When a local optimum is reached, this is fixed by Ruin-and-Recreate: TODO: spesifics.
	/// </summary>
	public class RuinAndRecreateMultiPeriod : MetaHeuristicBase
	{
		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="solution">The solution to be solved.</param>
		/// <param name="metaHeurParams">The basic parameters of the meta-heuristic.</param>
		/// <param name="environment">The shared services of the optimization environment.</param>
		public RuinAndRecreateMultiPeriod(PgoSolution solution, MetaHeuristicParameters metaHeurParams, OptimizerEnvironment environment)
			: base(metaHeurParams, solution, environment)
		{
		}

		/// <summary>
		/// Provides a solution to the <see cref="PgoProblem"/> contained in the input solution. Starts with the given
		/// input solution if that is feasible with respect to the given <paramref name="criteriaSet"/>. Otherwise,
		/// attempts to create a new feasible start solution. If this fails, the function returns. Otherwise it goes on to 
		/// run the Ruin And Recreate search until interrupted, continuously reporting on new best found solutions.
		/// </summary>
		/// <param name="inputSol">The initial solution, assumed to be of type <see cref="PgoSolution"/>. 
		/// This holds the  <see cref="PgoProblem"/> to be solved, but is otherwise ignored.
		/// </param>
		/// <param name="criteriaSet"></param>
		/// <param name="stopCriterion"></param>
		/// <returns>The final solution, or null if no solution was found or the <paramref name="stopCriterion"/> is triggered before the search has come far enough to produce at least one solution.</returns>
		public override ISolution Optimize(ISolution inputSol, ICriteriaSet criteriaSet, StopCriterion stopCriterion)
		{
			FireOptimizationStarted(inputSol, criteriaSet, stopCriterion);

			if (Verbose)
				Console.WriteLine($"RuinAndRecreateMultiPeriod: Starting -------");

			//Create search visualizer
			SetUpSearchProfiler(inputSol);

			//Initialize
			InitializeBestSolution(inputSol, criteriaSet);

			//Create first feasible solution, or use the inputSol if that is feasible
			PgoSolution startSolution = (inputSol as PgoSolution);
			if (!startSolution.IsFeasible(criteriaSet))
				startSolution = CreateInitialSolution(startSolution.Problem, criteriaSet, stopCriterion);

			if (startSolution != null)
			{
				RuinAndRecreate(startSolution, criteriaSet, stopCriterion);
			}

			FireOptimizationStopped(_bestSolution, criteriaSet, stopCriterion);

			if (startSolution != null)
			{
				if (!startSolution.IsFeasible(criteriaSet))
				{
					Console.WriteLine("Could not find a feasible start solution for the search. Failing constraints are:");
					foreach (IConstraint con in criteriaSet.Constraints)
					{
						if (!con.IsSatisfied(startSolution))
							Console.WriteLine($"\t - {con.GetType().ToString()}: {con.Reason(startSolution)}");
					}
					return null;
				}

				UpdateBestSolution(startSolution, criteriaSet);
			}

			return _bestSolution;
		}

		/// <summary>
		/// Runs the actual ruin and recreate algorithm
		/// </summary>
		/// <param name="solution"></param>
		/// <param name="criteriaSet"></param>
		/// <param name="stopCriterion"></param>
		private void RuinAndRecreate(PgoSolution solution, ICriteriaSet criteriaSet, StopCriterion stopCriterion)
		{
			UpdateBestSolution(solution, criteriaSet);

			//Now, improve on this
			
			int i = 0;
			Random rand = RandomCreator.GetRandomGenerator();

			while (!stopCriterion.IsTriggered)
			{
				// Do local search
				solution = RunLocalSearch(solution, criteriaSet, stopCriterion);

				if (Verbose)
					Console.WriteLine($"RuinAndRecreateMultiPeriod: New local optimum {solution.ObjectiveValue}");

				solution.Name = (++i).ToString();
				FireLocalOptimumFound(solution);


				//Ruin-And-Recreate by closing the same switches in all period solutions, and then repairing.

				//The number of switches to select for closing at each iteration
				double fraction = 0.01;
				int m = (int)(solution.Problem.Network.SwitchableLines.Count() * fraction);
				m = Math.Max(m, 5);
				IEnumerable<Line> switchesToClose = solution.Problem.Network.SwitchableLines.RandomElements(m, rand);

				List<PeriodSolution> newPeriodSolutions = new List<PeriodSolution>();
				foreach (Period period in solution.Problem.Periods)
				{
					if (stopCriterion.IsTriggered)
						break;

					PgoSolution sol = solution.CreateSinglePeriodCopy(period);

					//First, we ruin by closing a selected set of m switches
					//TODO select switches based on switching cost (biased selection)?

					switchesToClose.Do(s => sol.SetSwitch(period, s, false));

					//Then re-create by the construction heuristic

					// Construct a radial and feasible solution.
					sol = MakeFeasible(sol, criteriaSet, stopCriterion);

					if (sol.IsFeasible(criteriaSet))
					{
						PeriodSolution newPeriodSol = sol.GetPeriodSolution(period);
						newPeriodSolutions.Add(newPeriodSol);
					}
				}

				newPeriodSolutions.Do(s => solution.UpdateSolutionForPeriod(s));

				if (Verbose)
					Console.WriteLine($"R&R: After diversification, solution value is {solution.ObjectiveValue}");

				FireIteration(solution);
			}
		}

		/// <summary>
		/// When a new best solution if found from Descent
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void OnDescentSolver_BestSolutionFound(object sender, SolutionEventArgs e)
		{
			FireBestSolutionFound(e.Solution);
		}
	}
}
