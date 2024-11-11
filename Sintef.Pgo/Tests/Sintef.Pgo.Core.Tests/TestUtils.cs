using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.DataContracts;
using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Sintef.Pgo.Core.Test
{
	public class TestUtils : Sintef.Scoop.Kernel.TestUtils
	{
		/// <summary>
		/// The full path of the Pgo project root
		/// </summary>
		public static string PgoRootDir => DirectoryFinder.FindDirectoryAboveCurrent(null, new[] { "Data", "Sintef.Pgo", "Scripts" });

		/// <summary>
		/// The full path of the test projects folder
		/// </summary>
		public static string TestsDir => Path.Combine(PgoRootDir, "Sintef.Pgo", "Tests");

		/// <summary>
		/// The full path of the test data folder
		/// </summary>
		public static string TestDataDir => Path.Combine(TestsDir, "Data");

		/// <summary>
		/// The full path of the expected test results folder
		/// </summary>
		public static string ExpectedResultsDir => Path.Combine(TestsDir, "ExpectedResults");

		/// <summary>
		/// The full path of the Data folder under the Pgo project root
		/// </summary>
		public static string PgoDataDir => Path.Combine(PgoRootDir, "Data");

		/// <summary>
		/// The full path of the folder containing DIGIN data
		/// </summary>
		public static string DiginDir => Path.Combine(PgoDataDir, "DIGIN10", "v2.2");

		/// <summary>
		/// The full path of the folder containing DIGIN grid data on JSON-LD format
		/// </summary>
		public static string DiginGridDir => Path.Combine(DiginDir, "Grid", "CIMJSON-LD");

		/// <summary>
		/// Returns the full path to the named file in the test data folder
		/// </summary>
		public static string TestDataFile(string fileName) => Path.Combine(TestDataDir, fileName);

		/// <summary>
		/// Returns the full path to the named file in the example data folder
		/// </summary>
		public static string ExampleDataFile(string fileName) => Path.Combine(PgoDataDir, "Examples", fileName);

		/// <summary>
		/// An example CIM network file, with the combined DIGIN network data
		/// </summary>
		public static string DiginCombinedNetworkFile => Path.Combine(DiginDir, "CombinedNetwork.jsonld");

		/// <summary>
		/// An example CIM demands/config file, with the combined DIGIN SSH data
		/// </summary>
		public static string DiginCombinedSshFile => Path.Combine(DiginDir, "CombinedSsh.jsonld");


		/// <summary>
		/// Creates a default period
		/// </summary>
		public static Pgo.DataContracts.Period DefaultPeriod => new()
		{
			StartTime = DateTime.Now,
			EndTime = DateTime.Now.AddHours(1),
			Id = $"Period 1"
		};

		public static PgoProblem SetupProblemWithRandomData(Random random = null, int periodCount = 1, int sizeCategory = 1)
		{
			random = random ?? new Random();
			PowerNetwork network;
			switch (sizeCategory)
			{
				case 1:
					network = SmallRandomNetwork(random);
					break;

				case 2:
					network = MediumRandomNetwork(random);
					break;

				case 3:
					network = LargeRandomNetwork(random);
					break;

				default:
					throw new NotImplementedException();
			}

			return ProblemWithRandomDemands(network, random, periodCount);
		}

		/// <summary>
		/// Returns a small network suitable for testing
		/// </summary>
		public static NetworkBuilder SmallTestNetwork(IFlowProvider flowProvider = null)
		{
			NetworkBuilder builder = new NetworkBuilder(flowProvider);

			// There is a generator in a cycle of 6 nodes, N1-N6. One line, L6, is open.

			builder.Add("N1[generator] -- L1[closed] -- N2[consumption=(1,0)] -- L2[closed] -- N3[consumption=(1,0)] -- L3[closed] -- N4[consumption=(1,0)]");
			builder.Add("N4 -- L4[closed] -- N5[consumption=(1,0)] -- L5[closed] -- N6[consumption=(1,0)] -- L6[open] -- N1");

			// ... and some extra, open links within the cycle

			builder.Add("N1 -- Lx[open] -- N5");
			builder.Add("N3 -- Ly[open] -- N6");
			builder.Add("N4 -- Lz[open] -- N2");

			// ... and a linear network with one generator

			builder.Add("N10[generator] -- L10[closed] -- N11[consumption=(1,0)] -- L11[closed] -- N12[consumption=(1,0)]");

			// ... and some open links between the cycle and the linear network

			builder.Add("N2 -- La[open] -- N10");
			builder.Add("N3 -- Lb[open] -- N11");
			builder.Add("N4 -- Lc[open] -- N12");

			return builder;
		}

		/// <summary>
		/// Returns a small network suitable for testing
		/// </summary>
		public static NetworkBuilder ExampleNetworkWithLeafComponents(IFlowProvider flowProvider = null)
		{
			NetworkBuilder builder = new NetworkBuilder(flowProvider);

			builder.Add("n1[generator] -- l11 -- n11[consumption=(1,0)]");
			builder.Add("n1 -- l21[open] -- n21 -- l22 -- n22[consumption=(1,0)]"); // leaf component from n21
			builder.Add("n1 -- l31 -- n31 -- l32[open] -- n32 -- l33 -- n33[consumption=(1,0)]"); // leaf component from n31

			builder.Add("n1 -- l41 -- n41");
			builder.Add("n41 -- l42a[open] -- n42a -- l43a -- n43");
			builder.Add("n41 -- l42b[open] -- n42b -- l43b -- n43");
			builder.Add("n43 -- l44 -- n44[consumption=(1,0)]");
			builder.Add("n43 -- l45 -- n45[consumption=(1,0)] -- l46 -- n46[consumption=(1,0)]");

			return builder;
		}

		/// <summary>
		/// Returns a small network suitable for testing parallel and serial aggregation
		/// </summary>
		public static NetworkBuilder SmallTestNetworkThatCanBeAggregated(IFlowProvider flowProvider = null)
		{
			NetworkBuilder builder = new NetworkBuilder(flowProvider);

			// N1 and N2 are connected by three lines in parallel 
			builder.Add("N1[generator] -- La1 -- N2[consumption=(1,0)]");
			builder.Add("N1 -- La3 -- N2");
			builder.Add("N2 -- La2 -- N1");

			// N3 and N4 are connected by four lines in series
			builder.Add("N3[generator] -- Lb1 -o- Lb2 -- Nb");
			builder.Add("N4[consumer] -- Lb3 -o- Lb4 -- Nb");

			// N5 and N6 are connected by both parallel and serial lines
			builder.Add("N5[generator] -- Lc99 -- N6[consumption=(1,0)]");
			builder.Add("N5 -- Lc1 -o- Lc2 -o- lc3 -- Nc -- lc4 -o- lc5 -- N6");
			builder.Add("N5 -- Lc6 -o- Lc7 -o- lc8 -- Nc -- lc9 -o- lc10 -- N6");
			builder.Add("N6 -- Lc11 -o- Lc12 -o- lc13 -- Nc -- lc14 -o- lc15 -- N5");

			return builder;
		}

		/// <summary>
		/// Parses all DIGIN10 data
		/// </summary>
		public static CimJsonParser ParseAllDiginData()
		{
			var parser = new CimJsonParser(new DiginUnitsProfile());

			parser.ParseAllJsonFiles(DiginGridDir);
			parser.CreateCimObjects();

			return parser;
		}

		/// <summary>
		/// Parses all DIGIN10 data necessary to define the power network
		/// </summary>
		public static CimJsonParser ParseAllDiginNetworkData()
		{
			return CimJsonParser.ParseDiginFormatNetworkData(DiginGridDir);
		}

		/// <summary>
		/// Parses the steady state data (_SSH files) from DIGIN10
		/// </summary>
		/// <returns></returns>
		public static CimJsonParser ParseDiginSteadyStateData()
		{
			var parser = new CimJsonParser(new DiginUnitsProfile());

			parser.ParseAllJsonFiles(DiginGridDir, "SSH");

			parser.CreateCimObjects();

			return parser;
		}

		/// <summary>
		/// Creates a problem with random demands, based on the given network
		/// </summary>
		/// <param name="network">The network</param>
		/// <param name="random">The random generator to use. If null, one is created</param>
		/// <param name="periodCount">The number of (equal-length) periods to use</param>
		/// <param name="firstPeriod">The first period. If null, a default period is used</param>
		public static PgoProblem ProblemWithRandomDemands(PowerNetwork network, Random random = null,
			int periodCount = 1, Period firstPeriod = null,
			IFlowProvider flowProvider = null)
		{
			random = random ?? new Random();
			firstPeriod = firstPeriod ?? Period.Default;

			var initialPeriodData = new PeriodData(network, new PowerDemands(network), firstPeriod);
			initialPeriodData.Demands.SetAllConsumerDemands(new Complex(1, 0));

			List<PeriodData> allData = new List<PeriodData>();
			for (int i = 0; i < periodCount; ++i)
			{
				allData.Add(initialPeriodData.GetModifiedPeriodData(random, firstPeriod.Length.Times(i), i));

			}

			return new PgoProblem(allData,
				flowProvider ?? new SimplifiedDistFlowProvider(), "Problem with random demands");
		}

		/// <summary>
		/// Returns a random network with 2 providers and 20 lines
		/// </summary>
		/// <param name="random">The random generator to use</param>
		public static PowerNetwork SmallRandomNetwork(Random random, Action<RandomNetworkBuilder> modifyParameters = null)
		{
			var creator = new RandomNetworkBuilder(random);

			modifyParameters?.Invoke(creator);

			creator.Components.ConsumerCount = 12;
			creator.Components.BreakersAreSwitches = true;

			return creator.Create(true);
		}

		/// <summary>
		/// Returns a random network with 2 providers and 100 lines
		/// </summary>
		/// <param name="random">The random generator to use</param>
		public static PowerNetwork MediumRandomNetwork(Random random)
		{
			var creator = new RandomNetworkBuilder(random);

			creator.Topology.LineCount = 100;
			creator.Topology.BranchCount = 15;
			creator.Topology.CycleCount = 8;

			creator.Components.ProviderCount = 2;
			creator.Components.ConsumerCount = 80;
			creator.Components.BreakerCount = 7;
			creator.Components.BreakersAreSwitches = true;

			return creator.Create(true);
		}

		/// <summary>
		/// Returns a random network with 2 providers and 400 lines
		/// </summary>
		/// <param name="random">The random generator to use</param>
		public static PowerNetwork LargeRandomNetwork(Random random)
		{
			var creator = new RandomNetworkBuilder(random);

			creator.Topology.LineCount = 400;
			creator.Topology.BranchCount = 25;
			creator.Topology.CycleCount = 18;

			creator.Components.ProviderCount = 2;
			creator.Components.ConsumerCount = 200;
			creator.Components.BreakerCount = 7;
			creator.Components.BreakersAreSwitches = true;

			return creator.Create(true);
		}

		/// <summary>
		/// Returns a linear network with 1 provider and the given number of lines
		/// </summary>
		/// <param name="random">The random generator to use</param>
		public static PowerNetwork LinearNetwork(int lineCount, Random random)
		{
			var creator = new RandomNetworkBuilder(random);

			creator.Topology.LineCount = lineCount;
			creator.Topology.BranchCount = 0;
			creator.Topology.CycleCount = 0;

			creator.Components.ProviderCount = 1;
			creator.Components.ConsumerCount = 200;
			creator.Components.BreakerCount = 7;
			creator.Components.BreakersAreSwitches = true;

			return creator.Create(true);
		}

		/// <summary>
		/// Asserts that invoking the Action throws an exception
		/// </summary>
		/// <param name="action">The action to execute</param>
		/// <param name="requiredInMessage">If not null, this string must be contained in the exception message</param>
		/// <param name="requiredMessage">If not null, the this string must equal exception message</param>
		/// <param name="requiredType">If not null, the exception thrown must be of this type</param>
		public static void AssertException(Action action, string requiredInMessage = null, string requiredMessage = null, Type requiredType = null)
		{
			try
			{
				action.Invoke();
			}
			catch (Exception ex)
			{
				if (requiredType != null)
					Assert.AreEqual(requiredType, ex.GetType());
				if (requiredMessage != null)
					Assert.AreEqual(requiredMessage, ex.Message);
				if (requiredInMessage != null)
					StringAssert.Contains(ex.Message, requiredInMessage);

				// Success
				return;
			}
			Assert.Fail("Expected an exception but did not get one");
		}

		/// <summary>
		/// Verifies that LegalMove returns the correct value for all tested moves.
		/// 
		/// Also verifies that the moves tested of each move type include both feasible and infeasible moves.
		/// </summary>
		/// <param name="constraint">The constraint to test</param>
		/// <param name="allMoves">The moves to test the constraint for</param>
		/// <param name="allMovesAreFeasible">Contains the names of move classes where it is ok
		///   that none of the moves tested are infeasible</param>
		public static void VerifyLegalMove(IConstraint constraint, IEnumerable<Move> allMoves, string allMovesAreFeasible = "")
		{
			var solution = allMoves.First().Solution;

			Assert.IsTrue(constraint.IsSatisfied(solution), "The initial solution must satisfy the constraint being tested");

			var groups = allMoves.GroupBy(m => m.GetType());

			foreach (var group in groups)
			{
				int feasibleCount = 0;
				int infeasibleCount = 0;

				foreach (var move in group)
				{
					bool deltaFeasible = constraint.LegalMove(move);

					move.Apply(constraint.RequiresPropagation);
					bool feasible = constraint.IsSatisfied(solution);
					move.GetReverse().Apply(constraint.RequiresPropagation);

					if (feasible != deltaFeasible)
						Assert.AreEqual(feasible, deltaFeasible, $"Wrong LegalValue for move: {move}");

					if (feasible)
						++feasibleCount;
					else
						++infeasibleCount;
				}

				Console.WriteLine($"Type {group.Key.Name}: {feasibleCount} feasible, {infeasibleCount} infeasible");

				string moveType = group.Key.Name;

				Assert.AreNotEqual(0, feasibleCount, $"No feasible moves of type {moveType}");

				if (!allMovesAreFeasible.Contains(moveType))
					Assert.AreNotEqual(0, infeasibleCount,
						$"There are no infeasible moves of type {moveType}. If this is expected, " +
						$"add the string '{moveType}' to the {nameof(allMovesAreFeasible)} parameter");

				else
					Assert.AreEqual(0, infeasibleCount,
						$"We were promised there are no infeasible moves of type {moveType} (in {nameof(allMovesAreFeasible)})," +
						" but one or more were found");
			}
		}

		/// <summary>
		/// Verifies that DeltaValue returns the correct value for all tested moves.
		/// 
		/// Also verifies that the moves tested of each move type include moves with nonzero delta value.
		/// </summary>
		/// <param name="objective">The objective to test</param>
		/// <param name="allMoves">The moves to test the objective for</param>
		/// <param name="tolerance">The absolute numerical tolerance to use when comparing delta values</param>
		/// <param name="allMovesAreNeutral">Contains the names of move classes where it is expected
		///   that all the moves tested have zero delta value</param>
		///   <param name="allMovesMayBeNeutral">Contains the names of move classes where it is ok
		///   that all the moves tested have zero delta value</param>
		public static void VerifyDeltaValue(IObjective objective, IEnumerable<Move> allMoves,
			double tolerance = 0,
			string allMovesAreNeutral = "", string allMovesMayBeNeutral = "")
		{
			var solution = allMoves.First().Solution;

			var groups = allMoves.GroupBy(m => m.GetType());

			foreach (var group in groups)
			{
				int zeroCount = 0;
				int nonzeroCount = 0;

				foreach (var move in group)
				{
					double valueBefore = objective.Value(solution);
					double deltaValue = objective.DeltaValue(move);

					move.Apply(objective.RequiresPropagation);
					double valueAfter = objective.Value(solution);
					move.GetReverse().Apply(objective.RequiresPropagation);

					if (valueAfter - valueBefore != deltaValue)
						Assert.AreEqual(valueAfter - valueBefore, deltaValue, tolerance, $"Wrong delta value for move: {move}");

					if (deltaValue == 0)
						++zeroCount;
					else
						++nonzeroCount;
				}

				Console.WriteLine($"Type {group.Key.Name}: {zeroCount} zero, {nonzeroCount} nonzero");

				string moveType = group.Key.Name;

				if (!allMovesAreNeutral.Contains(moveType))
				{
					if (!allMovesMayBeNeutral.Contains(moveType))
					{
						Assert.AreNotEqual(0, nonzeroCount,
							$"All moves have zero delta value for type {moveType}. If this is expected, " +
							$"add the string '{moveType}' to the {nameof(allMovesAreNeutral)} parameter");
					}
				}
				else
					Assert.AreEqual(0, nonzeroCount,
						$"We were promised that all moves of type {moveType} have zero delta value (in {nameof(allMovesAreNeutral)})," +
						" but one or more moves with nonzero delta value were found");
			}
		}

		/// <summary>
		/// Verifies that the PowerFlowDelta of all moves get the update of topological relations right.
		/// </summary>
		/// <param name="allMoves">The moves to test the objective for</param>
		public static void VerifyDeltaTopology(IEnumerable<Move> allMoves)
		{
			var solution = allMoves.First().Solution;
			foreach (var move in allMoves)
			{
				//Provider references in original solution
				if (!(move is SwapSwitchStatusMove ssMove))
					continue;
				PgoSolution sol = move.Solution as PgoSolution;
				PeriodSolution perSol = sol.GetPeriodSolution(ssMove.Period);
				IEnumerable<Bus> allBuses = sol.PowerNetwork.Buses;
				Dictionary<Bus, Bus> providerForBus = new Dictionary<Bus, Bus>();
				allBuses.Do(b => providerForBus[b] = perSol.NetConfig.ProviderForBus(b));

				//Modify this with those in the power flow delta
				var pfDelta = ssMove.GetCachedPowerFlowDelta(new SimplifiedDistFlowProvider());
				pfDelta.BusDeltas.Do(kvp => providerForBus[kvp.Key] = kvp.Value.NewProvider);

				move.Apply(true);

				//Providers after the move
				foreach (Bus bus in allBuses)
				{
					Assert.AreSame(providerForBus[bus], perSol.NetConfig.ProviderForBus(bus));
				}

				move.GetReverse().Apply(true);

			}
		}


		/// <summary>
		/// Enumerates all moves that open one switch and closes another, for the given solution
		/// </summary>
		public static IEnumerable<Move> AllMovesFor(PgoSolution solution, Period period = null) => solution.GetAllRadialSwapMoves(period);

		/// <summary>
		/// Creates a <see cref="PeriodData"/> based on a modified baran-wu problem instance.
		/// </summary>
		/// <param name="modifyAction">If not null, this action is applied the the parsed power
		///   grid before it's turned into a <see cref="PowerNetwork"/></param>
		public static PeriodData CreateModifiedBaranWuCase(Action<PowerGrid> modifyAction = null)
		{
			string dataDir = TestDataDir;

			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestDataFile("baran-wu-modified.json"), modifyAction);
			var periodData = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, TestDataFile("baran-wu-modified_forecast.json"));
			return new PeriodData(network, periodData[0].Demands, periodData[0].Period.Clone("BaranWeModSinglePer"));
		}

		/// <summary>
		/// Verifies that the text <paramref name="actualContents"/> matches the contents of the file <paramref name="expectedFile"/>.
		/// If this fails, the actual contents remains on disk as a file next to <paramref name="expectedFile"/>, with suffix "_out".
		/// </summary>
		/// <param name="acceptedDifferences">If not null, line differences that are listed in this
		///   enumerable, are not counted as a mismatch. Each string pair may match at most
		///   one pair of differing lines.</param>
		/// <param name="accept">If not null, a function that, given one line from each reader, returns true to indicate that
		///   lines are not considered a mismatch.</param>
		public static void SaveCompareAndDelete(string expectedFile, string actualContents,
			IEnumerable<(string, string)> acceptedDifferences = null, Func<string, string, bool> accept = null)
		{
			string actualFile = expectedFile + "_out";
			File.WriteAllText(actualFile, actualContents);

			CompareAndDelete(expectedFile, actualFile, acceptedDifferences, accept);
		}

		/// <summary>
		/// Verifies that the text contents of the given files are equal.
		/// If so, the <paramref name="actualFile"/> is deleted.
		/// </summary>
		/// <param name="acceptedDifferences">If not null, line differences that are listed in this
		///   enumerable, are not counted as a mismatch. Each string pair may match at most
		///   one pair of differing lines.</param>
		/// <param name="accept">If not null, a function that, given one line from each reader, returns true to indicate that
		///   lines are not considered a mismatch.</param>
		public static void CompareAndDelete(string expectedFile, string actualFile,
			IEnumerable<(string, string)> acceptedDifferences = null, Func<string, string, bool> accept = null)
		{
			string expectedText = File.ReadAllText(expectedFile);
			string actualText = File.ReadAllText(actualFile);

			if (FileUtilities.FilesAreEqual(expectedFile, actualFile))
			{
				File.Delete(actualFile);
				return;
			}

			var (line1, line2) = FileUtilities.FirstDifferingLines(expectedFile, actualFile, acceptedDifferences: acceptedDifferences, accept: accept);

			if (line1 == null && line2 == null)
			{
				// Only accepted differences were found
				File.Delete(actualFile);
				return;
			}

			Assert.Fail($"File comparison failed for {actualFile}\nThe first differing lines are\n{line1}\nand\n{line2}");
		}

		/// <summary>
		/// Verifies that the given solution matches our saved solution for the
		/// DIGIN case.
		/// </summary>
		public static void AssertValidDiginSolution(CimJsonLdSolution solution, bool requireExact = false)
		{
			var settings = new JsonSerializerSettings()
			{
				ContractResolver = new CamelCasePropertyNamesContractResolver()
			};

			string solutionString = JsonConvert.SerializeObject(solution, Formatting.Indented, settings);

			int acceptedCount = 0;
			bool Accept(string a, string b)
			{
				if (requireExact)
					return a == b;

				// We accept up to two text line differences: graph Id and timestamp
				// (these are just metadata)
				if (a.Contains("@id") || a.Contains("2022-09-06T00:00:00.0000000"))
				{
					return ++acceptedCount <= 2;
				}
				return false;
			}

			string expectedFile = Path.Combine(ExpectedResultsDir, "DiginSolution.jsonld");
			SaveCompareAndDelete(expectedFile, solutionString, accept: Accept);
		}
	}
}
