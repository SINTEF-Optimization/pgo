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
	/// A simple solver for the <see cref="PgoProblem"/>. Simply goes through the time periods
	/// in chronological order, creating an optimal solution for each period sub problem.
	/// The solution of each problem is taken as the reference solution for ConfigStabilityObjective when
	/// solving the next problem (for the next time period).
	/// </summary>
	internal class SimpleMultiPeriodsolver : Optimizer
	{
		#region Public properties 

		/// <summary>
		/// The maximum number of iterations. If 1, only one first pass through the period is made (this is the minimum).
		/// If a higher number is given, that many passes are made (unless the user interrupts the search).
		/// </summary>
		public int MaxIterations { get; }

		#endregion

		#region Private data members

		/// <summary>
		/// Parameters for the subsolvers used
		/// </summary>
		private MetaHeuristicParameters _metaHeuristicParams;

		/// <summary>
		/// The optimizer environment
		/// </summary>
		private OptimizerEnvironment _environment;
		
		/// <summary>
		/// The criteria given to <see cref="Optimize"/>
		/// </summary>
		private ICriteriaSet _criteriaSet;

		/// <summary>
		/// Stop criterion used for calls to <see cref="Optimize"/>.
		/// </summary>
		protected StopCriterion _myStop;

		/// <summary>
		/// The working solution that we construct and improve during the search.
		/// </summary>
		protected PgoSolution _workingSolution;

		/// <summary>
		/// The time limit for running the solver.
		/// </summary>
		protected TimeSpan _timout;

		#endregion

		#region Construction

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="metaHeuristicParams">Parameters for subsolvers</param>
		/// <param name="timout">The time limit for running the solver.</param>
		/// <param name="maxIterations">The maximum number of iterations. If 1, only one first pass through the period is made (this is the minimum).
		/// If a higher number is given, that many passes are made (unless the user interrupts the search).</param>
		/// <param name="environment">The optimizer environment</param>
		public SimpleMultiPeriodsolver(MetaHeuristicParameters metaHeuristicParams, TimeSpan timout, int maxIterations, OptimizerEnvironment environment) : base()
		{
			_timout = timout;
			MaxIterations = maxIterations;
			_environment = environment;
			_metaHeuristicParams = metaHeuristicParams;
		}




		#endregion

		#region Public methods

		/// <summary>
		/// Provides a solution to the <see cref="PgoProblem"/> contained in the input solution. 
		/// First, the optimiser produces a solution that is a copy of the problem's start configuration in each period. 
		/// This is then improved
		/// as each period problem is optimized one by one, using the given criterion set, except that if
		/// a ConfigStabilityObjective is
		/// included in the given criteria set, the cost of changing configurations from one period to the next is considered.
		/// (If not, each period problem is considered as independent).
		/// After the first pass, new passes through the periods are then made in which the difference to both the earlier and the following solutions
		/// are taken into account, until the user stops the optimization.
		/// </summary>
		/// <param name="solution">The initial solution, assumed to be of type <see cref="PgoSolution"/>. 
		/// This holds the  <see cref="PgoProblem"/> to be solved, but is otherwise ignored.
		/// </param>
		/// <param name="criteriaSet"></param>
		/// <param name="stopCriterion"></param>
		/// <returns>The final solution, or null if no solution was found or the <paramref name="stopCriterion"/> is triggered before the search is complete.</returns>
		public override ISolution Optimize(ISolution solution, ICriteriaSet criteriaSet, StopCriterion stopCriterion)
		{
			FireOptimizationStarted(solution, criteriaSet, stopCriterion);

			_criteriaSet = criteriaSet;
			_myStop = stopCriterion;
			PgoSolution mpSol = solution as PgoSolution;
			PgoProblem mpProb = mpSol.Problem;
			double periodSolverTimout = _timout.TotalSeconds / ((double)mpProb.PeriodCount);

			var feasibilitySolver = OptimizerFactory.CreateFeasibilitySolver(_metaHeuristicParams, mpSol, _environment);

			FireStartingSubOptimizer(feasibilitySolver);

			_workingSolution = feasibilitySolver.Optimize(mpSol, criteriaSet, stopCriterion) as PgoSolution;


			if (!_workingSolution.IsFeasible(criteriaSet))
				return null;

			FireBestSolutionFound(_workingSolution);

			//First pass
			Console.WriteLine($"---------------------SimpleMultiPeriod solver: First pass through the periods ----" +
				$"start solution has value {criteriaSet.Objective.Value(_workingSolution)}------------------------");
			//We need a constructor, with a descent repair at leaf nodes


			RunThroughAllPeriods(mpProb, periodSolverTimout);

			//New passes, considering changes also wrt. to the next period.
			int iterationCounter = 1;
			while (!_myStop.IsTriggered && iterationCounter < MaxIterations)
			{
				Console.WriteLine($"---------------------SimpleMultiPeriod solver: new pass through the periods ----------------------------: {iterationCounter++}.");

				RunThroughAllPeriods(mpProb, periodSolverTimout);
			}

			FireOptimizationStopped(_workingSolution, criteriaSet, stopCriterion);

			return _workingSolution;
		}




		#endregion

		#region Private/protected methods

		/// <summary>
		/// Updates the best found solution if the given period solution represents an improvement
		/// </summary>
		/// <param name="period">The period for which we update the solution.</param>
		/// <param name="newPerSol"></param>
		/// <param name="oldPerSol">The existing period solution. If null, a copy of this will be retrieved from the information in the working solution.</param>
		protected void UpdateBestFoundSolution(Period period, PgoSolution newPerSol, PgoSolution oldPerSol = null)
		{
			PgoProblem mpProb = _workingSolution.Problem;
			PgoProblem p = newPerSol.Problem;

			if (oldPerSol == null)
			{
				oldPerSol = new PgoSolution(p);
				oldPerSol.UpdateSolutionForPeriod(_workingSolution.GetPeriodSolution(period));
			}
			double perSolValueBefore = _criteriaSet.Objective.Value(oldPerSol);
			if (_criteriaSet.Objective.Value(newPerSol).LessThanWithTolerance(perSolValueBefore, 1e-9))
			{
				_workingSolution.UpdateSolutionForPeriod((newPerSol.Clone() as PgoSolution).GetPeriodSolution(period));

				PgoSolution clone = _workingSolution.Clone() as PgoSolution;
				FireBestSolutionFound(clone);
			}
		}

		/// <summary>
		/// Retrieves the objective of the given type from the given criteria set.
		/// </summary>
		/// <param name="criteriaSet"></param>
		/// <returns></returns>
		protected static T FindObjective<T>(ICriteriaSet criteriaSet) where T : IObjective
		{
			return (T)(criteriaSet.Objective as AggregateObjective)?.Components.SingleOrDefault(c => c is T);
		}

		/// <summary>
		/// Runs through all time periods, allocating some time to the optimisation of each, taking into account the differences in switch configurations
		/// with previous and the next periods. Uses the same criteria set for the period problems as for the overall problem.
		/// </summary>
		/// <param name="mpProb"></param>
		/// <param name="periodSolverTimout">Timeout for each period.</param>
		private void RunThroughAllPeriods(PgoProblem mpProb, double periodSolverTimout)
		{
			int periodCounter = 0;
			PgoSolution prevPeriodSol = null; //The solution of the previous period.
			foreach (PeriodData p in mpProb.AllPeriodData)
			{
				Console.WriteLine($"---------------------SimpleMultiPeriod solver, period {periodCounter} --------- value = {_criteriaSet.Objective.Value(_workingSolution)}------------");

				if (_myStop.IsTriggered)
					return;

				PgoProblem periodEncoding = mpProb.CreateSinglePeriodCopy(p.Period);

				PgoSolution periodSolution = new PgoSolution(periodEncoding);
				periodSolution.UpdateSolutionForPeriod(_workingSolution.GetPeriodSolution(p.Period));

				var periodSolver = OptimizerFactory.CreateRandomRestartSolver(_metaHeuristicParams, periodSolution, periodSolution.Problem.CriteriaSet, _environment);

				periodSolver.BestSolutionFound += PeriodSolver_BestSolutionFound;

				FireStartingSubOptimizer(periodSolver);

				PgoSolution newPerSol = new OptimizerRunner(periodSolver, TimeSpan.FromSeconds(periodSolverTimout))
					.Optimize(periodSolution, _criteriaSet, _myStop) as PgoSolution;

				//Did we improve the period solution? If so, this should also improve the overall solution, and we update.
				UpdateBestFoundSolution(p.Period, newPerSol);

				++periodCounter;
				prevPeriodSol = newPerSol;
			}
		}

		/// <summary>
		/// Called when the period solver finds a new best solution. Since this means that 
		/// also the total is improved, we construct the corresponding multi-period solution,
		/// and report a new best to listeners.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void PeriodSolver_BestSolutionFound(object sender, SolutionEventArgs e)
		{
			PgoSolution sol = e.Solution as PgoSolution;
			Period period = sol.SinglePeriodSolutions.Single().Period;
			UpdateBestFoundSolution(period, e.Solution as PgoSolution);
		}

		#endregion
	}
}
