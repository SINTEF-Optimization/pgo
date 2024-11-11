using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for criteria based on random data.
	/// </summary>
	[TestClass]
	public class MoreCriteriaTests
	{
		Random _random = new Random();
		PgoProblem _problem;

		[TestInitialize]
		public void Setup()
		{
			Setup(null);
		}

		private void Setup(Action<RandomNetworkBuilder> modifyParameters)
		{
			var network = TestUtils.SmallRandomNetwork(_random, modifyParameters);
			_problem = TestUtils.ProblemWithRandomDemands(network, _random, periodCount: 2);
		}

		[TestMethod]
		public void DeltaValueWorks_KileCostObjective()
		{
			KileCostObjective objective = new KileCostObjective(_problem.Network, _problem.Periods,
				new ExpectedConsumptionFromDemand(_problem.AllPeriodData),
				LineFaultPropertiesProvider.RandomFor(_problem.Network, _random),
				ConsumerCategoryProvider.RandomFor(_problem.Network, _random), true);

			VerifyDeltaValue(objective);
		}

		[TestMethod]
		public void DeltaValueWorks_KileCostObjective_NoLineFaultsOrBreakers()
		{
			Setup(c =>
			{
				c.Components.BreakerCount = 0;
				c.Components.AddBreakersAtGenerators = false;
			});

			LineFaultPropertiesProvider lineFaultPropertiesProvider = new LineFaultPropertiesProvider(_problem.Network);
			lineFaultPropertiesProvider.SetAll(0, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

			KileCostObjective objective = new KileCostObjective(_problem.Network, _problem.Periods,
				new ExpectedConsumptionFromDemand(_problem.AllPeriodData),
				lineFaultPropertiesProvider,
				ConsumerCategoryProvider.RandomFor(_problem.Network, _random), true);

			VerifyDeltaValue(objective, "SwapSwitchStatusMove");
		}

		private void VerifyDeltaValue(IObjective objective, string allMovesAreNeutral = "")
		{
			var solution = _problem.RadialSolution(_random);

			//TestBench.Program.Run(_problem, solution);

			var moves = TestUtils.AllMovesFor(solution);

			if (moves.Any())
				TestUtils.VerifyDeltaValue(objective, moves, tolerance: 1e-10, allMovesAreNeutral: allMovesAreNeutral);
		}
	}
}
