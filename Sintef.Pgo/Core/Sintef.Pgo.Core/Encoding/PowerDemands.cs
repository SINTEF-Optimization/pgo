using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A power demand for each consumer bus.
	/// </summary>
	public class PowerDemands
	{
		/// <summary>
		/// Enumerates all the demands, in VA.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<KeyValuePair<Bus, Complex>> Demands => _powerDemand;

		/// <summary>
		/// The sum of all demands
		/// </summary>
		/// <returns></returns>
		public Complex Sum => _powerDemand.Values.ComplexSum();

		#region Private data members

		/// <summary>
		/// Complex power demand for each consumer in the network. 
		/// A demand is a non-negative number (active and reactive both).
		/// </summary>
		public Dictionary<Bus, Complex> _powerDemand;

		#endregion

		#region Construction

		/// <summary>
		/// Initializes zero demand for each consumer in the <paramref name="network"/>.
		/// </summary>
		public PowerDemands(PowerNetwork network)
		{
			_powerDemand = network.Consumers.ToDictionary(c => c, c => new Complex(0, 0));
		}

		/// <summary>
		/// Copy constructor.
		/// </summary>
		/// <param name="other">The demands to copy from.</param>
		public PowerDemands(PowerDemands other)
		{
			_powerDemand = new Dictionary<Bus, Complex>(other._powerDemand);
		}

		/// <summary>
		/// Clone function, returns new PowerDemands with the same data. The
		/// bus references are the same.
		/// </summary>
		/// <returns></returns>
		public PowerDemands Clone() => new PowerDemands(this);

		/// <summary>
		/// Clones the demands to the given <paramref name="network"/>, with buses identified by name.
		/// Demands for consumers not in the target network are ignored.
		/// </summary>
		/// <returns></returns>
		public PowerDemands CloneTo(PowerNetwork network)
		{
			PowerDemands clone = new PowerDemands(network);
			foreach (var (bus, demand) in _powerDemand)
			{
				if (network.HasBus(bus.Name))
					clone.SetPowerDemand(network.GetBus(bus.Name), demand);
			}

			return clone;
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Returns the complex power demand of the given bus, in VA. Throws if the bus is not a consumer.
		/// </summary>
		public Complex PowerDemand(Bus bus)
		{
			if (bus.Type != BusTypes.PowerConsumer)
				throw new InvalidOperationException("This bus is not a power consumer!");
			return _powerDemand[bus];
		}

		/// <summary>
		/// Sets the complex power demand of the given bus, in VA. Throws if the bus is not a consumer.
		/// Throws an exception if the demand is negative (in either component, real or imaginary).
		/// </summary>
		public void SetPowerDemand(Bus bus, Complex demand)
		{
			if (bus.Type != BusTypes.PowerConsumer)
				throw new InvalidOperationException("This bus is not a power consumer!");
			if (demand.Real < 0)
				throw new ArgumentException($"PowerDemands.SetPowerDemand: demand is negative: {demand}");
			_powerDemand[bus] = demand;
		}

		/// <summary>
		/// Returns the active power demand in the given bus, in W. Throws if the bus is not a consumer.
		/// </summary>
		public double ActivePowerDemand(Bus bus) => PowerDemand(bus).Real;

		/// <summary>
		/// Sets the active power demand in the given bus, in W. Throws if the bus is not a consumer.
		/// </summary>
		public void SetActivePowerDemand(Bus bus, double demand) => SetPowerDemand(bus, new Complex(demand, PowerDemand(bus).Imaginary));

		/// <summary>
		/// Returns the reactive power demand in the given bus, in var. Throws if the bus is not a consumer.
		/// </summary>
		public double ReactivePowerDemand(Bus bus) => PowerDemand(bus).Imaginary;

		/// <summary>
		/// Sets the reactive power demand in the given bus, in var. Throws if the bus is not a consumer.
		/// </summary>
		public void SetReactivePowerDemand(Bus bus, double demand) => SetPowerDemand(bus, new Complex(PowerDemand(bus).Real, demand));


		#region Temporary functions for use in research

		/// <summary>
		/// Returns a random modification of the given input demand. Used to generate synthetic data for testing.
		/// </summary>
		/// <param name="originalDemand"></param>
		/// <param name="random"></param>
		/// <returns></returns>
		public static Complex GetModifiedDemand(Complex originalDemand, Random random)
		{
			double activeDem =  originalDemand.Real * (((double)random.Next(70, 130))) / 100.0;
			double reactiveDem = originalDemand.Imaginary * (((double)random.Next(70, 130))) / 100.0;
			return new Complex(activeDem, reactiveDem);
		}

		/// <summary>
		/// Returns a random modification of the given input demands for all buses, based on this demand object. 
		/// Used to generate synthetic data for testing.
		/// </summary>
		/// <param name="random"></param>
		/// <returns></returns>
		public PowerDemands GetModifiedDemand(Random random)
		{
			PowerDemands modifiedDemands = new PowerDemands(this);
			_powerDemand.Do(kvp => modifiedDemands.SetPowerDemand(kvp.Key, GetModifiedDemand(kvp.Value, random)));
			return modifiedDemands;
		}

		/// <summary>
		/// Sets the demand for all consumers to the given value
		/// </summary>
		public void SetAllConsumerDemands(Complex demand)
		{
			foreach (var consumer in _powerDemand.Keys.ToList())
				_powerDemand[consumer] = demand;
		}

		#endregion


		#endregion
	}
}
