using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Caches a double value per period for Power solutions.
	/// The period value is calculated by a configurable function.
	/// </summary>
	public class PerPeriodValueCacheClient : PerPeriodValueCacheClient<double>
	{
		/// <summary>
		/// New object using the given value function.
		/// </summary>
		/// <param name="valueFunction"></param>
		public PerPeriodValueCacheClient(Func<PeriodSolution, double> valueFunction)
		: base(valueFunction)
		{
		}

		internal PerPeriodValueCacheClient(Func<PgoSolution, Period, double> valueFunction,
			Func<ChangeSwitchesMove, IEnumerable<Period>> invalidateFunction)
			: base(valueFunction, invalidateFunction)
		{
		}
	}

	/// <summary>
	/// Caches a value of generic type <typeparamref name="T"/> per period for Power solutions.
	/// The period value is calculated by a configurable function.
	/// </summary>
	public class PerPeriodValueCacheClient<T> : CacheClient
	{
		/// <summary>
		/// The function that calculates the value of a period
		/// </summary>
		private Func<PgoSolution, Period, T> ValueFunction { get; }

		/// <summary>
		/// The function that produces the periods in which a move invalidates
		/// the cached value
		/// </summary>
		private Func<ChangeSwitchesMove, IEnumerable<Period>> InvalidateFunction { get; }

		/// <summary>
		/// Initializes a cache client for the case 
		/// that the cached value is calculated independently for each
		/// period solution.
		/// </summary>
		/// <param name="valueFunction">The function that calculates the value of a period solution</param>
		public PerPeriodValueCacheClient(Func<PeriodSolution, T> valueFunction)
		{
			ValueFunction = (solution, period) => valueFunction(solution.GetPeriodSolution(period));
			InvalidateFunction = (move) => new[] { move.Period };
		}

		/// <summary>
		/// Initializes a cache client for the case that the cached value for a period
		/// can depend on the solution in other periods.
		/// </summary>
		/// <param name="valueFunction">The function that calculates the value for a period</param>
		/// <param name="invalidateFunction">The function that produces the periods in which a move invalidates
		///   the cached value</param>
		internal PerPeriodValueCacheClient(Func<PgoSolution, Period, T> valueFunction,
			Func<ChangeSwitchesMove, IEnumerable<Period>> invalidateFunction)
		{
			ValueFunction = valueFunction;
			InvalidateFunction = invalidateFunction;
		}

		/// <summary>
		/// Computes cached data for the given solution
		/// </summary>
		public override CachedData ComputeCachedData(ISolution solution, CachedData oldData)
		{
			var mySolution = (PgoSolution)solution;

			var values = mySolution.SinglePeriodSolutions.ToDictionary(
				p => p.Period,
				p => ValueFunction(mySolution, p.Period)
			);

			return new PerPeriodCachedValue(values);
		}

		/// <summary>
		/// Invalidates the values that need invalidating due to the given move
		/// </summary>
		public override bool BeginUpdateCachedData(Move move, CachedData data)
		{
			var myMove = (ChangeSwitchesMove)move;
			var myData = (PerPeriodCachedValue)data;

			foreach (var period in InvalidateFunction(myMove))
				myData.InvalidPeriods.Add(period);

			return true;
		}

		/// <summary>
		/// Retrieves, and possibly calculates/updates, the per-period values
		/// of the given solution
		/// </summary>
		public Dictionary<Period, T> Values(ISolution solution)
		{
			var mySolution = (PgoSolution)solution;

			PerPeriodCachedValue data = ((PerPeriodCachedValue)solution.Cache.GetDataFor(this));

			lock (data)
			{
				if (data.InvalidPeriods.Count == 0)
					// Data does not need update - return it
					return data.Values;

				// Data needs update in one or more periods.

				if (data.PeriodsToUpdate == null)
				{
					// We're the first thread to notice. 
					// Initialize data for the update
					data.PeriodsToUpdate = data.InvalidPeriods.ToList();
					data.IncompletePeriodCount = data.PeriodsToUpdate.Count;
					data.UpdateFinished = new TaskCompletionSource<bool>();
				}
			}

			// Participate in updating the cached data

			while (true)
			{
				Period period = null;
				lock (data)
				{
					// Get a period that no-one is working on

					if (data.PeriodsToUpdate != null && data.PeriodsToUpdate.Count > 0)
					{
						period = data.PeriodsToUpdate[data.PeriodsToUpdate.Count - 1];
						data.PeriodsToUpdate.RemoveAt(data.PeriodsToUpdate.Count - 1);
					}
				}

				if (period == null)
				{
					// There is no more unclaimed work.
					// Wait for the update to finish and return the cached data.

					data.UpdateFinished?.Task.Wait();

					return data.Values;
				}

				// Compute period's value
				T value = default;
				try
				{
					value = ValueFunction(mySolution, period);
				}
				finally
				{
					lock (data)
					{
						// Update cached data
						data.Values[period] = value;
						--data.IncompletePeriodCount;

						if (data.IncompletePeriodCount == 0)
						{
							// Update is complete for all periods. Register this.

							data.InvalidPeriods.Clear();
							data.PeriodsToUpdate = null;
							data.UpdateFinished.SetResult(true);
							data.UpdateFinished = null;
						}
					}
				}
			}
		}

		/// <summary>
		/// Cached data for a solution
		/// </summary>
		private class PerPeriodCachedValue : CachedData
		{
			/// <summary>
			/// The value per period
			/// </summary>
			public Dictionary<Period, T> Values { get; }

			/// <summary>
			/// The periods whose value needs recalculation
			/// </summary>
			public HashSet<Period> InvalidPeriods { get; }

			/// <summary>
			/// During recalculation, the periods for which update work has not yet started
			/// </summary>
			public List<Period> PeriodsToUpdate { get; set; }

			/// <summary>
			/// During recalculation, the number of periods for which the update has not yet completed
			/// </summary>
			public int IncompletePeriodCount { get; set; }

			/// <summary>
			/// During recalculating, contains a task that will complete when recalculation completes
			/// </summary>
			public TaskCompletionSource<bool> UpdateFinished { get; set; }

			public PerPeriodCachedValue(Dictionary<Period, T> values)
			{
				Values = values;
				InvalidPeriods = new HashSet<Period>();
			}

			public override CachedData Clone()
			{
				throw new NotImplementedException();
			}
		}
	}
}
