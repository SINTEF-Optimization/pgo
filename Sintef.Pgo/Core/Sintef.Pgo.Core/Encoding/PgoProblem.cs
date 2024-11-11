using System;
using System.Collections.Generic;
using System.Linq;
using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;
using System.Numerics;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// An encoding for the multi-period configuration problem.
	/// The data for each period is given by a <see cref="PeriodData"/>.
	/// </summary>
	public class PgoProblem : Encoding
	{
		#region Public properties 

		/// <summary>
		/// One <see cref="PeriodData"/> for each of the problem periods to be considered, in chronological order.
		/// </summary>
		public IEnumerable<PeriodData> AllPeriodData => _periodData.Values;

		/// <summary>
		/// The number of periods that the problem covers.
		/// </summary>
		public int PeriodCount => _periodData.Count;

		/// <summary>
		/// Enumerates the periods the problem is for, in chronological order.
		/// </summary>
		public IEnumerable<Period> Periods => _periodData.Keys;

		/// <summary>
		/// The initial configuration of the switches that is used before the first time period.
		/// Used in the <see cref="ConfigChangeCost"/> objective.
		/// </summary>
		public NetworkConfiguration StartConfiguration { get; private set; }

		/// <summary>
		/// The configuration that one would ideally use after the planning horizon.
		/// Used in the <see cref="ConfigChangeCost"/> objective.
		/// </summary>
		public NetworkConfiguration TargetPostConfiguration { get; private set; }

		/// <summary>
		/// The network, common to all period problems.
		/// </summary>
		public PowerNetwork Network { get; set; }

		/// <summary>
		/// The flow provider for the problem
		/// </summary>
		public IFlowProvider FlowProvider => CriteriaSet.FlowProvider();


		/// <summary>
		/// If set to true, clones of solutions based on this encoding will include a cloned flow.
		/// The default is false.
		/// </summary>
		public bool IncludeFlowInSolutionClones { get; set; } = false;

		/// <summary>
		/// A set of extra objects associated with the problem.
		/// A typical use case is if the creator of the problem wants to attach extra
		/// information that can be used e.g. for visualization.
		/// </summary>
		public Dictionary<string, object> ExtraData { get; } = new();

		#endregion

		#region Private data members

		/// <summary>
		/// Data for each period, ordered by the period start time
		/// </summary>
		private SortedDictionary<Period, PeriodData> _periodData;

		/// <summary>
		/// The previous period for each period (null for the first period)
		/// </summary>
		private Dictionary<Period, Period> _prevPeriod;

		/// <summary>
		/// The next period for each period (null for the last period)
		/// </summary>
		private Dictionary<Period, Period> _nextPeriod;

		#endregion

		#region Construction

		/// <summary>
		/// Initializes a configuration problem for the period demands.
		/// </summary>
		/// <param name="demands">The power demands of the problem, listed per period (in chronological order)</param>
		/// <param name="flowProvider">The provider to use for calculating power flows</param>
		/// <param name="name">The name of the problem</param>
		/// <param name="startConfiguration">The configuration that is used before the start of the first period. Optional, if not given no
		///		stability objective will be used to minimize changes wrt. the start Configuration..</param>
		/// <param name="targetPostConfiguration">The configuration that we would like to see after the end of 
		///		the planning horizon (used by the ChangeCost objective). Optional. If not given, no
		///		stability objective will be used to minimize changes wrt. this target "post" configuration.</param>
		/// <param name="criteriaSet">The criteria set that defines solution feasibity and objective value. 
		///		If null, a default criteria set using <paramref name="flowProvider"/> will be used.</param>
		public PgoProblem(IEnumerable<PeriodData> demands, IFlowProvider flowProvider, string name,
			NetworkConfiguration startConfiguration = null, NetworkConfiguration targetPostConfiguration = null,
			CriteriaSet criteriaSet = null)
		{
			if (demands.AdjacentPairs().Any(pair => pair.Item1.Period.StartTime >= pair.Item2.Period.StartTime))
				throw new ArgumentException("The demands are not chronological");

			if (demands.Select(d => d.Network).Distinct().Count() > 1)
				throw new ArgumentException("The demands are for different networks");

			List<PeriodData> allData = new List<PeriodData>(demands);

			Initialize(allData, flowProvider, name, startConfiguration, targetPostConfiguration, criteriaSet);
		}

		/// <summary>
		/// Creates a single period encoding, based on the given <paramref name="periodData"/>.
		/// </summary>
		/// <param name="periodData">Data for the single period in the problem</param>
		/// <param name="flowProvider">The provider to use for calculating power flows</param>
		/// <param name="startConfiguration">The configuration that is used before the start of the first period. Optional. If not given, no
		///		stability objective will be used to minimize changes wrt. the start Configuration.</param>
		/// <param name="targetPostConfiguration">The configuration that we would like to see after the end of 
		///		the planning horizon (used by the ChangeCost objective). Optional. If not given, no
		///		stability objective will be used to minimize changes wrt. this target "post" configuration.</param>
		/// <param name="criteriaSet">The criteria set that defines solution feasibity and objective value. 
		///		If null, a default criteria set using <paramref name="flowProvider"/> will be used.</param>
		public PgoProblem(PeriodData periodData, IFlowProvider flowProvider, 
			NetworkConfiguration startConfiguration = null, NetworkConfiguration targetPostConfiguration = null,
			CriteriaSet criteriaSet = null)
		{
			List<PeriodData> allData = new List<PeriodData>() { periodData };

			Initialize(allData, flowProvider, periodData.Name, startConfiguration, targetPostConfiguration, criteriaSet);
		}

		/// <summary>
		/// Initializes the problem
		/// </summary>
		/// <param name="periodData"></param>
		/// <param name="flowProvider"></param>
		/// <param name="name"></param>
		/// <param name="startConfiguration">The configuration that is used before the start of the first period. If null, no
		/// stability objective will be used to minimize changes wrt. the start Configuration..</param>
		/// <param name="targetPostConfiguration">The configuration that we would like to see after the end of 
		/// the planning horizon (used by the ChangeCost objective).</param>
		/// <param name="criteriaSet">The criteria set that defines solution feasibity and objective value. 
		///		If null, a default criteria set using <paramref name="flowProvider"/> will be used.</param>
		private void Initialize(List<PeriodData> periodData, IFlowProvider flowProvider, string name, NetworkConfiguration startConfiguration,
			NetworkConfiguration targetPostConfiguration, CriteriaSet criteriaSet)
		{
			Name = name;
			Network = periodData.First().Network;

			if (startConfiguration != null && startConfiguration.Network != Network)
				throw new ArgumentException("Start configuration refers to different network");

			if (targetPostConfiguration != null && targetPostConfiguration.Network != Network)
				throw new ArgumentException("Target configuration refers to different network");

			if (flowProvider != null && criteriaSet != null)
				if (criteriaSet.FlowProvider() != flowProvider)
					throw new Exception();


			StartConfiguration = startConfiguration?.Clone();
			TargetPostConfiguration = targetPostConfiguration?.Clone();

			_periodData = new SortedDictionary<Period, PeriodData>(new KeyComparer<Period, DateTime>(p => p.StartTime));
			_prevPeriod = new Dictionary<Period, Period>();
			_nextPeriod = new Dictionary<Period, Period>();

			foreach (var data in periodData)
				_periodData.Add(data.Period, data);

			_prevPeriod[periodData.First().Period] = null;
			foreach (var (x, y) in periodData.AdjacentPairs())
			{
				_nextPeriod[x.Period] = y.Period;
				_prevPeriod[y.Period] = x.Period;
			}
			_nextPeriod[periodData.Last().Period] = null;

			CriteriaSet = criteriaSet ?? CreateDefaultCriteriaset(flowProvider, Network, AllPeriodData);
		}

		/// <summary>
		/// Creates a new problem based on the single period indicated by the given <paramref name="period"/>.
		/// No start or end configurations are set in the new problem.
		/// </summary>
		/// <param name="period"></param>
		/// <param name="criteriaSet">If not null, the problem is created based on this criteria set. 
		///   If null, it's based on <see cref="Encoding.CriteriaSet"/></param>
		/// <returns></returns>
		public PgoProblem CreateSinglePeriodCopy(Period period, ICriteriaSet criteriaSet = null)
		{
			PeriodData periodData = GetData(period);
			var periodCriteria = new CriteriaSet(criteriaSet ?? CriteriaSet);
			PgoProblem periodProblem = new PgoProblem(periodData, periodCriteria.FlowProvider(), criteriaSet: periodCriteria);

			return periodProblem;
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Replaces the problem's criteria set with the given criteria set
		/// </summary>
		/// <param name="newCriteria">The criteria set to use</param>
		public void UseCriteria(CriteriaSet newCriteria)
		{
			CriteriaSet = newCriteria;
		}

		/// <summary>
		/// Sets the objective weights for the given objective components.
		/// </summary>
		/// <param name="objectiveComponentWeights"></param>
		public void SetObjectiveWeights(IEnumerable<ObjectiveWeight> objectiveComponentWeights)
		{
			AggregateObjective agg = CriteriaSet.Objective as AggregateObjective;
			foreach (var ow in objectiveComponentWeights)
			{
				IObjective obj = agg.Components.SingleOrDefault(o => (o as Criterion).Name == ow.ObjectiveName);
				if (obj == null)
					throw new Exception($"An attempt was made to set a weight for unknown objective '{ow.ObjectiveName}'");
				if (ow.Weight < 0)
					throw new Exception($"An attempt was made to set a negative weight for objective '{ow.ObjectiveName}'");
			}

			foreach (var ow in objectiveComponentWeights)
			{
				IObjective obj = agg.Components.SingleOrDefault(o => (o as Criterion).Name == ow.ObjectiveName);
				agg.SetWeight(obj, ow.Weight);
			}
		}

		/// <summary>
		/// Returns the i'th period of the problem.
		/// </summary>
		/// <param name="i">Period index</param>
		/// <returns></returns>
		internal Period GetPeriod(int i)
		{
			return _periodData.ElementAt(i).Key;
		}

		/// <summary>
		/// Returns the period with the given <paramref name="id"/>.
		/// </summary>
		/// <param name="id">Period id</param>
		/// <returns>The identified Period, or null if no such exists.</returns>
		internal Period GetPeriodById(string id) => _periodData.SingleOrDefault(p => p.Key.Id == id).Key;

		/// <summary>
		/// Gets the period data for the given period.
		/// </summary>
		public PeriodData GetData(Period period) => _periodData[period];

		/// <summary>
		/// Creates the default criteria set to use with the search. Can be modified before giving it to the <see cref="PgoProblem"/> ctor.
		/// </summary>
		/// <returns></returns>
		public static CriteriaSet CreateDefaultCriteriaset(IFlowProvider provider, PowerNetwork network, IEnumerable<PeriodData> allPeriodData)
		{
			CriteriaSet crit = new CriteriaSet();
			AggregateObjective aggregateObjective = new AggregateObjective();
			crit.AddObjective(aggregateObjective);

			#region Objective components:

			//Total loss
			aggregateObjective.AddComponent(new TotalLossObjective(provider), 1e6);

			//Line capacity as objective
			aggregateObjective.AddComponent(new LineCapacityCriterion(provider), 1000.0);

			//Keep away from line capacity:
			aggregateObjective.AddComponent(new LineCapacityCriterion(provider, 0.5), 1.0);

			//Switching cost
			aggregateObjective.AddComponent(new ConfigChangeCost(), 0.002);

			// KILE cost
			var kileObjective = new KileCostObjective(network, allPeriodData, true);
			aggregateObjective.AddComponent(kileObjective, 1.0);

			#endregion

			#region Constraints

			// The radiality constraint cannot be added in general, since we may have parallel lines
			//crit.AddConstraint(new SingleSubstationConnectionConstraint() { Name = "The network must be radial" });

			crit.AddConstraint(new ConsumerVoltageLimitsConstraint(provider, 0.3) { Name = "Consumer bus voltage limits" });
			crit.AddConstraint(new LineVoltageLimitsConstraint(provider, 0.3) { Name = "Line voltage limits" });
			
			crit.AddConstraint(new SubstationCapacityConstraint(provider));

			crit.AddConstraint(new TransformerModesConstraint());

			crit.AddConstraint(new FlowComputationSuccessConstraint(provider));

			//	crit.AddConstraint(new PowerConservationConstraint());

			//Disabling this for now, since it does not work well for "optimal dele"
			//Using an objective instead. TODO later: let the user choose constraints in the API.
			//crit.AddConstraint(new LineCapacityCriterion(provider));

			//crit.AddConstraint(new AllBusesServedConstraint());

			#endregion


			//TODO remove, this is just for debugging
			//	crit = Instrumentation.CriteriaChecker.Instrument(crit) as CriteriaSet;

			return crit;
		}

		/// <summary>
		/// Returns the period that comes before the given <paramref name="period"/> in the chronological sequence
		/// of periods, or null if there is none.
		/// </summary>
		public Period PreviousPeriod(Period period) => _prevPeriod[period];

		/// <summary>
		/// Returns the period that comes after the given <paramref name="period"/> in the chronological sequence
		/// of periods.
		/// </summary>
		public Period NextPeriod(Period period) => _nextPeriod[period];

		/// <summary>
		/// Creates and returns a solution to this problem that is radial in each period.
		/// The solution is not necessarily feasible.
		/// </summary>
		/// <param name="random">The random generator to use</param>
		public PgoSolution RadialSolution(Random random = null)
		{
			var solution = new PgoSolution(this);
			solution.MakeRadialFlowPossible(random);
			return solution;
		}

		/// <summary>
		/// Returns the sub objectives in the given criteria set that are of the given type
		/// </summary>
		/// <typeparam name="T">The type of constraint to find</typeparam>
		public static IEnumerable<T> FindObjectives<T>(CriteriaSet critSet) where T : IObjective
		{
			return (critSet.Objective as AggregateObjective).Components.OfType<T>();
		}

		#region Analysis and problem aggregation

		/// <summary>
		/// Analysises the problem, and returns a textual description of the analysis result.
		/// TODO move to somewhere else
		/// </summary>
		/// <returns></returns>
		public string Analyse()
		{
			string result = Network.AnalyseNetworkProperties(verbose: true) + "\n";
			(string descr, NetworkConfiguration config) = Network.AnalyseLeafAggregatability();
			result += descr + "\n";

			//Get a grip on the size of the numbers
			result += AnalyseDemandsCurrentsAndVoltages(Network).problemDescription;

			//if (config.IsConnected && !config.HasCycles)
			//{
			//	result += "Feasibility analysis:  ";
			//	PgoSolution sol = FeasibleSolutionConstructor.MakeFeasible(this);
			//	result += sol.IsFeasible ? "OK\n" : "No feasible solution could be constructed for the problem\n";
			//	//TODO report details
			//}
			return result;
		}

		/// <summary>
		/// Reports analysis results pertaining to voltages, currents and demands.
		/// </summary>
		/// <param name="connectedNetwork">A network where consumers that can not be connected to a provider have been removed</param>
		/// <returns></returns>
		public (bool ok, string problemDescription) AnalyseDemandsCurrentsAndVoltages(PowerNetwork connectedNetwork)
		{
			string result = "Demands, Currents and Voltages:\n";
			bool allok = true;
			foreach (var (period, periodData) in _periodData)
			{
				(bool ok, string desc) = AnalysePowerValuesAndVoltageLimits(periodData, connectedNetwork);
				if (!ok)
					allok = false;
				result += $"\t Period {period.Index}: {desc}";
			}
			result += "\n";
			return (allok, result);
		}

		/// <summary>
		/// Returns an aggregated, simplified, problem with an aggregated network that retains all the physical properties of the network in the original problem.
		/// I.e. the aggregated network can be configured in the same ways, to give the same power flows and the same objective values,
		/// as the original network.
		/// </summary>
		/// <param name="aggregation">The network aggregation that provides an aggregation of the problem's network.</param>
		/// <param name="addRadialityConstraint">If true, adds radiality constraint, <see cref="SingleSubstationConnectionConstraint"/>.</param>
		public PgoProblem CreateAggregatedProblem(NetworkAggregation aggregation, bool addRadialityConstraint)
		{
			if (Network != aggregation.OriginalNetwork)
				throw new ArgumentException("Cannot aggregate for a different network");

			PowerNetwork aggNet = aggregation.AggregateNetwork;
			IEnumerable<PeriodData> aggPerDatas = _periodData.Select(p => new PeriodData(aggNet, p.Value.Demands.CloneTo(aggNet), p.Key));
			NetworkConfiguration startconfig = null;
			if (StartConfiguration != null)
			{
				startconfig = new NetworkConfiguration(aggNet, new SwitchSettings(aggNet, l => StartConfiguration.IsOpen(Network.GetLine(l.Name))));
			}
			NetworkConfiguration endConfig = null;
			if (TargetPostConfiguration != null)
			{
				endConfig = new NetworkConfiguration(aggNet, TargetPostConfiguration.SwitchSettings.Clone());
			}

			PgoProblem aggregatedProblem = new PgoProblem(aggPerDatas, FlowProvider, $"Aggregate of {Name}", startconfig, endConfig);
			aggregatedProblem.CriteriaSet = CloneCriteriaForAggregate(aggregatedProblem, CriteriaSet, addRadialityConstraint);

			return aggregatedProblem;
		}

		/// <summary>
		/// Creates a criteria set that is equivalent to the one given, but for a different, aggregate problem.
		/// </summary>
		/// <param name="aggregateProblem">The aggregrate problem to create the criteria for</param>
		/// <param name="originalSet">The criteria set, valid for this problem, to copy from</param>
		/// <param name="addRadialityConstraint">If true, a radiality constraint is included in the new criteria set
		///   (if there was none in the original)</param>
		public CriteriaSet CloneCriteriaForAggregate(PgoProblem aggregateProblem, ICriteriaSet originalSet, bool addRadialityConstraint)
		{
			CriteriaSet newCrit = new CriteriaSet();

			foreach (var constraint in originalSet.Constraints)
			{
				newCrit.AddConstraint((constraint as ICanCloneForAggregateProblem).CloneForAggregateProblem(this, aggregateProblem));
			}

			if (addRadialityConstraint)
			{
				var radialityConstraint = newCrit.Constraints.OfType<SingleSubstationConnectionConstraint>().SingleOrDefault();
				if (radialityConstraint == null)
					newCrit.AddConstraint(new SingleSubstationConnectionConstraint() { Name = "The network must be radial" });
			}


			AggregateObjective agg = new AggregateObjective();
			foreach (var (obj, weight) in (originalSet.Objective as AggregateObjective).WeightedComponents)
			{
				agg.AddComponent((obj as ICanCloneForAggregateProblem).CloneForAggregateProblem(this, aggregateProblem), weight);
			}
			newCrit.AddObjective(agg);

			return newCrit;
		}


		/// <summary>
		/// Creates a criteria set that is equivalent to the one given, but using for a different flow provider
		/// </summary>
		/// <param name="originalSet">The criteria set to copy from</param>
		/// <param name="provider">The flow provider to use in the new criteria</param>
		public static CriteriaSet CloneCriteriaForProvider(ICriteriaSet originalSet, IFlowProvider provider)
		{
			CriteriaSet newCrit = new CriteriaSet();

			foreach (var constraint in originalSet.Constraints)
			{
				if (constraint is FlowDependentCriterion flowConstraint)
					newCrit.AddConstraint(flowConstraint.WithProvider(provider));
				else
					newCrit.AddConstraint(constraint);
			}

			AggregateObjective agg = new AggregateObjective();
			foreach (var (objective, weight) in (originalSet.Objective as AggregateObjective).WeightedComponents)
			{
				if (objective is FlowDependentCriterion flowObjective)
					agg.AddComponent(flowObjective.WithProvider(provider), weight);
				else
					agg.AddComponent(objective, weight);
			}
			newCrit.AddObjective(agg);

			return newCrit;
		}

		#endregion

		#endregion

		#region Private methods

		/// <summary>
		/// Looks at the power values to see if they make sense
		/// </summary>
		/// <param name="periodData">Data for the period to analyze</param>
		/// <param name="connectedNetwork">A network where consumers that can not be connected to a provider have been removed</param>
		/// <returns></returns>
		private (bool ok, string description) AnalysePowerValuesAndVoltageLimits(PeriodData periodData, PowerNetwork connectedNetwork)
		{
			string result = string.Empty;

			//Can the sum of power be delivered
			Complex demand = periodData.Demands.Sum;
			Complex maxSupply = Network.Providers.Select(p => p.GenerationCapacity).ComplexSum();
			if (demand.Magnitude > maxSupply.Magnitude)
				result += $"The total demand {demand} > total generation capacity {maxSupply}\n";

			//Can the needed current be delivered through the connections to the providers (based on IMax)?
			double totalPowerCapacity = Network.Providers.Sum(p => p.IncidentLines.Sum(l => l.IMax * p.GeneratorVoltage));
			if (totalPowerCapacity < demand.Magnitude)
				result += $"The total demand {demand.Magnitude} cannot be delivered through the output lines from the providers, which can carry a maximum total power of {totalPowerCapacity}\n";

			//Are all voltage limits on consumers within reasonable distance from at least one provider
			if (Network.Providers.Any() && !Network.PowerTransformers.Any())
			{
				double genVoltageMin = Network.Providers.Min(p => p.GeneratorVoltage);
				double genVoltageMax = Network.Providers.Max(p => p.GeneratorVoltage);

				List<Bus> consumersOutside = Network.Consumers.Where(c => c.VMax < genVoltageMin * 0.5 || c.VMin > genVoltageMax * 1.5).ToList();
				if (consumersOutside.Any())
				{
					result += $"Some consumers have voltage limits far outside the range [{genVoltageMin},{genVoltageMax}] for providers:\n\t";
					result += consumersOutside
						.Select(c => $"{c.Name}: [{c.VMin},{c.VMax}]")
						.Join(", ");
				}
			}

			var unconnectedConsumersWithDemand = Network.Consumers
				.Where(c => !connectedNetwork.HasBus(c.Name) && periodData.Demands.PowerDemand(c) != 0)
				.ToList();
			if (unconnectedConsumersWithDemand.Any())
			{
				result += $"{unconnectedConsumersWithDemand.Count} consumers have nonzero demand but cannot be connected to a provider:\n\t";
				result += $"{unconnectedConsumersWithDemand.Take(10).Select(c => c.Name).Concatenate(", ")}";
			}

			return result.NullOrEmpty() ? (true, "OK\n") : (false, result);
		}

		#endregion
	}
}
