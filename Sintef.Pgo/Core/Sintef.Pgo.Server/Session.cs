using System;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sintef.Scoop.Utilities;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;

namespace Sintef.Pgo.Server
{
	/// <summary>
	/// Base class for optimisation sessions, implementing <see cref="ISession"/>.
	/// </summary>
	public class Session : ISession
	{
		#region Public properties 

		/// <inheritdoc/>
		public string Id { get; private set; }

		/// <summary>
		/// The ID of the network that this session is using.
		/// </summary>
		public string NetworkId { get; private set; }

		/// <summary>
		/// Solver running state, true if the solver is running, false if not.
		/// </summary>
		public bool OptimizationIsRunning { get; private set; } = false;

		/// <summary>
		/// The objective value of the best solution
		/// </summary>
		public double BestSolutionValue => _bestSolution.ObjectiveValue;

		/// <summary>
		/// True if the best solution is feasible, false if not
		/// </summary>
		public bool BestSolutionIsFeasible => _bestSolution.IsFeasible;

		/// <summary>
		/// Returns the currently best known solution to the problem.
		/// Returns null if none has been found.
		/// </summary>
		public IPgoSolution BestSolution => _bestSolution;

		/// <summary>
		/// Returns the IDs of all solutions held by the session
		/// </summary>
		public IEnumerable<string> SolutionIds
		{
			get
			{
				IEnumerable<string> result = _solutions.Keys;
				if (_bestSolution != null)
					result = result.Concat("best");

				return result;
			}
		}

		/// <summary>
		/// The network configuration problem that this session is for.
		/// </summary>
		public PgoProblem Problem { get; private set; }

		/// <summary>
		/// The default algorithm to use, depending on the kind of <see cref="Problem"/> we are facing.
		/// </summary>
		public AlgorithmType DefaultAlgorithm
		{
			get
			{
				if (Problem.Periods.Count() == 1)
					return AlgorithmType.RuinAndRecreate;
				else
					return AlgorithmType.RuinAndRecreate;
			}
		}

		/// <summary>
		/// True if the session is not optimizing, and the timeout interval has elapsed since
		/// the last time any of these events took place
		///		- The session was created
		///		- Optimization stopped
		///		- The timeout interval was changed
		///		- A solution was added or removed
		/// </summary>
		public bool HasTimedOut => !OptimizationIsRunning && DateTime.Now > _timeoutTime;

		#endregion

		#region Private data members

		/// <summary>
		/// The best found solution thus far
		/// </summary>
		private PgoSolution _bestSolution;

		/// <summary>
		/// The additional solutions, by ID
		/// </summary>
		private Dictionary<string, PgoSolution> _solutions = new Dictionary<string, PgoSolution>();

		/// <summary>
		/// Used to stop optimization on demand.
		/// </summary>
		private StopCriterion _stopCriterion = null;

		/// <summary>
		/// The task running optimization. Null if optimization is not running
		/// </summary>
		private Task _optimizeTask = null;

		/// <summary>
		/// The network manager that we get from the server
		/// </summary>
		private NetworkManager _networkManager;

		/// <summary>
		/// The timeout interval
		/// </summary>
		private TimeSpan _timeoutInterval;

		/// <summary>
		/// The time at which the timeout inteval ends
		/// </summary>
		private DateTime _timeoutTime;

		#endregion

		#region Events

		/// <summary>
		/// Raised when optimization has started
		/// </summary>
		public event EventHandler<OptimizationStartedEventArgs> OptimizationStarted;

		/// <summary>
		/// Event raised each time a new best solution is found, communicating the full solution.
		/// </summary>
		public event EventHandler<SolutionEventArgs> BestSolutionFound;

		/// <summary>
		/// Event raised each time a new best solution is found, communicating the objective value
		/// </summary>
		public event EventHandler<ValueEventArgs> BestSolutionValueFound;

		/// <summary>
		/// Raised when optimization has stopped, whether by user action or because the algorithm was done.
		/// </summary>
		public event EventHandler OptimizationStopped;

		#endregion

		#region Construction

