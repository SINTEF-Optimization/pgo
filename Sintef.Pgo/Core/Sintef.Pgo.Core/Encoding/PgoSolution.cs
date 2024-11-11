using System;
using System.Collections.Generic;
using System.Linq;
using Sintef.Scoop.Kernel;
using Sintef.Scoop.Kernel.ConflictbasedBranchAndBound;
using Sintef.Pgo.Core.IO;
using Sintef.Scoop.Utilities;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A solution of a <see cref="PgoProblem"/>.
	/// Contains one <see cref="PeriodSolution"/> per time period in the encoding.
	/// </summary>
	public class PgoSolution : Sintef.Scoop.Kernel.Solution, IPgoSolution, INodeSolution
	{
		#region Public properties 

		/// <summary>
		/// An enumeration of the single period solutions that are computed so far
		/// </summary>
		public IEnumerable<PeriodSolution> SinglePeriodSolutions => PeriodSolutions.Values;

		/// <summary>
		/// The problem that the solution is for.
		/// </summary>
		public PgoProblem Problem => Encoding as PgoProblem;

		/// <summary>
		/// The power network that the solution is defined on.
		/// </summary>
		public PowerNetwork PowerNetwork => Problem.Network;

		/// <summary>
		/// True when all period problems have complete period solutions and has a flow
		/// in each for the given flow provider
		/// </summary>
		public bool IsComplete(IFlowProvider flowProvider)
		{
			bool result = PeriodSolutions.Count == Problem.PeriodCount && PeriodSolutions.All(s => s.Value.Flow(flowProvider) != null);
			return result;
		}

		/// <summary>
		/// The number of periods in the problem
		/// </summary>
		public int PeriodCount => Problem.PeriodCount;

		/// <summary>
		/// Copy the switch settings from the given solution
		/// </summary>
		/// <param name="sol"></param>
		/// <param name="aggregator">The aggregation that gives the mapping between this solution's network
		///   and the network of <paramref name="sol"/></param>
		public void CopySwitchSettingsFrom(IPgoSolution sol, NetworkAggregation aggregator)
		{
			Func<Line, bool> missingLineValues = _ => false;


			// If there is a start solution, we copy switch settings from it for all switches
			// that are not present in the aggregated network.
			// Note: this is only correct as long as switches are never aggregated.
			if (Problem.StartConfiguration != null)
			{
				missingLineValues = l =>
				{
					if (Problem.Network.TryGetLine(l.Name, out var line))
					{
						return Problem.StartConfiguration.IsOpen(line);
					}
					else
					{
						return false;
					}

				};
			}

			foreach (var (src, dest) in sol.SinglePeriodSolutions.Zip(SinglePeriodSolutions))
				dest.NetConfig.CopySwitchSettingsFrom(src.NetConfig, aggregator, missingLineValues);
		}

		/// <summary>
		/// Switch settings for each period, given in chronological order of period start time.
		/// </summary>
		public IEnumerable<(Period, SwitchSettings)> SwitchSettingsPerPeriod => PeriodSolutions.Select(kvp => (kvp.Key, kvp.Value.SwitchSettings));

		#endregion

		#region Private data members

		/// <summary>
		/// One <see cref="PeriodSolution"/> per time period in the encoding.
		/// </summary>
		private Dictionary<Period, PeriodSolution> PeriodSolutions { get; set; }

		#endregion

		#region Construction

		/// <summary>
		/// Creates a solution with solutions that have all switches closed.
		/// </summary>
		public PgoSolution(PgoProblem problem) : base(problem)
		{
			PeriodSolutions = problem.AllPeriodData.ToDictionary(p => p.Period, p => new PeriodSolution(p));
		}

		/// <summary>
		/// Creates a solution with the specified network configurations 
		/// </summary>
		/// <param name="problem">The problem the solution is for</param>
		/// <param name="periodConfigurations">The configuration to use for each period</param>
		public PgoSolution(PgoProblem problem, Dictionary<Period, NetworkConfiguration> periodConfigurations) : base(problem)
		{
			if (!problem.Periods.SetEquals(periodConfigurations.Keys))
				throw new ArgumentException("Periods mismatch");

			PeriodSolutions = periodConfigurations.ToDictionary(kv => kv.Key,
				kv => new PeriodSolution(problem.GetData(kv.Key), kv.Value.Clone()));
		}

		/// <summary>
		/// Copy constructor
		/// </summary>
		public PgoSolution(PgoSolution other, bool copyFlows = false) : base(other)
		{
			PeriodSolutions = other.PeriodSolutions.ToDictionary(t => t.Key, t => t.Value.Clone(copyFlows));
		}

		/// <summary>
		/// Creates and returns a disaggregated solution
		/// </summary>
		/// <param name="aggregatedSolution">The aggregate solution to disaggregate</param>
		/// <param name="encoding">The encoding for the disaggregated solution</param>
		/// <param name="aggregator">The aggregator</param>
		public static PgoSolution Disaggregate(PgoSolution aggregatedSolution, PgoProblem encoding, NetworkAggregation aggregator)
		{
			var result = new PgoSolution(encoding);

			result.CopySwitchSettingsFrom(aggregatedSolution, aggregator);
			result.CopyDisaggregatedFlows(aggregatedSolution, aggregator);

			return result;
		}

		/// <summary>
		/// Returns a clone of this solution. Flows are not copied.
		/// </summary>
		public override ISolution Clone() => Problem.IncludeFlowInSolutionClones ? CloneWithFlows() : new PgoSolution(this);

		/// <summary>
		/// Returns a clone of this solution. Flows are copied.
		/// </summary>
		public PgoSolution CloneWithFlows() => new PgoSolution(this, true);

		/// <summary>
		/// Creates a solution where the configuration in each period is equal to the
		/// <paramref name="problem"/>'s start configuration
		/// </summary>
		public static PgoSolution CreateUnchangingSolution(PgoProblem problem)
		{
			var solution = new PgoSolution(problem);

			foreach (var periodData in problem.AllPeriodData)
			{
				solution.PeriodSolutions[periodData.Period] = new PeriodSolution(periodData, problem.StartConfiguration.Clone());
			}

			return solution;
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Updates the solution to have the same switch settings as the given
		/// period solution, in the period it is for.
		/// The update is done by applying a suitable move.
		/// </summary>
		internal void UpdateSolutionForPeriod(PeriodSolution periodSolution)
		{
			UpdateSolution(periodSolution.Period, periodSolution.NetConfig);
		}

		/// <summary>
		/// Updates the solution to have the same switch settings 
		/// in the given period as the given configuration .
		/// The update is done by applying a suitable move.
		/// </summary>
		internal void UpdateSolution(Period period, NetworkConfiguration configuration)
		{
			ChangeSwitchesMove move = this.CreateMove(period, line => configuration.IsOpen(line));
			if (move.DoesSomething)
				move.Apply(true);
		}

		/// <summary>
		/// Gets the solution for the given period
		/// </summary>
		public PeriodSolution GetPeriodSolution(Period p) => PeriodSolutions[p];

		/// <summary>
		/// Returns the <paramref name="i"/>'th period solution.
		/// Throws an exception if this is not defined.
		/// </summary>
		/// <param name="i">Period index</param>
		/// <returns></returns>
		internal PeriodSolution GetPeriodSolution(int i) => PeriodSolutions[Problem.GetPeriod(i)];

		/// <summary>
		/// Creates a copy of the solution just for the given period, for
		/// a corresponding single-period problem
		/// </summary>
		/// <param name="period"></param>
		/// <returns></returns>
		public PgoSolution CreateSinglePeriodCopy(Period period)
		{
			PgoProblem spProblem = Problem.CreateSinglePeriodCopy(period);
			PgoSolution sol = new PgoSolution(spProblem);
			sol.UpdateSolutionForPeriod(PeriodSolutions[period]);

			return sol;
		}

		/// <summary>
		/// Enumerates all moves that open one switch and close another while keeping
		/// the configuration radial
		/// </summary>
		/// <param name="period">If given, the period to create moves for.
		///   If null, the result includes moves for all periods</param>
		public IEnumerable<Move> GetAllRadialSwapMoves(Period period = null)
		{
			IEnumerable<PeriodSolution> singlePeriodSolutions;
			if (period == null)
				singlePeriodSolutions = SinglePeriodSolutions;
			else
				singlePeriodSolutions = new[] { GetPeriodSolution(period) };

			return singlePeriodSolutions.SelectMany(
				sol => sol.NetConfig.OpenLines.SelectMany(
				line => new CloseSwitchAndOpenOtherNeighbourhood(this, line, sol.Period, false)));
		}

		/// <summary>
		/// Updates this solution's flows with the disaggregated verions of all flows stored in another solution
		/// </summary>
		/// <param name="aggSol">The aggregate solution to copy from</param>
		/// <param name="aggregation">The aggregation that aggSol is for</param>
		public void CopyDisaggregatedFlows(PgoSolution aggSol, NetworkAggregation aggregation)
		{
			foreach (var periodSolution in SinglePeriodSolutions)
			{
				var period = periodSolution.Period;

				foreach (var (flowProvider, aggregateFlow) in aggSol.GetPeriodSolution(period).Flows)
				{
					if (aggregateFlow == null)
						continue;

					var fullFlow = flowProvider.DisaggregateFlow(aggregateFlow, aggregation,
						periodSolution.NetConfig, periodSolution.PeriodData.Demands);

					periodSolution.SetFlow(flowProvider, fullFlow);
				}
			}
		}

		/// <summary>
		/// Sets the state of the switchable <paramref name="line"/>
		/// </summary>
		/// <param name="period">Indicates the period that the switch will be set for.</param>
		/// <param name="line"></param>
		/// <param name="sw">true if line is to be opened, false if it is to be closed</param>
		public void SetSwitch(Period period, Line line, bool sw) => GetPeriodSolution(period).SetSwitch(line, sw);

		/// <summary>
		/// Compute the power flow arising from the current switch setting (unless no switch settings have changed
		/// since the last flow calculation, in which case the function does nothing.
		/// </summary>
		/// <returns>true if computation succeeded, false otherwise</returns>
		public bool ComputeFlow(Period period, IFlowProvider provider)
		{
			PeriodSolution perSol = GetPeriodSolution(period);
			bool success = perSol.ComputeFlow(provider);

			return success;
		}

		/// <summary>
		/// Returns a JSON-formatted string representing the solution.
		/// </summary>
		/// <param name="flowProvider">The flow provider to report flows for. If null,
		///   no flows are reported</param>
		/// <param name="prettify">Whether to pretty-print the json</param>
		public string ToJson(IFlowProvider flowProvider, bool prettify = false)
		{
			return PgoJsonParser.ConvertToJsonString(this, flowProvider, prettify);
		}

		/// <summary>
		/// Returns the period solution that comes before this one in the solution.
		/// If the given solution is for the first period, the function returns null.
		/// </summary>
		/// <param name="sol"></param>
		/// <returns></returns>
		internal PeriodSolution GetPreviousPeriodSolution(PeriodSolution sol)
		{
			Period prevPeriod = Problem.PreviousPeriod(sol.Period);
			return (prevPeriod == null) ? null : GetPeriodSolution(prevPeriod);
		}

		/// <summary>
		/// Returns the switch settings of the period solution that comes before this one in the solution.
		/// If the given solution is for the first period, the start configuration of the problem is returned.
		/// </summary>
		/// <param name="period">The current period.</param>
		/// <returns>The previous switch settings, or null if the input solution is for the first period, and there is no StartConfiguration set on <see cref="Problem"/>.</returns>
		internal SwitchSettings GetPreviousPeriodSwitchSettings(Period period)
		{
			Period previousPeriod = Problem.PreviousPeriod(period);
			SwitchSettings referenceSettings = (previousPeriod == null)
				? Problem.StartConfiguration?.SwitchSettings
				: GetPeriodSolution(previousPeriod).SwitchSettings;
			return referenceSettings;
		}

		/// <summary>
		/// Returns the switch settings of the period solution that comes after this one in the solution, or null if this is the last.
		/// </summary>
		/// <param name="period">The current period.</param>
		/// <returns></returns>
		internal SwitchSettings GetNextPeriodSwitchSettings(Period period)
		{
			Period nextPeriod = Problem.NextPeriod(period);
			SwitchSettings referenceSettings = (nextPeriod == null)
				? Problem.TargetPostConfiguration?.SwitchSettings
				: GetPeriodSolution(nextPeriod).SwitchSettings;
			return referenceSettings;
		}

		/// <summary>
		/// Return summary information about this solution, for the given criteria set
		/// </summary>
		/// <returns></returns>
		public SolutionInfo Summarize(CriteriaSet criteriaSet)
		{
			var info = new SolutionInfo
			{
				IsFeasible = this.IsFeasible(criteriaSet),
				ObjectiveValue = criteriaSet.Objective.Value(this)
			};

			AggregateObjective agg = criteriaSet.Objective as AggregateObjective;
			info.ObjectiveComponents = new List<ObjectiveComponentWithWeight>(
				agg.Components.Select(comp => new ObjectiveComponentWithWeight
				{
					Name = (comp as Criterion).Name,
					Value = comp.Value(this),
					Weight = agg.GetWeight(comp)
				})
			);

			if (!info.IsFeasible)
				info.ViolatedConstraints = criteriaSet.Constraints
					.Where(c => !c.IsSatisfied(this))
					.Select(c =>
					{
						string name = c.ToString();
						string description = null;
						try
						{
							description = c.Reason(this);
						}
						catch (NotImplementedException) { }

						return new ConstraintViolationInfo
						{
							Name = name,
							Description = description,
						};
					})
					.ToList();

			info.PeriodInformation = new List<PeriodInfo>();
			PeriodSolution prev = null;
			foreach (var cur in SinglePeriodSolutions)
			{
				int changedSwitches;
				if (prev == null)
				{
					NetworkConfiguration startConfig = (Encoding as PgoProblem).StartConfiguration;
					if (startConfig == null)
					{
						changedSwitches = 0;
					}
					else
					{
						changedSwitches = cur.NetConfig.SwitchSettings.NumberOfDifferentSwitches(startConfig.SwitchSettings);
					}
				}
				else
				{
					changedSwitches = cur.NumberOfDifferentSwitches(prev);
				}
				prev = cur;

				var periodModel = new Sintef.Pgo.DataContracts.Period
				{
					Id = cur.Period.Id,
					StartTime = cur.Period.StartTime,
					EndTime = cur.Period.EndTime
				};

				info.PeriodInformation.Add(new PeriodInfo { Period = periodModel, ChangedSwitches = changedSwitches });
			}

			return info;
		}

		/// <summary>
		/// Creates a move that swaps that status of the switches with given IDs.
		/// One switch must be open, and one must be closed.
		/// </summary>
		/// <param name="lineId1">The ID of one line</param>
		/// <param name="lineId2">The ID of the other line</param>
		/// <param name="periodIndex">The index of the period in which to do the swap</param>
		/// <returns>The move</returns>
		internal SwapSwitchStatusMove SwapMove(string lineId1, string lineId2, int periodIndex = 0)
		{
			var network = Problem.Network;
			Period period = Problem.GetPeriod(periodIndex);
			var (toOpen, toClose) = (network.GetLine(lineId1), network.GetLine(lineId2));

			if (GetPeriodSolution(period).IsOpen(toOpen))
				(toOpen, toClose) = (toClose, toOpen);

			return new SwapSwitchStatusMove(this, period, toOpen, toClose);
		}

		#endregion

		#region Private methods

		#endregion
	}

	/// <summary>
	/// Extension methods for <see cref="PgoSolution"/>
	/// </summary>
	public static class PgoSolutionExtensions
	{

		/// <summary>
		/// Applies moves to make the solution radial and make transformer modes valid in each period
		/// </summary>
		public static void MakeRadialFlowPossible(this PgoSolution solution, Random random = null, StopCriterion stopCriterion = null)
		{
			foreach (var period in solution.Problem.Periods)
				CreateMoveForRadialFlow(solution, period, random, stopCriterion).Apply(true);
		}

		/// <summary>
		/// Creates a move that makes the network configuration in the given period solution 
		/// radial and make transformer modes valid
		/// </summary>
		public static ChangeSwitchesMove CreateMoveForRadialFlow(this PgoSolution solution, Period period,
			Random random = null, StopCriterion stopCriterion = null)
		{
			var radialConfiguration = new NetworkConfiguration(solution.GetPeriodSolution(period).NetConfig);
			radialConfiguration.MakeRadialFlowPossible(random, stopCriterion: stopCriterion);

			bool ShouldBeOpen(Line line) => radialConfiguration.IsOpen(line);

			return CreateMove(solution, period, ShouldBeOpen);
		}

		/// <summary>
		/// Creates a move that updates the network configuration in the given <paramref name="period"/>
		/// to equal the given <paramref name="targetConfiguration"/>
		/// </summary>
		public static ChangeSwitchesMove CreateUpdateMove(this PgoSolution solution, Period period, NetworkConfiguration targetConfiguration)
		{
			return CreateMove(solution, period, line => targetConfiguration.IsOpen(line));
		}

		/// <summary>
		/// Creates a move that modifies this solution in the given period. The new setting of each switch is
		/// specified by a function.
		/// </summary>
		/// <param name="solution"></param>
		/// <param name="period">The period to modify</param>
		/// <param name="shouldBeOpen">Returns true for a switch that is open after the move, false 
		///   if the switch is closed after the move</param>
		/// <returns>The move</returns>
		public static ChangeSwitchesMove CreateMove(this PgoSolution solution, Period period, Func<Line, bool> shouldBeOpen)
		{
			var periodSolution = solution.GetPeriodSolution(period);

			var linesToOpen = periodSolution.ClosedSwitches.Where(s => shouldBeOpen(s));
			var linesToClose = periodSolution.OpenSwitches.Where(s => !shouldBeOpen(s));

			return CreateMove(solution, period, linesToOpen, linesToClose);
		}

		/// <summary>
		/// Creates a move that modifies this solution in the given period, by opening and cl_perosing given
		/// sets of switches
		/// </summary>
		/// <param name="solution"></param>
		/// <param name="period">The period to modify</param>
		/// <param name="switchesToOpen">The switches to open</param>
		/// <param name="switchesToClose">The switches to close</param>
		/// <returns>The move</returns>
		public static ChangeSwitchesMove CreateMove(this PgoSolution solution, Period period, IEnumerable<Line> switchesToOpen, IEnumerable<Line> switchesToClose)
		{
			return new ChangeSwitchesMove(solution, period, switchesToOpen, switchesToClose);
		}

		/// <summary>
		/// Updates the solution is the given period so that all switches are open, except the
		/// given ones, which are closed.
		/// </summary>
		/// <param name="solution">The solution to modify</param>
		/// <param name="period">The period to modify in</param>
		/// <param name="closedSwitches">The names of switches to be closed, separarated by space</param>
		public static void SetClosedOnly(this PgoSolution solution, Period period, string closedSwitches)
		{
			closedSwitches = $" {closedSwitches} ";
			solution.CreateMove(period, line => !closedSwitches.Contains($" {line.Name} ")).Apply(true);
		}

		/// <summary>
		/// Updates the solution is the given period so that all switches are closed, except the
		/// given ones, which are open.
		/// </summary>
		/// <param name="solution">The solution to modify</param>
		/// <param name="period">The period to modify in</param>
		/// <param name="openSwitches">The names of switches to be open, separarated by space</param>
		public static void SetOpenOnly(this PgoSolution solution, Period period, string openSwitches)
		{
			openSwitches = $" {openSwitches} ";
			solution.CreateMove(period, line => openSwitches.Contains($" {line.Name} ")).Apply(true);
		}
	}
}
