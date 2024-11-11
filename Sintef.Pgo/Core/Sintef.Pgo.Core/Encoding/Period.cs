using System;
using System.Collections.Generic;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A time period
	/// </summary>
	public class Period
	{
		/// <summary>
		/// The start time of the period
		/// </summary>
		public DateTime StartTime { get; }

		/// <summary>
		/// The end time of the period
		/// </summary>
		public DateTime EndTime { get; }

		/// <summary>
		/// The length of the period
		/// </summary>
		public TimeSpan Length => EndTime - StartTime;

		/// <summary>
		/// The 0-based index of the period among all periods in the master problem.
		/// A sub problem for a single period may then have a <see cref="Period"/> with <see cref="Index"/> != 0.
		/// Use as little as possible.
		/// </summary>
		public int Index;

		/// <summary>
		/// Unique identifier of the period. If not given in the constructor, it is set as a string representation of the
		/// <see cref="Index"/>.
		/// </summary>
		public string Id { get; private set; }

		/// <summary>
		/// Initializes a period
		/// </summary>
		/// <param name="startTime"></param>
		/// <param name="endTime"></param>
		/// <param name="index"></param>
		/// <param name="id">Unique identifier of the period. Optional, if not given 
		/// it is set as a string representation of the <paramref name="index"/>.</param>
		public Period(DateTime startTime, DateTime endTime, int index, string id = null)
		{
			StartTime = startTime;
			EndTime = endTime;
			Index = index;
			Id = id ?? index.ToString();
		}

		/// <summary>
		/// Copy constructor. 
		/// </summary>
		/// <param name="otherPeriod">The period to copy</param>
		/// <param name="idOverride">If given, used as id instead of otherPeriod's Id.</param>
		public Period(Period otherPeriod, string idOverride = null)
			: this(otherPeriod.StartTime, otherPeriod.EndTime, otherPeriod.Index, idOverride ?? otherPeriod.Id)
		{
		}

		/// <summary>
		/// Returns a default 1h period with index 0.
		/// </summary>
		public static Period Default => new Period(new DateTime(2000, 1, 1), new DateTime(2000, 1, 1, 1, 0, 0), 0);

		/// <summary>
		/// Returns a period immediately following the given one, with the specified length, and index equal to period.Index + 1.
		/// </summary>
		public static Period Following(Period period, TimeSpan timeSpan) => new Period(period.EndTime, period.EndTime + timeSpan, period.Index + 1);


		/// <summary>
		/// String representation.
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			return $"Period {Index}: {Id}";
		}

		/// <summary>
		/// Makes a clone of the period.
		/// </summary>
		/// <param name="idOverride">If given, used as the id of the clone.</param>
		internal Period Clone(string idOverride = null) => new Period(this, idOverride);
	}
}
