using System;
using System.Collections.Generic;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Provides consumers' expected energy consumption
	/// within a time interval. The time interval itself is a random variable, with a specified probability
	/// distribution for when it starts.
	/// Used in the calculation of KILE costs.
	/// </summary>
	public interface IExpectedConsumptionProvider
	{
		/// <summary>
		/// Returns the expected total active energy consumption for the given <paramref name="consumer"/>, for
		/// a time interval that has the specified length and whose start time is uniformly
		/// distributed within the given <paramref name="startPeriod"/>.
		/// 
		/// The unit is Wh.
		/// </summary>
		/// <param name="consumer">The consumer whose consumption to consider</param>
		/// <param name="startPeriod">The period within which the interval starts</param>
		/// <param name="intervalLength">The length of the interval</param>
		double EnergyConsumption(Bus consumer, Period startPeriod, TimeSpan intervalLength);
	}

	/// <summary>
	/// Provides consumers' expected energy consumption
	/// within a time interval. The time interval itself is a random variable, with a specified probability
	/// distribution for when it starts.
	/// Used in the calculation of KILE costs.
	/// </summary>
	public abstract class ExpectedConsumptionProvider : IExpectedConsumptionProvider
	{
		/// <summary>
		/// Returns the expected total active energy consumption for the given <paramref name="consumer"/>, for
		/// a time interval that has the specified length and whose start time is uniformly
		/// distributed within the given <paramref name="startPeriod"/>.
		/// 
		/// The unit is Wh.
		/// </summary>
		/// <param name="consumer">The consumer whose consumption to consider</param>
		/// <param name="startPeriod">The period within which the interval starts</param>
		/// <param name="intervalLength">The length of the interval</param>
		public double EnergyConsumption(Bus consumer, Period startPeriod, TimeSpan intervalLength)
		{
			// Get the time spans of constant power from the start of startPeriod onward
			var powerSpans = PowerSpans(consumer, startPeriod.StartTime);

			double consumption = 0;

			foreach (var span in powerSpans)
			{
				var (spanStart, spanLength, isLastSpan)
					= (span.Start, span.Length, span.IsLast);

				if (spanStart >= startPeriod.EndTime + intervalLength)
					break;

				// Find the average overlap between the interval and this span
				double averageEndPosition = AveragePosition(startPeriod.StartTime + intervalLength, startPeriod.EndTime + intervalLength);
				double averageStartPosition = AveragePosition(startPeriod.StartTime, startPeriod.EndTime);
				double averageOverlapHours = (averageEndPosition - averageStartPosition) * spanLength.TotalHours;

				// Add the average consumption in this span
				consumption += span.ActivePower * averageOverlapHours;

				if (isLastSpan)
					break;


				// Finds the average value, expressed as a fractional position in the span
				// [spanStart, spanStart + spanLength],
				// of a random variable chosen uniformly in (startTime, endTime) and then
				// bounded to not go outside the span.
				// (If isLastSpan is true, bounding at the end of the span does not take place)
				double AveragePosition(DateTime startTime, DateTime endTime)
				{
					var length = (endTime - startTime).TotalHours;
					var periodEnd = spanStart + spanLength;

					if (spanStart >= endTime)
						// Always bounded by start
						return 0;

					if (!isLastSpan && periodEnd <= startTime)
						// Always bounded by end
						return 1;

					double probabilityInside = 1;
					double probabilibyAtEnd = 0;

					if (startTime < spanStart)
					{
						// Find
						double probabilityAtStart = (spanStart - startTime).TotalHours / length;
						probabilityInside -= probabilityAtStart;
						startTime = spanStart;
					}

					if (!isLastSpan && periodEnd < endTime)
					{
						probabilibyAtEnd = (endTime - periodEnd).TotalHours / length;
						probabilityInside -= probabilibyAtEnd;
						endTime = periodEnd;
					}

					return probabilibyAtEnd + probabilityInside * ((endTime - spanStart) + (startTime - spanStart)).TotalHours / 2 / spanLength.TotalHours;
				}
			}

			return consumption;
		}

		/// <summary>
		/// Enumerates the spans of constant power consumption for the given 
		/// <paramref name="consumer"/>, from <paramref name="startTime"/> onward.
		/// </summary>
		public abstract IEnumerable<PowerSpan> PowerSpans(Bus consumer, DateTime startTime);

		/// <summary>
		/// Information about a time span with constant active power consumption for
		/// a consumer
		/// </summary>
		public struct PowerSpan
		{
			/// <summary>
			/// The start of the span
			/// </summary>
			public DateTime Start;

			/// <summary>
			/// The length of the span
			/// </summary>
			public TimeSpan Length;

			/// <summary>
			/// The (constant) active power consumption during the span
			/// </summary>
			public double ActivePower;

			/// <summary>
			/// If true, this span is the last, and the same power consumption
			/// continues indefinitely after its end.
			/// </summary>
			public bool IsLast;
		};
	}

	public static partial class Extensions
	{
		/// <summary>
		/// Returns the expected average active power supplied to the given <paramref name="consumer"/>, for
		/// a time interval that has the specified length and whose start time is uniformly
		/// distributed within the given <paramref name="startPeriod"/>.
		/// 
		/// The unit is W.
		/// </summary>
		/// <param name="provider">Provider of expected consumption.</param>
		/// <param name="consumer">The consumer whose consumption to consider</param>
		/// <param name="startPeriod">The period within which the interval starts</param>
		/// <param name="intervalLength">The length of the interval</param>
		public static double AveragePower(this IExpectedConsumptionProvider provider, Bus consumer, Period startPeriod, TimeSpan intervalLength)
		{
			return provider.EnergyConsumption(consumer, startPeriod, intervalLength) / intervalLength.TotalHours;
		}
	}
}
