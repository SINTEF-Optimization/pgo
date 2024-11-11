using Sintef.Scoop.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using Sintef.Scoop.Utilities;
using System.Runtime.Serialization;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// An objective that evaluates the total expected KILE cost for a solution, over
	/// the time periods covered.
	/// 
	/// For more details about KILE cost, see <see cref="KileCostCalculator"/>
	/// </summary>
	public class KileCostObjective : Criterion, ICompositeObjective, ICanCloneForAggregateProblem
	{
		/// <summary>
		/// True if the objective always evaluates to zero, because there are no faulting lines in the network
		/// </summary>
		public bool IsAlwaysZero { get; }

		#region Private data members

		/// <summary>
		/// The network the objective is for
		/// </summary>
		private PowerNetwork _network;

		/// <summary>
		/// The periods the objective calculates for
		/// </summary>
		private IEnumerable<Period> _periods;

		/// <summary>
		/// Provides expected per-consumer consumption
		/// </summary>
		private IExpectedConsumptionProvider _expectedConsumptionProvider;

		/// <summary>
		/// Provides the line fault properties to use
		/// </summary>
		private LineFaultPropertiesProvider _lineFaultPropertiesProvider;

		/// <summary>
		/// Provides the KILE categories for consumers
		/// </summary>
		private ConsumerCategoryProvider _consumerCategoryProvider;

		/// <summary>
		/// Caches the total expected KILE cost per period
		/// </summary>
		private PerPeriodValueCacheClient _valuePerPeriodCache;

		/// <summary>
		/// Holds the calculators that have been created for specific periods
		/// </summary>
		private Dictionary<Period, KileCostCalculator> _calculators;

		#endregion

		/// <summary>
		/// Initializes a KILE cost objective
		/// </summary>
		/// <param name="network">The network the objective is for</param>
		/// <param name="periods">The periods the objective calculates for</param>
		/// <param name="expectedConsumptionProvider">The provider to use for getting expected power consumption</param>
		/// <param name="lineFaultPropertiesProvider">The provider to use for getting line fault properties</param>
		/// <param name="consumerCategoryProvider">The provider to use for getting KILE categories for consumers</param>
		/// <param name="cacheExpectedConsumption">If true, adds a cache for the <paramref name="expectedConsumptionProvider"/>
		///   to improve performance. Highly recommended, but for now, only works if all consumption spans have the same
		///   length and all periods in the problem start on a span boundary.</param>
		public KileCostObjective(PowerNetwork network,
			IEnumerable<Period> periods,
			ExpectedConsumptionProvider expectedConsumptionProvider,
			LineFaultPropertiesProvider lineFaultPropertiesProvider,
			ConsumerCategoryProvider consumerCategoryProvider,
			bool cacheExpectedConsumption)
		{
			_network = network;
			_periods = periods;

			_expectedConsumptionProvider = expectedConsumptionProvider;
			if (cacheExpectedConsumption && network.Consumers.Any())
				_expectedConsumptionProvider = new ExpectedConsumptionCache(network, periods, expectedConsumptionProvider);

			_lineFaultPropertiesProvider = lineFaultPropertiesProvider;
			_consumerCategoryProvider = consumerCategoryProvider;

			_valuePerPeriodCache = new PerPeriodValueCacheClient(ExpectedKileCost);

			_calculators = periods.ToDictionary(p => p, p => new KileCostCalculator(p, _network, _expectedConsumptionProvider, _lineFaultPropertiesProvider, _consumerCategoryProvider));
			foreach (var p in periods.Skip(1))
				_calculators[p].ShareNetworkCacheWith(_calculators[periods.First()]);

			Name = "KILE cost";

			// Check up front that the KILE calculator can work with the network
			KileCostCalculator.VerifyNetwork(_network, _lineFaultPropertiesProvider);

			if (_network.Lines.All(l => lineFaultPropertiesProvider.FaultsPerYear(l) == 0))
				IsAlwaysZero = true;
		}

		/// <summary>
		/// Initializes a KILE cost objective, based on the given problem.
		/// </summary>
		/// <param name="network">The network the objective is for</param>
		/// <param name="periodData">Data for the periods the objective calculates for</param>
		/// <param name="cacheExpectedConsumption">If true, adds a cache for the expectedConsumptionProvider
		///   to improve performance. Highly recommended, but for now, only works if all consumption spans have the same
		///   length and all periods in the problem start on a span boundary.</param>
		public KileCostObjective(PowerNetwork network, IEnumerable<PeriodData> periodData, bool cacheExpectedConsumption)
			: this(network, periodData.Select(d => d.Period), new ExpectedConsumptionFromDemand(periodData),
				network.PropertiesProvider, network.CategoryProvider, cacheExpectedConsumption)
		{
		}

		/// <summary>
		/// Returns the total expected KILE cost for the <paramref name="solution"/>
		/// </summary>
		public override double Value(ISolution solution)
		{
			if (IsAlwaysZero)
				return 0;

			return _valuePerPeriodCache.Values(solution).Values.Sum();
		}

		/// <summary>
		/// Returns the change in KILE cost over the given move
		/// </summary>
		public override double DeltaValue(Move move)
		{
			if (IsAlwaysZero)
				return 0;

			KileCostCalculator calculator = CalculatorFor(move);
			ChangeSwitchesMove myMove = (ChangeSwitchesMove)move;
			var period = myMove.Period;
			var mySolution = (PgoSolution)move.Solution;
			NetworkConfiguration configuration = mySolution.GetPeriodSolution(period).NetConfig;

			var deltaRegions = calculator.DeltaRegionsForAffects(move);

			double deltaValue = 0;

			foreach (var region in deltaRegions)
			{
				double regionCost = 0;

				foreach (var line in region.Lines)
				{
					regionCost += calculator.IndicatorsFor(configuration, region.Consumers, line).ExpectedKileCost;
				}

				if (region.IsAdded)
					deltaValue += regionCost;
				else
					deltaValue -= regionCost;
			}

			return deltaValue;
		}

		/// <summary>
		/// Clones the criterion to be valid for an aggregate of the given
		/// <paramref name="originalProblem"/>.
		/// </summary>
		/// <param name="originalProblem">The original problem</param>
		/// <param name="aggregateProblem">The aggregate problem</param>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new KileCostObjective(aggregateProblem.Network, aggregateProblem.AllPeriodData, _expectedConsumptionProvider is ExpectedConsumptionCache);
		}

		#region Private methods

		/// <summary>
		/// Returns the total expected KILE cost for the given period solution
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		private double ExpectedKileCost(PeriodSolution solution)
		{
			NetworkConfiguration configuration = solution.NetConfig;

			if (!configuration.IsRadial)
				return 0;

			return CalculatorFor(solution.Period).TotalIndicators(configuration).ExpectedKileCost;
		}

		/// <summary>
		/// Returns a calculator that can calculate values for the solution and period
		/// of the given <paramref name="move"/>.
		/// </summary>
		private KileCostCalculator CalculatorFor(Move move)
		{
			ChangeSwitchesMove myMove = (ChangeSwitchesMove)move;
			var period = myMove.Period;
			return CalculatorFor(period);
		}

		/// <summary>
		/// Returns a calculator that can calculate values for the given <paramref name="period"/> .
		/// </summary>
		internal KileCostCalculator CalculatorFor(Period period)
		{
			// Return existing calculator if one exists
			return _calculators[period];
		}

		#endregion

		#region Slices, for annotation

		/// <summary>
		/// Returns a collection of smaller slices of this objctive. These sub-objectives are mainly useful 
		/// for annotation.
		/// </summary>
		public IEnumerable<IObjective> GetParts()
		{
			if (_periods.Count() == 1)
			{
				// Split directly into lines/consumers
				foreach (var part in new Slice(this, _periods).GetParts())
					yield return part;
				yield break;
			}

				// One slice with all periods
			yield return new Slice(this, _periods);

			// ...and one for each period
			foreach (var period in _periods)
				yield return new Slice(this, new[] { period });
		}

		/// <summary>
		/// A smaller part of the objective, considering only a subset of periods, faulting lines
		/// and/or consumers.
		/// Mainly useful for annotation.
		/// </summary>
		private class Slice : Criterion, ICompositeObjective, ISolutionAnnotator
		{
			/// <summary>
			/// The objective this is a part of
			/// </summary>
			private KileCostObjective _owner;

			/// <summary>
			/// The periods considered
			/// </summary>
			private List<Period> _periods;
			
			/// <summary>
			/// The lines whose faults are considered
			/// </summary>
			private List<Line> _faultingLines;

			/// <summary>
			/// The consumers considered
			/// </summary>
			private List<Bus> _consumers;

			/// <summary>
			/// Initializes a slice
			/// </summary>
			/// <param name="kileCostObjective"></param>
			/// <param name="periods"></param>
			/// <param name="lines"></param>
			/// <param name="consumers"></param>
			public Slice(KileCostObjective kileCostObjective, IEnumerable<Period> periods,
				IEnumerable<Line> lines = null,
				IEnumerable<Bus> consumers = null)
			{
				_owner = kileCostObjective;
				_periods = periods.ToList();

				PowerNetwork network = _owner._network;
				_faultingLines = (lines ?? network.Lines.Where(l => _owner._lineFaultPropertiesProvider.FaultsPerYear(l) > 0)).ToList();
				_consumers = (consumers ?? network.Consumers).ToList();

				// Show slice information in Name

				string periodSpec = $"{_periods.Count} lines";
				if (_periods.Count == _owner._periods.Count())
					periodSpec = "All periods";
				if (_periods.Count == 1)
					periodSpec = $"Period {_periods.Single().Index}";

				string lineSpec = $"{_faultingLines.Count} lines";
				if (_faultingLines.Count == 1)
					lineSpec = $"line {_faultingLines.Single()}";

				string consumerSpec = $"{_consumers.Count} consumers";
				if (_consumers.Count == 1)
					consumerSpec = $"consumer {_consumers.Single()}";

				Name = $"{periodSpec}, {lineSpec}, {consumerSpec}";
			}

			/// <summary>
			/// Returns annotations for the given solution
			/// </summary>
			public IEnumerable<Annotation> Annotate(ISolution solution)
			{
				if (_faultingLines.Count > 1 && _consumers.Count > 1)
					// We don't annotate multiple lines against multiple consumers
					yield break;

				var mySolution = (PgoSolution)solution;

				Color lineColor = Color.OrangeRed;
				Color sectionColor = Color.Orange;
				Color repairColor = Color.OrangeRed;

				// Show for each period considered

				foreach (var period in _periods)
				{
					NetworkConfiguration configuration = mySolution.GetPeriodSolution(period).NetConfig;
					KileCostCalculator calculator = _owner.CalculatorFor(period);

					if (_faultingLines.Count == 1 && _faultingLines.Single().IsSwitchable && configuration.IsOpen(_faultingLines.Single()))
					{
						Line line = _faultingLines.Single();
						yield return new LineAnnotation(line, period, $"Line {line} is open");
					}

					else if (_consumers.Count > 1)
					{
						// Slice is for a single line. Show what consumers it affects

						Line line = _faultingLines.Single();
						var consumers = calculator.AffectedConsumers(configuration, line).Intersect(_consumers).ToList();

						var mustWait = consumers.Where(c => calculator.MustWaitForRepair(configuration, line, c));
						var mustNotWait = consumers.Where(c => !calculator.MustWaitForRepair(configuration, line, c));

						yield return new LineAnnotation(line, period, $"Faulty line: {line}") { Color = lineColor };
						yield return new BusAnnotation(mustNotWait, period, $"Wait for sectioning: {mustNotWait.Count()} consumers") { Color = sectionColor };
						yield return new BusAnnotation(mustWait, period, $"Wait for repair: {mustWait.Count()} consumers") { Color = repairColor };
					}

					else if (_faultingLines.Count > 1)
					{
						// Slice is for a single consumer. Show what lines affect it

						var consumer = _consumers.Single();
						var lines = _faultingLines.Where(l => calculator.Affects(configuration, l, consumer)).ToList();

						var mustWait = lines.Where(line => calculator.MustWaitForRepair(configuration, line, consumer));
						var mustNotWait = lines.Where(line => !calculator.MustWaitForRepair(configuration, line, consumer));

						yield return new BusAnnotation(consumer, period, $"Affected consumer: {consumer}") { Color = lineColor };
						yield return new LineAnnotation(mustNotWait, period, $"Wait for sectioning: {mustNotWait.Count()} lines") { Color = sectionColor };
						yield return new LineAnnotation(mustWait, period, $"Wait for repair: {mustWait.Count()} lines") { Color = repairColor };
					}

					else
					{
						// Slice is for a single line and consumer. Show details.

						Line line = _faultingLines.Single();
						var consumer = _consumers.Single();

						var indicators = calculator.IndicatorsFor(configuration, consumer, line);

						yield return new LineAnnotation(line, period, $"Faulty line: {line}") { Color = lineColor };
						yield return new BusAnnotation(consumer, period, $"Affected consumer: {consumer}" +
							$"\nMust wait: {calculator.MustWaitForRepair(configuration, line, consumer)}" +
							$"\nExpected hours: {indicators.ExpectedOutageHours}" +
							$"\nExpected Watt hours: {indicators.ExpectedOutageWattHours}" +
							$"\nExpected cost: {indicators.ExpectedKileCost}"
							);
					}
				}
			}

			/// <summary>
			/// Enumerates the sub-slices this slice can be divided into
			/// </summary>
			/// <returns></returns>
			public IEnumerable<IObjective> GetParts()
			{
				List<IObjective> result = new List<IObjective>();
				if (_faultingLines.Count > 1)
					result.AddRange(_faultingLines.Select(line => new Slice(_owner, _periods, new[] { line }, _consumers)));

				if (_consumers.Count > 1)
					result.AddRange(_consumers.Select(consumer => new Slice(_owner, _periods, _faultingLines, new[] { consumer })));

				if (result.Any())
					return result;

				return new[] { this };
			}

			/// <summary>
			/// Returns the total KILE cost for the periods/lines/consumers considered
			/// by this slice
			/// </summary>
			public override double Value(ISolution solution)
			{
				if (_faultingLines.Count > 1 && _consumers.Count > 1)
					return double.NaN;

				if (_faultingLines.Count == 0 || _consumers.Count == 0)
					return 0;

				var mySolution = (PgoSolution)solution;
				double result = 0;
				foreach (var period in _periods)
				{
					NetworkConfiguration configuration = mySolution.GetPeriodSolution(period).NetConfig;
					KileCostCalculator calculator = _owner.CalculatorFor(period);

					if (_faultingLines.Count == 1 && _faultingLines.Single().IsSwitchable && configuration.IsOpen(_faultingLines.Single()))
						continue; // Open line does not fault

					else if (_consumers.Count > 1)
						result += calculator.IndicatorsForFaultsIn(configuration, _faultingLines.Single()).ExpectedKileCost;

					else if (_faultingLines.Count > 1)
						result += calculator.IndicatorsForOutagesAt(configuration, _consumers.Single()).ExpectedKileCost;

					else
						result += calculator.IndicatorsFor(configuration, _consumers.Single(), _faultingLines.Single()).ExpectedKileCost;
				}

				return result;
			}
		}

		#endregion
	}
}
