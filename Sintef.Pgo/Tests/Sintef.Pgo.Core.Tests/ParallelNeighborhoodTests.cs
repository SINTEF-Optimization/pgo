using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Scoop.Kernel;
using System;
using System.Linq;
using Sintef.Scoop.Utilities;
using System.Collections.Generic;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for the <see cref="MoveDependenceRule"/> used in parallel neighborhood descent
	/// </summary>
	[TestClass]
	public class ParallelNeighborhoodTests
	{
		private NetworkBuilder _builder = new NetworkBuilder();
		private PgoProblem _problem;
		private Period _firstPeriod;
		private PgoSolution _solution;
		private MoveDependenceRule _rule;

		private TimeSpan _testTime = TimeSpan.FromSeconds(0.3);

		[TestInitialize]
		public void Setup()
		{
			// Network and initial configuration:
			//
			//        s1     s2     s3
			//  Gen1 --- N2 -/- N3 --- Gen4
			//   |       |      |        | 
			//   | u1    / u2   | u3     / u4
			//   |       |      |        |
			// Cons1 --- M2 -/- M3 --- Cons4
			//        t1     t2     t3
			//
			//  / is an open switch.

			_builder.Add("Gen1[generatorVoltage=200] -- s1[closed] -- N2 -- s2[open] -- N3 -- s3[closed] -- Gen4[generatorVoltage=200]");
			_builder.Add("Cons1[consumption=(1,0)] -- t1[closed] -- M2 -- t2[open] -- M3 -- t3[closed] -- Cons4[consumption=(1,0)]");
			_builder.Add("Gen1 -- u1[closed] -- Cons1");
			_builder.Add("N2 -- u2[open] -- M2");
			_builder.Add("N3 -- u3[closed] -- M3");
			_builder.Add("Gen4 -- u4[open] -- Cons4");

			var periods = _builder.RepeatedPeriodData(3);
			_solution = _builder.Solution(periods, "3-period problem");
			_problem = _solution.Problem;
			_firstPeriod = _problem.Periods.First();

			_rule = new MoveDependenceRule();
		}

		[TestMethod]
		public void MovesInDifferentPeriodsAreIndependent()
		{
			var move1 = _solution.SwapMove("s1", "u2");
			var move2 = _solution.SwapMove("s1", "u2", periodIndex: 1);

			Assert.IsTrue(AreIndependent(move1, move2));
		}

		[TestMethod]
		public void MovesInvolvingSameSwitchAreDependent()
		{
			var move1 = _solution.SwapMove("s1", "u2");
			var move2a = _solution.SwapMove("s1", "u2"); // Both same switches
			var move2b = _solution.SwapMove("s1", "s2"); // Opening same switch
			var move2c = _solution.SwapMove("t1", "u2"); // Closing same switch

			Assert.IsTrue(AreDependent(move1, move2a));
			Assert.IsTrue(AreDependent(move1, move2b));
			Assert.IsTrue(AreDependent(move1, move2c));
		}

		[TestMethod]
		public void TwoMovesAreIndependentWhenTheResultingSolutionIsRadial()
		{
			foreach (var (move1, move2) in MovePairsToTest(10).StopAfter(_testTime))
				Assert.AreEqual(SolutionAfterMovesIsRadial(move1, move2), AreIndependent(move1, move2));
		}

		[TestMethod]
		public void AMoveCanBeUpdatedWhenTheResultingSolutionIsRadial()
		{
			foreach (var (move1, move2) in MovePairsToTest(10).StopAfter(_testTime))
				Assert.AreEqual(SolutionAfterMovesIsRadial(move1, move2), CanUpdate(move1, move2));
		}

		[TestMethod]
		public void ThreeMovesAreIndependentWhenTheResultingSolutionIsRadial()
		{
			foreach (var (move1, move2, move3) in MoveTriplesToTest(10).StopAfter(_testTime))
				Assert.AreEqual(SolutionAfterMovesIsRadial(move1, move2, move3), AreIndependent(move1, move2, move3));
		}

		[TestMethod]
		public void AMoveCanBeUpdatedOverTwoMovesOnlyWhenTheResultingSolutionIsRadial()
		{
			foreach (var (move1, move2, move3) in MoveTriplesToTest(10).StopAfter(_testTime))
			{
				if (!SolutionAfterMovesIsRadial(move1, move2, move3))
					Assert.IsFalse(CanUpdate(move1, move2, move3));
			}

			// It is possible for updating to fail even when the final solution is radial, 
			// since move1 and move3 may be dependent, even if the whole set is independent.
		}

		/// <summary>
		/// Produces all pairs of moves for a sequence of configurations.
		/// Beware that the solution is modified during the enumeration if
		/// <paramref name="configurationCount"/> > 1.
		/// </summary>
		private IEnumerable<(SwapSwitchStatusMove, SwapSwitchStatusMove)> MovePairsToTest(int configurationCount = 1)
		{
			Random r = new();

			for (int i = 0; i < configurationCount; ++i)
			{
				var allMoves = _solution.GetAllRadialSwapMoves(_firstPeriod).Cast<SwapSwitchStatusMove>();

				// Try all combinations of possible moves
				foreach (var move1 in allMoves)
				{
					foreach (var move2 in allMoves)
					{
						yield return (move1, move2);
					}
				}

				if (configurationCount > 1)
				{
					// Apply a random move to get a different configuration, and repeat, a few times
					var move = allMoves.RandomElement(r);
					Console.WriteLine(move);
					move.Apply(false);
				}
			}
		}

		/// <summary>
		/// Produces all triples of moves for a sequence of configurations.
		/// The two first moves are always independent.
		/// Beware that the solution is modified during the enumeration if
		/// <paramref name="configurationCount"/> > 1.
		/// </summary>
		private IEnumerable<(SwapSwitchStatusMove, SwapSwitchStatusMove, SwapSwitchStatusMove)> MoveTriplesToTest(int configurationCount)
		{
			foreach (var (move1, move2) in MovePairsToTest(configurationCount))
			{
				if (AreDependent(move1, move2))
					continue;

				var allMoves = _solution.GetAllRadialSwapMoves(_firstPeriod).Cast<SwapSwitchStatusMove>();
				foreach (var move3 in allMoves)
					yield return (move1, move2, move3);
			}
		}

		/// <summary>
		/// Returns true if the given moves are independent
		/// </summary>
		private bool AreIndependent(params SwapSwitchStatusMove[] moves) => !AreDependent(moves);

		/// <summary>
		/// Returns true if the given moves are dependent
		/// </summary>
		private bool AreDependent(params SwapSwitchStatusMove[] moves)
		{
			var earlierMoves = moves.Take(moves.Length - 1).Select(m => Info(m));

			return _rule.AreDependent(earlierMoves, Info(moves.Last()));
		}

		/// <summary>
		/// Returns true if applying the given moves in turn produces a radial solution
		/// </summary>
		bool SolutionAfterMovesIsRadial(params SwapSwitchStatusMove[] moves)
		{
			if (moves.SelectMany(m => m.SwitchesToChange).Distinct().Count() != moves.Length * 2)
				// Moves involve the same switch(es)
				return false;

			foreach (var move in moves)
				move.Apply(false);

			bool result = _solution.GetPeriodSolution(_firstPeriod).IsRadial;

			foreach (var move in moves.Reverse())
				move.GetReverse().Apply(false);

			return result;
		}

		/// <summary>
		/// Returns true if the dependence rule is able to update the last of <paramref name="moves"/>
		/// over applying the earlier moves in the array
		/// </summary>
		private bool CanUpdate(params SwapSwitchStatusMove[] moves)
		{
			var earlierMoves = moves.Take(moves.Length - 1);

			return Updated(earlierMoves, moves.Last()) != null;
		}

		/// <summary>
		/// Returns the updated version of <paramref name="moveToUpdate"/> produced by the dependence rule
		/// when applying <paramref name="movesToBeApplied"/>.
		/// Returns null if the rule cannot update the move.
		/// </summary>
		private Move Updated(IEnumerable<SwapSwitchStatusMove> movesToBeApplied, Move moveToUpdate)
		{
			foreach (var move in movesToBeApplied)
			{
				Func<Move> updateFunc = null;
				if (moveToUpdate != null)
					updateFunc = _rule.Update(moveToUpdate, move);

				move.Apply(false);

				moveToUpdate = updateFunc?.Invoke();
			}
			foreach (var move in movesToBeApplied.Reverse())
				move.GetReverse().Apply(false);

			return moveToUpdate;
		}

		private MoveInfo Info(SwapSwitchStatusMove move)
		{
			return new MoveInfo(move, _problem.CriteriaSet.Objective.DeltaValue(move));
		}
	}
}