		/// <summary>
		/// Creates a Session based on the json file format. Takes the ID by which the session is identified. 
		/// Creates the optimisation problem that the session is for.
		/// </summary>
		/// <param name="id">The id of the new session.</param>
		/// <param name="networkId">The id of the network.</param>
		/// <param name="networkManager">The manager of the power network on which the session's problem is defined.</param>
		/// <param name="forecast">forecast data for the problem</param>
		/// <param name="startConfiguration">The configuration that is used before the start of the first period.</param>
		/// <param name="endConfiguration">The configuration that is desired after the end of the last period. Optional.</param>
		public static Session Create(string id, string networkId, NetworkManager networkManager, List<PeriodData> forecast, NetworkConfiguration startConfiguration,
			NetworkConfiguration endConfiguration = null)
		{
			IFlowProvider flowProvider = Utils.CreateFlowProvider(FlowApproximation.IteratedDF);

			var problem = new PgoProblem(forecast, flowProvider, id, startConfiguration, endConfiguration);

			return new Session(id, problem, networkManager)
			{
				NetworkId = networkId,
			};
		}

		/// <summary>
		/// Initializes a session
		/// </summary>
		/// <param name="id"></param>
		/// <param name="problem"></param>
		/// <param name="networkManager"></param>
		public Session(string id, PgoProblem problem, NetworkManager networkManager)
		{
			Id = id;
			Problem = problem;
			_networkManager = networkManager;

			// Avoid timing out unless we're told to
			_timeoutTime = DateTime.MaxValue;
			_timeoutInterval = TimeSpan.FromDays(365);

			// Add a new trivial best solution and compute its flow.
			_bestSolution = problem.StartConfiguration != null ?
				ConstructUnchangingSolution() : ConstructEmptySolution();
			ComputeFlow(_bestSolution);
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Runs optimization. The best known solution at any time during
		/// the run is communicated by the <see cref="BestSolutionFound"/> event. To check whether
		/// the solver is running, see <see cref="OptimizationIsRunning"/>.
		/// The task completes when optimization has stopped.
		/// </summary>
		public Task StartOptimization(StopCriterion stopCriterion) => StartOptimization(null, stopCriterion);

		/// <summary>
		/// Runs optimization. The best known solution at any time during
		/// the run is communicated by the <see cref="BestSolutionFound"/> event. To check whether
		/// the solver is running, see <see cref="OptimizationIsRunning"/>.
		/// The task completes when optimization has stopped.
		/// </summary>
		/// <param name="inputSol">If not null, this is the initial solution as a starting point for the optimizer. This object will not be modified.
		/// If the inputSolution is null, the optimization will continue from the best known solution (or from an empty solution if no solution has
		/// been found yet).</param>
		/// <param name="stopCriterion">If not null, the optimization will stop when this stop
		///   criterion is triggered</param>
		public async Task StartOptimization(PgoSolution inputSol = null, StopCriterion stopCriterion = null)
		{
			OptimizerParameters parameters = new OptimizerParameters()
			{
				Algorithm = DefaultAlgorithm,
				OptimizerType = OptimizerType.Descent,
				ExplorerType = ExplorerType.DeltaExplorer,
				
				// 
				// The following configuration might give better performance, but we experienced
				// mysterious freezes in ASP.NET on cloud deployments using this. We suspect that it
				// was caused by starvation of ASP.NET in the async scheduler when the number of CPUs
				// is low, and we have disabled task-based parallel processing here until this
				// has been further investigated.
				//
				// OptimizerType = OptimizerType.ParallelDescent,
				// MinimumParallelMoves = 250,
				// ExplorerType = ExplorerType.ParallelExporer
			};

			PgoSolution initSolution = inputSol;
			if (initSolution == null)
			{
				// Gather all solutions we know
				var solutions = _solutions.Values.ToList();
				if (_bestSolution != null)
					solutions.Add(_bestSolution);

				// Choose the best feasible one as the initial solution
				solutions = solutions.Where(s => s.IsFeasible(Problem.CriteriaSet)).ToList();
				if (solutions.Any())
					initSolution = solutions.ArgMin(s => Problem.CriteriaSet.Objective.Value(s));
				else
					// .. or create a new one if we have none
					initSolution = ConstructEmptySolution();
			}

			await Optimize(initSolution, parameters, Problem.CriteriaSet, stopCriterion);
		}

		/// <summary>
		/// Stops any running optimization. If SolverIsRunning = false,
		/// nothing happens.  the OptimizationStopped event will be raised when the algorithm has terminated.
		/// The SolverIsRunning flag remains true until the 
		/// optimization has actually stopped.
		/// The task completes when optimization has stopped.
		/// </summary>
		public async Task StopOptimization()
		{
			_stopCriterion?.Trigger();

			if (_optimizeTask != null)
				await _optimizeTask;

			_optimizeTask = null;
		}

		/// <summary>
		/// Returns a clone of the best known solution
		/// </summary>
		/// <returns></returns>
		public IPgoSolution GetBestSolutionClone()
		{
			if (_bestSolution == null)
				return null;

			PgoSolution clone;
			lock (_bestSolution)
			{
				clone = _bestSolution.CloneWithFlows();
			}
			return clone;
		}

		/// <summary>
		/// Gets the session's solution with the given ID. The solution was added earlier using
		/// <see cref="AddSolution"/>, or, if the ID is 'best', a copy of the best solution.
		/// Returns null if the solution does not exist.
		/// </summary>
		public IPgoSolution GetSolution(string solutionId)
		{
			if (solutionId == "best")
			{
				if (_bestSolution == null)
					return null;

				return GetBestSolutionClone();
			}

			if (_solutions.TryGetValue(solutionId, out var solution))
				return solution;

			return null;
		}

		/// <summary>
		/// Constructs an "empty" solution, that is, a solution in which all
		/// switches are closed for all time periods.
		/// </summary>
		public PgoSolution ConstructEmptySolution()
		{
			return new PgoSolution(Problem);
		}

		/// <summary>
		/// Creates a solution where the configuration in each period is equal to the
		/// problem's start configuration
		/// </summary>
		public PgoSolution ConstructUnchangingSolution()
		{
			return PgoSolution.CreateUnchangingSolution(Problem);
		}

		/// <summary>
		/// Sets objective weights in the current <see cref="Problem"/>.
		/// </summary>
		/// <param name="objectiveComponentWeights"></param>
		public void SetObjectiveWeights(IEnumerable<ObjectiveWeight> objectiveComponentWeights)
		{
			Problem.SetObjectiveWeights(objectiveComponentWeights);
		}

		/// <summary>
		/// Returns the currently used objective weights.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<ObjectiveWeight> GetObjectiveWeights()
		{
			AggregateObjective agg = Problem.CriteriaSet.Objective as AggregateObjective;
			return agg.WeightedComponents.Select(tup => new ObjectiveWeight() { ObjectiveName = (tup.Item1 as Criterion).Name, Weight = tup.Item2 });
		}

		/// <summary>
		/// Get the demands that were used to create this session.
		/// </summary>
		/// <returns></returns>
		public Demand GetDemands() => PgoJsonParser.ConvertToJson(Problem.AllPeriodData);

		/// <summary>
		/// Repair a solution (connect components, remove cycles, make transformers use valid modes).
		/// </summary>
		/// <param name="oldSolution"></param>
		/// <returns></returns>
		public IPgoSolution RepairSolution(IPgoSolution oldSolution)
		{
			var solution = (oldSolution as PgoSolution).CloneWithFlows();
			var aggregateSolution = Aggregate(solution);
			aggregateSolution.MakeRadialFlowPossible(new Random(42));
			solution.CopySwitchSettingsFrom(aggregateSolution, _networkManager.AcyclicAggregator);
			return solution;
		}

		/// <summary>
		/// Repairs the given solution and adds the result to the solution pool.
		/// </summary>
		/// <param name="oldSolution">The solution to repair</param>
		/// <param name="newSolutionId">The ID to give the repaired solution</param>
		/// <returns>A message describing the repair</returns>
		public string Repair(IPgoSolution oldSolution, string newSolutionId)
		{
			if (RepairSolution(oldSolution) is not PgoSolution solution)
				throw new Exception("Could not repair solution");

			ComputeFlow(solution);
			AddSolution(solution, newSolutionId);

			// Make an analysis of changes to report back to the user.
			var switchesOpened = 0;
			var switchesClosed = 0;

			foreach (var ((p1, oldSetting), (p2, newSetting)) in (oldSolution as PgoSolution).SwitchSettingsPerPeriod.Zip(solution.SwitchSettingsPerPeriod))
			{
				switchesClosed += newSetting.ClosedSwitches.Where(sw => !oldSetting.IsClosed(sw)).Count();
				switchesOpened += newSetting.OpenSwitches.Where(sw => oldSetting.IsClosed(sw)).Count();
			}

			return $"Repair was successful after opening {switchesOpened} and closing {switchesClosed} switches.";
		}

		/// <summary>
		/// Returns summary information about the given solution
		/// </summary>
		public SolutionInfo Summarize(IPgoSolution solution)
		{
			if (solution.Encoding != Problem)
				throw new ArgumentException("The solution is not for this session's problem");

			SolutionInfo summary = solution.Summarize(Problem.CriteriaSet);

			// The reason for infeasibility may be that the configuration(s) is not radial.
			// This is not checked for in the full problem, so do it for the aggregated problem
			var aggregateSolution = Aggregate(solution);

			var badPeriods = aggregateSolution.SinglePeriodSolutions
				.Where(s => !s.AllowsRadialFlow(requireConnected: false))
				.Select(s => s.Period);

			if (badPeriods.Any())
			{
				summary.IsFeasible = false;
				summary.ViolatedConstraints.Add(new ConstraintViolationInfo
				{
					Name = "Radial configuration",
					Description = "The configuration is not radial in periods: " + badPeriods.Select(p => p.Id).Concatenate(", ")
				});
			}

			if (aggregateSolution.SinglePeriodSolutions.FirstOrDefault(p => p.UnconnectedConsumersWithDemand.Any()) is PeriodSolution unconnectedPeriodSol)
			{
				summary.IsFeasible = false;
				var unconnected = unconnectedPeriodSol.UnconnectedConsumersWithDemand;

				var unconnectedLoad = unconnectedPeriodSol.UnconnectedConsumersWithDemand.Select(b => unconnectedPeriodSol.PeriodData.Demands.ActivePowerDemand(b)).Sum();
				var totalLoad = unconnectedPeriodSol.Network.Consumers.Select(b => unconnectedPeriodSol.PeriodData.Demands.ActivePowerDemand(b)).Sum();
				var unconnectedLoadRatio = unconnectedLoad / totalLoad;

				summary.ViolatedConstraints.Add(new ConstraintViolationInfo
				{
					Name = "Connnected consumers",
					Description = Invariant($"These consumers are unconnected and have a demand of {unconnectedLoad:N2} VA active power ({unconnectedLoadRatio:P} of total active power demand) in period {unconnectedPeriodSol.Period.Id}: {string.Join(", ", unconnected)}")
				});
			}

			if (aggregateSolution.SinglePeriodSolutions.FirstOrDefault(p => p.NetConfig.HasCycles) is PeriodSolution cyclePeriodSol)
			{
				var cycle = cyclePeriodSol.NetConfig.CycleBridges.Select(b => cyclePeriodSol.NetConfig.FindCycleWithBridge(b))
					.ArgMin(c => c.Count());
				var disaggregatedCycle = _networkManager.AcyclicAggregator.OneDisaggregatedPath(cycle)
					.Select(dl => dl.Line.Name);
				summary.ViolatedConstraints.Add(new ConstraintViolationInfo
				{
					Name = "Cyclic power flow",
					Description = $"There are {cyclePeriodSol.NetConfig.CycleBridges.Count()} cycles in period {cyclePeriodSol.Period.Id}. The first cycle consists of these lines: {string.Join(", ", disaggregatedCycle)}"
				});
			}

			var badTransformerPeriods = aggregateSolution.SinglePeriodSolutions
				.Where(s => s.HasTransformersUsingMissingModes)
				.Select(s => s.Period);

			if (badTransformerPeriods.Any())
			{
				var constraintViolationInfo = new ConstraintViolationInfo
				{
					Name = "Invalid tranformer modes",
					Description = "The configuration uses one or more missing transformer modes in periods: " + badTransformerPeriods.Select(p => p.Id).Concatenate(", ")
				};

				summary.ViolatedConstraints.Add(constraintViolationInfo);

				if (aggregateSolution.SinglePeriodSolutions.FirstOrDefault(p => p.HasTransformersUsingMissingModes) is PeriodSolution missingModesPeriodSol)
				{
					var examples = missingModesPeriodSol.NetConfig.TransformersUsingMissingModes
						.Select(t => $"transformer connecting {string.Join("/", t.Terminals.Select(b => b.ToString()))}");
					constraintViolationInfo.Description += $"\nThese transformers are used in invalid modes in period {missingModesPeriodSol.Period.Id}: {string.Join(", ", examples)}";
				}
			}

			return summary;
		}

		/// <summary>
		/// Ensures that the given solution has a computed flow for all periods where it is radial
		/// (on the aggregated network)
		/// </summary>
		public void ComputeFlow(PgoSolution solution)
		{
			var aggregateSolution = Aggregate(solution);

			foreach (var periodSolution in aggregateSolution.SinglePeriodSolutions)
			{
				periodSolution.ComputeFlow(aggregateSolution.Problem.FlowProvider);
			}

			solution.CopyDisaggregatedFlows(aggregateSolution, _networkManager.AcyclicAggregator);
		}

		/// <summary>
		/// Adds a solution to the session
		/// </summary>
		/// <param name="solution">The solution</param>
		/// <param name="solutionId">The ID to reference the solution by</param>
		public void AddSolution(IPgoSolution solution, string solutionId)
		{
			if (solutionId == "best")
				throw new ArgumentException("The solution ID 'best' is reserved for the best known solution");

			if (_solutions.ContainsKey(solutionId))
				throw new ArgumentException($"There is already a solution with ID '{solutionId}'");

			_solutions.Add(solutionId, (PgoSolution)solution);

			// Reset the timeout
			_timeoutTime = DateTime.Now + _timeoutInterval;
		}

		/// <summary>
		/// Updates an existing solution
		/// </summary>
		/// <param name="solution">The solution</param>
		/// <param name="solutionId">The solution ID</param>
		public void UpdateSolution(PgoSolution solution, string solutionId)
		{
			if (solutionId == "best")
				throw new ArgumentException("The solution with ID 'best' can not be updated");

			if (!_solutions.ContainsKey(solutionId))
				throw new ArgumentException($"There is no solution with ID '{solutionId}'");

			_solutions[solutionId] = solution;

			// Reset the timeout
			_timeoutTime = DateTime.Now + _timeoutInterval;
		}

		/// <summary>
		/// Removes a solution from the session
		/// </summary>
		/// <param name="solutionId">The ID of the solution to remove</param>
		public void RemoveSolution(string solutionId)
		{
			if (solutionId == "best")
				throw new ArgumentException("Cannot remove the 'best' solution");

			if (!_solutions.ContainsKey(solutionId))
				throw new ArgumentException($"There is no solution with ID '{solutionId}'");

			_solutions.Remove(solutionId);

			// Reset the timeout
			_timeoutTime = DateTime.Now + _timeoutInterval;
		}

		/// <summary>
		/// Sets the interval after which the session times out
		/// </summary>
		public void SetTimesOutAfter(TimeSpan time)
		{
			_timeoutInterval = time;
			_timeoutTime = DateTime.Now + _timeoutInterval;
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Returns the aggregated version of the given solution (using
		/// acyclic aggregation)
		/// </summary>
		private PgoSolution Aggregate(IPgoSolution solution)
		{
			NetworkAggregation aggregator = _networkManager.AcyclicAggregator;

			var aggregateProblem = Problem.CreateAggregatedProblem(aggregator, true);
			var aggregateSolution = new PgoSolution(aggregateProblem);
			aggregateSolution.CopySwitchSettingsFrom(solution, aggregator);

			return aggregateSolution;
		}

		/// <summary>
		/// Execute optimization, using events to communicate state. The best known solution at any time during
		/// the run is communicated by the <see cref="BestSolutionFound"/> event. To check whether
		/// the solver is running, see <see cref="OptimizationIsRunning"/>.
		/// </summary>
		/// <param name="solution">The input solution</param>
		/// <param name="parameters">Parameters for the optimization</param>
		/// <param name="criteriaSet">The criteria to optimize for</param>
		/// <param name="stopCriterion">Stopping criterion (for asynchronous cancellation)</param>
		private async Task Optimize(PgoSolution solution, OptimizerParameters parameters, CriteriaSet criteriaSet, StopCriterion stopCriterion)
		{
			_stopCriterion = stopCriterion ?? new StopCriterion();

			//Optimize
			OptimizationIsRunning = true;

			RaiseOptimizationStarted(solution, parameters, criteriaSet);

			try
			{
				// Create solver; any exception here will cause immediate failure
				IOptimizer solver = new ConfigOptimizerFactory(_networkManager).CreateOptimizer(parameters, solution, criteriaSet, new OptimizerEnvironment());

				// Then run the solver on a background thread
				_optimizeTask = Task.Run(() => { RunOptimization(solution, parameters, solver); });

				await _optimizeTask;
			}
			finally
			{
				// Reset the timeout
				_timeoutTime = DateTime.Now + _timeoutInterval;

				OptimizationIsRunning = false;

				RaiseOptimizationStopped();
			}
		}

		/// <summary>
		/// Runs the optimization
		/// </summary>
		/// <param name="solution">The input solution, will not be modified during the search.</param>
		/// <param name="parameters">Parameters for the optimization</param>
		/// <param name="solver">The solver, created from the parameters</param>
		private void RunOptimization(PgoSolution solution, OptimizerParameters parameters, IOptimizer solver)
		{
			solver.BestSolutionFound += OnBestSolutionFound;
			solver.BestSolutionValueFound += OnBestSolutionValueFound;

			try
			{
				solution = solver.Optimize(solution.Clone(), _stopCriterion) as PgoSolution;

#if DEBUG
				if (solution.ObjectiveValue != _bestSolution.ObjectiveValue)
					throw new Exception("Bad!");
#endif
			}
			finally
			{
				solver.BestSolutionFound -= OnBestSolutionFound;
				solver.BestSolutionValueFound -= OnBestSolutionValueFound;
			}
		}

		/// <summary>
		/// Raises the <see cref="OptimizationStarted"/> event.
		/// </summary>
		private void RaiseOptimizationStarted(PgoSolution solution, OptimizerParameters parameters, CriteriaSet criteriaSet)
		{
			var args = new OptimizationStartedEventArgs(solution, parameters, criteriaSet);
			OptimizationStarted?.Invoke(this, args);
		}

		/// <summary>
		/// Raises the <see cref="OptimizationStopped"/> event.
		/// </summary>
		private void RaiseOptimizationStopped() => OptimizationStopped?.Invoke(this, EventArgs.Empty);

		/// <summary>
		/// Forwards best solution found events from the solver mechanisms to the caller.
		/// </summary>
		private void OnBestSolutionFound(object sender, SolutionEventArgs e)
		{
			UpdateBestSolution(e.Solution as PgoSolution);
		}

		/// <summary>
		/// Updates the best solution with this one, if it is better
		/// </summary>
		/// <param name="solution"></param>
		private void UpdateBestSolution(PgoSolution solution)
		{
			_bestSolution = solution;
			BestSolutionFound?.Invoke(this, new SolutionEventArgs(_bestSolution));
		}

		private void OnBestSolutionValueFound(object sender, ValueEventArgs e)
		{
			BestSolutionValueFound?.Invoke(sender, e);
		}

		/// <summary>
		/// Formats a string using the Invariant locale
		/// </summary>
		private string Invariant(FormattableString s) => FormattableString.Invariant(s);

		#endregion
	}

	/// <summary>
	/// Arguments for the <see cref="Session.OptimizationStarted"/> event
	/// </summary>
	public class OptimizationStartedEventArgs
	{
		/// <summary>
		/// The solution optimization starts from
		/// </summary>
		public PgoSolution Solution { get; }

		/// <summary>
		/// The parameters for optmizing
		/// </summary>
		public OptimizerParameters Parameters { get; }

		/// <summary>
		/// The criteria set defining the constraints and objectives
		/// </summary>
		public CriteriaSet CriteriaSet { get; }

		/// <summary>
		/// Initializes the event arguments
		/// </summary>
		public OptimizationStartedEventArgs(PgoSolution solution, OptimizerParameters parameters, CriteriaSet criteriaSet)
		{
			Solution = solution;
			Parameters = parameters;
			CriteriaSet = criteriaSet;
		}
	}
}
