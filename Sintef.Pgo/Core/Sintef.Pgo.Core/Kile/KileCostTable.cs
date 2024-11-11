using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Represents a table containing the per-kWh KILE costs, k_{P,ref} specified in paragraph 9.2 of
	/// "Forskrift om økonomisk og teknisk rapportering, inntektsramme for nettvirksomheten og tariffer",
	/// https://lovdata.no/dokument/SF/forskrift/1999-03-11-302/KAPITTEL_4-3#%C2%A79-2
	/// 
	/// The table specifies the KILE cost per kWh as a function of consumer category and the duration
	/// of the power outage.
	/// </summary>
	public class KileCostTable
	{
		/// <summary>
		/// The table coefficents, by consumer category and duration category
		/// </summary>
		private Dictionary<(ConsumerCategory, DurationCategory), (double Constant, double PerHour)> _coefficients;

		/// <summary>
		/// The consumer category provider to use
		/// </summary>
		private ConsumerCategoryProvider _categoryProvider;

		/// <summary>
		/// Contains cached values for <see cref="CostPerW"/>, indexed by bus index and <see cref="DurationCategory"/>.
		/// </summary>
		(double CostPerW, double CostPerWh)[,] _cachedCostPerW;

		/// <summary>
		/// Initializes the table
		/// </summary>
		public KileCostTable(PowerNetwork network, ConsumerCategoryProvider categoryProvider)
		{
			_categoryProvider = categoryProvider;
			_cachedCostPerW = new (double CostPerW, double CostPerWh)[network.BusIndexBound, (int)DurationCategory.MaxValue];

			InitializeCoefficients();
		}

		/// <summary>
		/// Returns the KILE cost per W for a power outage of the given <paramref name="duration"/> 
		/// at the given <paramref name="consumer"/> 
		/// </summary>
		public double CostPerW(Bus consumer, TimeSpan duration)
		{
			DurationCategory durationCat = Category(duration);

			// Look up cost in the cache

			var (costPerW, costPerWh) = _cachedCostPerW[consumer.Index, (int)durationCat];
			if (costPerW != 0 || costPerWh != 0)
				return costPerW + costPerWh * duration.TotalHours;

			// Not present: Calculate

			double costPerKW = 0;
			double costPerKWh = 0;

			foreach (var consumerCat in _categoryProvider.Categories(consumer))
			{
				var fraction = _categoryProvider.ConsumptionFraction(consumer, consumerCat);
				var (constant, perHour) = _coefficients[(consumerCat, durationCat)];

				costPerKW += fraction * constant;
				costPerKWh += fraction * perHour;
			}

			costPerW = costPerKW / 1000;
			costPerWh = costPerKWh / 1000;

			// Store in cache and return

			_cachedCostPerW[consumer.Index, (int)durationCat] = (costPerW, costPerWh);

			return costPerW + costPerWh * duration.TotalHours;
		}

		/// <summary>
		/// Sets up the _coefficients
		/// </summary>
		private void InitializeCoefficients()
		{
			_coefficients = new Dictionary<(ConsumerCategory, DurationCategory), (double, double)>();

			var consumer = ConsumerCategory.Agriculture;
			//-----------------------------------
			Add(DurationCategory.LessThan1Minute, 5, 14.3);
			Add(DurationCategory.LessThan1Hour, 5, 14.3);
			Add2(DurationCategory.LessThan4Hours, 19, 15.6, 1);
			Add2(DurationCategory.LessThan8Hours, 66, 14.3, 4);
			Add2(DurationCategory.AtLeast8Hours, 66, 14.3, 4);

			consumer = ConsumerCategory.Domestic;
			//-----------------------------------
			Add(DurationCategory.LessThan1Minute, 1.1, 9.8);
			Add(DurationCategory.LessThan1Hour, 1.1, 9.8);
			Add(DurationCategory.LessThan4Hours, 1.1, 9.8);
			Add(DurationCategory.LessThan8Hours, 1.1, 9.8);
			Add(DurationCategory.AtLeast8Hours, 1.1, 9.8);

			consumer = ConsumerCategory.Industry;
			//-----------------------------------
			Add(DurationCategory.LessThan1Minute, 34, 0);
			Add(DurationCategory.LessThan1Hour, 34, 84.7);
			Add2(DurationCategory.LessThan4Hours, 118, 82.3, 1);
			Add2(DurationCategory.LessThan8Hours, 365, 55.6, 4);
			Add2(DurationCategory.AtLeast8Hours, 588, 36.5, 8);

			consumer = ConsumerCategory.Trade;
			//-----------------------------------
			Add(DurationCategory.LessThan1Minute, 16, 0);
			Add(DurationCategory.LessThan1Hour, 28, 168.3);
			Add2(DurationCategory.LessThan4Hours, 196, 91.1, 1);
			Add2(DurationCategory.LessThan8Hours, 469, 141.3, 4);
			Add2(DurationCategory.AtLeast8Hours, 1034, 102.4, 8);

			consumer = ConsumerCategory.Public;
			//-----------------------------------
			Add(DurationCategory.LessThan1Minute, 7, 0);
			Add(DurationCategory.LessThan1Hour, 60, 113.2);
			Add2(DurationCategory.LessThan4Hours, 173, 27.9, 1);
			Add2(DurationCategory.LessThan8Hours, 257, 51.8, 4);
			Add2(DurationCategory.AtLeast8Hours, 464, 17.6, 8);

			consumer = ConsumerCategory.ElectricIndustry;
			//-----------------------------------
			Add(DurationCategory.LessThan1Minute, 49, 2.8);
			Add(DurationCategory.LessThan1Hour, 49, 2.8);
			Add(DurationCategory.LessThan4Hours, 49, 2.8);
			Add(DurationCategory.LessThan8Hours, 91, 2.8);
			Add(DurationCategory.AtLeast8Hours, 91, 2.8);


			// Register cost = constant + perHour * t.   t has unit hours
			void Add(DurationCategory duration, double constant, double perHour)
			{
				_coefficients.Add((consumer, duration), (constant, perHour));
			}

			// Register cost = constant + perHour * (t - offsetHours)
			void Add2(DurationCategory duration, double constant, double perHour, double offsetHours)
			{
				Add(duration, constant - perHour * offsetHours, perHour);
			}
		}

		/// <summary>
		/// Returns the duration category that an outage of the given <paramref name="duration"/> falls in
		/// </summary>
		private DurationCategory Category(TimeSpan duration)
		{
			if (duration.TotalMinutes < 1)
				return DurationCategory.LessThan1Minute;

			double hours = duration.TotalHours;
			if (hours < 1)
				return DurationCategory.LessThan1Hour;
			if (hours < 4)
				return DurationCategory.LessThan4Hours;
			if (hours < 8)
				return DurationCategory.LessThan8Hours;

			return DurationCategory.AtLeast8Hours;
		}

		/// <summary>
		/// A category of outage durations
		/// </summary>
		private enum DurationCategory
		{
			LessThan1Minute,
			LessThan1Hour,
			LessThan4Hours,
			LessThan8Hours,
			AtLeast8Hours,
			MaxValue
		}
	}
}
