using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Scoop.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for <see cref="RetryImprovingMoveInAdjacentPeriodsSelector"/>
	/// </summary>
	[TestClass]
	public class NeighborhoodSelectionTests
	{
		PgoSolution _solution;

		PgoProblem Problem => _solution.Problem;
		CriteriaSet Criteria => Problem.CriteriaSet;
		PowerNetwork Network => Problem.Network;

		[TestInitialize]
		public void Setup()
		{
			// Set up network.
			// There are three pairs of lines between the producer and the consumer, and in each pair,
			// one is better (has less resistance). In the initial solution, the worst line is chosen in 
			// each pair.

			NetworkBuilder builder = new NetworkBuilder();

			builder.Add("Producer[generatorVoltage=1000.0]");
			builder.Add("Consumer[consumption=(100, 0)]");
			builder.Add("Producer -- Line1a[r=1;open] -- N1 -- Line2a[r=1;open] -- N2 -- Line3a[r=1;open] -- Consumer");
			builder.Add("Producer -- Line1b[r=2;closed] -- N1 -- Line2b[r=2;closed] -- N2 -- Line3b[r=2;closed] -- Consumer");

			// The problem has three periods.

			var periods = builder.RepeatedPeriodData(3);
			_solution = builder.Solution(periods, "problem");
		}

		[DataTestMethod]
		[DataRow(0)]
		[DataRow(1)]
		[DataRow(2)]
		public void SelectorSuggestsRetryingAnImprovingMoveInAdjacentPeriods(int startPeriodIndex)
		{
			// Start by exploring one neighborhood in the start period
			var nh = new CloseSwitchAndOpenOtherNeighbourhood(Network.GetLine("Line2a"), Problem.GetPeriod(startPeriodIndex));

			// Optimize
			var selector = CreateRetrySelector(nh);
			var selected = Optimize(selector);

			// Verify:

			// The first neighborhood selected is the one we created
			Assert.AreSame(nh, selected[0].Nh);
			
			// Similar moves were applied in each period
			string[] expectedMoves = new[] { "Open Line2b, close Line2a, period 0", "Open Line2b, close Line2a, period 1", "Open Line2b, close Line2a, period 2" };
			CollectionAssert.AreEquivalent(expectedMoves, selected.Select(x => x.Move.ToString()).ToList());
		}

		[TestMethod]
		public void CannotRetryMoveIfSwitchesHaveADifferentSetting()
		{
			// Make the switch settings in period 0 and 2 different
			_solution.SwapMove("Line2a", "Line2b", 0).Apply(true);
			_solution.SwapMove("Line2a", "Line2b", 2).Apply(true);

			// Optimize
			var nh = new CloseSwitchAndOpenOtherNeighbourhood(Network.GetLine("Line2a"), Problem.GetPeriod(1));
			var selector = CreateRetrySelector(nh);
			var selected = Optimize(selector);

			// A move was made, but was not retried, because the switch settings in adjacent periods did not allow it
			Assert.AreEqual(1, selected.Count);
			Assert.IsNotNull(selected[0].Move);
		}

		[TestMethod]
		public void NonimprovingMovesAreNotRetried()
		{
			// Change solution to use Line2a in each period
			foreach (var period in Problem.Periods)
				_solution.SwapMove("Line2a", "Line2b", period.Index).Apply(true);

			// Optimize
			var nh = new CloseSwitchAndOpenOtherNeighbourhood(Network.GetLine("Line2b"), Problem.GetPeriod(1));
			var selector = CreateRetrySelector(nh);
			var selected = Optimize(selector, acceptableSetBack: 1000);

			// A move was made, but was not retried due to being nonimproving
			Assert.AreEqual(1, selected.Count);
			Assert.IsNotNull(selected[0].Move);
		}

		[TestMethod]
		public void SelectorHandlesParallelExploration()
		{
			// Start by exploring two neighborhoods in different periods
			var nh1 = new CloseSwitchAndOpenOtherNeighbourhood(Network.GetLine("Line2a"), Problem.GetPeriod(1));
			var nh2 = new CloseSwitchAndOpenOtherNeighbourhood(Network.GetLine("Line1a"), Problem.GetPeriod(2));

			// Optimize, with 2 neighborhoods in parallel
			var selector = CreateRetrySelector(nh1, nh2);
			var selected = Optimize(selector, parallelDegree: 2);

			// Verify:

			// The two first neighborhoods selected are the ones we created
			CollectionAssert.AreEquivalent(new[] { nh1, nh2 }, selected.Take(2).Select(x => x.Nh).ToList());

			// Six moves were made in total
			Assert.AreEqual(6, selected.Count);
		}

		[TestMethod]
		public void RetrySelectorIsCreatedByOptimizerFactory()
		{
			var parameters = new OptimizerParameters().MetaHeuristicParams.LocalSearchParameters;
			var localSearcher = OptimizerFactory.CreateLocalSearchSolver(parameters, _solution, Criteria, new());
			var selector = localSearcher.NeighborhoodSelector;

			Assert.IsNotNull(selector as RetryImprovingMoveInAdjacentPeriodsSelector);
		}

		/// <summary>
		/// Creates a <see cref="RetryImprovingMoveInAdjacentPeriodsSelector"/>,
		/// where the main selector selects each of <paramref name="initialNeighborhoods"/> once.
		/// </summary>
		private static INeighborhoodSelector CreateRetrySelector(params CloseSwitchAndOpenOtherNeighbourhood[] initialNeighborhoods)
		{
			// Create a main selector that selects the initial neighborhoods
			var mainSelector = SequentialSelector.Build().SelectEachOf(initialNeighborhoods).Once.Stop();

			// Wrap it in the retry selector
			var selector = new RetryImprovingMoveInAdjacentPeriodsSelector(mainSelector);
			return selector;
		}

		/// <summary>
		/// Runs optimization and reports the neighborhoods and moves selected.
		/// </summary>
		/// <param name="selector">The neighborhood selector to use</param>
		/// <param name="parallelDegree">The number of neighborhoods to explore in parallel</param>
		/// <param name="acceptableSetBack">The value given to <see cref="Descent.AcceptableSetBack"/></param>
		private List<(INeighborhood Nh, Move Move, double DeltaValue)> Optimize(INeighborhoodSelector selector, 
			int parallelDegree = 1, double acceptableSetBack = 0)
		{
			// Set up registering the neighborhoods and moves
			List<(INeighborhood Nh, Move Move, double DeltaValue)> selected = new();
			((RetryImprovingMoveInAdjacentPeriodsSelector)selector).RegisteringResult 
				+= (s, e) => selected.Add((e.Neighborhood, e.MoveInfo.Move, e.MoveInfo.DeltaValue));

			// Create optimizer
			LocalSearcher optimizer;
			if (parallelDegree == 1)
			{
				optimizer = new Descent(selector, new DeltaValueExplorer()) 
				{ AcceptableSetBack = acceptableSetBack };
			}
			else
			{
				optimizer = new ParallelNhDescent(selector, new DeltaValueExplorer(), new MoveDependenceRule())
				{ MinimumParallelNeighborhoods = parallelDegree };
			}

			// Optimize
			optimizer.Optimize(_solution, Criteria);

			return selected;
		}
	}
}
