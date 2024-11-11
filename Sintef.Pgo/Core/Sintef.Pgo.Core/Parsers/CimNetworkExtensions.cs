using Sintef.Pgo.Cim;
using Sintef.Pgo.DataContracts;
using Sintef.Scoop.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace Sintef.Pgo.Core.IO
{
	// Ensure nulls are ignored in Select() and SelectMany() below
	using Sintef.Scoop.Linq.SelectUnlessNull;

	/// <summary>
	/// Extension methods for <see cref="CimNetwork"/>
	/// </summary>
	internal static class CimNetworkExtensions
	{
		/// <summary>
		/// Enumerates all <see cref="Equipment"/> in the network
		/// </summary>
		public static IEnumerable<Equipment> Equipment(this CimNetwork network)
		{
			return network.ConductingEquipment().Cast<Equipment>()
				.Concat(network.GeneratingUnits);
		}

		/// <summary>
		/// Enumerates all <see cref="EquipmentContainer"/>s in the network
		/// </summary>
		public static IEnumerable<EquipmentContainer> EquipmentContainers(this CimNetwork network)
		{
			return network.Equipment()
				.Select(e => e.EquipmentContainer)
				.Distinct();
		}

		/// <summary>
		/// Enumerates all <see cref="ConductingEquipment"/> in the network
		/// </summary>
		public static IEnumerable<ConductingEquipment> ConductingEquipment(this CimNetwork network)
		{
			return network.ACLineSegments.Cast<ConductingEquipment>()
				.Concat(network.GeneratingUnits.SelectMany(g => g.RotatingMachines))
				.Concat(network.PowerTransformers)
				.Concat(network.Switches)
				.Concat(network.EnergyConsumers)
				.Concat(network.EquivalentInjections);
		}

		/// <summary>
		/// Enumerates all <see cref="Terminal"/>s in the network
		/// </summary>
		public static IEnumerable<Terminal> Terminals(this CimNetwork network)
			=> network.ConductingEquipment()
				.SelectMany(e => e.Terminals);

		/// <summary>
		/// Enumerates all <see cref="ConnectivityNode"/>s in the network
		/// </summary>
		public static IEnumerable<ConnectivityNode> ConnectivityNodes(this CimNetwork network)
			=> network.Terminals()
				.Select(t => t.ConnectivityNode)
				.Distinct();

		/// <summary>
		/// Enumerates all <see cref="EquivalentInjection"/>s in the network that represent providers
		/// </summary>
		public static IEnumerable<EquivalentInjection> ProviderEquivalentInjections(this CimNetwork network)
			=> network.EquivalentInjections
				.Where(e => IsProvider(e));

		/// <summary>
		/// Enumerates all <see cref="EquivalentInjection"/>s in the network that represent consumers
		/// </summary>
		public static IEnumerable<EquivalentInjection> ConsumerEquivalentInjections(this CimNetwork network)
			=> network.EquivalentInjections
				.Where(e => !IsProvider(e));

		/// <summary>
		/// Enumerates all <see cref="VoltageLevel"/>s in the network
		/// </summary>
		public static IEnumerable<VoltageLevel> VoltageLevels(this CimNetwork network)
			=> network.EquipmentContainers()
				.OfType<VoltageLevel>();

		/// <summary>
		/// Enumerates all <see cref="BaseVoltage"/>s in the network
		/// </summary>
		public static IEnumerable<BaseVoltage> BaseVoltages(this CimNetwork network)
			=> network.ConductingEquipment().Select(x => x.BaseVoltage)
				.Concat(network.PowerTransformers.SelectMany(t => t.PowerTransformerEnds).Select(e => e.BaseVoltage))
				.Concat(network.VoltageLevels().Select(x => x.BaseVoltage))
				.Distinct();

		/// <summary>
		/// Enumerates all <see cref="IdentifiedObject"/>s in the network
		/// </summary>
		public static IEnumerable<IdentifiedObject> IdentifiedObjects(this CimNetwork network)
			=> network.Equipment().Cast<IdentifiedObject>()
				.Concat(network.BaseVoltages())
				.Concat(network.EquipmentContainers())
				.Concat(network.ConductingEquipment().SelectMany(e => e.Terminals))
				.Concat(network.ConnectivityNodes())
				.Concat(network.PowerTransformers.SelectMany(t => t.PowerTransformerEnds));

		/// <summary>
		/// Returns true if the given <see cref="EquivalentInjection"/> represents a provider, false if it
		/// represents a consumer (or is badly defined).
		/// </summary>
		private static bool IsProvider(EquivalentInjection e)
		{
			return e.maxP?.Value <= 0;
		}
	}
}
