using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Optimizer factory for PGO solvers
	/// </summary>
	public class OptimizerFactory : IOptimizerFactory
	{
		/// <summary>
		/// Creates an optimizer. 
		/// 
		/// Accepts parameters of type <see cref="StandardOptimizerParameters"/> or
		/// <see cref="OptimizerParameters"/>.
		/// </summary>
		/// <param name="parameters">Parameters for creating the optimizer</param>
		/// <param name="solution">The solution that will be optimized</param>
		/// <param name="criteria">The criteria to optimize for</param>
		/// <param name="environment"></param>
		/// <returns>The optimizer</returns>
		public IOptimizer CreateOptimizer(IOptimizerParameters parameters, ISolution solution, ICriteriaSet criteria,
			OptimizerEnvironment environment)
		{
			if (parameters is StandardOptimizerParameters standardParameters)
				return new StandardOptimizerFactory(new OptimizerFactoryPlugin()).CreateOptimizer(standardParameters, solution, criteria, environment);


			OptimizerParameters myParameters = (OptimizerParameters)parameters;
			PgoSolution mySolution = (PgoSolution)solution;

			bool isSinglePeriod = mySolution.PeriodCount == 1;


			switch (myParameters.Algorithm)
			{
				case AlgorithmType.RandomRestart:

					if (!isSinglePeriod)
						throw new Exception($"{myParameters.Algorithm} is unsuitable for multi-period problems");

					return CreateRandomRestartSolver(myParameters.MetaHeuristicParams, mySolution, criteria, environment);

				case AlgorithmType.SimpleMultiPeriodsolver:

					return CreateSimpleMultiPeriodSolver(myParameters, mySolution, criteria, environment);

				case AlgorithmType.RuinAndRecreate:

					return CreateRuinAndRecreateSolver(myParameters, mySolution, criteria, environment);

				default:

					throw new ArgumentException("Unknown algorithm");
			}
		}

		/// <summary>
		/// Creates a solver that can remove infeasibilities in a solution
		/// </summary>
		/// <param name="searchParams">The optimization parameters from which the type of constructor and type of neighbourhood 
		/// explorer can be determined (the latter is needed only if the chosen constructor uses local search).</param>
		/// <param name="solution">The problem that will be optimized</param>
		/// <param name="environment"></param>
		public static IOptimizer CreateFeasibilitySolver(MetaHeuristicParameters searchParams, PgoSolution solution, OptimizerEnvironment environment)
		{
			switch (searchParams.ConstructorType)
			{
				case ConstructorType.MakeRadialAndFeasibleHeuristic:
					ExplorerType? explorerType = searchParams.ExplorerType;
					if (!explorerType.HasValue)
						throw new ArgumentException("No explorer type given for the construction of a local search based feasibility solver");
					return new FeasibleSolutionConstructor(searchParams, environment);
				default:
					throw new NotImplementedException($"Cannot create feasibility solver corresponding of type {searchParams.ConstructorType}");
			}
		}

		/// <summary>
		/// Creates parameters that make the <see cref="StandardOptimizerFactory"/> create a Descent-type
		/// local search solver.
		/// </summary>
		/// <param name="useSmallNeighbourhoods">Whether to use small neighborhoods</param>
		/// <returns></returns>
		public static StandardOptimizerParameters StandardLocalSearchParameters(bool useSmallNeighbourhoods)
		{
			return new StandardOptimizerParameters
			{
				OptimizerType = OptimizerType.Descent,
				ExplorerParameters = new ExplorerParameters()
				{
					ExplorerType = ExplorerType.StatsticalExplorer
				},
				PluginParameters = new OptimizerFactoryPluginParameters()
				{
					UseSmallNeighbourhoods = useSmallNeighbourhoods
				},
				RelaxInfeasible = false
			};
		}

		/// <summary>
		/// Creates a <see cref="Descent"/>-type optimizer for multi-period problems 
		/// from the given parameters.
		/// </summary>
		/// <param name="parameters">The optimizer's parameters.</param>
		/// <param name="solution">The initial solution.</param>
		/// <param name="criteria">The optimization criteria set.</param>
		/// <param name="environment">The shared services of the optimization environment.</param>
		/// <returns>The new Descent object.</returns>
		public static LocalSearcher CreateLocalSearchSolver(StandardOptimizerParameters parameters, PgoSolution solution,
			CriteriaSet criteria, OptimizerEnvironment environment)
		{
			return new StandardOptimizerFactory(new OptimizerFactoryPlugin())
				.CreateOptimizer(parameters, solution, criteria, environment) as LocalSearcher;
		}

		/// <summary>
		/// Creates a random restart solver. This solver is suitable only for single period problems.
		/// </summary>
		/// <param name="parameters">Parameters for creating the solver</param>
		/// <param name="solution">The solution that will be optimized</param>
		/// <param name="criteria"></param>
		/// <param name="environment"></param>
		public static IOptimizer CreateRandomRestartSolver(MetaHeuristicParameters parameters, PgoSolution solution, ICriteriaSet criteria, OptimizerEnvironment environment)
		{
			//Set up the meta-heuristic solver				
			return new RandomRestartMetaHeuristic(solution, parameters, environment);
		}


		/// <summary>
		/// Creates a ruin and recreate solver.
		/// </summary>
		/// <param name="parameters">Parameters for creating the solver</param>
		/// <param name="solution">The solution that will be optimized</param>
		/// <param name="criteria"></param>
		/// <param name="environment"></param>
		private static Optimizer CreateRuinAndRecreateSolver(OptimizerParameters parameters, PgoSolution solution, ICriteriaSet criteria, OptimizerEnvironment environment)
		{
			return new RuinAndRecreateMultiPeriod(solution, parameters.MetaHeuristicParams, environment);
		}

		/// <summary>
		/// Creates a 'simple multiperiod solver'
		/// </summary>
		/// <param name="parameters">Parameters for creating the solver</param>
		/// <param name="solution">The solution that will be optimized</param>
		/// <param name="criteria"></param>
		/// <param name="environment"></param>
		private static Optimizer CreateSimpleMultiPeriodSolver(OptimizerParameters parameters, PgoSolution solution, ICriteriaSet criteria, OptimizerEnvironment environment)
		{
			TimeSpan timeout = parameters.SimpleMPSolverSolverTimeOut;
			if (timeout == default(TimeSpan))
			{
				double secPerPeriod = 5;
				int numPeriods = solution.PeriodCount;
				timeout = TimeSpan.FromSeconds(numPeriods * secPerPeriod);
			}
			int maxIterations = parameters.SimpleMPSolverMaxIterations > 0 ? parameters.SimpleMPSolverMaxIterations : int.MaxValue;
			return new SimpleMultiPeriodsolver(parameters.MetaHeuristicParams, timeout, maxIterations, environment);
		}
	}

	/// <summary>
	/// A plugin for the <see cref="StandardOptimizerFactory"/>, for creating PGO neighborhood selectors
	/// </summary>
	public class OptimizerFactoryPlugin : IStandardOptimizerFactoryPlugin
	{
		/// <summary>
		/// Creates a neighborhood selector for Descent
		/// </summary>
		public INeighborhoodSelector CreateDescentSelector(Encoding encoding, StandardOptimizerFactoryPluginParameters parameters)
		{
			var myParameters = parameters as OptimizerFactoryPluginParameters;
			var problem = encoding as PgoProblem;

			var nhs = CloseSwitchAndOpenOtherNeighbourhood.GenerateNeighbourhoodsFromNetworkPerTimePeriod(
				problem, myParameters.UseSmallNeighbourhoods, myParameters.RandomizeOrder);

			var innerSelector = SequentialSelector.Build()
				.SelectEachOf(nhs).Once
				.RepeatUntilNoMoveChosen();

			var selector = new RetryImprovingMoveInAdjacentPeriodsSelector(innerSelector);

			return selector;
		}

		/// <summary>
		/// Creates a neighborhood selector for focal point search
		/// </summary>
		public FocalNeighborhoodSelector CreateFocalPointSelector(Encoding encoding, StandardOptimizerFactoryPluginParameters parameters)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Creates a neighborhood selector for Tabu search
		/// </summary>
		public INeighborhoodSelector CreateTabuSelector(Encoding encoding, StandardOptimizerFactoryPluginParameters parameters)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Creates a tabu list of PGO
		/// </summary>
		public ITabuList CreateTabuList(Encoding encoding, StandardOptimizerFactoryPluginParameters parameters)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Creates a move dependence rule of type <see cref="MoveDependenceRule"/>
		/// </summary>
		public IDependenceRule CreateDependenceRule(Encoding encoding, StandardOptimizerFactoryPluginParameters parameters)
		{
			var myParameters = parameters as OptimizerFactoryPluginParameters;
			var problem = encoding as PgoProblem;

			return new MoveDependenceRule();
		}

		public SolutionFixer CreateSolutionFixer(Encoding encoding, StandardOptimizerFactoryPluginParameters parameters)
		{
			throw new NotImplementedException();
		}
	}

	/// <summary>
	/// Parameters for the <see cref="OptimizerFactoryPlugin"/>
	/// </summary>
	public class OptimizerFactoryPluginParameters : StandardOptimizerFactoryPluginParameters
	{
		/// <summary>
		/// If true, uses small neighborhoods
		/// </summary>
		public bool UseSmallNeighbourhoods { get; set; }

		/// <summary>
		/// If true, the order in which neighborhoods are generated, is randomized
		/// </summary>
		public bool RandomizeOrder { get; internal set; }
	}

	/// <summary>
	/// Extension methods for the optimizer factory
	/// </summary>
	public static class OptimizerFactoryExtensions
	{
		/// <summary>
		/// Returns the PGO plugin parameters of <paramref name="parameters"/>
		/// </summary>
		public static OptimizerFactoryPluginParameters PgoPluginParameters(this StandardOptimizerParameters parameters)
			=> (OptimizerFactoryPluginParameters)parameters.PluginParameters;
	}
}