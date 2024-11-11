using Sintef.Pgo.Cim;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// A collection of CIM objects that define a power network.
	/// 
	/// A <see cref="CimNetwork"/> contains the CIM objects that are converted into a PGO power network.
	/// The contained objects (e.g. <see cref="ACLineSegment"/>) have names, attributes and associations as
	/// specified by the CIM standard, although only a subset of the standard is used. All documentation in
	/// the contained objects themselves is copied from the CIM standard, and is not specific to their use in PGO. The
	/// rules for how PGO interprets the CIM data is described in the documentation of <see cref="CimNetwork"/> and its properties.
	/// 
	/// General
	///  - All CIM objects must have a unique MRID.
	///  - Associations must be bidirectional. E.g if a <see cref="PowerTransformer"/> contains a number of
	///    <see cref="PowerTransformerEnd"/>s, each of them must refer back to the power transformer.
	///    When PGO deserializes CIM objects from JSON-LD, this is handled automatically. However, 
	///    a client who builds a <see cref="CimNetwork"/> directly must take care of this.
	///  - All <see cref="ConductingEquipment"/> must contain <see cref="Terminal"/>s that are
	///    connected via <see cref="ConnectivityNode"/>s.
	///  - In some situations, PGO uses the base voltage of some <see cref="ConductingEquipment"/>. The base voltage is found as 
	///    follows: 
	///    - If a <see cref="BaseVoltage"/> is specified directly for the conducting equipment, use it.
	///    - Otherwise, look at the eqipment's container. If this is a <see cref="VoltageLevel"/>, use that
	///      level's BaseVoltage. If it is a <see cref="Bay"/>, use the BaseVoltage of the bay's VoltageLevel.
	///      If the container is another type or not specified, PGO emits an error.
	///    - From the identified BaseVoltage, the nominalVoltage is used.
	/// </summary>
	public class CimNetwork
	{
		/// <summary>
		/// The AC line segments
		/// 
		/// PGO creates an internal line for each <see cref="ACLineSegment"/>.
		/// The properties 'r' and 'x' must be given to specify the impedance.
		/// 
		/// If the line segment has any <see cref="OperationalLimitSet"/>s that contains <see cref="CurrentLimit"/>s, these
		/// are used to set the line's maximum current.
		/// </summary>
		public List<ACLineSegment> ACLineSegments { get; set; } = new();

		/// <summary>
		/// The generating units
		/// 
		/// PGO creates a power producer for each <see cref="GeneratingUnit"/> and the
		/// associated <see cref="SynchronousMachine"/>s. The properties are set as follows:
		/// - The produced voltage is set to the value of <see cref="SynchronousMachine"/>'s 'ratedU', or if
		///   that is not set, machine's base voltage. This
		///   value must be common for all machines.
		/// - The min/max active power is set from the generating unit's 'minOperatingP' and 'maxOperatingP'.
		/// - The min/max reactive power is set from the sum of 'minQ'/'maxQ' of the synchronous machines.
		/// </summary>
		public List<GeneratingUnit> GeneratingUnits { get; set; } = new();

		/// <summary>
		/// The power transformers
		/// 
		/// PGO creates a transformer for each <see cref="PowerTransformer"/>. The transformer is assumed to
		/// be lossless and give fixed voltage ratios, which are determined by the ratios between 'ratedU' values of
		/// the <see cref="PowerTransformerEnd"/>s.
		/// </summary>
		public List<PowerTransformer> PowerTransformers { get; set; } = new();

		/// <summary>
		/// The switches
		/// 
		/// PGO creates an internal line for each <see cref="Switch"/>. The line may be controllable
		/// and/or have a breaker function, depending on the type of <see cref="Switch"/>. The defaults
		/// are listed below, but may be overridden by options in <see cref="CimNetworkConversionOptions"/>.
		/// 
		/// The following <see cref="Switch"/> types are considered controllable in PGO, that is, PGO
		/// is allowed to open/close the switch when optimizing the network configuration:
		/// - <see cref="Disconnector"/>
		/// - <see cref="LoadBreakSwitch"/>
		/// - <see cref="Breaker"/>
		/// - <see cref="DisconnectingCircuitBreaker"/>
		/// 
		/// The following <see cref="Switch"/> types are considered to have breaker function in PGO's calculation of
		/// Kile costs:
		/// - <see cref="Fuse"/>
		/// - <see cref="Sectionalizer"/>
		/// - <see cref="Recloser"/>
		/// 
		/// The following <see cref="Switch"/> types are modelled as regular lines, neither a switch nor a breaker:
		/// - <see cref="Jumper"/> 
		/// 
		/// The following <see cref="Switch"/> types are ignored:
		/// - <see cref="GroundDisconnector"/>.
		/// </summary>
		public List<Switch> Switches { get; set; } = new();

		/// <summary>
		/// The energy consumers
		///    
		/// If <see cref="CimNetworkConversionOptions"/> lists 'energyConsumers' in consumerSources,
		/// PGO creates a consumer for each <see cref="EnergyConsumer"/>, except <see cref="StationSupply"/>.
		/// 
		/// The consumer receives
		/// minimum and maximum voltage limits relative to the consumer's base voltage, if this is specified in the
		/// <see cref="CimNetworkConversionOptions"/>.
		/// </summary>
		public List<EnergyConsumer> EnergyConsumers { get; set; } = new();

		/// <summary>
		/// The equivalent injections
		/// 
		/// If <see cref="CimNetworkConversionOptions"/> lists 'equivalentInjections' in consumerSources,
		/// PGO creates a consumer for each <see cref="EquivalentInjection"/> whose minP is positive, zero or not specified.
		/// 
		/// The consumer receives
		/// minimum and maximum voltage limits relative to the consumer's base voltage, if this is specified in the
		/// <see cref="CimNetworkConversionOptions"/>.
		/// 
		/// If <see cref="CimNetworkConversionOptions"/> lists 'equivalentInjections' in providerSources,
		/// PGO creates a provider for each <see cref="EquivalentInjection"/> whose maxP is negative or zero.
		/// 
		/// The provider's capacity is given by the injection's 'minP', 'maxP', 'minQ' and 'maxQ' properties.
		/// The provider's generator voltage is set to the 'ratedU' voltage of the <see cref="PowerTransformerEnd"/> connected
		/// to the same connectivity node. If there is no such transformer end, the
		/// provider's generator voltage is set by finding the base voltage of the equivalent injection.
		/// </summary>
		public List<EquivalentInjection> EquivalentInjections { get; set; } = new();

		/// <summary>
		/// Creates a <see cref="CimNetwork"/> by collecting the relevant CIM objects in <paramref name="cimObjects"/>
		/// </summary>
		public static CimNetwork FromObjects(IEnumerable<IdentifiedObject> cimObjects)
		{
			return new CimNetwork()
			{
				ACLineSegments = cimObjects.OfType<ACLineSegment>().ToList(),
				GeneratingUnits = cimObjects.OfType<GeneratingUnit>().ToList(),
				PowerTransformers = cimObjects.OfType<PowerTransformer>().ToList(),
				Switches = cimObjects.OfType<Switch>().ToList(),
				EnergyConsumers = cimObjects.OfType<EnergyConsumer>().ToList(),
				EquivalentInjections = cimObjects.OfType<EquivalentInjection>().ToList()
			};
		}
	}
}
