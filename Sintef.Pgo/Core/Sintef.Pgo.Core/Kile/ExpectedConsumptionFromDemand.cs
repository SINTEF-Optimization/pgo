using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Calculates expected energy consumption by assuming that the consumption
	/// equals the demand specified in a sequence of <see cref="PeriodData"/>.
	/// The consumption after the last period is assumed to equal that in the last period.
	/// </summary>
	public class ExpectedConsumptionFromDemand : ExpectedConsumptionProvider
	{
		/// <summary>
		/// Data for each period, including the per-consumer demand
		/// </summary>
		private List<PeriodData> _allPeriodData;

		/// <summary>
		/// The period index of the first element on _allPeriodData
		/// </summary>
		private int _firstIndex;

		/// <summary>
		/// Initializes the consumption provider.
		/// </summary>
		/// <param name="allPeriodData">Data for each period, including the per-consumer demand</param>
		public ExpectedConsumptionFromDemand(IEnumerable<PeriodData> allPeriodData)
		{
			_allPeriodData = allPeriodData.ToList();
			_firstIndex = allPeriodData.First().Period.Index;

			for (int i = 0; i < _allPeriodData.Count; ++i)
			{
				if (_allPeriodData[i].Period.Index != _firstIndex + i)
					throw new ArgumentException("Index mismatch in period data given to consumption estimator");
			}
		}

		/// <summary>
		/// Presents the demands given for each period as a sequence of
		/// spans of constant power consumption for the given 
		/// <paramref name="consumer"/>, from <paramref name="startTime"/> onward
		/// </summary>
		public override IEnumerable<PowerSpan> PowerSpans(Bus consumer, DateTime startTime)
		{
			int periodIndex = _allPeriodData.First(d => d.Period.EndTime > startTime).Period.Index - _firstIndex;

			while (periodIndex < _allPeriodData.Count)
			{
				var period = _allPeriodData[periodIndex].Period;
				yield return new PowerSpan()
				{
					Start = period.StartTime,
					Length = period.Length,
					ActivePower = _allPeriodData[periodIndex].Demands.ActivePowerDemand(consumer),
					IsLast = (periodIndex == _allPeriodData.Count - 1)
				};

				++periodIndex;
			}
		}
	}
}
