using Sintef.Pgo.Cim;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sintef.Scoop.Utilities;
using Sintef.Pgo.DataContracts;

using Voltage = UnitsNet.ElectricPotential;
using Resistance = UnitsNet.ElectricResistance;
using Conductance = UnitsNet.ElectricConductance;
using CurrentFlow = UnitsNet.ElectricCurrent;
using ActivePower = UnitsNet.Power;
using ReactivePower = UnitsNet.ReactivePower;
using ApparentPower = UnitsNet.ApparentPower;

namespace Sintef.Pgo.Core.IO
{

	/// <summary>
	/// Converts demands data between CIM format and <see cref="PowerDemands"/>
	/// </summary>
	public class CimDemandsConverter
	{
		/// <summary>
		/// The power network to create demands for
		/// </summary>
		private PowerNetwork _network;

		/// <summary>
		/// The parser from whose data the power network was created, if available
		/// </summary>
		private CimJsonParser _networkParser;

		/// <summary>
		/// Initializes the converter
		/// </summary>
		/// <param name="network">The power network to create demands forT</param>
		/// <param name="networkParser">The parser from whose data the power network was created, if available</param>
		public CimDemandsConverter(PowerNetwork network, CimJsonParser networkParser = null)
		{
			_network = network;
			_networkParser = networkParser;
		}

		/// <summary>
		/// Converts multi-period demand data from the REST API to PGO demands
		/// </summary>
		public List<PeriodData> ToPeriodData(List<CimJsonLdPeriodAndDemands> restDemandData)
		{
			// Convert to the .NET API version of CIM demands

			List<CimPeriodAndDemands> demandData = ToCimPeriodAndDemands(restDemandData);

			return ToPeriodData(demandData);
		}

		/// <summary>
		/// Converts multi-period demand data from the REST API to the .NET API
		/// </summary>
		public List<CimPeriodAndDemands> ToCimPeriodAndDemands(List<CimJsonLdPeriodAndDemands> restDemandData)
		{
			var result = new List<CimPeriodAndDemands>();

			foreach (var periodAndDemands in restDemandData)
			{
				var periodData = new CimPeriodAndDemands();
				periodData.Period = periodAndDemands.Period;
				periodData.Demands = ToCimDemands(periodAndDemands.Demands);

				result.Add(periodData);
			}

			return result;
		}

		/// <summary>
		/// Converts multi-period demand data from the .NET API to PGO demands
		/// </summary>
		public List<PeriodData> ToPeriodData(List<CimPeriodAndDemands> demandData)
		{
			if (demandData.Contains(null))
				throw new ArgumentException("The list of periods and demands contains null");

			// Convert periods

			var pgoPeriods = PgoJsonParser.ParsePeriods(demandData.Select(x => x.Period).ToList());

			// Convert demands for each period

			List<PeriodData> allPeriodData = new();

			foreach (var (cimPeriodAndDemands, pgoPeriod) in demandData.Zip(pgoPeriods))
			{
				try
				{
					if (cimPeriodAndDemands.Demands == null)
						throw new ArgumentException("The demands is null");

					// Convert to demands
					PowerDemands demands = ToPowerDemands(cimPeriodAndDemands.Demands);

					// Use as demands for a single period
					var periodData = new PeriodData(_network, demands, pgoPeriod);
					allPeriodData.Add(periodData);
				}
				catch (Exception ex)
				{
					throw new Exception($"Period '{pgoPeriod.Id}': {ex.Message}");
				}
			}

			return allPeriodData;
		}

		/// <summary>
		/// Builds demand data to send through the REST interface from a collection of
		/// CIM demand (SSH) datasets.
		/// A 1-hour period is created for each dataset.
		/// </summary>
		/// <param name="cimDemands">The demand dataset for each period, as CIM SSH datasets
		///   serialized to JSON-LD.</param>
		public static List<CimJsonLdPeriodAndDemands> ToRestDemands(IEnumerable<string> cimDemands)
		{
			var result = new List<CimJsonLdPeriodAndDemands>();

			DateTime start = DateTime.Now;

			foreach (var cim in cimDemands)
			{
				var period = new Pgo.DataContracts.Period
				{
					StartTime = start,
					EndTime = start.AddHours(1),
					Id = $"Period {result.Count + 1}"
				};

				var jsonDemands = new CimJsonLdPeriodAndDemands
				{
					Period = period,
					Demands = JsonConvert.DeserializeObject<JObject>(cim)
				};

				result.Add(jsonDemands);
				start = start.AddHours(1);
			}

			return result;
		}

