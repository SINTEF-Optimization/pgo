using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A constraint that requires the network to be radial
	/// (see <see cref="NetworkConfiguration.IsRadial"/>).
	///
	/// As an objective, it counts the number of periods in which the
	/// network is not radial and adds a very large penalty for each.
	/// </summary>
	public class SingleSubstationConnectionConstraint : Criterion, ISolutionAnnotator, ICanCloneForAggregateProblem, ISolutionDependentComposite
	{
		/// <summary>
		/// Clones the criterion to be valid for an aggregate of the given
		/// <paramref name="originalProblem"/>.
		/// </summary>
		/// <param name="originalProblem">The original problem</param>
		/// <param name="aggregateProblem">The aggregate problem</param>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new SingleSubstationConnectionConstraint() { Name = Name };
		}

		/// <summary>
		/// Returns true if the solution has a radial network in all periods
		/// </summary>
		public override bool IsSatisfied(ISolution solution)
		{
			return Value(solution) == 0;
		}

		/// <summary>
		/// Returns the number of periods where the solution's network is not radial,
		/// times a very large penalty.
		/// </summary>
		public override double Value(ISolution solution)
		{
			if (solution is PgoSolution mpSol)
			{
				return 1e100 * mpSol.SinglePeriodSolutions.Count(s => !s.IsRadial);
			}
			else
				throw new Exception("Unknown solution type");
		}

		/// <summary>
		/// Returns a string representation of violations of the constraint.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override string Reason(ISolution solution)
		{
			if (solution is PgoSolution mpSol)
			{
				return String.Concat(mpSol.SinglePeriodSolutions.Select(s =>
				{
					string r = ReasonForSingle(s);
					return String.IsNullOrEmpty(r) ? String.Empty : $"(SinglePerSolInfeasible:{r})";
				}));
			}
			else
				throw new Exception("Unknown solution type");
		}

		/// <summary>
		/// Returns whether the solution implied by the move is a feasible one, 
		/// that is, whether the move is legal.
		/// </summary>
		public override bool LegalMove(Move move)
		{
			if (move is SwapSwitchStatusMove)
				return true; //The definition of the moves by itself should ensure this
			else
				throw new NotImplementedException(string.Format("The criterion '{0}' does not implement LegalMove() for the given type of move {1}", this, move.GetType()));
		}

		/// <summary>
		/// Returns annotations for the given solution.
		/// Annotates diconnected buses and cycles in each period
		/// </summary>
		public IEnumerable<Annotation> Annotate(ISolution solution)
		{
			var mySolution = (PgoSolution)solution;
			var problem = mySolution.Problem;

			foreach (var period in problem.Periods)
			{
				var configuration = mySolution.GetPeriodSolution(period).NetConfig;

				if (!configuration.IsConnected)
					yield return DisconnectedAnnotation(period, configuration);

				if (configuration.HasCycles)
					yield return CycleAnnotation(period, configuration);
			}
		}

		/// <summary>
		/// Returns annotators that highlight lines according to the distance
		/// (in lines) they are from the provider they're connected to
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public IEnumerable<object> GetParts(ISolution solution)
		{
			var mySolution = (PgoSolution)solution;
			var problem = mySolution.Problem;

			// Find the maximum distance of any connected bus from its provider

			int MaxDistance(Period period)
			{
				var configuration = mySolution.GetPeriodSolution(period).NetConfig;
				return configuration.Network.Buses.Where(b => configuration.BusIsConnected(b)).Max(b => configuration.DistanceToProvider(b));
			}
			int maxDistance = problem.Periods.Max(p => MaxDistance(p));

			// Create an annotator covering up to that distance
			int step = 1;
			while (step < maxDistance)
				step *= 10;
			DistanceAnnotator distanceAnnotator = new DistanceAnnotator(1, maxDistance, step);

			// Return the first level of sub-annotators (i.e. expand once automatically)
			return distanceAnnotator.GetParts(solution);
		}

		private static string ReasonForSingle(PeriodSolution sol)
		{
			if (sol.IsRadial)
			{
				return "No bus is associated with more than one substation";
			}
			else
			{
				return $"The network is not radial with these switch settings. There is at least one cycle:\r\n {sol.NetConfig.GetCyclesDescriptionByNodeIDs()}";
			}
		}

		/// <summary>
		/// Returns an annotation indicating all buses in cycles in the given configuration
		/// </summary>
		private Annotation CycleAnnotation(Period period, NetworkConfiguration configuration)
		{
			var buses = configuration.BusesInCycles.ToList();
			return new BusAnnotation(buses, period, $"There are cycles with {buses.Count} buses in total");
		}

		/// <summary>
		/// Returns an annotation indicating all diconnected buses in the given configuration
		/// </summary>
		private Annotation DisconnectedAnnotation(Period period, NetworkConfiguration configuration)
		{
			var buses = configuration.DisconnectedBuses.ToList();
			return new BusAnnotation(buses, period, $"There are {buses.Count} disconnected buses");
		}

		/// <summary>
		/// An annotator that highlights all lines whose distance to its
		/// connected provider falls in a specified range.
		/// </summary>
		private class DistanceAnnotator : ISolutionAnnotator, ISolutionDependentComposite
		{
			/// <summary>
			/// The low end of the range
			/// </summary>
			private int _minDistance;

			/// <summary>
			/// The high end of the range
			/// </summary>
			private int _maxDistance;

			/// <summary>
			/// The step size used to create the range
			/// </summary>
			private int _step;

			/// <summary>
			/// Initialize the annotator
			/// </summary>
			public DistanceAnnotator(int minDistance, int maxDistance, int step)
			{
				_minDistance = minDistance;
				_maxDistance = maxDistance;
				_step = step;
			}

			/// <summary>
			/// Returns annotations for the given solution:
			/// One for all lines with a distance within the range, and one for all
			/// lines closer than the range.
			/// </summary>
			public IEnumerable<Annotation> Annotate(ISolution solution)
			{
				var mySolution = (PgoSolution)solution;
				var problem = mySolution.Problem;

				foreach (var period in  problem.Periods)
				{
					var configuration = mySolution.GetPeriodSolution(period).NetConfig;
					var buses = configuration.Network.Buses
						.Except(configuration.Network.Providers);
					
					// Highlight lines with distance within the range

					var busesInRange = buses.Where(b => _minDistance <= configuration.DistanceToProvider(b) &&configuration.DistanceToProvider(b) <= _maxDistance);
					var lines = busesInRange.Select(b => configuration.UpstreamLine(b));
					
					yield return new LineAnnotation(lines, period, $"Distance to provider in {_minDistance}--{_maxDistance}: {lines.Count()} lines")
					{
						Color = Color.DodgerBlue
					};

					// Hightlight closer buses

					var busesBelowRange = buses.Where(b => configuration.DistanceToProvider(b) < _minDistance);
					lines = busesBelowRange.Select(b => configuration.UpstreamLine(b));

					yield return new LineAnnotation(lines, period, $"Distance to provider < {_maxDistance}: {lines.Count()} lines")
					{
						Color = Color.RoyalBlue
					};
				}
			}

			/// <summary>
			/// Breaks this annotator into (up to) 10 parts with smaller ranges
			/// </summary>
			public IEnumerable<object> GetParts(ISolution solution)
			{
				if (_step == 1)
					yield break;

				int step = _step / 10;
				int distance = _minDistance;
				while (distance <= _maxDistance)
				{
					int max = Math.Min(_maxDistance, distance + step - 1);
					yield return new DistanceAnnotator(distance, max, step);
					distance += step;
				}
			}

			/// <summary>
			/// Returns a description of the annotator
			/// </summary>
			public override string ToString()
			{
				if (_minDistance == _maxDistance)
					return $"Provider distance {_minDistance}";

				return $"Provider distance {_minDistance} - {_maxDistance}";
			}
		}
	}
}
