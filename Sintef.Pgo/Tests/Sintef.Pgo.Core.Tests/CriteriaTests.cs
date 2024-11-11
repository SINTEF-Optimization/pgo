using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

using System.Numerics;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Various tests for testing constraints and objectives.
	/// </summary>
	[TestClass]
	public class CriteriaTests
	{
		/// <summary>
		/// Solution used in many tests.
		/// </summary>
		/// <returns></returns>
		PgoSolution _IEEE34Solution;

		/// <summary>
		/// Data for a single period problem used in many tests
		/// </summary>
		PeriodData _singlePeriodBaranWuProblem;

		/// <summary>
		/// Single period initial solution used in many tests, for each kind of flow approximation.
		/// </summary>
		readonly Dictionary<FlowApproximation, PeriodSolution> _initSolSinglePeriodBaranWu = new Dictionary<FlowApproximation, PeriodSolution>();

		/// <summary>
		/// Multi period initial solution used in many tests, for each kind of flow approximation.
		/// </summary>
		readonly Dictionary<FlowApproximation, PgoSolution> _initSolMultiPeriodBaranWu = new Dictionary<FlowApproximation, PgoSolution>();

		/// <summary>
		/// Flow providers for each kind of flow approximation
		/// </summary>
		readonly Dictionary<FlowApproximation, IFlowProvider> _flowProviders = new Dictionary<FlowApproximation, IFlowProvider>();

		[TestInitialize]
		public void SetUp()
		{
			_IEEE34Solution = CreateIEEE34Solution();

			//Create single period problem
			_singlePeriodBaranWuProblem = TestUtils.CreateModifiedBaranWuCase();

			//Create single period initial solutions with different flow approximations.
			_initSolSinglePeriodBaranWu[FlowApproximation.SimplifiedDF] =
				Utils.ConstructFeasibleSolution(_singlePeriodBaranWuProblem, FlowApproximation.SimplifiedDF);

			//initSolSinglePeriodBaranWu[FlowApproximations.DC] = Utils.ConstructRadialSolution(singlePeriodBaranWuProblem, FlowApproximations.DC) as PgoSolution;
			//initSolSinglePeriodBaranWu[FlowApproximations.Taylor] = Utils.ConstructRadialSolution(singlePeriodBaranWuProblem, FlowApproximations.Taylor) as PgoSolution;

			_flowProviders[FlowApproximation.SimplifiedDF] = new SimplifiedDistFlowProvider();

			//		_initSolMultiPeriodBaranWu[FlowApproximations.DC] = CreateInitialMultiPeriodSolutionForFlowApproximation(FlowApproximations.DC);
			//		_initSolMultiPeriodBaranWu[FlowApproximations.Taylor] = CreateInitialMultiPeriodSolutionForFlowApproximation(FlowApproximations.Taylor);
			_initSolMultiPeriodBaranWu[FlowApproximation.SimplifiedDF] = CreateInitialMultiPeriodSolution(FlowApproximation.SimplifiedDF, false);
		}

		/// <summary>
		/// Tests that for a simple test case with a radial solution, the radial relationships of the network configuration are internally consistent.
		/// </summary>
		[TestMethod]
		public void InterBusRelationshipsAreConsistent()
		{
			Assert.IsTrue(_initSolSinglePeriodBaranWu[FlowApproximation.SimplifiedDF].IsRadial);

			NetworkConfiguration nc = _initSolSinglePeriodBaranWu[FlowApproximation.SimplifiedDF].NetConfig;

			foreach (Bus b in _singlePeriodBaranWuProblem.Network.Buses)
			{
				foreach (var child in nc.DownstreamBuses(b))
				{
					Assert.AreEqual(b, nc.UpstreamBus(child));
				}
				foreach (var line in nc.DownstreamLines(b))
				{
					Assert.AreEqual(line, nc.UpstreamLine(nc.DownstreamEnd(line)));
				}

				Assert.IsTrue(b.IsProvider || nc.UpstreamBus(b) != null);
			}

			Assert.AreEqual(_singlePeriodBaranWuProblem.Network.Buses.Count(), _singlePeriodBaranWuProblem.Network.Providers.Sum(prov => nc.GetNumberOfBusesInTree(prov)));

		}

		/// <summary>
		/// Sets up a multi-period solution
		/// </summary>
		/// <param name="approximation">The flow approximation to use</param>
		/// <param name="optimize">If true, the solution is slightly optimized. If false, it has an arbitrary 
		///   radial configuration for each period</param>
		/// <returns>A solution, or null if no solution could be found.</returns>
		internal PgoSolution CreateInitialMultiPeriodSolution(FlowApproximation approximation, bool optimize)
		{
			PgoProblem problem = SyntheticProblemCreator.CreateSyntheticCase(_singlePeriodBaranWuProblem, 3, 3, 5, approximation, approximation);
			PgoSolution solution = new PgoSolution(problem);

			if (optimize)
			{
				OptimizerParameters pars = new OptimizerParameters()
				{
					Algorithm = AlgorithmType.SimpleMultiPeriodsolver,
					SimpleMPSolverSolverTimeOut = TimeSpan.FromSeconds(problem.PeriodCount),
					SimpleMPSolverMaxIterations = 1
				};
				pars.MetaHeuristicParams.UseSmallNeighbourhoods = true;

				IOptimizer solver = new ConfigOptimizerFactory(new NetworkManager(problem.Network)).CreateOptimizer(pars, solution, problem.CriteriaSet, new OptimizerEnvironment());

				return solver.Optimize(solution) as PgoSolution;
			}
			else
			{
				return Utils.ConstructFeasibleSolution(problem);
			}
		}

		[TestMethod]
		public void ConstraintCheckIEEE34()
		{
			// We know that with no switches set,
			// there are not enough components
			Assert.IsFalse(_IEEE34Solution.IsFeasible);
		}

		[TestMethod]
		[Ignore]
		public void ConstraintCheckIEEE34WithSwitch830To854Set()
		{
			var solution = _IEEE34Solution.Clone() as PgoSolution;
			PeriodSolution persol = solution.SinglePeriodSolutions.SingleOrDefault();

			Line l = persol.Network.GetLine("830", "854");
			persol.SetSwitch(l, true);

			Assert.IsTrue(solution.IsFeasible);
		}

		[TestMethod]
		[Ignore]
		public void ConstraintCheckIEEE34WithSwitch814To850Set()
		{
			var solution = _IEEE34Solution.Clone() as PgoSolution;
			PeriodSolution perSol = solution.SinglePeriodSolutions.SingleOrDefault();

			Line l = perSol.Network.GetLine("814", "850");
			perSol.SetSwitch(l, true);

			var singleSubstationConstraint = FindConstraint<SingleSubstationConnectionConstraint>(solution.Encoding);
			var operationalCapacityConstraint = FindConstraint<SubstationCapacityConstraint>(solution.Encoding);
			var powerConservationConstraint = FindConstraint<PowerConservationConstraint>(solution.Encoding);

			Assert.IsFalse(solution.IsFeasible); // Fails over unsufficient capacity on one generator

			Assert.IsTrue(singleSubstationConstraint.IsSatisfied(solution));
			Assert.IsFalse(operationalCapacityConstraint.IsSatisfied(solution));
			Assert.IsTrue(powerConservationConstraint.IsSatisfied(solution));
		}

		[TestMethod]
		[Ignore]
		public void ConstraintCheckIEEE34WithTwoSwitchesSet()
		{
			var solution = _IEEE34Solution.Clone() as PgoSolution;
			PeriodSolution perSol = solution.SinglePeriodSolutions.SingleOrDefault();

			Line l = perSol.Network.GetLine("858", "834");
			perSol.SetSwitch(l, true);

			perSol.SetSwitch(perSol.Network.GetLine("814", "850"), true);
			// Disconnects into three total components, two of which are correctly served

			var singleSubstationConstraint = FindConstraint<SingleSubstationConnectionConstraint>(solution.Encoding);
			var operationalCapacityConstraint = FindConstraint<SubstationCapacityConstraint>(solution.Encoding);
			var powerConservationConstraint = FindConstraint<PowerConservationConstraint>(solution.Encoding);

			Assert.IsFalse(solution.IsFeasible); // Fails over unsufficient capacity on one generator

			Assert.IsTrue(singleSubstationConstraint.IsSatisfied(solution));
			Assert.IsTrue(operationalCapacityConstraint.IsSatisfied(solution));
			Assert.IsFalse(powerConservationConstraint.IsSatisfied(solution));
		}

		[TestMethod]
		public void ObjectiveComponentsDependOnPeriodLength()
		{
			static double EvalObjectiveOfType(PgoSolution s, Type t)
			{
				var objective = (s.Encoding.CriteriaSet.Objective as AggregateObjective).Components.Where(c => t.IsAssignableFrom(c.GetType())).First();
				return objective.Value(s);
			}

			foreach (var objectiveType in new[] { typeof(TotalLossObjective) })
			{
				var sol1 = BaranWuExampleProblem(_singlePeriodBaranWuProblem, null);
				var value1 = EvalObjectiveOfType(sol1, objectiveType);
				var sol2 = BaranWuExampleProblem(DoublePeriodLength(_singlePeriodBaranWuProblem), sol1.SinglePeriodSolutions.Single().SwitchSettings);
				var value2 = EvalObjectiveOfType(sol2, objectiveType);

				Assert.IsTrue(value1 > 1 && value2 > 1);
				Assert.IsTrue(Math.Abs(2 * value1 - value2) <= 1e-9);
			}
		}

		private PeriodData DoublePeriodLength(PeriodData d)
		{
			var newEndTime = d.Period.EndTime + (d.Period.EndTime - d.Period.StartTime);
			return new PeriodData(d.Network, d.Demands, new Period(d.Period.StartTime, newEndTime, d.Period.Index));
		}

		private PgoSolution BaranWuExampleProblem(PeriodData periodData, SwitchSettings switchSettings = null)
		{
			var flowProvider = new SimplifiedDistFlowProvider();

			PgoProblem prob = new PgoProblem(periodData, flowProvider);
			var criteria = prob.CriteriaSet;

			if (switchSettings == null)
			{
				var initSol = _initSolSinglePeriodBaranWu[flowProvider.FlowApproximation];

				for (int i = 0; i < 10; ++i)
				{
					if (initSol == null)
						initSol = Utils.ConstructFeasibleSolution(periodData, flowProvider.FlowApproximation, criteria: criteria);
				}

				if (initSol == null)
					throw new Exception("Could not construct feasible solution.");

				switchSettings = initSol.SwitchSettings;
			}

			var periodSolution = new PeriodSolution(periodData, switchSettings);

			//Set up encoding/solution
			PgoSolution sol = new PgoSolution(prob);
			sol.UpdateSolutionForPeriod(periodSolution);
			return sol;
		}


		#region Testing Objective's DeltaValue

		/// <summary>
		/// Tests delta value calculations for the total loss objective, with SimplifiedDistFlow.
		/// </summary>
		[TestMethod]
		public void TestDeltaTotalLossAndSimplifiedDistFlow() => TestDeltaValues<TotalLossObjective>(new SimplifiedDistFlowProvider(), 1E-9);


		/// <summary>
		/// Tests delta value calculations for the voltage limits constraint (as objective), with SimplifiedDistFlow.
		/// </summary>
		[TestMethod]
		public void TestDeltaVoltageLimitsAndSimplifiedDistFlow()
			=> TestDeltaValues<ConsumerVoltageLimitsConstraint>(new SimplifiedDistFlowProvider(), 1E-9,
				modify: AddVoltageLimitsAsObjective,
				allMovesMayBeNeutral: "SwapSwitchStatusMove");

		private CriteriaSet AddVoltageLimitsAsObjective(CriteriaSet crit)
		{
			CriteriaSet myCrit = crit.Clone();
			(myCrit.Objective as AggregateObjective).AddComponent(new ConsumerVoltageLimitsConstraint(_flowProviders[FlowApproximation.SimplifiedDF], 0.3), 1);

			Complex mingGenVoltage = _singlePeriodBaranWuProblem.Network.Providers.Min(p => p.GeneratorVoltage);
			_singlePeriodBaranWuProblem.Network.Consumers.Do(c => { c.VMin = 0; c.VMax = mingGenVoltage.Magnitude / 2; });

			return myCrit;
		}

		/// <summary>
		/// Tests delta value calculations for the voltage limits criterion (as objective), with SimplifiedDistFlow.
		/// </summary>
		[TestMethod]
		public void TestDeltaLineVoltageLimitsAndSimplifiedDistFlow()
		{
			TestDeltaValue(generator1HasHighVoltage: true, largeVoltageMargin: true);
			TestDeltaValue(true, false);

			// Sometimes, there is no single move from the initial solution that changes the penalty:
			TestDeltaValue(false, false, allMovesMayBeNeutral: true);
			TestDeltaValue(false, true, true);


			void TestDeltaValue(bool generator1HasHighVoltage, bool largeVoltageMargin, bool allMovesMayBeNeutral = false)
			{
				var allMovesMayBeeNeutralNames = allMovesMayBeNeutral ? nameof(SwapSwitchStatusMove) : "";

				TestDeltaValues<LineVoltageLimitsConstraint>(new SimplifiedDistFlowProvider(), 1E-9,
					modify: (crit) =>
					{
						IFlowProvider flowProvider = _flowProviders[FlowApproximation.SimplifiedDF];
						CriteriaSet myCrit = crit.WithProvider(flowProvider);
						(myCrit.Objective as AggregateObjective).AddComponent(new LineVoltageLimitsConstraint(flowProvider, 0.3), 1.0);
						return myCrit;
					},
					periodData: GenerateLineVoltageLimitedProblem(generator1HasHighVoltage, largeVoltageMargin), allMovesMayBeNeutral: allMovesMayBeeNeutralNames);
			}
		}

		/// <summary>
		/// Test the delta value calculations for the line voltage limits criterion (as constraint), with SimplifiedDistFlow.
		/// </summary>
		[TestMethod]
		public void TestLegalMovesVoltageLimitsAndSimplifiedDistFlow()
		{
			LegalMovesVoltageLimitsConfig(generator1HasHighVoltage: true, largeVoltageMargin: true,
				closedSwitches: new[] { 2, 3, 5, 7, 9, 11, 13, 14, 15 });
			LegalMovesVoltageLimitsConfig(true, false, new[] { 2, 3, 5, 7, 9, 11, 13, 14, 15 });

			LegalMovesVoltageLimitsConfig(false, false, new[] { 1, 2, 3, 4, 6, 9, 10, 13, 14 });
			LegalMovesVoltageLimitsConfig(false, true, new[] { 1, 2, 3, 4, 6, 9, 10, 13, 14 });

			void LegalMovesVoltageLimitsConfig(bool generator1HasHighVoltage, bool largeVoltageMargin, IEnumerable<int> closedSwitches)
			{
				var perDat = GenerateLineVoltageLimitedProblem(generator1HasHighVoltage, largeVoltageMargin);
				PgoProblem problem = new PgoProblem(perDat, new SimplifiedDistFlowProvider());
				var constraint = problem.CriteriaSet.Constraints.OfType<LineVoltageLimitsConstraint>().Single();
				var solution = new PgoSolution(problem);
				solution.SetClosedOnly(problem.Periods.Single(), string.Join(" ", closedSwitches.Select(x => $"switch{x}")));
				var perSol = solution.SinglePeriodSolutions.First();

				List<Move> moves = new List<Move>();
				foreach (Line switchToClose in perSol.OpenSwitches)
				{
					CloseSwitchAndOpenOtherNeighbourhood nh = new CloseSwitchAndOpenOtherNeighbourhood(switchToClose, perDat.Period);
					nh.Init(solution);
					moves.AddRange(nh);
				}
				if (moves.Any())
					TestUtils.VerifyLegalMove(constraint, moves, "");
			}
		}

		[TestMethod]
		public void BusVoltageCheckerWorksWithTransformersInNetwork()
		{

			foreach (var (voltageLimitMargin, largeVoltageLimitMargin) in new[] { (50, false), (900, true) })
			{
				foreach (var validPath in new[] { false, true })
				{
					foreach (var highBaseVoltage in new[] { false, true })
					{
						var builder = new NetworkBuilder();
						var vMin = 1950;
						var vMax = 2050;
						var baseVoltage = 2000 + (highBaseVoltage ? 1.0 : -1.0) * (largeVoltageLimitMargin ? 1000 : 200);
						builder.Add($"G1[generatorVoltage={baseVoltage}] -- l1[open] -- Consumer[vMinV={vMin};vMaxV={vMax};consumption=(1000,0)]");
						builder.Add($"G1 -- l2[open] -- T_in -- tl1 -- T[transformer;voltages=({baseVoltage},2000);operation=fixed;factor=0.98] -- tl2 -- T_out -- l3 -- Consumer");
						builder.Add($"Consumer -- l4[vMax={vMax}] -- OtherNode");

						var flowProvider = new SimplifiedDistFlowProvider();

						// Set up a problem
						var perDat = builder.PeriodData;
						PgoProblem problem = new PgoProblem(builder.PeriodData, flowProvider);
						PgoSolution mpSol = new PgoSolution(problem);

						// Create a single period solution data
						PeriodSolution solution = mpSol.GetPeriodSolution(perDat.Period);

						solution.SetSwitch("G1", "Consumer", validPath); // true = open
						solution.SetSwitch("G1", "T_in", !validPath);

						var lineConstraint = new LineVoltageLimitsConstraint(flowProvider, 0.3);
						var consumerConstraint = new ConsumerVoltageLimitsConstraint(flowProvider, 0.3);

						var lineSatisfied = !highBaseVoltage || validPath;
						var consumerSatisfied = validPath;
						Assert.AreEqual(lineSatisfied, lineConstraint.IsSatisfied(mpSol));
						Assert.AreEqual(consumerSatisfied, consumerConstraint.IsSatisfied(mpSol));

						var simplifiedCriterion = largeVoltageLimitMargin || !highBaseVoltage;

						// the non-critical lines have vmax=inf so topology-based satisfaction will always be in use
						Assert.AreEqual(true, lineConstraint.VoltageCheck.HaveUsedTopologyBasedSatisfaction);
						Assert.AreEqual(!lineSatisfied && simplifiedCriterion, lineConstraint.VoltageCheck.HaveUsedTopologyBasedRefutation);
						Assert.AreEqual(!(validPath || simplifiedCriterion), lineConstraint.VoltageCheck.HaveUsedFlowCalculation);

						Assert.AreEqual(false, consumerConstraint.VoltageCheck.HaveUsedTopologyBasedSatisfaction);
						Assert.AreEqual(!consumerSatisfied && simplifiedCriterion, consumerConstraint.VoltageCheck.HaveUsedTopologyBasedRefutation);
						Assert.AreEqual(validPath || !simplifiedCriterion, consumerConstraint.VoltageCheck.HaveUsedFlowCalculation);
					}
				}
			}
		}


		/// <summary>
		/// Tests delta value calculations for thesub station capacity criterion, with Simplified Distflow
		/// </summary>
		[TestMethod]
		public void TestDeltaSubStationCapacityConstraintAndSimplifiedDistFlow()
		{
			PeriodData prob = TestUtils.CreateModifiedBaranWuCase();

			// Modify production capacities so that some moves will be illegal
			prob.Network.Providers.Last().ActiveGenerationCapacity = 2_000_000;
			double sumOfDemands = prob.Network.Consumers.Sum(b => prob.Demands.ActivePowerDemand(b));
			prob.Network.Providers.First().ActiveGenerationCapacity = sumOfDemands - 1_000_000;

			TestLegalMoves<SubstationCapacityConstraint>(new SimplifiedDistFlowProvider(), prob);

			// Remove constraint, and add as relaxed objective
			CriteriaSet modify(CriteriaSet crit) => crit.GetRelaxedForConstraint(typeof(SubstationCapacityConstraint));

			TestDeltaValues<SubstationCapacityConstraint>(new SimplifiedDistFlowProvider(), 1E-9, prob, modify);
		}


		/// <summary>
		/// Tests delta value calculations for the total generation objective, with SimplifiedDistFlow.
		/// </summary>
		[TestMethod]
		public void TestDeltaConfigChangeCostAndSimplifiedDistFlow() => TestDeltaValuesMultiPeriodProblem<ConfigChangeCost>(new SimplifiedDistFlowProvider(), 1E-9, true);


		/// <summary>
		/// Tests delta value calculations for the total generation objective, with SimplifiedDistFlow.
		/// TODO: Change implementation, for this case the value is always null for this objective, and the test makes no sense.
		/// </summary>
		[TestMethod]
		public void TestDeltaLineCapacityConstraintAndSimplifiedDistFlow()
		{
			PeriodData prob = TestUtils.CreateModifiedBaranWuCase(Modify);

			TestDeltaValues<LineCapacityCriterion>(new SimplifiedDistFlowProvider(), 1E-9, prob);


			void Modify(PowerGrid g)
			{
				//Modify line capacities so that IMax will make the use of the line infeasible
				var line = g.Lines.Single(l => l.IsSwitchable && l.SourceNode == "34" || l.TargetNode == "34");
				line.CurrentLimit = 0.0001;
			}
		}

		#endregion

		#region Testing Constraints' IsFeasible

		/// <summary>
		/// Tests delta value calculations for the total loss objective, with SimplifiedDistFlow.
		/// </summary>
		[TestMethod]
		public void TestFeasibilityPowerConservationConstraintAndSimplifiedDistFlow() => TestFeasibility(new SimplifiedDistFlowProvider(), p => new PowerConservationConstraint(p));

		#endregion


		#region Testing Constraints' LegalMove


		/// <summary>
		/// Tests delta value calculations for the total generation objective, with SimplifiedDistFlow.
		/// TODO: Change implementation, for this case moves are always feasible, and the test makes no sense.
		/// </summary>
		[TestMethod]
		public void TestLegalMovesLineCapacityConstraintAndSimplifiedDistFlow()
		{
			PeriodData prob = TestUtils.CreateModifiedBaranWuCase(Modify);

			TestLegalMoves<LineCapacityCriterion>(new SimplifiedDistFlowProvider(), prob, constraintProvider: p => new LineCapacityCriterion(p.FlowProvider));


			void Modify(PowerGrid grid)
			{
				//Modify line capacities so that IMax will make the use of the line infeasible
				SetIMax("line1", 150);
				SetIMax("line2", 150);
				SetIMax("line21", 150);
				SetIMax("line23", 150);

				void SetIMax(string Name, double V)
				{
					var line = grid.Lines.Single(l => l.Id == Name);
					line.CurrentLimit = V;
				}
			}
		}


		private PeriodData GenerateLineVoltageLimitedProblem(bool generator1HasHighVoltage, bool largeVoltageMargin)
		{
			return TestUtils.CreateModifiedBaranWuCase(Modify);


			void Modify(PowerGrid grid)
			{
				foreach (var line in grid.Lines)
				{
					line.Resistance = 0.000000001;
					line.Reactance = 0.000000001;
					line.CurrentLimit = double.PositiveInfinity;
				}

				if (largeVoltageMargin)
				{
					foreach (var node in grid.Nodes)
					{
						if (node.Type != NodeType.PowerProvider)
							node.MaximumVoltage = 100.1;
					}
				}

				// force 24-25 to be connected to 1 (by setting 34 to high voltage)
				var generator34 = grid.Nodes.Single(b => b.Id == "34");
				var generator1 = grid.Nodes.Single(b => b.Id == "1");

				var line_24_25 = grid.Lines.Single(l => l.SourceNode == "24" && l.TargetNode == "25");
				line_24_25.VoltageLimit = 13;

				var highGeneratorVoltage = largeVoltageMargin ? 100.0 : 15.0;
				if (generator1HasHighVoltage)
				{
					generator1.GeneratorVoltage = highGeneratorVoltage;
				}
				else
				{
					generator34.GeneratorVoltage = highGeneratorVoltage;
				}
			}
		}

		[TestMethod]
		public void IsFeasibleAndValueWork_LineVoltageLimitConstraint()
		{
			// Create a network where only one of the three generator can be used, due to a line voltage limit
			NetworkBuilder builder = new NetworkBuilder
			{
				DefaultImpedance = 0
			};

			builder.Add("Gen1[generatorVoltage=200] -- s1[open] -- N1");
			builder.Add("Gen2[generatorVoltage=101] -- s2[open] -- N1 -- line[vMax=100] -- Consumer[consumption=(10, 0)]");
			builder.Add("Gen3[generatorVoltage=99] -- s3[closed] -- N1");

			PgoSolution solution = builder.SinglePeriodSolution;
			var problem = solution.Problem;
			var period = problem.Periods.Single();
			var criterion = problem.CriteriaSet.Constraints.OfType<LineVoltageLimitsConstraint>().Single();

			// It's ok to use Gen3
			Assert.IsTrue(criterion.IsSatisfied(solution));
			Assert.AreEqual(0, criterion.Value(solution));

			Assert.IsTrue(criterion.VoltageCheck.HaveUsedTopologyBasedSatisfaction);
			Assert.IsFalse(criterion.VoltageCheck.HaveUsedFlowCalculation);
			Assert.IsFalse(criterion.VoltageCheck.HaveUsedTopologyBasedRefutation);

			// Gen2's voltage is too high (1V)
			solution.SetClosedOnly(period, "s2");

			Assert.IsFalse(criterion.IsSatisfied(solution));
			Assert.AreEqual(1, criterion.Value(solution));

			Assert.IsTrue(criterion.VoltageCheck.HaveUsedFlowCalculation);
			Assert.IsFalse(criterion.VoltageCheck.HaveUsedTopologyBasedRefutation);

			// Gen1's voltage is also too high
			solution.SetClosedOnly(period, "s1");

			Assert.IsFalse(criterion.IsSatisfied(solution));
			Assert.AreEqual(200, criterion.Value(solution));

			Assert.IsTrue(criterion.VoltageCheck.HaveUsedTopologyBasedRefutation);
		}

		/// <summary>
		/// Tests delta value calculations for the single substation connection constraint, with SimplifiedDistFlow.
		/// Simply tests that no solutions arise that are not radial.
		/// </summary>
		[TestMethod]
		public void TestLegalMovesSingleSubstationConnectionConstraintAndSimplifiedDistFlow()
			=> TestLegalMoves<SingleSubstationConnectionConstraint>(new SimplifiedDistFlowProvider(), allMovesCanBeFeasibleFor: "SwapSwitchStatusMove",
				constraintProvider: p => new SingleSubstationConnectionConstraint());

		/// <summary>
		/// Tests LegalMove for <see cref="TransformerModesConstraint"/>.
		/// </summary>
		[TestMethod]
		public void TestLegalMovesTransformerModesConstraint()
			=> TestLegalMoves<TransformerModesConstraint>(new SimplifiedDistFlowProvider(),
				perDat: ValidTransformerModesTests.TwoThreeWindingTransformersTestNetworkPeriodData(),
				allMovesCanBeFeasibleFor: "SwapSwitchStatusMove",
				constraintProvider: p => new TransformerModesConstraint());

		#endregion

		[TestMethod]
		public void OptimizingAnInitialSolutionworks()
		{
			CreateInitialMultiPeriodSolution(FlowApproximation.SimplifiedDF, true);
		}

		/// <summary>
		/// Tests delta value calculations on a modified Baran-Wu Case, for the given type 
		/// of objective and the given flow provider
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="periodData">Give a suitable single period problem data here. Optional. If null, a "modified Baran and Wu case is used.</param>
		/// <param name="flowProvider">The flow provider to use.</param>
		private void TestDeltaValues<T>(IFlowProvider flowProvider, double tolerance, PeriodData periodData = null,
			Func<CriteriaSet, CriteriaSet> modify = null, string allMovesAreNeutral = "", string allMovesMayBeNeutral = "") where T : IObjective
		{
			PeriodSolution initSol = null;
			if (periodData == null)
			{
				periodData = _singlePeriodBaranWuProblem;
				initSol = _initSolSinglePeriodBaranWu[flowProvider.FlowApproximation];
			}

			PgoProblem prob = new PgoProblem(periodData, flowProvider);
			var criteria = prob.CriteriaSet;
			if (modify != null)
				criteria = modify(criteria);

			IEnumerable<T> objs = PgoProblem.FindObjectives<T>(criteria);
			Assert.IsTrue(objs.Any(), "No objectives defined to test");

			for (int i = 0; i < 10; ++i)
			{
				if (initSol == null)
					initSol = Utils.ConstructFeasibleSolution(periodData, flowProvider.FlowApproximation, criteria: criteria);
			}

			if (initSol == null)
				throw new Exception("Could not construct feasible solution.");

			//Set up encoding/solution
			PgoSolution sol = new PgoSolution(prob);
			sol.UpdateSolutionForPeriod(initSol);

			//Create a neighbourhood for each closed switch			
			SwitchSettings copyOfSwitchSettings = initSol.CloneSwitchSettings();
			List<Move> moves = new List<Move>();
			foreach (Line switchToClose in copyOfSwitchSettings.OpenSwitches)
			{
				CloseSwitchAndOpenOtherNeighbourhood nh = new CloseSwitchAndOpenOtherNeighbourhood(switchToClose, initSol.Period);
				nh.Init(sol);
				moves.AddRange(nh);
			}
			if (moves.Any())
				objs.Do(obj => TestUtils.VerifyDeltaValue(obj, moves, tolerance, allMovesAreNeutral, allMovesMayBeNeutral));
		}

		/// <summary>
		/// Tests delta value calculations on a modified Baran-Wu Case, for the given type 
		/// of objective and the given flow provider
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="flowProvider">The flow provider to use.</param>
		/// <param name="mayHaveZeroDelta">If true, we accept that some neigbhourhoods may have all zero-delta moves.</param>
		private void TestDeltaValuesMultiPeriodProblem<T>(IFlowProvider flowProvider, double tolerance, bool mayHaveZeroDelta) where T : IObjective
		{
			PgoSolution optMpSol = _initSolMultiPeriodBaranWu[flowProvider.FlowApproximation];
			PgoProblem mpProb = optMpSol.Problem;

			IEnumerable<T> objs = PgoProblem.FindObjectives<T>(mpProb.CriteriaSet);
			Assert.IsTrue(objs.Any(), "No objectives defined to test");
			string mayIncludeZeroDelta = mayHaveZeroDelta ? typeof(SwapSwitchStatusMove).Name : "";

			//Create a neighbourhood for each closed switch
			foreach (PeriodSolution periodSolution in optMpSol.SinglePeriodSolutions)
			{
				SwitchSettings copyOfSwitchSettings = periodSolution.CloneSwitchSettings();
				List<Move> moves = new List<Move>();
				foreach (Line switchToClose in copyOfSwitchSettings.OpenSwitches)
				{
					CloseSwitchAndOpenOtherNeighbourhood nh = new CloseSwitchAndOpenOtherNeighbourhood(switchToClose, periodSolution.Period);
					nh.Init(optMpSol);
					moves.AddRange(nh);
				}
				if (moves.Any())
					objs.Do(obj => TestUtils.VerifyDeltaValue(obj, moves, tolerance, "", mayIncludeZeroDelta));
			}
		}


		/// <summary>
		/// Tests delta value calculations on a modified Baran-Wu Case, for the given type 
		/// of objective and the given flow provider
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="flowProvider">The flow provider to use.</param>
		/// <param name="perDat">Give a suitable single period problem data here. Optional. If null, a "modified Baran and Wu case is used.</param>
		/// <param name="allMovesAreFeasible">Contains the names of move classes where it is ok
		///   that none of the moves tested are infeasible. Optional.</param>
		private void TestLegalMoves<T>(IFlowProvider flowProvider, PeriodData perDat = null, string allMovesCanBeFeasibleFor = "",
			Func<PgoProblem, IConstraint> constraintProvider = null) where T : IConstraint
		{
			if (perDat == null)
				perDat = TestUtils.CreateModifiedBaranWuCase();

			PgoProblem problem = new PgoProblem(perDat, flowProvider);
			if (constraintProvider != null)
			{
				problem.CriteriaSet.ClearConstraints();
				problem.CriteriaSet.AddConstraint(constraintProvider?.Invoke(problem));
			}

			IConstraint constraint = problem.CriteriaSet.Constraints.OfType<T>().Single();

			//Create initial solution
			PgoSolution initSol = Utils.ConstructFeasibleSolution(problem);
			PeriodSolution perSol = initSol.GetPeriodSolution(perDat.Period);

			//Create a neighbourhood for each open switch, and make an aggregated list of all moves
			List<Move> moves = new List<Move>();
			foreach (Line switchToClose in perSol.OpenSwitches)
			{
				CloseSwitchAndOpenOtherNeighbourhood nh = new CloseSwitchAndOpenOtherNeighbourhood(switchToClose, perDat.Period);
				nh.Init(initSol);
				moves.AddRange(nh);
			}
			if (moves.Any())
				TestUtils.VerifyLegalMove(constraint, moves, allMovesCanBeFeasibleFor);
		}


		/// <summary>
		/// Tests feasibility calculations on a modified Baran-Wu Case, for the given type 
		/// of objective and the given flow provider
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="flowProvider">The flow provider to use.</param>
		private void TestFeasibility(IFlowProvider flowProvider, Func<IFlowProvider, IConstraint> constraintFactory)
		{
			PgoProblem problem = new PgoProblem(TestUtils.CreateModifiedBaranWuCase(), flowProvider);
			var constraint = constraintFactory(flowProvider);

			//Create initial solution
			PgoSolution initSol = Utils.ConstructFeasibleSolution(problem);

			//Check that this is feasible
			Assert.IsTrue(constraint.IsSatisfied(initSol));
		}


		private PgoSolution CreateIEEE34Solution()
		{
			PgoProblem problem = new PgoProblem(IEEE34NetworkMaker.IEEE34(), new SimplifiedDistFlowProvider());
			return new PgoSolution(problem);
		}

		/// <summary>
		/// Returns the single constraint in the encoding that is of the given type.
		/// </summary>
		/// <typeparam name="T">The type of constraint to find</typeparam>
		private T FindConstraint<T>(Encoding encoding) where T : IConstraint
		{
			return encoding.CriteriaSet.Constraints.OfType<T>().Single();
		}
	}
}

