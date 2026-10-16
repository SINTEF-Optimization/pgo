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
		/// Data for each period, including the per-consumer demand, indexed by period index.
		/// Entries for periods that were not given are null.
		/// </summary>
		private List<PeriodData> _allPeriodData;

		/// <summary>
		/// Initializes the consumption provider.
		/// </summary>
		/// <param name="allPeriodData">Data for each period, including the per-consumer demand</param>
		public ExpectedConsumptionFromDemand(IEnumerable<PeriodData> allPeriodData)
		{
			_allPeriodData = new List<PeriodData>();

			foreach (var data in allPeriodData)
			{
				int index = data.Period.Index;
				while (_allPeriodData.Count <= index)
					_allPeriodData.Add(null);

				if (_allPeriodData[index] != null)
					throw new ArgumentException("Duplicate period index in period data given to consumption estimator");

				_allPeriodData[index] = data;
			}
		}

		/// <summary>
		/// Presents the demands given for each period as a sequence of
		/// spans of constant power consumption for the given 
		/// <paramref name="consumer"/>, from <paramref name="startTime"/> onward
		/// </summary>
		public override IEnumerable<PowerSpan> PowerSpans(Bus consumer, DateTime startTime)
		{
			int periodIndex = _allPeriodData.First(d => d != null && d.Period.EndTime > startTime).Period.Index;

			while (periodIndex < _allPeriodData.Count)
			{
				if (_allPeriodData[periodIndex] == null)
				{
					++periodIndex;
					continue;
				}

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
