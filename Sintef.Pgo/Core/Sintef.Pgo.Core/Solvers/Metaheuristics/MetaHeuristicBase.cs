using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Parameters for any meta-heuristic.
	/// </summary>
	[TypeConverter(typeof(GenericObjectConverter<MetaHeuristicParameters>))]
	public class MetaHeuristicParameters
	{
		/// <summary>
		/// The type of solver used for construction of initial feasible solutions.  The default is 
		/// <see cref="ConstructorType.MakeRadialAndFeasibleHeuristic"/>
		/// </summary>
		public ConstructorType ConstructorType { get; set; } = ConstructorType.MakeRadialAndFeasibleHeuristic;

		/// <summary>
		/// The type of neighborhood explorer that is used in the local search and
		/// feasibility search phases of a meta-heuristic.
		/// </summary>
		public ExplorerType ExplorerType
		{
			get => LocalSearchParameters.ExplorerParameters.ExplorerType;
			set
			{
				LocalSearchParameters.ExplorerParameters.ExplorerType = value;
				FeasibilitySearchParameters.ExplorerParameters.ExplorerType = value;
			}
		}

		/// <summary>
		/// The type of optimizer that is used in the local search and
		/// feasibility search phases of a meta-heuristic.
		/// </summary>
		public OptimizerType OptimizerType
		{
			get => LocalSearchParameters.OptimizerType;
			set
			{
				LocalSearchParameters.OptimizerType = value;
				FeasibilitySearchParameters.OptimizerType = value;
			}
		}

		/// <summary>
		/// The value used for <see cref="ParallelNhDescent.MinimumParallelMoves"/>
		/// in the local search and feasibility search phases of a meta-heuristic.
		/// </summary>
		public int MinimumParallelMoves
		{
			get => LocalSearchParameters.MinimumParallelMoves;
			set
			{
				LocalSearchParameters.MinimumParallelMoves = value;
				FeasibilitySearchParameters.MinimumParallelMoves = value;
			}
		}

		/// <summary>
		/// If true, the local searcher uses small neighborhoods
		/// </summary>
		public bool UseSmallNeighbourhoods
		{
			get => LocalSearchParameters.PgoPluginParameters().UseSmallNeighbourhoods;
			set
			{
				LocalSearchParameters.PgoPluginParameters().UseSmallNeighbourhoods = value;
				FeasibilitySearchParameters.PgoPluginParameters().UseSmallNeighbourhoods = value;
			}
		}

		/// <summary>
		/// The number of Descent iterations that pass before a new best solution is reported from Descent.
		/// A value of 0 means that improvements fire the BestSolutionFound event right away.
		/// A higher value can be used to limit the frequency that this event is fired in search phases where frequent
		/// improvements are found. Note that new best solutions are always reported, just a little later.
		/// The default value is 10.
		/// </summary>
		public int DescentReportingDelay { get; set; } = 10;

		/// <summary>
		/// If not null, this object will be asked to set up search visualisation. 
		/// If null, then no search visualisation will happen
		/// </summary>
		public ICanSetupSearchProfiler LocalSearchProfilerSetupper { get; set; } = null;

		/// <summary>
		/// The random number generator that optimizers should use
		/// </summary>
		public Random Random { get; set; } = new();

		/// <summary>
		/// Parameters for the local search optimizer used to improve a solution
		/// </summary>
		public StandardOptimizerParameters LocalSearchParameters { get; set; }

		/// <summary>
		/// Parameters for the local search optimizer used when removing infeasibilities
		/// from a solution
		/// </summary>
		public StandardOptimizerParameters FeasibilitySearchParameters { get; set; }

		/// <summary>
		/// Returns the standard parameters, deemed to be best for the general case
		/// </summary>
		/// <returns></returns>
		public static MetaHeuristicParameters Standard()
		{
			var parameters = new MetaHeuristicParameters()
			{
				LocalSearchParameters = OptimizerFactory.StandardLocalSearchParameters(false),
				FeasibilitySearchParameters = OptimizerFactory.StandardLocalSearchParameters(false)
			};

			return parameters;
		}

		private MetaHeuristicParameters()
		{
		}

		/// <summary>
		/// Returns a clone of the parameter set.
		/// </summary>
		/// <returns></returns>
		public MetaHeuristicParameters Clone()
		{
			return new MetaHeuristicParameters()
			{
				DescentReportingDelay = DescentReportingDelay,
				ConstructorType = ConstructorType,
				ExplorerType = ExplorerType,
				LocalSearchProfilerSetupper = LocalSearchProfilerSetupper,
				LocalSearchParameters = (StandardOptimizerParameters)LocalSearchParameters.Clone(),
				FeasibilitySearchParameters = (StandardOptimizerParameters)FeasibilitySearchParameters.Clone()
			};
		}

	}

	/// <summary>
	/// An abstract base class for meta-heuristic optimizer of <see cref="PgoProblem"/>'s. Assumes that a Descent local search is used.
	/// Andles some setup and event forwarding functionality that is typically common to meta-heuristics.
	/// </summary>
	public abstract class MetaHeuristicBase : Optimizer
	{
		/// <summary>
		/// Console output verbosity on/off. Default = false (off)
		/// </summary>
		public bool Verbose { get; set; } = false;

		/// <summary>
		/// The descent-type local search solver used for solution improvement.
		/// </summary>
		public LocalSearcher LocalSearcher => _localSearchSolver;

		/// <summary>
		/// The basic parameters of the meta-heuristic.
		/// </summary>
		protected MetaHeuristicParameters MetaHeurParams { get; }

		#region Private data members

		/// <summary>
		/// The descent-type local search solver used for solution improvement.
		/// </summary>
		protected LocalSearcher _localSearchSolver;

		/// <summary>
		/// The optimizer environment
		/// </summary>
		protected OptimizerEnvironment _environment;

		/// <summary>
		/// The best found solution value
		/// </summary>
		protected double _bestObjValue = double.PositiveInfinity;

		/// <summary>
		/// The best found solution so far
		/// </summary>
		protected ISolution _bestSolution = null;

		#endregion

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="parameters">The basic parameters of the meta-heuristic.</param>
		/// <param name="solution">The solution to be solved.</param>
		/// <param name="environment">The optimizer environment</param>
		public MetaHeuristicBase(MetaHeuristicParameters parameters, PgoSolution solution, OptimizerEnvironment environment) : base()
		{
			MetaHeurParams = parameters;
			_environment = environment;

			_localSearchSolver = OptimizerFactory.CreateLocalSearchSolver(parameters.LocalSearchParameters, solution, solution.Encoding.CriteriaSet, environment);
			_localSearchSolver.ReportBestSolutionDelay = parameters.DescentReportingDelay;
		}

		#region Private/protected methods

		/// <summary>
		/// Creates an initial feasible solution to the given problem.
		/// Returns null if a feasible solution was not found.
		/// </summary>
		/// <param name="problem"></param>
		/// <param name="criteriaSet"></param>
		/// <param name="stop"></param>
		/// <returns></returns>
		protected PgoSolution CreateInitialSolution(PgoProblem problem, ICriteriaSet criteriaSet, StopCriterion stop)
		{
			PgoSolution solution = new PgoSolution(problem);
			solution = MakeFeasible(solution, criteriaSet, stop);

			if (solution.IsFeasible(criteriaSet))
				return solution;
			else
				return null;
		}

		/// <summary>
		/// Produces a feasible solution, starting from <paramref name="sol"/>
		/// </summary>
		/// <param name="sol"></param>
		/// <param name="criteriaSet"></param>
		/// <param name="stopCriterion"></param>
		/// <returns></returns>
		protected PgoSolution MakeFeasible(PgoSolution sol, ICriteriaSet criteriaSet, StopCriterion stopCriterion)
		{
			var solver = OptimizerFactory.CreateFeasibilitySolver(MetaHeurParams, sol, _environment);
			return solver.Optimize(sol, criteriaSet, stopCriterion) as PgoSolution;
		}

		/// <summary>
		/// Runs a descent-type local seach optimization startin from <paramref name="initSol"/>
		/// </summary>
		/// <param name="initSol"></param>
		/// <param name="criteriaSet"></param>
		/// <param name="stopCrit"></param>
		/// <returns></returns>
		protected PgoSolution RunLocalSearch(PgoSolution initSol, ICriteriaSet criteriaSet, StopCriterion stopCrit)
		{
			FireStartingSubOptimizer(_localSearchSolver);

			_localSearchSolver.Iteration += _descentSolver_Iteration;
			_localSearchSolver.BestSolutionValueFound += _descentSolver_BestSolutionValueFound;
			_localSearchSolver.BestSolutionFound += _descentSolver_BestSolutionFound;
			_localSearchSolver.OptimizationStarted += _descentSolver_OptimizationStarted;

			PgoSolution sol = _localSearchSolver.Optimize(initSol, criteriaSet, stopCrit) as PgoSolution;

			_localSearchSolver.Iteration -= _descentSolver_Iteration;
			_localSearchSolver.BestSolutionValueFound -= _descentSolver_BestSolutionValueFound;
			_localSearchSolver.BestSolutionFound -= _descentSolver_BestSolutionFound;
			_localSearchSolver.OptimizationStarted -= _descentSolver_OptimizationStarted;

			return sol;


			// For visualisation of new solution values found by the local search.
			void _descentSolver_BestSolutionFound(object sender, SolutionEventArgs e)
			{
				if (Verbose)
					Console.WriteLine("New LS solution found with obj val = " + e.Solution.ObjectiveValue.ToInvariantString());
				//		if (e.Solution.ObjectiveValue.LessThanWithTolerance(_bestObjValue, 1e-9))
				{
					FireBestSolutionFound(e.Solution);
					_bestObjValue = criteriaSet.Objective.Value(e.Solution);
					_bestSolution = e.Solution;
				}
			}
		}

		/// <summary>
		/// Updates the best solution if the new given solution was an improvement.
		/// </summary>
		/// <param name="sol"></param>
		/// <param name="criteriaSet"></param>
		protected void UpdateBestSolution(ISolution sol, ICriteriaSet criteriaSet)
		{
			double objectiveValue = criteriaSet.Objective.Value(sol);
			if (objectiveValue < _bestObjValue)//.LessThanWithTolerance(bestObjValue, 1e-9))
			{
				FireBestSolutionFound(sol);
				_bestObjValue = objectiveValue;
				_bestSolution = sol.Clone();
			}
		}

		/// <summary>
		/// Initializes the current best solution based on the input (assumed initial) solution.
		/// </summary>
		/// <param name="inputSol"></param>
		/// <param name="criteriaSet"></param>
		protected void InitializeBestSolution(ISolution inputSol, ICriteriaSet criteriaSet)
		{
			if (inputSol != null)
			{
				_bestSolution = inputSol?.Clone();
				if (inputSol.IsFeasible(criteriaSet))
				{
					FireBestSolutionFound(inputSol);
					_bestObjValue = criteriaSet.Objective.Value(inputSol);
				}
				else
				{
					_bestObjValue = double.PositiveInfinity;
				}
			}
			else
			{
				_bestObjValue = double.PositiveInfinity;
				_bestSolution = null;
			}
		}

		/// <summary>
		/// Sets up a profiler for the local search, if a reference to an <see cref="ICanSetupSearchProfiler"/> was given in the constructor.
		/// </summary>
		/// <param name="inputSol"></param>
		protected void SetUpSearchProfiler(ISolution inputSol)
			=> MetaHeurParams.LocalSearchProfilerSetupper?.SetUpSearchProfiler(inputSol.Encoding, _localSearchSolver, this);

		/// <summary>
		/// Simply forwards the iteration event to the caller, so that each local search iteration is reported.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void _descentSolver_Iteration(object sender, SolutionEventArgs e)
		{
			FireIteration(e);
		}

		/// <summary>
		/// For visualisation of local search
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void _descentSolver_OptimizationStarted(object sender, OptimizeEventArgs e)
		{
			if (Verbose)
				Console.WriteLine("New local search, initial solution value = " + e.Solution.ObjectiveValue);
		}

		/// <summary>
		/// For visualisation of new solution values found by the local search.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void _descentSolver_BestSolutionValueFound(object sender, ValueEventArgs e)
		{
			if (Verbose)
				Console.WriteLine("New LS obj val = " + e.Value.ToInvariantString());
		}

		#endregion
	}
}
