using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Provides functionality for calculating KILE costs.
	/// 
	/// KILE ('Kvalitetsjusterte inntektsrammer ved ikke levert energi') is a penalty
	/// for power outages at consumer nodes. The penalty equals (an estimate of)
	/// the power that was not supplied, multiplied by a factor that depends on the
	/// category of consumer (e.g. Industry or Agriculture) and the duration of the
	/// outage.
	/// See the documentation of <see cref="KileCostTable"/> for more information on
	/// KILE calculation.
	/// 
	/// This calculator relies on data supplied by a number of other objects:
	///  - A <see cref="ConsumerCategoryProvider"/>
	///  - An <see cref="IExpectedConsumptionProvider"/>
	///  - A <see cref="KileCostTable"/>
	/// 
	/// Based on these, the calculator can compute the expected KILE cost for an outage
	/// at a given consumer with a given duration. The calculator is configured for
	/// a specific <see cref="Period"/> and assumes that the outage starts at a random time
	/// with a uniform distribution over the period.
	/// 
	/// Further, the calculator is configured with:
	///  - An <see cref="ILineFaultPropertiesProvider"/>
	///  
	/// Based on these, the calculator uses the RELRAD method to work out the expected number of power outages
	/// over the period and their expected durations. Combining this with the KILE calculation above, 
	/// the calculator can find the total expected KILE cost for the network configuration, for
	/// a single consumer, or summed for the whole network. The final results are presented
	/// in the <see cref="Indicators"/> structure.
	/// </summary>
	public class KileCostCalculator
	{
		#region Private data members

		/// <summary>
		/// The period to calculate for
		/// </summary>
		private Period _period;

		/// <summary>
		/// Provides expected per-consumer consumption
		/// </summary>
		private IExpectedConsumptionProvider _consumptionProvider;

		/// <summary>
		/// Provides the line fault properties to use
		/// </summary>
		private ILineFaultPropertiesProvider _lineFaultPropertiesProvider;

		/// <summary>
		/// Provides the KILE categories for consumers
		/// </summary>
		private ConsumerCategoryProvider _categoryProvider;

		/// <summary>
		/// The table of KILE costs
		/// </summary>
		private KileCostTable _kileCostTable;

		/// <summary>
		/// Cache used by <see cref="MustWaitForRepair"/>: 
		/// _waitingConsumers[line.Index] contains the consumer buses that
		/// must wait for repair when the line has a fault
		/// </summary>
		private Bus[][] _waitingConsumers;

		#endregion

		/// <summary>
		/// Initializes a KILE cost calculator
		/// </summary>
		/// <param name="period">The period to calculate for</param>
		/// <param name="network">The network to calculate for</param>
		/// <param name="consumptionProvider">The provider to use for getting expected power consumption</param>
		/// <param name="lineFaultPropertiesProvider">The provider to use for getting line fault properties</param>
		/// <param name="categoryProvider">The provider to use for getting KILE categories for consumers</param>
		public KileCostCalculator(Period period, PowerNetwork network,
			IExpectedConsumptionProvider consumptionProvider, ILineFaultPropertiesProvider lineFaultPropertiesProvider,
			ConsumerCategoryProvider categoryProvider)
		{
			_period = period;
			_consumptionProvider = consumptionProvider;
			_lineFaultPropertiesProvider = lineFaultPropertiesProvider;
			_categoryProvider = categoryProvider;
			_kileCostTable = new KileCostTable(network, categoryProvider);
			_waitingConsumers = new Bus[network.LineIndexBound][];

			VerifyNetwork(network, lineFaultPropertiesProvider);
		}

		/// <summary>
		/// Makes this calculator share data that only depends on the network with the <paramref name="other"/>
		/// calculator, which is for the same network. This improves efficiency.
		/// </summary>
		public void ShareNetworkCacheWith(KileCostCalculator other)
		{
			_waitingConsumers = other._waitingConsumers;
		}

		#region Public methods

		/// <summary>
		/// Verifies that the network satisfies the calculator's conditions, and throws an exception
		/// if it does not.
		/// 
		/// The conditions are:
		///  - Any path between a provider and a line that can fault must contain a circuit breaker
		///  - Any path between a provider and a line that can fault must contain a (nonfaulting) switch
		/// </summary>
		public static void VerifyNetwork(PowerNetwork network, ILineFaultPropertiesProvider lineFaultPropertiesProvider)
		{
			var fullConfiguration = NetworkConfiguration.AllClosed(network);

			foreach (var provider in network.Providers)
			{
				Line badLine = fullConfiguration
					.LinesAround(provider, false, stopAt: (l, b) => l.IsBreaker)
					.Where(l => lineFaultPropertiesProvider.FaultsPerYear(l) > 0)
					.FirstOrDefault();

				if (badLine != null)
					throw new Exception($"Line {badLine} can fault, but there is no breaker between it and provider {provider}");

				badLine = fullConfiguration
					.LinesAround(provider, false, stopAt: (l, b) => l.IsSwitchable && lineFaultPropertiesProvider.FaultsPerYear(l) == 0)
					.Where(l => lineFaultPropertiesProvider.FaultsPerYear(l) > 0)
					.FirstOrDefault();

				if (badLine != null)
					throw new Exception($"Line {badLine} can fault, but there is no nonfaulting switch between it and provider {provider}");
			}
		}

		/// <summary>
		/// Returns indicators for power outages at <paramref name="consumer"/> caused by faults 
		/// in <paramref name="faultyLine"/>
		/// </summary>
		public Indicators IndicatorsFor(NetworkConfiguration configuration, Bus consumer, Line faultyLine)
		{
			if (CanFault(faultyLine))
				return IndicatorsFor(consumer, faultyLine, MustWaitForRepair(configuration, faultyLine, consumer));
			else
				return new Indicators();
		}

		/// <summary>
		/// Returns summed indicators for power outages at any of <paramref name="consumers"/> caused by faults 
		/// in <paramref name="faultyLine"/>
		/// </summary>
		public Indicators IndicatorsFor(NetworkConfiguration configuration, IEnumerable<Bus> consumers, Line faultyLine)
		{
			List<Bus> waitForSectioning = new List<Bus>();
			List<Bus> waitForRepair = new List<Bus>();

			foreach (var consumer in consumers)
			{
				if (MustWaitForRepair(configuration, faultyLine, consumer, false))
					waitForRepair.Add(consumer);
				else
					waitForSectioning.Add(consumer);
			}

			return new[] {
				IndicatorsFor(waitForSectioning, faultyLine, false),
				IndicatorsFor(waitForRepair, faultyLine, true)
			}.Sum();
		}

		/// <summary>
		/// Returns indicators for power outages at <paramref name="consumer"/> caused by faults 
		/// in <paramref name="faultyLine"/>
		/// </summary>
		/// <param name="consumer"></param>
		/// <param name="faultyLine"></param>
		/// <param name="mustWaitForRepair">
		/// If false, power to the consumer is restored, via a different route, when the faulty line 
		/// has been isolated from the majority of the network.
		/// If true, power is restored only later, after the fault has been repaired. 
		/// </param>
		public Indicators IndicatorsFor(Bus consumer, Line faultyLine, bool mustWaitForRepair)
		{
			double expectedfaultsPerYear = _lineFaultPropertiesProvider.FaultsPerYear(faultyLine);
			if (expectedfaultsPerYear == 0)
				return new Indicators();

			double yearsInPeriod = _period.Length.TotalDays / 365;
			double expectedFaultsInPeriod = expectedfaultsPerYear * yearsInPeriod;

			TimeSpan outageDuration = _lineFaultPropertiesProvider.SectioningTime(faultyLine);
			if (mustWaitForRepair)
				outageDuration += _lineFaultPropertiesProvider.RepairTime(faultyLine);

			var averagePower = _consumptionProvider.AveragePower(consumer, _period, outageDuration);
			var kileCostPerOutage = ExpectedKileCost(consumer, outageDuration);

			var result = new Indicators();

			result.ExpectedOutageHours = expectedFaultsInPeriod * outageDuration.TotalHours;
			result.ExpectedOutageWattHours = result.ExpectedOutageHours * averagePower;
			result.ExpectedKileCost = expectedFaultsInPeriod * kileCostPerOutage;

			return result;
		}

		/// <summary>
		/// Returns summed indicators for power outages at any of <paramref name="consumers"/> caused by faults 
		/// in <paramref name="faultyLine"/>
		/// </summary>
		/// <param name="consumers"></param>
		/// <param name="faultyLine"></param>
		/// <param name="mustWaitForRepair">
		/// If false, power to the consumers is restored, via a different route, when the faulty line 
		/// has been isolated from the majority of the network.
		/// If true, power is restored only later, after the fault has been repaired. 
		/// </param>
		private Indicators IndicatorsFor(List<Bus> consumers, Line faultyLine, bool mustWaitForRepair)
		{
			var result = new Indicators();

			double expectedfaultsPerYear = _lineFaultPropertiesProvider.FaultsPerYear(faultyLine);
			if (expectedfaultsPerYear == 0)
				return result;

			double yearsInPeriod = _period.Length.TotalDays / 365;
			double expectedFaultsInPeriod = expectedfaultsPerYear * yearsInPeriod;

			TimeSpan outageDuration = _lineFaultPropertiesProvider.SectioningTime(faultyLine);
			if (mustWaitForRepair)
				outageDuration += _lineFaultPropertiesProvider.RepairTime(faultyLine);

			double totalAveragePower = 0;
			double totalKileCostPerOutage = 0;
			foreach (var consumer in consumers)
			{
				double averagePower = _consumptionProvider.AveragePower(consumer, _period, outageDuration);
				double costPerW = _kileCostTable.CostPerW(consumer, outageDuration);

				totalAveragePower += averagePower;
				totalKileCostPerOutage += costPerW * averagePower;

			}
			double expectedOutageHours = expectedFaultsInPeriod * outageDuration.TotalHours;

			result.ExpectedOutageHours = expectedOutageHours * consumers.Count;
			result.ExpectedOutageWattHours = expectedOutageHours * totalAveragePower;
			result.ExpectedKileCost = expectedFaultsInPeriod * totalKileCostPerOutage;

			return result;
		}

		/// <summary>
		/// Returns true if the given line can fault, false if not
		/// </summary>
		public bool CanFault(Line line) => _lineFaultPropertiesProvider.FaultsPerYear(line) > 0;

		/// <summary>
		/// Enumerates all buses that will lose power when the given line develops
		/// a fault.
		/// </summary>
		public IEnumerable<Bus> AffectedBuses(NetworkConfiguration configuration, Line faultingLine)
		{
			// Find the first upstream circuit breaker

			Line firstHigherBreaker = UpstreamBreaker(configuration, faultingLine);

			if (firstHigherBreaker == null)
				throw new ArgumentException($"There is no breaker upstream of faulting line {faultingLine}");

			// All buses supplied through this breaker will lose power

			var belowBreaker = configuration.DownstreamEnd(firstHigherBreaker);

			return configuration.BusesInSubtree(belowBreaker);
		}

		/// <summary>
		/// Enumerates all consumers that will lose power when the given line develops
		/// a fault.
		/// </summary>
		public IEnumerable<Bus> AffectedConsumers(NetworkConfiguration configuration, Line faultingLine)
		{
			// Find the first upstream circuit breaker

			Line firstHigherBreaker = UpstreamBreaker(configuration, faultingLine);

			if (firstHigherBreaker == null)
				throw new ArgumentException($"There is no breaker upstream of faulting line {faultingLine}");

			// All consumers supplied through this breaker will lose power

			return configuration.ConsumersBelow(firstHigherBreaker);
		}

		/// <summary>
		/// Enumerates all lines that cause the given consumer to lose power if
		/// they fault
		/// </summary>
		public IEnumerable<Line> LinesAffecting(NetworkConfiguration configuration, Bus consumer)
		{
			// An unconnected bus is not affected.
			if (configuration.ProviderForBus(consumer) == null)
			{
				return Enumerable.Empty<Line>();
			}

			var upstreamBreakers = configuration.LinesToProvider(consumer)
				.Where(l => l.IsBreaker);

			// The consumer is affected by faults in the lines downstream from each
			// of these breakers, down to the next breaker below
			var lines = upstreamBreakers
				.SelectMany(breaker => configuration.LinesInSubtree(configuration.DownstreamEnd(breaker), stopAt: IsBreaker));
			return lines;


			bool IsBreaker(Line line) => line.IsBreaker;
		}

		/// <summary>
		/// Returns true if the given <paramref name="consumer"/> has to wait until the
		/// fault is repaired before power is restored, in case of a fault in the given <paramref name="faultingLine"/>.
		/// Returns false if power to the consumer is restored once the faulty line has been isolated from the
		/// rest of the network ('sectioned').
		/// </summary>
		/// <param name="configuration"></param>
		/// <param name="faultingLine"></param>
		/// <param name="consumer"></param>
		/// <param name="checkConfiguration">If true (the default), the function throws an exception unless the line
		///   actually can fault and affects the consumer in the current configuration.</param>
		public bool MustWaitForRepair(NetworkConfiguration configuration, Line faultingLine, Bus consumer, bool checkConfiguration = true)
		{
			// Serve result from cache if present (bypassing checks for efficiency)

			var disconnectedConsumers = _waitingConsumers[faultingLine.Index];
			if (disconnectedConsumers != null)
			{
				foreach (var c in disconnectedConsumers)
				{
					if (ReferenceEquals(c, consumer))
						return true;
				}
				return false;
			}

			// Argument checks

			if (!consumer.IsConsumer)
				throw new ArgumentException($"{consumer.Name} is not a consumer");

			if (checkConfiguration)
			{
				if (faultingLine.IsSwitchable && configuration.IsOpen(faultingLine))
					throw new ArgumentException($"{faultingLine.Name} is open -- cannot fault");

				if (!AffectedConsumers(configuration, faultingLine).Contains(consumer))
					throw new ArgumentException($"{consumer.Name} is not affected by faults at {faultingLine.Name}");
			}
			else
			{
				if (!CanFault(faultingLine))
					throw new ArgumentException($"{faultingLine.Name} cannot fault");
			}

			// Result was not found in cache -- calculate it:
			disconnectedConsumers = CalculateAndCacheDisconnectedConsumers(configuration, faultingLine);

			// Return result
			return disconnectedConsumers.Contains(consumer);
		}

		/// <summary>
		/// Returns indicators for power outages caused by faults 
		/// in <paramref name="faultyLine"/>, summed over all affected consumers
		/// </summary>
		public Indicators IndicatorsForFaultsIn(NetworkConfiguration configuration, Line faultyLine)
		{
			if (!CanFault(faultyLine))
				return new Indicators();

			IEnumerable<Bus> consumers = AffectedConsumers(configuration, faultyLine).ToList();

			return IndicatorsFor(configuration, consumers, faultyLine);
		}

		/// <summary>
		/// Returns indicators for total power outage at <paramref name="consumer"/>,
		/// summed over all faulting lines.
		/// </summary>
		/// <param name="configuration"></param>
		/// <param name="consumer"></param>
		/// <returns></returns>
		public Indicators IndicatorsForOutagesAt(NetworkConfiguration configuration, Bus consumer)
		{
			return LinesAffecting(configuration, consumer)
				.Select(line => IndicatorsFor(configuration, consumer, line))
				.Sum();
		}

		/// <summary>
		/// Returns indicators for total power outage for the network configuration, summed 
		/// over all faulting lines and all consumers.
		/// </summary>
		public Indicators TotalIndicators(NetworkConfiguration configuration)
		{
			return configuration.PresentLines.Where(l => !l.IsBreaker)
			 .Select(l => IndicatorsForFaultsIn(configuration, l))
			 .Sum();
		}

		/// <summary>
		/// Returns true if a fault in <paramref name="line"/> causes a power outage at <paramref name="consumer"/>
		/// </summary>
		public bool Affects(NetworkConfiguration configuration, Line line, Bus consumer)
		{
			if (line.IsSwitchable && configuration.IsOpen(line))
				return false;

			return AffectedConsumers(configuration, line).Contains(consumer);
		}

		/// <summary>
		/// Returns the expected KILE cost for a power outage at <paramref name="consumer"/>.
		/// The outage lasts for <paramref name="duration"/>.
		/// </summary>
		public double ExpectedKileCost(Bus consumer, TimeSpan duration)
		{
			double costPerW = _kileCostTable.CostPerW(consumer, duration);
			double power = _consumptionProvider.AveragePower(consumer, _period, duration);

			return costPerW * power;
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Returns a specification of the line/consumer pairs for which <see cref="Affects"/>
		/// changes value across the <paramref name="move"/>.
		/// When <see cref="DeltaRegion.IsAdded"/> is true, the value changes from false to true
		/// for all pairs indicated by the <see cref="DeltaRegion"/>.
		/// When <see cref="DeltaRegion.IsAdded"/> is false, the value changes from true to false.
		/// 
		/// The implementation ensures that the same line/consumer pair does not occur in
		/// more than one region.
		/// </summary>
		internal IEnumerable<DeltaRegion> DeltaRegionsForAffects(Move move)
		{
			if (!(move is SwapSwitchStatusMove swapMove))
				throw new NotImplementedException("Can only provide delta regions for SwapSwitchStatusMove");

			var period = swapMove.Period;
			if (period != _period)
				yield break;
			var configuration = swapMove.Solution.GetPeriodSolution(period).NetConfig;

			var toOpen = swapMove.SwitchToOpen;
			var toClose = swapMove.SwitchToClose;

			Bus nodeA = configuration.UpstreamEnd(toOpen);
			Bus nodeB = toOpen.OtherEnd(nodeA);
			Bus nodeC = configuration.EndAtOrDownstreamOf(toClose, nodeB);
			Bus nodeD = toClose.OtherEnd(nodeC);

			var breakersBetweenBAndC = configuration.PathToProvider(nodeC)
				.Select(l => l.Line)
				.TakeWhile(l => l != toOpen)
				.Where(l => l.IsBreaker)
				.Reverse()
				.ToList();

			Line breakerForA = UpstreamBreaker(configuration, nodeA);
			Line breakerForD = UpstreamBreaker(configuration, nodeD);

			// Before the move, the situation looks as follows:
			//
			//                         toOpen                         toClose
			// <- upstream ----- nodeA  ----  nodeB ----...---- nodeC         nodeD ----- upstream ->
			//
			// After the move, toOpen has been opened and toClose has been closed.

			// First, consider the effect of the move on consumers outside the nodeB-nodeC segment

			#region Handle special cases for efficiency. Disregard this at first reading

			bool useGeneralCase = true;

			if (!toOpen.IsBreaker && !toClose.IsBreaker && breakerForA != null && breakerForD != null)
			{
				if (breakersBetweenBAndC.Count == 0)
				{
					// Lines around B and lines around C are equal: all lines between B and C.
					// We exploit this to avoid both adding and removing the same consumers for these lines
					var lineToOpen = new List<Line> { toOpen };
					var lineToClose = new List<Line> { toClose };

					if (breakerForA == breakerForD)
					{
						// All consumers are common: skip all lines between B and C
						var consumers = configuration.ConsumersBelow(breakerForA, stopAt: toOpen).ToList();

						yield return RemovedRegion(lineToOpen, consumers);
						yield return AddedRegion(lineToClose, consumers);
						useGeneralCase = false;
					}
					else if (configuration.IsAncestor(breakerForA, breakerForD))
					{
						// Skip common consumers below breakerForD
						var linesBToC = LinesAround(configuration, nodeB, stopAtUpstreamBreaker: true, stopAtDownstreamBreakers: true, stopAt: toOpen);
						var consumersForA = configuration.ConsumersBelow(breakerForA, stopAt: breakerForD, stopAt2: toOpen);
						var consumersForD = configuration.ConsumersBelow(breakerForD, stopAt: toOpen);
						List<Bus> allConsumers = new List<Bus>(consumersForA);
						allConsumers.AddRange(consumersForD);

						yield return RemovedRegion(lineToOpen, allConsumers);
						yield return RemovedRegion(linesBToC, consumersForA);
						yield return AddedRegion(lineToClose, consumersForD);
						useGeneralCase = false;
					}
					else if (configuration.IsAncestor(breakerForD, breakerForA))
					{
						// Skip common consumers below breakerForA
						var linesBToC = LinesAround(configuration, nodeB, stopAtUpstreamBreaker: true, stopAtDownstreamBreakers: true, stopAt: toOpen);
						var consumersForA = configuration.ConsumersBelow(breakerForA, stopAt: toOpen);
						var consumersForD = configuration.ConsumersBelow(breakerForD, stopAt: breakerForA, stopAt2: toOpen);
						List<Bus> allConsumers = new List<Bus>(consumersForA);
						allConsumers.AddRange(consumersForD);

						yield return AddedRegion(lineToClose, allConsumers);
						yield return AddedRegion(linesBToC, consumersForD);
						yield return RemovedRegion(lineToOpen, consumersForA);
						useGeneralCase = false;
					}
				}
			}
			if (useGeneralCase)
			#endregion
			{
				if (!toOpen.IsBreaker && breakerForA != null)
				{
					// A consumer around nodeA is affected by faults in lines around nodeB before the move, but not after

					var consumers = configuration.ConsumersBelow(breakerForA, stopAt: toOpen);
					var lines = LinesAround(configuration, nodeB, stopAtUpstreamBreaker: true, stopAtDownstreamBreakers: true, stopAt: toOpen);
					lines.Add(toOpen);

					yield return RemovedRegion(lines, consumers);
				}

				if (!toClose.IsBreaker && breakerForD != null)
				{
					// A consumer around nodeD is affected by faults in lines around nodeC after the move, but not before

					var consumers = configuration.ConsumersBelow(breakerForD, stopAt: toOpen);
					var lines = LinesAround(configuration, nodeC, stopAtUpstreamBreaker: true, stopAtDownstreamBreakers: true, stopAt: toOpen);
					lines.Add(toClose);

					yield return AddedRegion(lines, consumers);
				}
			}

			// Then consider the effect on consumers in the nodeB-nodeC segment (including branches hanging off it).
			{
				var consumers = configuration.ConsumersBelow(toOpen);

				// Before the move, all these consumers are affected by faults in lines upstream of nodeA
				var linesBefore = LinesAround(configuration, nodeA, stopAtUpstreamBreaker: false, stopAtDownstreamBreakers: true, stopAt: toOpen);
				linesBefore.Add(toOpen);

				// ...while after, they are affected by faults in lines upstream of nodeD
				var linesAfter = LinesAround(configuration, nodeD, stopAtUpstreamBreaker: false, stopAtDownstreamBreakers: true, stopAt: toOpen);
				linesAfter.Add(toClose);

				yield return RemovedRegion(linesBefore.Except(linesAfter).ToList(), consumers);
				yield return AddedRegion(linesAfter.Except(linesBefore).ToList(), consumers);
			}

			// These consumers also depend on lines between nodeB and nodeC.
			// Consider each group of such lines (from left to right), separated by circuit breakers.

			var limits = new[] { toOpen }.Concat(breakersBetweenBAndC).Concat(toClose);

			foreach ((Line leftLimit, Line rightLimit) in limits.AdjacentPairs())
			{
				Bus atLeftLimit = configuration.DownstreamEnd(leftLimit);

				// Consider the lines between leftLimit and rightLimit
				var lines = LinesAround(configuration, atLeftLimit, stopAtUpstreamBreaker: true, stopAtDownstreamBreakers: true, stopAt: toOpen);

				// Before the move, they affect consumers to the right (downstream) of rightLimit
				if (rightLimit != toClose)
				{
					var consumers = configuration.ConsumersBelow(rightLimit);
					yield return RemovedRegion(lines, consumers);
				}

				// ...while after the move, they affect consumers to the left (the new downstream) of leftLimit
				if (leftLimit != toOpen)
				{
					var consumers = configuration.ConsumersBelow(toOpen, stopAt: leftLimit);
					yield return AddedRegion(lines, consumers);
				}
			}


			// Local functions:

			DeltaRegion AddedRegion(List<Line> lines, List<Bus> consumers)
			{
				return new DeltaRegion(consumers, lines.Where(CanFault).ToList(), isAdded: true);
			}

			DeltaRegion RemovedRegion(List<Line> lines, List<Bus> consumers)
			{
				return new DeltaRegion(consumers, lines.Where(CanFault).ToList(), isAdded: false);
			}
		}

		/// <summary>
		/// Returns the closest upstream breaker for the line, not including the line itself
		/// </summary>
		private Line UpstreamBreaker(NetworkConfiguration configuration, Line line) => UpstreamBreaker(configuration, configuration.UpstreamEnd(line));

		/// <summary>
		/// Returns the closest upstream breaker for the bus
		/// </summary>
		private Line UpstreamBreaker(NetworkConfiguration configuration, Bus bus)
		{
			return configuration
				.LinesToProvider(bus)
				.FirstOrDefault(l => l.IsBreaker);
		}

		/// <summary>
		/// Enumerates the connected lines in the vicinity of <paramref name="bus"/>.
		/// </summary>
		/// <param name="configuration"></param>
		/// <param name="bus">The bus to start at</param>
		/// <param name="stopAtUpstreamBreaker">If true, the result will not extend across a breaker
		///   in the upstream direction (and not include the breaker)</param>
		/// <param name="stopAtDownstreamBreakers">If true, the result will not extend across a breaker
		///   in the downstream direction (and not include the breaker). 
		///   This includes breakers that are reached by first going upstream
		///   and then down a different branch.</param>
		/// <param name="stopAt">If not null, the result will not extend across this line, and not include
		///   the line itself</param>
		/// <returns></returns>
		private List<Line> LinesAround(NetworkConfiguration configuration, Bus bus, bool stopAtUpstreamBreaker, bool stopAtDownstreamBreakers, Line stopAt = null)
		{
			return configuration.LinesAround(bus, true, stopAt: StopAt);


			bool StopAt(Line line, Bus from)
			{
				if (line == stopAt)
					return true;

				if (line.IsBreaker)
				{
					bool goingUpstream = (configuration.DownstreamEnd(line) == from);

					if (goingUpstream && stopAtUpstreamBreaker)
						return true;
					if (!goingUpstream && stopAtDownstreamBreakers)
						return true;
				}

				return false;
			}
		}

		/// <summary>
		/// Returns the consumer buses that must wait for repair when <paramref name="faultingLine"/> has a fault,
		/// and caches the result
		/// </summary>
		private Bus[] CalculateAndCacheDisconnectedConsumers(NetworkConfiguration configuration, Line faultingLine)
		{
			Bus[] disconnectedConsumers;
			PowerNetwork network = configuration.Network;

			// The faulty line will be isolated by opening the closest switches

			var isolatingSwitches = network.ClosestSwitches(faultingLine);

			// The consumers that are disconnected when these switches are open
			// must wait for repair

			var isolatingConfiguration = NetworkConfiguration.AllClosed(network);
			foreach (var sw in isolatingSwitches)
				isolatingConfiguration.SetSwitch(sw, open: true);

			disconnectedConsumers = network.Consumers.Where(c => isolatingConfiguration.ProviderForBus(c) == null).ToArray();

			// Update cache

			_waitingConsumers[faultingLine.Index] = disconnectedConsumers;

			if (!faultingLine.IsSwitchable)
			{
				// The same set of customers is also correct for other lines inside the same 
				// set of isolating switches
				var otherLines = isolatingConfiguration.LinesAround(faultingLine.Node1, true).Except(faultingLine);

				foreach (var line in otherLines)
					_waitingConsumers[line.Index] = disconnectedConsumers;
			}

			return disconnectedConsumers;
		}

		#endregion

		#region Inner types

		/// <summary>
		/// Indicators for power outages and KILE costs.
		/// 
		/// The meaning of the indicators depends on context:
		/// The consumers whose outages are considered can range from a single consumer,
		/// up to all consumers in the whole network.
		/// The lines whose faults are considered can range from a single line,
		/// up to all lines in the whole network.
		/// The time period covered can vary from a single period to a full multi-period horizon.
		/// </summary>
		public class Indicators
		{
			/// <summary>
			/// The expected number of hours that the consumer(s) is without power in the period
			/// </summary>
			public double ExpectedOutageHours { get; internal set; }

			/// <summary>
			/// The expected number of Wh demanded but not delivered to the consumer(s) in the period
			/// </summary>
			public double ExpectedOutageWattHours { get; internal set; }

			/// <summary>
			/// The expected KILE cost in the period
			/// </summary>
			public double ExpectedKileCost { get; internal set; }

			/// <summary>
			/// Returns true if this Indicators and the <paramref name="other"/> Indicators
			/// are equal within the given tolerance
			/// </summary>
			public bool Equals(Indicators other, double tolerance)
			{
				return ExpectedOutageHours.EqualsWithTolerance(other.ExpectedOutageHours, tolerance)
					&& ExpectedOutageWattHours.EqualsWithTolerance(other.ExpectedOutageWattHours, tolerance)
					&& ExpectedKileCost.EqualsWithTolerance(other.ExpectedKileCost, tolerance);
			}
		}

		/// <summary>
		/// A partial specification of KILE cost changes due to a move.
		/// Contains a collection of lines and a collection of consumers. 
		/// If <see cref="IsAdded"/> is true, the semantics is that a fault in any of the lines
		/// affects all the consumers after the move, but not before the move.
		/// If <see cref="IsAdded"/> is false, they affect before the move, but not after.
		/// </summary>
		internal class DeltaRegion
		{
			/// <summary>
			/// The lines
			/// </summary>
			public IEnumerable<Line> Lines { get; set; }

			/// <summary>
			/// The consumers
			/// </summary>
			public IEnumerable<Bus> Consumers { get; set; }

			/// <summary>
			/// If true, 'line affects consumer' relations are added by the move. If false,
			/// they are removed.
			/// </summary>
			public bool IsAdded { get; set; }

			/// <summary>
			/// Initializes a delta region
			/// </summary>
			public DeltaRegion(List<Bus> consumers, List<Line> lines, bool isAdded)
			{
				Consumers = consumers;
				Lines = lines;
				IsAdded = isAdded;
			}

			public override string ToString()
			{
				string consumers = Consumers.Select(x => x.ToString()).Concatenate(" ");
				string lines = Lines.Select(x => x.ToString()).Concatenate(" ");
				string sign = IsAdded ? "+" : "-";

				if (consumers.Count() + lines.Count() < 8)
					return $"{sign}:  {consumers}   /   {lines}";
				else
					return $"{sign}:  {consumers}\n   /   {lines}";
			}
		}

		#endregion

	}

	public static partial class Extensions
	{
		/// <summary>
		/// Sum indicators component-wise.
		/// </summary>
		/// <param name="sequence"></param>
		/// <returns></returns>
		public static KileCostCalculator.Indicators Sum(this IEnumerable<KileCostCalculator.Indicators> sequence)
		{
			return new KileCostCalculator.Indicators
			{
				ExpectedOutageHours = sequence.Sum(x => x.ExpectedOutageHours),
				ExpectedOutageWattHours = sequence.Sum(x => x.ExpectedOutageWattHours),
				ExpectedKileCost = sequence.Sum(x => x.ExpectedKileCost),
			};
		}
	}
}
