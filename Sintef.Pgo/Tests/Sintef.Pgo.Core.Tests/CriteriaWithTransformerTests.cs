using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class CriteriaWithTransformerTests
	{
		PgoSolution _solution;
		PgoProblem _problem;
		IFlowProvider _flowProvider = new SimplifiedDistFlowProvider();

		[TestInitialize]
		public void Setup()
		{
			NetworkBuilder builder = new NetworkBuilder();

			builder.Add("Producer[generatorVoltage=1000] -- b1[breaker;r=0] -o- Line1[closed] "
				+ "-o- Trafo[transformer; voltages=(1000,500); factor=0.98] -o- Line2[closed] -- Consumer[consumption=(10,0)]");
			builder.Add("Producer -- b2[breaker;r=0] -o- Line3[open] -- Consumer");

			Random random = new Random();

			builder.Network.PropertiesProvider.SetAll(1, TimeSpan.FromHours(1), TimeSpan.FromHours(2));
			builder.Network.PropertiesProvider.Randomize(random);
			builder.Network.PropertiesProvider.SuppressFaultsAroundProviders();
			builder.Network.CategoryProvider.Randomize(random);

			_solution = builder.SinglePeriodSolution;
			_problem = _solution.Problem;
		}

		[TestMethod]
		public void ValueIsCorrect_TotalLoss()
		{
			var objective = new TotalLossObjective(_flowProvider);

			_solution.SinglePeriodSolutions.First().Flow(_flowProvider).Write();
			var periodLength = _solution.SinglePeriodSolutions.First().Period.Length.TotalSeconds;

			// Loss is 0.2W in transformer and 0.0005W in the lines
			Assert.AreEqual(0.2005 *  periodLength / (1.0e6 * 3600.0), objective.Value(_solution), 1e-7);
		}

		[TestMethod]
		public void DeltaValueIsCorrect_TotalLoss()
		{
			var objective = new TotalLossObjective(_flowProvider);

			var moves = CloseSwitchAndOpenOtherNeighbourhood.AllMoves(_solution);
			TestUtils.VerifyDeltaValue(objective, moves, 1e-9);
		}

		[TestMethod]
		public void DeltaValueIsCorrect_KileCost()
		{
			var objective = new KileCostObjective(_problem.Network, _problem.AllPeriodData, true);

			var moves = CloseSwitchAndOpenOtherNeighbourhood.AllMoves(_solution);
			TestUtils.VerifyDeltaValue(objective, moves);
		}
	}
}
