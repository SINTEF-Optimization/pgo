using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Scoop.Utilities;
using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for KILE cost calculations
	/// </summary>
	[TestClass]
	public class KileCostTests
	{
		private const double _tolerance = 1e-10;
		private bool _requireRadialNetwork = true;
		private bool _createCalculatorInSetup = true;

		// Default calculation period: 1 hour
		private Period _firstPeriod = Period.Default;

		private PowerNetwork _network;
		private NetworkConfiguration _configuration;
		private PgoProblem _problem;
		private KileCostCalculator _calculator;
		private KileCostObjective _objective;

		[TestMethod]
		public void CalculatorRequiresABreakerAndASwitchBetweenProviderAndFaultingLine()
		{
			// This is OK:
			Setup("Generator[generator]  -- B[breaker] -o- S[closed] -o-  Line[faultsPerYear=0.1; sectioningTime=PT4H; repairTime=PT2H]  --  Consumer");

			// No breaker above faulting line - bad:
			TestUtils.AssertException(() =>
				Setup("Generator[generator]  -- S[closed] -o-  Line[faultsPerYear=0.1; sectioningTime=PT4H; repairTime=PT2H]  -o- B[breaker] --  Consumer"),
				requiredMessage: "Line Line can fault, but there is no breaker between it and provider Generator");

			// No switch above faulting line - bad:
			TestUtils.AssertException(() =>
				Setup("Generator[generator]  -- B[breaker] -o-  Line[faultsPerYear=0.1; sectioningTime=PT4H; repairTime=PT2H]  -o- S[closed] --  Consumer"),
				requiredMessage: "Line Line can fault, but there is no nonfaulting switch between it and provider Generator");

			// OK if line cannot fail:
			Setup("Generator[generator]  --  Line[faultsPerYear=0]  --  Consumer");
		}

		[TestMethod]
		public void IndicatorsForConsumerDueToLineAreCorrect()
		{
			Use1YearPeriod();
			Setup(
				"Consumer[consumption=(10,0); type=Industry]",
				"Generator[generator]  --  B[breaker] -o- L[closed] -o-  Line[faultsPerYear=0.1; sectioningTime=PT4H; repairTime=PT2H]  --  Consumer");

			var bus = _network.GetBus("Consumer");
			var line = _network.GetLine("Line");

			// Outage time time is 4h:
			var indicators = _calculator.IndicatorsFor(bus, line, mustWaitForRepair: false);
			double kileCost = _calculator.ExpectedKileCost(bus, TimeSpan.FromHours(4));

			Assert.AreEqual(0.1 * 4, indicators.ExpectedOutageHours);
			Assert.AreEqual(0.1 * 4 * 10, indicators.ExpectedOutageWattHours, _tolerance);
			Assert.AreEqual(0.1 * kileCost, indicators.ExpectedKileCost, _tolerance);

			// With repair, outage time time is 4+2h:
			indicators = _calculator.IndicatorsFor(bus, line, mustWaitForRepair: true);
			kileCost = _calculator.ExpectedKileCost(bus, TimeSpan.FromHours(4 + 2));

			Assert.AreEqual(0.1 * (4 + 2), indicators.ExpectedOutageHours);
			Assert.AreEqual(0.1 * (4 + 2) * 10, indicators.ExpectedOutageWattHours, _tolerance);
			Assert.AreEqual(0.1 * kileCost, indicators.ExpectedKileCost, _tolerance);
		}

		[TestMethod]
		public void IndicatorsScaleWithPeriodLength()
		{
			Setup(
				"Consumer[consumption=(10,0); type=Industry]",
				"Generator[generator]  --  B[breaker] -o- L[closed]  -o-  Line[faultsPerYear=0.1; sectioningTime=PT4H; repairTime=PT2H]  --  Consumer");

			var bus = _network.GetBus("Consumer");
			var line = _network.GetLine("Line");

			var indicators = _calculator.IndicatorsFor(bus, line, mustWaitForRepair: false);
			double kileCost = _calculator.ExpectedKileCost(bus, TimeSpan.FromHours(4));

			// Since the (default) period is 1 hour, the expected outage is much less than the yearly value
			double timeFactor = 1.0 / 365 / 24;

			Assert.AreEqual(0.1 * 4 * timeFactor, indicators.ExpectedOutageHours, _tolerance);
			Assert.AreEqual(0.1 * 4 * 10 * timeFactor, indicators.ExpectedOutageWattHours, _tolerance);
			Assert.AreEqual(0.1 * kileCost * timeFactor, indicators.ExpectedKileCost, _tolerance);
		}

		[TestMethod]
		public void AffectedConsumersIsCorrect()
		{
			Setup(
				"Generator[generator] -- Line1[breaker] -- N1 -- Line2 -- N2 -- Line3[breaker] -- N3",
				"N1 -- Line4 -- N4[consumption=(1,0)]",
				"N2 -- Line5 -- N5",
				"N3 -- Line6 -- N6[consumption=(1,0)]");

			// When a line faults, all buses below the first circuit breaker upstream of the line
			// are affected (lose power).

			AssertAffectedBuses("Line2", "N1 N2 N3 N4 N5 N6");
			AssertAffectedBuses("Line3", "N1 N2 N3 N4 N5 N6"); // We assume a breaker cannot handle its own fault
			AssertAffectedBuses("Line4", "N1 N2 N3 N4 N5 N6");
			AssertAffectedBuses("Line6", "N3 N6");

			AssertAffectedConsumers("Line4", "N4 N6");
			AssertAffectedConsumers("Line6", "N6");


			void AssertAffectedBuses(string faultyLineName, string expectedConsumers)
			{
				var line = _network.GetLine(faultyLineName);

				var consumers = _calculator.AffectedBuses(_configuration, line);
				Assert.AreEqual(expectedConsumers, consumers.Select(c => c.Name).OrderBy(n => n).Concatenate(" "));
			}

			void AssertAffectedConsumers(string faultyLineName, string expectedConsumers)
			{
				var line = _network.GetLine(faultyLineName);

				var consumers = _calculator.AffectedConsumers(_configuration, line);
				Assert.AreEqual(expectedConsumers, consumers.Select(c => c.Name).OrderBy(n => n).Concatenate(" "));
			}
		}

		[TestMethod]
		public void MustWaitForRepairIsCorrect()
		{
			Setup(
				"Consumer1[consumption=(1,0)]",
				"Consumer2[consumption=(1,0)]",
				"Consumer3[consumption=(1,0)]",
				"Generator1[generator] -- Br1[breaker] -o- Switch[closed] -- Consumer0 -- Line1[closed] -- Consumer1 -- Line2[closed] -- Consumer2 -- Line3[open] -- Generator2[generator]",
				"Generator1 -- Br2[breaker] -o- Switch2[closed] -o- Line4[closed] -- N1 -- Line5[closed] -- Consumer3");

			TestUtils.AssertException(() => MustWaitForRepair("Line3", "Consumer1"), requiredMessage: "Line3 is open -- cannot fault");
			TestUtils.AssertException(() => MustWaitForRepair("Line2", "Consumer3"), requiredMessage: "Consumer3 is not affected by faults at Line2");

			// A consumer must wait for repair if it cannot be isolated from the faulty line, or if
			// no path to a generator remains when the area around the faulty line har been isolated.

			Assert.IsFalse(MustWaitForRepair("Line1", "Consumer2")); // Consumer can be isolated from fault and supplied using another route
			Assert.IsTrue(MustWaitForRepair("Line2", "Consumer1")); // Consumer cannot be isolated from fault
			Assert.IsTrue(MustWaitForRepair("Line1", "Consumer1")); // Consumer cannot be isolated from fault
			Assert.IsTrue(MustWaitForRepair("Line4", "Consumer3")); // Consumer can be isolated from fault but cannot get power through another path

			Setup(
				"Consumer[consumption=(1,0)]",
				"Generator[generator] -- Br[breaker] -- N1",
				"N1 -- L1a[closed] -- N2a -- L2a -- N3a -- L3a[closed] -- N4 -- L4 -- Consumer",
				"N1 -- L1b[open]  --  N2b -- L2b -- N3b -- L3b[open]  --  N4",
				"N2a -- Bridge[closed] -- N2b");

			// When L2a faults, it is isolated by opening L1a, L3a and Bridge.
			// Then, power is supplied to the Consumer by closing L1b and L3b.
			Assert.IsFalse(MustWaitForRepair("L2a", "Consumer")); // Consumer can be isolated from fault and supplied using another route



			bool MustWaitForRepair(string lineName, string busName)
			{
				var line = _network.GetLine(lineName);
				var bus = _network.GetBus(busName);

				return _calculator.MustWaitForRepair(_configuration, line, bus);
			}
		}

		[TestMethod]
		public void OutageDueToLineEqualsSumOverAffectedConsumers()
		{
			SetupCalculatorWithRandomData();

			foreach (var line in LinesThatCanFault)
			{
				var indicators = _calculator.IndicatorsForFaultsIn(_configuration, line);

				var sumOverConsumers = _calculator.AffectedConsumers(_configuration, line)
					.Select(consumer => _calculator.IndicatorsFor(_configuration, consumer, line))
					.Sum();

				Assert.IsTrue(indicators.Equals(sumOverConsumers, 1e-6));
			}
		}

		[TestMethod]
		public void OutageForConsumerEqualsSumOverLinesAffectingIt()
		{
			SetupCalculatorWithRandomData();

			foreach (var consumer in _network.Consumers)
			{
				var indicators = _calculator.IndicatorsForOutagesAt(_configuration, consumer);

				var sumOverLines = LinesThatCanFault
					.Where(l => _calculator.Affects(_configuration, l, consumer))
					.Select(l => _calculator.IndicatorsFor(_configuration, consumer, l))
					.Sum();

				Assert.IsTrue(indicators.Equals(sumOverLines, 1e-6));
			}
		}

		[TestMethod]
		public void TotalOutageEqualsSumOverFaultingLines()
		{
			SetupCalculatorWithRandomData();

			var indicators = _calculator.TotalIndicators(_configuration);

			var sumOverLines = LinesThatCanFault
				.Select(l => _calculator.IndicatorsForFaultsIn(_configuration, l))
				.Sum();

			Assert.IsTrue(indicators.Equals(sumOverLines, 1e-6));
		}

		[TestMethod]
		public void TotalOutageEqualsSumsOverConsumerNodes()
		{
			SetupCalculatorWithRandomData();

			var indicators = _calculator.TotalIndicators(_configuration);

			var sumOverConsumers = _network.Consumers
				.Select(consumer => _calculator.IndicatorsForOutagesAt(_configuration, consumer))
				.Sum();

			Assert.IsTrue(indicators.Equals(sumOverConsumers, 1e-6));
		}

		[TestMethod]
		public void PowerEstimationFromDemandIsCorrect()
		{
			Setup("Consumer[consumption=(1,0)] -- Line -- G[generator]");
			var consumer = _network.Consumers.Single();

			// Define three periods
			var demands1 = new PowerDemands(_network); // (The period is 1h)
			var demands2 = new PowerDemands(_network);
			var demands3 = new PowerDemands(_network);

			// Define demand in each
			demands1.SetActivePowerDemand(consumer, 1);
			demands2.SetActivePowerDemand(consumer, 2);
			demands3.SetActivePowerDemand(consumer, 3);

			// Make period data from the demands for the given periods
			var periodData1 = new PeriodData(_network, demands1, Period.Default);
			var periodData2 = new PeriodData(_network, demands2, Period.Following(periodData1.Period, TimeSpan.FromHours(1)));
			var periodData3 = new PeriodData(_network, demands3, Period.Following(periodData2.Period, TimeSpan.FromHours(1.3)));

			var periodData = new List<PeriodData> { periodData1, periodData2, periodData3 };
			var problem = new PgoProblem(periodData, new SimplifiedDistFlowProvider(), "name");
			var (period1, period2, period3) = problem.Periods;


			var powerEstimator = new ExpectedConsumptionFromDemand(problem.AllPeriodData);

			// The average 1h interval lies half in period1 and half in period2:
			AssertAveragePower(period1, 1, 1.5);

			// Half of the time, we're halfway into period2:
			AssertAveragePower(period1, 0.5, 1.25);

			// The average 1h interval lies half in period2 and half in period3:
			AssertAveragePower(period2, 1, 2.5);

			// The average 2h interval covers half of period1, all of period2 and half of period3:
			AssertAveragePower(period1, 2, 2);

			// The first 1h has average demand 2.5, the last has always 3:
			AssertAveragePower(period2, 2, 2.75);

			// The demand for any time starting in period3 is 3, since period3's demand is used after the last period
			AssertAveragePower(period3, 1, 3);
			AssertAveragePower(period3, 2, 3);


			void AssertAveragePower(Period period, double durationHours, double expectedAveragePower)
			{
				var span = TimeSpan.FromHours(durationHours);
				double averagePower = powerEstimator.AveragePower(consumer, period, span);

				Assert.AreEqual(expectedAveragePower, averagePower);
			}
		}

		[TestMethod, Ignore]
		public void PowerEstimationFromReferencePowerIsCorrect()
		{
			// This is a reminder that the actual definition of KILE cost is based on a reference power
			// per consumer, adjusted with time-dependent correction factors, while for now, we
			// base it on the actual demand. 
			// We should implement another power estimator for this, but not until we've verified
			// what the customer wants.

			Assert.Fail();
		}

		[TestMethod]
		public void KileCostForSingleConsumerIsCorrect()
		{
			_requireRadialNetwork = false;

			Setup(
			"Agriculture[consumption=(1000,0); type=Agriculture]",
			"Domestic[consumption=(1000,0); type=Domestic]",
			"Industry[consumption=(1000,0); type=Industry]",
			"Industry2[consumption=(2000,0); type=Industry]",
			"Trade[consumption=(1000,0); type=Trade]",
			"Public[consumption=(1000,0); type=Public]",
			"ElIndustry[consumption=(1000,0); type= ElectricIndustry]",

			"Mixed[consumption=(1000,0); type=Agriculture/0.2,Domestic/0.8]");

			// All costs verified below are per kWh

			// Duration: less than 1 minute
			var duration = TimeSpan.FromSeconds(30);
			AssertKileCost("Industry", 34);
			AssertKileCost("Industry2", 34 * 2);  // 2 kW -> twice the cost
			AssertKileCost("Agriculture", 5 + 14.3 * duration.TotalHours);

			// From 1 minute to 1 hour
			duration = TimeSpan.FromMinutes(30);
			AssertKileCost("Domestic", 1.1 + 9.8 * duration.TotalHours);
			AssertKileCost("Public", 60 + 113.2 * duration.TotalHours);

			// From 1 to 4 hours
			duration = TimeSpan.FromHours(2.6);
			AssertKileCost("Agriculture", 19 + 15.6 * (duration.TotalHours - 1));
			AssertKileCost("Trade", 196 + 91.1 * (duration.TotalHours - 1));

			// From 4 to 8 hours
			duration = TimeSpan.FromHours(5.2);
			AssertKileCost("ElIndustry", 91 + 2.8 * duration.TotalHours);
			AssertKileCost("Trade", 469 + 141.3 * (duration.TotalHours - 4));

			// 8 hours and more
			duration = TimeSpan.FromHours(19);
			AssertKileCost("Public", 464 + 17.6 * (duration.TotalHours - 8));
			AssertKileCost("Domestic", 1.1 + 9.8 * duration.TotalHours);

			// Check a different duration in the same duration range
			duration = TimeSpan.FromHours(21);
			AssertKileCost("Public", 464 + 17.6 * (duration.TotalHours - 8));
			AssertKileCost("Domestic", 1.1 + 9.8 * duration.TotalHours);

			// Consumer belongs to multiple categories
			double agricultureCost = 66 + 14.3 * (duration.TotalHours - 4);
			double domesticCost = 1.1 + 9.8 * duration.TotalHours;
			AssertKileCost("Mixed", 0.2 * agricultureCost + 0.8 * domesticCost);



			void AssertKileCost(string consumerName, double expectedCost)
			{
				var consumer = _network.GetBus(consumerName);

				var cost = _calculator.ExpectedKileCost(consumer, duration);

				Assert.AreEqual(expectedCost, cost, _tolerance);
			}
		}

		[TestMethod]
		public void KileCostObjectiveValueIsCorrect()
		{
			SetupProblemWithRandomData(new Random(), periodCount: 2);

			var solution = _problem.RadialSolution();

			double expectedValue = _problem.Periods
				.Sum(period => _objective.CalculatorFor(period).TotalIndicators(solution.GetPeriodSolution(period).NetConfig).ExpectedKileCost);

			Assert.AreEqual(expectedValue, _objective.Value(solution));
		}

		[TestMethod]
#if DEBUG
		[Ignore] // Performance test fails in debug builds
#endif
		public void KileCostDeltaValueIsEfficient()
		{
			Random random = new Random();

			// Use a random period length, to avoid coincidences
			_firstPeriod = new Period(new DateTime(2001, 1, 1), new DateTime(2001, 1, 1, 1, random.Next(60), 20), 0);

			SetupProblemWithRandomData(random, periodCount: 7, sizeCategory: 3);

			var solution = _problem.RadialSolution(random);

			Stopwatch sw = new Stopwatch();
			sw.Start();

			for (int i = 0; i < 20; ++i)
			{
				var moves = solution.GetAllRadialSwapMoves().Shuffled(random).Take(100).ToList();
				double sum = moves.Sum(m => _objective.DeltaValue(m));
				Console.WriteLine(sum);
			}

			sw.Stop();
			Console.WriteLine(sw.Elapsed.TotalSeconds);

			Assert.IsTrue(sw.Elapsed.TotalSeconds < 3.5, $"We expected the test to run in less than 3.5 seconds; it took {sw.Elapsed.TotalSeconds}");
		}

		[TestMethod]
		public void DeltaRegionsAffectedIsCorrect()
		{
			var random = new Random();
			for (int i = 0; i < 10; ++i)
			{
				SetupProblemWithRandomData(random);

				var solution = _problem.RadialSolution(random);
				var calculator = _objective.CalculatorFor(_firstPeriod);
				var config = solution.GetPeriodSolution(_firstPeriod).NetConfig;

				// We have a radial solution.
				// Find all line/consumer pairs where a fault in the line affects the
				// consumer. Each is represented by a string.
				var allPairsBefore = AllAffectsPairs(config, calculator).ToList();

				//TestBench.Program.Run(_problem, solution);

				foreach (var move in TestUtils.AllMovesFor(solution, _firstPeriod))
				{
					// Find all pairs after the move has been applied
					move.Apply(true);
					var allPairsAfter = AllAffectsPairs(config, calculator).ToList();
					move.GetReverse().Apply(true);

					// Ask for a delta evaluation of which lines affect which consumers
					var deltaRegions = calculator.DeltaRegionsForAffects(move).ToList();

					// Then verify that the delta is correct

					var added = deltaRegions.Where(r => r.IsAdded).SelectMany(r => AllPairs(r)).ToList();
					var removed = deltaRegions.Where(r => !r.IsAdded).SelectMany(r => AllPairs(r)).ToList();

					var expectedAdded = allPairsAfter.Except(allPairsBefore).ToList();
					var expectedRemoved = allPairsBefore.Except(allPairsAfter).ToList();

					CollectionAssert.AreEquivalent(expectedAdded, added);
					CollectionAssert.AreEquivalent(expectedRemoved, removed);
				}


				IEnumerable<string> AllPairs(KileCostCalculator.DeltaRegion region)
				{
					foreach (var consumer in region.Consumers)
					{
						foreach (var line in region.Lines)
						{
							if (calculator.CanFault(line))
								yield return $"{line}/{consumer}";
						}
					}
				}
			}

			IEnumerable<string> AllAffectsPairs(NetworkConfiguration configuration, KileCostCalculator calculator)
			{
				foreach (var consumer in _network.Consumers)
				{
					foreach (var line in calculator.LinesAffecting(configuration, consumer))
					{
						yield return $"{line}/{consumer}";
					}
				}
			}
		}

		[TestMethod]
		public void ProviderCanSuppressFaultsAroundProviders()
		{
			_createCalculatorInSetup = false; // Avoid automatic consistency checks
			Setup(
				"Generator[generator]  -- B1[breaker] -o- S1[closed] -o- Line1 --  Consumer1",
				"Generator  -- B2[breaker] -o- Line2 -o- S2[closed] --  Consumer2",
				"Generator  -- S3[closed] -o- Line3 -o- B3[breaker] --  Consumer3",
				"Generator  -- Line4 -o- S4[closed] -o- B4[breaker] --  Consumer4"
				);

			var faultProvider = LineFaultPropertiesProvider.RandomFor(_network);

			// This provider has positive fault frequency for all lines, 
			// including lines with insufficient separation from a generator

			Assert.AreEqual(8, faultProvider.FaultingLineCount);
			TestUtils.AssertException(() => KileCostCalculator.VerifyNetwork(_network, faultProvider),
				requiredMessage: "Line S3 can fault, but there is no breaker between it and provider Generator");

			faultProvider.SuppressFaultsAroundProviders();

			// Now only Line1 can fault

			Assert.AreEqual(1, faultProvider.FaultingLineCount);
			KileCostCalculator.VerifyNetwork(_network, faultProvider);
		}

		[TestMethod]
		public void ExpectedConsumptionCacheIsCorrect()
		{
			Random random = new Random();

			// Use a random period length, to avoid coincidences
			_firstPeriod = new Period(new DateTime(2001, 1, 1), new DateTime(2001, 1, 1, 1, random.Next(60), 20), 0);

			SetupProblemWithRandomData(random, periodCount: 8);

			var consumer = _problem.Network.Consumers.First();
			var consumptionProvider = new ExpectedConsumptionFromDemand(_problem.AllPeriodData);
			var consumptionCache = new ExpectedConsumptionCache(_problem.Network, _problem.Periods, consumptionProvider);

			foreach (var period in _problem.Periods)
			{
				for (int i = 0; i < 60 * 20; ++i)
				{
					var duration = TimeSpan.FromMinutes(i);

					double expectedConsumption = consumptionProvider.EnergyConsumption(consumer, period, duration);
					double cachedConsumption = consumptionCache.EnergyConsumption(consumer, period, duration);

					// This tolerance can be increased if necessary. We've already done it once, when moving from 
					// .NET Framework 4.7.2 to .NET 7.0, as a change in TimeSpan.FromHours caused worse rounding errors.
					Assert.AreEqual(expectedConsumption, cachedConsumption, 1e-7);
				}
			}
		}

		[TestMethod]
		public void KileObjectiveIsSetUpFromJson()
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("kilecost-network.json"));
			var demands = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, TestUtils.TestDataFile("kilecost-demands.json"));

			var problem = new PgoProblem(demands, new SimplifiedDistFlowProvider(), "Kile problem");
			var kileObjective = PgoProblem.FindObjectives<KileCostObjective>(problem.CriteriaSet).Single();

			var solution = new PgoSolution(problem);

			// We expect the cost calculated as follows:
			// 365 faults per year * 1 day period: Expected faults  = 1
			// Consumer category is Public, outage 3 hours: Cost per kW = 228.8
			// Consumption is 2000 kW.
			// 1 * 228.8 * 2000 = 457600

			Assert.AreEqual(457600, kileObjective.Value(solution), 0.1);
		}

		[TestMethod]
		public void KileObjectiveRecognizesIfItIsAlwaysZero()
		{
			// General setup: objective is not zero
			Setup(
				"Consumer[consumption=(10,0); type=Industry]",
				"Generator[generator]  --  B[breaker] -o- L[closed]  -o-  Line[faultsPerYear=0.1; sectioningTime=PT4H; repairTime=PT2H]  --  Consumer");

			Assert.AreNotEqual(0, _objective.Value(_problem.RadialSolution()));
			Assert.IsFalse(_objective.IsAlwaysZero);

			// Zero faults: objective is always zero
			Setup(
				"Consumer[consumption=(10,0); type=Industry]",
				"Generator[generator]  --  B[breaker] -o- L[closed]  -o-  Line[faultsPerYear=0]  --  Consumer");

			Assert.AreEqual(0, _objective.Value(_problem.RadialSolution()));
			Assert.IsTrue(_objective.IsAlwaysZero);
		}

		private IEnumerable<Line> LinesThatCanFault => _configuration.PresentLines.Where(l => !l.IsBreaker);

		/// <summary>
		/// Sets the time period used by the network builder and thus the KILE calculator to a 1 year period
		/// </summary>
		private void Use1YearPeriod()
		{
			_firstPeriod = new Period(new DateTime(2001, 1, 1), new DateTime(2002, 1, 1), 0); // Not a leap year
		}

		/// <summary>
		/// Sets up a network and KILE cost calculator based on the given network data
		/// </summary>
		/// <param name="networkSpec">Network data to feed to a <see cref="NetworkBuilder"/></param>
		private void Setup(params string[] networkSpec)
		{
			NetworkBuilder builder = new NetworkBuilder();
			builder.UsePeriod(_firstPeriod);

			foreach (var line in networkSpec)
				builder.Add(line);

			_network = builder.Network;
			_configuration = builder.Configuration;
			if (_createCalculatorInSetup)
				_calculator = new KileCostCalculator(builder.PeriodData.Period, builder.Network,
					builder.ConsumptionFromDemand, builder.LineFaultPropertiesProvider,
					builder.ConsumerCategoryProvider);

			if (_requireRadialNetwork)
				Assert.IsTrue(builder.Configuration.IsRadial);

			_problem = builder.SinglePeriodProblem;

			_objective = _problem.CriteriaSet.Objective.Components().OfType<KileCostObjective>().Single();
		}

		/// <summary>
		/// Initializes the KILE calculator with a random network, random demands and random
		/// fault properties.
		/// </summary>
		private void SetupCalculatorWithRandomData(int? seed = null)
		{
			Random random = new Random();
			if (seed != null)
				random = new Random(seed.Value);

			SetupProblemWithRandomData(random);

			var consumptionProvider = new ExpectedConsumptionFromDemand(_problem.AllPeriodData);
			var faultPropertiesProvider = LineFaultPropertiesProvider.RandomFor(_network);
			var consumerCategoryProvider = ConsumerCategoryProvider.RandomFor(_network);

			_configuration = NetworkConfiguration.AllClosed(_network);
			_configuration.MakeRadial(random);

			_calculator = new KileCostCalculator(_firstPeriod, _network, consumptionProvider, faultPropertiesProvider, consumerCategoryProvider);
		}

		private void SetupProblemWithRandomData(Random random = null, int periodCount = 1, int sizeCategory = 1)
		{
			random = random ?? new Random();
			switch (sizeCategory)
			{
				case 1:
					_network = TestUtils.SmallRandomNetwork(random);
					break;

				case 2:
					_network = TestUtils.MediumRandomNetwork(random);
					break;

				case 3:
					_network = TestUtils.LargeRandomNetwork(random);
					break;

				default:
					throw new NotImplementedException();
			}

			_problem = TestUtils.ProblemWithRandomDemands(_network, random, periodCount, _firstPeriod);

			_objective = new KileCostObjective(_problem.Network, _problem.Periods, new ExpectedConsumptionFromDemand(_problem.AllPeriodData),
				LineFaultPropertiesProvider.RandomFor(_network, random),
				ConsumerCategoryProvider.RandomFor(_network, random),
				true);

			_firstPeriod = _problem.AllPeriodData.First().Period;
		}
	}
}