		/// <summary>
		/// Converts the SSH data in the given parser to single-period PGO demands
		/// </summary>
		public PowerDemands ToPowerDemands(CimJsonParser demandsParser)
		{
			CimDemands cimDemands = ToCimDemands(demandsParser);

			return ToPowerDemands(cimDemands);
		}

		/// <summary>
		/// Converts the SSH data in the given parser to single-period .NET API demands
		/// </summary>
		public CimDemands ToCimDemands(CimJsonParser demandsParser)
		{
			foreach (var consumer in demandsParser.CreatedObjects<IdentifiedObject>())
			{
				demandsParser.FillMridIfMissing(consumer, _networkParser);
			}

			CimDemands cimDemands = CimDemands.FromObjects(demandsParser.CreatedObjects<IdentifiedObject>());
			return cimDemands;
		}

		/// <summary>
		/// Converts .NET API demands to single-period PGO demands
		/// </summary>
		public PowerDemands ToPowerDemands(CimDemands cimDemands)
		{
			var demands = new PowerDemands(_network);
			var busesWithDemand = new List<Bus>();

			if (cimDemands.EnergyConsumers == null)
				throw new ArgumentException("The list of energy consumers is null");
			if (cimDemands.EnergyConsumers.Contains(null))
				throw new ArgumentException("The list of energy consumers contains null");
			if (cimDemands.EquivalentInjections == null)
				throw new ArgumentException("The list of equivalent injections is null");
			if (cimDemands.EquivalentInjections.Contains(null))
				throw new ArgumentException("The list of equivalent injections contains null");

			foreach (var consumer in cimDemands.EnergyConsumers)
			{
				var (p, q) = CheckPower(consumer, consumer.P, consumer.Q);
				AddDemand(consumer, p, q);
			}

			foreach (var injection in cimDemands.EquivalentInjections)
			{
				if (!_network.TryGetBus(injection.MRID, out Bus bus))
					continue;

				var (p, q) = CheckPower(injection, injection.P, injection.Q);

				if (bus.IsConsumer)
					AddDemand(injection, p, q);

				if (bus.IsProvider)
				{
					if (p.Value > 0)
						Complain(injection, "The injection models a provider but consumes positive active power");
				}
			}

			// Complain if not all consumers were given a demand
			if (_network.Consumers.Except(busesWithDemand).FirstOrDefault() is Bus missingBus)
				throw new Exception($"No demand is specified for consumer '{missingBus.Name}'");

			return demands;


			void AddDemand(IdentifiedObject consumer, ActivePower p, ReactivePower q)
			{
				if (p.Value <= 0)
					Complain(consumer, "The active power for a consumer must be positive");

				if (!_network.TryGetBus(consumer.MRID, out Bus bus))
					return;

				if (bus.Type != BusTypes.PowerConsumer)
					Complain(consumer, "This is not a consumer in the network");

				demands.SetPowerDemand(bus, new Complex((double)p.Watts, q.VoltamperesReactive));
				busesWithDemand.Add(bus);
			}

			(ActivePower P, ReactivePower Q) CheckPower(IdentifiedObject consumer, ActivePower? activePower, ReactivePower? reactivePower)
			{
				if (activePower is not ActivePower p)
				{
					Complain(consumer, "No active power given");
					throw new Exception();
				}

				if (reactivePower is not ReactivePower q)
				{
					Complain(consumer, "No reactive power given");
					throw new Exception();
				}

				return (p, q);
			}
		}

		/// <summary>
		/// Converts the given SSH CIM dataset to single-period .NET API demands
		/// </summary>
		private CimDemands ToCimDemands(JObject demands)
		{
			// Read CIM data for period
			var parser = new CimJsonParser(_networkParser.Units);
			parser.Parse(demands);
			parser.CreateCimObjects();

			return ToCimDemands(parser);
		}

		/// <summary>
		/// Throws an exception, reporting the given problem for the given CIM object
		/// </summary>
		private void Complain(IdentifiedObject cimObject, string problem)
		{
			throw new Exception($"{cimObject.Describe()}: {problem}");
		}
	}
}
