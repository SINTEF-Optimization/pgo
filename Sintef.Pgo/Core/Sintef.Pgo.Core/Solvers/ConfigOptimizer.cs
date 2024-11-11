using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// The overall optimizer, used as the access point for optimizing the network configuration.
	/// Uses simplified/aggregated problems and power networks to speed up the optimization.
	/// </summary>
	public class ConfigOptimizer : Optimizer, IAnalysable
	{
		#region Public properties 

		/// <summary>
		/// When a solution value is less than this <see cref="Gap"/> away from the lower bound,
		/// we consider the solution to be optimal.
		/// </summary>
		public double GapTolerance { get; private set; }

		/// <summary>
		/// Parameters for building the inner solver
		/// </summary>
		public OptimizerParameters Parameters { get; }

		/// <summary>
		/// The Meta-heuristic that is in use, if any. Null, otherwise.
		/// </summary>
		public MetaHeuristicBase MetaHeuristic => _solver as MetaHeuristicBase;

		/// <summary>
		/// If true, the optimizer will aggregate the problem it's given to be acyclic
		/// and connected before passing it to the inner optimizer.
		/// </summary>
		public bool Aggregates { get; set; }

		/// <summary>
		/// If true, the optimizer will substitute the problem's flow solver with
		/// a <see cref="SimplifiedDistFlowProvider"/> before passing it to the inner optimizer.
		/// </summary>
		public bool SubstitutesFlowProvider { get; set; }

		#endregion

		#region Private data members

		/// <summary>
		/// The problem currently under optimization, as given to <see cref="Optimize"/>
		/// </summary>
		private PgoProblem _inputProblem;

		/// <summary>
		/// The criteria set we use for evaluating solutions to the problem under inner optimization.
		/// </summary>
		private ICriteriaSet _evaluationCritSet;

		/// <summary>
		/// The criteria set we use for evaluating the degree of infeasibility of
		/// solutions to the problem under inner optimization.
		/// </summary>
		private ICriteriaSet _relaxedEvaluationCritSet;

		/// <summary>
		/// The final criteria set we use for evaluating the disaggregated solution
		/// and reporting back solutions to the caller.
		/// </summary>
		private ICriteriaSet _reportingCritSet;

		/// <summary>
		/// The relaxed version of the final criteria set we use for evaluating the disaggregated solution
		/// and reporting back solutions to the caller.
		/// </summary>
		private ICriteriaSet _relaxedReportingCritSet;

		/// <summary>
		/// This is used to create aggregated problems.
		/// </summary>
		private NetworkManager _networkManager;

		/// <summary>
		/// The optimizer environment
		/// </summary>
		private OptimizerEnvironment _environment;

		/// <summary>
		/// The best aggregated solution found so far, received from the 
		/// optimizer and verified using iterated dist flow.
		/// </summary>
		private (PgoSolution sol, double value, double infeasibility) _bestAggregatedSolution
			= (null, double.PositiveInfinity, double.PositiveInfinity);

		/// <summary>
		/// The best full solution found so far, received from the 
		/// optimizer and verified using iterated dist flow.
		/// </summary>
		private (PgoSolution sol, double value, double infeasibility) _bestFullSolution
			= (null, double.PositiveInfinity, double.PositiveInfinity);

		/// <summary>
		/// The inner solver
		/// </summary>
		private IOptimizer _solver;

		#region IAnalysis-related members

		/// <summary>
		/// The stop criterion used in the current optimization, for stopping runs during analysis.
		/// This is reset on each analysis run.
		/// This is used to connect the _analysisStopInfo with the Optimizer functionality.
		/// </summary>
		private StopCriterion _analysisStopCriterion;

		/// <summary>
		/// Used by the analysis framework to stop the optimization on timeout or iterations.
		/// </summary>
		private AnalysisStopInfo _analysisStopInfo;

		/// <summary>
		/// Iteration counter used with <see name="_analysisStopInfo"/>.
		/// </summary>
		int _iterationCounter;

		/// <summary>
		/// Timer used with <see name="_analysisStopInfo"/>.
		/// </summary>
		HiPerfTimer _timer;

		#endregion

		#endregion

		#region Construction

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="innerSolverParameters">Parameters for building the inner solver</param>
		/// <param name="networkManager">This is used to create aggregated problems.</param>
		/// <param name="environment">The shared services of the optimization environment.</param>
		public ConfigOptimizer(OptimizerParameters innerSolverParameters, NetworkManager networkManager,
			OptimizerEnvironment environment)
		{
			Parameters = innerSolverParameters;
			_networkManager = networkManager;
			_environment = environment;
		}

		#endregion

		#region Public methods

		#region Optimizer implementation

		/// <summary>
		/// Constructs the suitable optimization algorithms, and runs them on the aggregated problem.
		/// Solutions that are produced are for the original problem.
		/// </summary>
		/// <param name="solution"></param>
		/// <param name="criteriaSet"></param>
		/// <param name="stopCriterion"></param>
		/// <returns></returns>
		public override ISolution Optimize(ISolution solution, ICriteriaSet criteriaSet, StopCriterion stopCriterion)
		{
			FireOptimizationStarted(solution, criteriaSet, stopCriterion);

			_analysisStopCriterion = new StopCriterion();
			_analysisStopCriterion.AttachTo(stopCriterion);

			PgoSolution pgoSolution = solution as PgoSolution;
			_inputProblem = pgoSolution.Problem;
			_reportingCritSet = criteriaSet;
			_relaxedReportingCritSet = _reportingCritSet.GetRelaxed();

			PgoProblem problem = _inputProblem;

			if (Aggregates)
			{
				// Convert to an aggregated problem
				problem = _inputProblem.CreateAggregatedProblem(_networkManager.AcyclicAggregator, true);
				criteriaSet = _inputProblem.CloneCriteriaForAggregate(problem, criteriaSet, true);

				PgoSolution aggregateSolution = new PgoSolution(problem);
				aggregateSolution.CopySwitchSettingsFrom(solution as PgoSolution, _networkManager.AcyclicAggregator);
				pgoSolution = aggregateSolution;
			}

			_evaluationCritSet = criteriaSet;
			_relaxedEvaluationCritSet = _evaluationCritSet.GetRelaxed();


			// Make input solution radial if necessary
			if (!pgoSolution.SinglePeriodSolutions.All(s => s.AllowsRadialFlow(requireConnected: true)))
			{
				Random r = Parameters.MetaHeuristicParams.Random;
				foreach (var periodSolution in pgoSolution.SinglePeriodSolutions)
				{
					if (!periodSolution.AllowsRadialFlow(requireConnected: true))
						periodSolution.NetConfig.MakeRadialFlowPossible(r, false, _analysisStopCriterion);
				}
			}

			if (pgoSolution.SinglePeriodSolutions.All(s => s.AllowsRadialFlow(requireConnected: true)))
			{
				if (SubstitutesFlowProvider)
				{
					// Use Simplified DistFlow in actual optimization
					criteriaSet = criteriaSet.WithProvider(new SimplifiedDistFlowProvider());
				}

				// Run the actual optimization
				_timer?.Start();
				RunInnerOptimizer(pgoSolution, criteriaSet, _analysisStopCriterion);
			}

			// Return the result 
			if (_bestFullSolution.sol != null)
				solution = _bestFullSolution.sol;

			FireOptimizationStopped(solution, _reportingCritSet, _analysisStopCriterion);

			_evaluationCritSet = null;
			_relaxedEvaluationCritSet = null;

			_analysisStopCriterion.DetachFrom(stopCriterion);

			return solution;
		}

		#endregion

		#region IAnalysable implementation

		/// <summary>
		/// Sets the stopping information to be used by the optimiser (in addition to the
		/// stop criterion supplied in IOptimizer.Optimise). This is to provide greater flexibility in setting up
		/// stopping criteria for underlying optimisers.
		/// </summary>
		/// <param name="stop"></param>
		public void SetStopInfo(AnalysisStopInfo stop)
		{
			_analysisStopInfo = stop;
			if (_analysisStopInfo != null)
			{
				_timer = new HiPerfTimer();
			}
			else
				_timer = null;
		}

		/// <summary>
		/// Sets the given value as the objects lower bound, but only if it is actually lower
		/// than the current one.
		/// </summary>
		/// <param name="lowerBound"></param>
		public void SetLowerBound(double lowerBound)
		{
			//Not using lower bounds yet...
		}

		/// <summary>
		/// Returns the best lower bound that is known to the object.
		/// </summary>
		/// <returns></returns>
		public double GetLowerBound()
		{
			//Not using lower bounds yet. But assume all objectives have non-negative values
			return 0;
		}

		/// <summary>
		/// Resets any data for the for a new run. Some solvers need this hook..
		/// </summary>
		public void InitializeOptimiserForNewRun()
		{
			_timer?.Reset();
			_iterationCounter = 0;
			_bestAggregatedSolution = (null, double.PositiveInfinity, double.PositiveInfinity);
		}


		#endregion
		#endregion

		#region Private methods

		/// <summary>
		/// Consume the given BlockingCollection and pass the solutions received to `CheckForBestSolution`. 
		/// This function should run on a background thread during `RunInnerOptimizer`.
		/// </summary>
		private void DisaggregationFromQueue(BlockingCollection<PgoSolution> queue, CancellationToken cancel)
		{
			try
			{
				// Consume the BlockingCollection
				while (true)
				{
					var batch = new Stack<PgoSolution>(new[] { queue.Take() });
					while (batch.Count > 0)
					{
						if (cancel.IsCancellationRequested)
							return;

						// Add new better solutions if any have arrived after starting the batch.
						while (queue.TryTake(out var t))
							batch.Push(t);

						var solution = batch.Pop();

						if (CheckForBestSolution(solution))
						{
							// The higher-objective solutions from the inner criterion
							// are unlikely to be better in the evaluation criterion.
							break;
						}
					}
				}
			}
			catch (InvalidOperationException)
			{
				// The queue has been closed, nothing more to do.
			}
		}

		/// <summary>
		/// Runs the actual optimization, on the aggregated problem
		/// </summary>
		/// <param name="solution">The (aggregated) solution to start from</param>
		/// <param name="criteria">The (aggregated) criteria set to optimize on</param>
		/// <param name="stopCriterion"></param>
		private void RunInnerOptimizer(PgoSolution solution, ICriteriaSet criteria, StopCriterion stopCriterion)
		{
			if (criteria.FlowProvider().FlowApproximation != FlowApproximation.SimplifiedDF && Parameters.MetaHeuristicParams.ExplorerType != ExplorerType.ReverseExplorer)
				throw new Exception("The inner optimizer only works with Simplified DistFlow, unless a 'ReverseExplorer' is used to explore LS neighbourhoods");

			CheckForBestSolution(solution);

			// Create the inner solver
			_solver = new OptimizerFactory().CreateOptimizer(Parameters, solution, criteria, _environment);

			// Create a message queue sending solutions to the background thread for iterated distflow evaluation and disaggregation.
			var disaggregationQueue = new BlockingCollection<PgoSolution>();
			var disaggregationTaskCancellation = new CancellationTokenSource();
			var disaggregationTask = Task.Run(() => DisaggregationFromQueue(disaggregationQueue, disaggregationTaskCancellation.Token));

			void Solver_BestSolutionFound(object o, SolutionEventArgs e) => disaggregationQueue.Add(e.Solution.Clone() as PgoSolution);
			_solver.BestSolutionFound += Solver_BestSolutionFound;

			_solver.LocalOptimumFound += Solver_LocalOptimumFound;
			EventHandlerManager eventManager = new EventHandlerManager();
			eventManager.IterationHandlers.Add(Solver_Iteration, attachIf: OptimizerIsDescent);

			eventManager.StartWatching(_solver);

			if (!solution.IsFeasible(criteria))
			{
				// Solution is infeasible. Optimize using relaxed criteria first

				var relaxedCriteria = criteria.GetRelaxedFor(solution);
				OptimizerRunner runner = new OptimizerRunner(_solver) { TargetObjectiveValue = 0 };
				FireStartingSubOptimizer(runner);
				solution = runner
					.Optimize(solution, relaxedCriteria, stopCriterion)
					as PgoSolution;
				disaggregationQueue.Add(solution.Clone() as PgoSolution);
			}

			if (!stopCriterion.IsTriggered)
			{
				// Optimize
				FireStartingSubOptimizer(_solver);
				var finalSolution = _solver.Optimize(solution, criteria, stopCriterion) as PgoSolution;
				disaggregationQueue.Add(finalSolution);
			}

			// Clean up
			_solver.BestSolutionFound -= Solver_BestSolutionFound;
			_solver.LocalOptimumFound -= Solver_LocalOptimumFound;
			eventManager.StopWatching(_solver);
			_solver = null;

			// Wait for background processing to finish.
			disaggregationQueue.CompleteAdding();
			disaggregationTaskCancellation.Cancel();
			disaggregationTask.Wait();
		}


		/// <summary>
		/// True if the <paramref name="arg1"/> is a <see cref="Descent"/> object.
		/// </summary>
		/// <param name="arg1"></param>
		/// <param name="arg2"></param>
		/// <returns></returns>
		private bool OptimizerIsDescent(object arg1, OptimizeEventArgs arg2)
		{
			if (arg1 is IOptimizer opt)
			{
				return opt is Descent;
			}
			throw new Exception("Got non-optimizer object where IOptimizer was expected");
		}



		/// <summary>
		/// This is just used for analysis of the underlying seach.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Solver_Iteration(object sender, SolutionEventArgs e)
		{
			CheckForAnalysisStop();

			//Communicate the iteration event if we are running analysys
			if (_analysisStopInfo != null)
				FireIteration(e);
		}

		/// <summary>
		/// Checks if we are running under the analysis framework, and if so, whether
		/// the on-going optimization should be stopped or not.
		/// </summary>
		private void CheckForAnalysisStop()
		{
			if (_analysisStopInfo != null)
			{
				if (_analysisStopInfo.Iterationlimit > 0 && ++_iterationCounter > _analysisStopInfo.Iterationlimit)
					_analysisStopCriterion.Trigger();
				if (_analysisStopInfo.Timelimit > 0 && _timer.DurationSoFar > _analysisStopInfo.Timelimit)
				{
					_analysisStopCriterion.Trigger();
				}
			}
			else if (IsOptimal(_bestAggregatedSolution.value))
				_analysisStopCriterion.Trigger();
		}

		/// <summary>
		/// Returns true if the given value is less than <see cref="GapTolerance"/> (or equal) away from 
		/// the lower bound, <see cref="GetLowerBound"/>.
		/// In the special case that <paramref name="value"/> == 0, and <see cref="GetLowerBound"/> is negative,
		/// the gap is considered to be (value - LB)/LB = 1
		/// </summary>
		/// <param name="value"></param>
		/// <returns></returns>
		private bool IsOptimal(double value) => Gap(value).LessOrEqualWithTolerance(GapTolerance, 1e-9);

		/// <summary>
		/// Returns the Gap relative to lower bound (<see cref="GetLowerBound"/>)
		/// corresponding to the given value.
		/// By definition, gap = |value-LB|/(1e-10+|LB|)
		/// </summary>
		/// <param name="value"></param>
		/// <returns></returns>
		public double Gap(double value) => Math.Abs(value - GetLowerBound()) / (1e-10 + Math.Abs(GetLowerBound()));


		/// <summary>
		/// Passes on local optimum events
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Solver_LocalOptimumFound(object sender, SolutionEventArgs e)
			=> FireLocalOptimumFound(() => ConvertToFullSolution(e.Solution as PgoSolution));


		/// <summary>
		/// If the solution (to the aggregated problem) is a new best, converts the solution to a full solution.
		/// If the solution (to the full problem) is still a new best, it is passed on to any listeners.
		/// </summary>
		private bool CheckForBestSolution(ISolution input)
		{
			// First check the aggregated solution using the evaluation criteria set.
			var aggregatedSolution = input as PgoSolution;
			var (isBestAgg, valueAgg, infeasibilityAgg) = IsSolutionNewBest(aggregatedSolution,
				_evaluationCritSet, _relaxedEvaluationCritSet,
				_bestAggregatedSolution.value, _bestAggregatedSolution.infeasibility);

			if (!isBestAgg)
				return false;

			_bestAggregatedSolution = (aggregatedSolution, valueAgg, infeasibilityAgg);

			// Then expand into the full (disaggregated) solution and evaluate using the reporting criteria set.
			var fullSolution = ConvertToFullSolution(aggregatedSolution);

			var (isBest, value, infeasibility) = IsSolutionNewBest(fullSolution,
				_reportingCritSet, _relaxedReportingCritSet,
				_bestFullSolution.value, _bestFullSolution.infeasibility);

			if (!isBest)
				return false;

			_bestFullSolution = (fullSolution, value, infeasibility);

			FireBestSolutionFound(fullSolution);
			// In the current version of Scoop, the above line also raises BestSolutionValue, making the below unnecessary:
			//FireBestSolutionValueFound(valueAgg);

			return true;
		}

		/// <summary>
		/// Returns true if the solution is a new best compared to the given old values.
		/// Also returns the solution's objective value and degreee of infeasibility.
		/// </summary>
		private static (bool isBest, double value, double infeasibility) IsSolutionNewBest(PgoSolution solution, ICriteriaSet objectiveCriteria, ICriteriaSet infeasibilityCriteria, double prevValue, double prevInfeasibility)
		{
			if (solution == null)
				return (false, double.PositiveInfinity, double.PositiveInfinity);

			double value = objectiveCriteria.Objective.Value(solution);
			double infeasibility = infeasibilityCriteria.Objective.Value(solution);

			if (infeasibility < prevInfeasibility)
				return (true, value, infeasibility);

			if (infeasibility > prevInfeasibility)
				return (false, value, infeasibility);

			return (value < prevValue, value, infeasibility);
		}

		/// <summary>
		/// Converts the given aggregate solution to a solution for the full problem,
		/// if necessary,
		/// by copying switch settings and flows.
		/// </summary>
		/// <param name="innerSolution">A solution produced by the inner solver</param>
		/// <returns></returns>
		private PgoSolution ConvertToFullSolution(PgoSolution innerSolution)
		{
			// Ensure that all flows required by the evaluation criteria set are computed
			_evaluationCritSet.Objective.Value(innerSolution);
			innerSolution.IsFeasible(_evaluationCritSet);

			if (!Aggregates)
				return innerSolution;

			// Disaggregate switch settings and flows
			return PgoSolution.Disaggregate(innerSolution, _inputProblem, _networkManager.AcyclicAggregator);
		}

		#endregion
	}
}
