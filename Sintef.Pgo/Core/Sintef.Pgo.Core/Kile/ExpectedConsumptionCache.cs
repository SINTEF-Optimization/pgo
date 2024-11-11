using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Caches the data provided by an <see cref="ExpectedConsumptionProvider"/> in order to
	/// improve efficiency.
	/// 
	/// The cache exploits the fact that the consumption is expressed as a sequence of spans, and is
	/// constant in each of these. This means that the expected power consumption is a piecewise
	/// constant function in the intervalLength, and its integral, <see cref="EnergyConsumption"/>, is a piecewise
	/// quadratic function in the intervalLength. This class computes and stores the coefficients
	/// of the quadratic function segments.
	/// 
	/// The current implementation requires that all spans have equal length and that each period
	/// in the problem starts at a span boundary. This can be generalized, at the expense of a little
	/// more complex lookup logic.
	/// </summary>
	public class ExpectedConsumptionCache : IExpectedConsumptionProvider
	{
		/// <summary>
		/// The provider whose data we cache
		/// </summary>
		private ExpectedConsumptionProvider _consumptionProvider;

		/// <summary>
		/// The length of all power spans, in hours
		/// </summary>
		private double _spanHours;

		/// <summary>
		/// The cached data, by [consumer bus index, start period index]
		/// </summary>
		private ConsumerAndPeriodData[,] _cachedData;

		/// <summary>
		/// Initializes the cache
		/// </summary>
		/// <param name="network"></param>
		/// <param name="periods"></param>
		/// <param name="consumptionProvider">The provider whose data to cache</param>
		public ExpectedConsumptionCache(PowerNetwork network, IEnumerable<Period> periods, ExpectedConsumptionProvider consumptionProvider)
		{
			_consumptionProvider = consumptionProvider;

			_cachedData = new ConsumerAndPeriodData[network.BusIndexBound, periods.Count()];

			// Find the length of one power span, so we can check all other spans later
			var consumer = network.Consumers.First();
			var time = periods.First().StartTime;
			_spanHours = consumptionProvider.PowerSpans(consumer, time).First().Length.TotalHours;
		}

		/// <summary>
		/// Returns the cached value of <see cref="ExpectedConsumptionProvider.EnergyConsumption"/> for
		/// the same arguments
		/// </summary>
		public double EnergyConsumption(Bus consumer, Period startPeriod, TimeSpan intervalLength)
		{
			// Look up data for the consumer and period
			ConsumerAndPeriodData data = _cachedData[consumer.Index, startPeriod.Index];

			double intervalHours = intervalLength.TotalHours;
			int spanDataIndex = (int)(intervalHours / _spanHours);

			if (data == null)
				// This consumer/period combination is new. Create its data.
				data = Cache(consumer, startPeriod, spanDataIndex);

			// Look up data for the correct span

			var spanData = data.SpanData;

			if (spanDataIndex >= spanData.Length)
			{
				if (data.IsComplete)
					// The interval extends past the last span:
					// the data for the last span is correct
					spanDataIndex = spanData.Length - 1;
				else
				{
					// Interval is longer than seen before. Extend the data.
					spanData = Cache(consumer, startPeriod, spanDataIndex).SpanData;
					if (spanDataIndex >= spanData.Length)
						spanDataIndex = spanData.Length - 1;
					}
				}

			// Compute the value

			return spanData[spanDataIndex].Value(intervalHours);
		}

		/// <summary>
		/// Updates the cached data for <paramref name="consumer"/> and <paramref name="startPeriod"/>.
		/// 
		/// Includes enough spans so that either <paramref name="spanDataIndex"/> is a valid index
		/// or the cached data is complete.
		/// </summary>
		/// <returns>The cached data</returns>
		private ConsumerAndPeriodData Cache(Bus consumer, Period startPeriod, int spanDataIndex)
		{
			var spanData = _consumptionProvider.PowerSpans(consumer, startPeriod.StartTime)
				.Take(spanDataIndex + 2)
				.Select((span, index) =>
				{
					if (span.Length.TotalHours != _spanHours)
						throw new Exception("Unequal span lengths are not handled by ExpectedConsumptionCache");

					// Find the span start
					double spanStartHours = index * _spanHours;

					// Find function values at span start/middle/end
					double leftValue = _consumptionProvider.EnergyConsumption(consumer, startPeriod, TimeSpan.FromHours(spanStartHours));
					double midValue = _consumptionProvider.EnergyConsumption(consumer, startPeriod, TimeSpan.FromHours(spanStartHours + 0.5 * _spanHours));
					double rightValue = _consumptionProvider.EnergyConsumption(consumer, startPeriod, TimeSpan.FromHours(spanStartHours + _spanHours));

					// Deduce the quadratic coefficients
					double c = leftValue;
					double a = 2 * (leftValue + rightValue - 2 * midValue) / (_spanHours * _spanHours);
					double b = (rightValue - leftValue) / _spanHours - a * _spanHours;

					return new SpanData(spanStartHours, a, b, c);
				})
				.ToArray();

			ConsumerAndPeriodData data = new ConsumerAndPeriodData()
			{
				SpanData = spanData,
				// The data is complete if we got less than we asked for:
				IsComplete = spanData.Length < spanDataIndex + 2
			};

			_cachedData[consumer.Index, startPeriod.Index] = data;

			return data;
		}

		/// <summary>
		/// Cached data for one consumer and start period
		/// </summary>
		private class ConsumerAndPeriodData
		{
			/// <summary>
			/// Data for each span (from the period start onward)
			/// </summary>
			public SpanData[] SpanData;

			/// <summary>
			/// If true, <see cref="SpanData"/> includes the last span from the underlying
			/// provider and cannot be extended. Data for the last span is valid
			/// also for longer intervals.
			/// </summary>
			public bool IsComplete;
		}

		/// <summary>
		/// A quadratic function segment covering one span.
		/// 
		/// The value is calculated as a*x*x + b*x + c, where x is the interval length
		/// minus the time from the period start to the span start, in hours.
		/// </summary>
		private class SpanData
		{
			private double _a;
			private double _b;
			private double _c;

			private double _spanStartHours;

			public SpanData(double spanStartHours, double a, double b, double c)
			{
				_spanStartHours = spanStartHours;
				_a = a;
				_b = b;
				_c = c;
			}

			internal double Value(double intervalHours)
			{
				double x = intervalHours - _spanStartHours;

				return _a * x * x + _b * x + _c;
			}
		}
	}
}
