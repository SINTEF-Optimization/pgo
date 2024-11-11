using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Power demands for a specific period
	/// </summary>
	public class PeriodData
	{
		#region Properties

		/// <summary>
		/// The period label
		/// </summary>
		public string Name => Period.Id;

		/// <summary>
		/// The network that the demands are defined for
		/// </summary>
		public PowerNetwork Network { get; }

		/// <summary>
		/// The power demands for each consumer bus in <see cref="Network"/>.
		/// </summary>
		public PowerDemands Demands { get; internal set; }

		/// <summary>
		/// The period this problem is for
		/// </summary>
		public Period Period { get; }

		#endregion

		#region Constructor

		/// <summary>
		/// Initializes a <see cref="PeriodData"/>
		/// </summary>
		/// <param name="period">The period the data is for</param>
		/// <param name="demands">Power demands, per consumer bus</param>
		/// <param name="network">The network the demands are for</param>
		public PeriodData(PowerNetwork network, PowerDemands demands, Period period)
		{
			Network = network;
			Demands = demands;
			Period = period;
		}

		/// <summary>
		/// Initializes zero demand for the given time period and period index.
		/// </summary>
		public PeriodData(PowerNetwork network, DateTime startOfPeriod, DateTime endOfPeriod, int index)
			: this(network, new PowerDemands(network), new Period(startOfPeriod, endOfPeriod, index))
		{
		}

		/// <summary>
		/// Initializes zero demand in a default period with index 0.
		/// </summary>
		public PeriodData(PowerNetwork network)
			: this(network, new DateTime(2000, 1, 1), new DateTime(2000, 1, 1, 1, 0, 0), 0)
		{
		}

		/// <summary>
		/// Copy constructor. Network, period and bus references are the same, but power demand values
		/// are cloned.
		/// </summary>
		public PeriodData(PeriodData other)
		{
			Demands = other.Demands.Clone();
			Period = other.Period;
			Network = other.Network;
		}

		/// <summary>
		/// Returns a clone of this period data. Network, period and bus references are the same, but power demand values
		/// are cloned.
		/// </summary>
		/// <returns></returns>
		public PeriodData Clone() => new PeriodData(this);

		#endregion

		/// <summary>
		/// Returns a random modification of the given input demands for all buses, based on this demand object. 
		/// Used to generate synthetic data for testing.
		/// </summary>
		/// <param name="random"></param>
		/// <param name="addTime">This time will be added to the start time of the this PowerDemand's StartOfPeriod, and given as StartOfPeriod in the returned demand.</param>
		/// <param name="index">The index of the new period</param>
		/// <returns></returns>
		public PeriodData GetModifiedPeriodData(Random random, TimeSpan addTime, int index)
		{
			PowerDemands modifiedDemands = Demands.GetModifiedDemand(random);
			return new PeriodData(Network, modifiedDemands, new Period(Period.StartTime + addTime, Period.EndTime + addTime, index));
		}
	}
}
