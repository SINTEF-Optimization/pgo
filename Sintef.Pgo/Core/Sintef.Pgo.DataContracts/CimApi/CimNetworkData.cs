using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Sintef.Pgo.Cim;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// Data for creating a network from CIM data on JSON-LD format
	/// </summary>
	public class CimJsonLdNetworkData
	{
		/// <summary>
		/// The CIM objects defining the network.
		/// 
		/// This is a JSON-LD graph object containing a collection of CIM objects. 
		/// For information on which CIM objects to include and how they are used in the PGO internal model,
		/// see the documentation of <see cref="CimNetwork"/>.
		/// </summary>
		[JsonRequired]
		public JObject Network { get; set; }

		/// <summary>
		/// The options to use when parsing CIM objects from JSON-LD.
		/// 
		/// The units profile specified here is also used for all other input/output of JSON-LD
		/// data for this network.
		/// </summary>
		[JsonRequired]
		public CimParsingOptions ParsingOptions { get; set; } = new();

		/// <summary>
		/// The options to use when converting CIM objects into a PGO network
		/// </summary>
		[JsonRequired]
		public CimNetworkConversionOptions ConversionOptions { get; set; } = new();
	}

	/// <summary>
	/// Data for creating a network from CIM objects
	/// </summary>
	public class CimNetworkData
	{
		/// <summary>
		/// The CIM objects defining the network.
		/// </summary>
		[JsonRequired]
		public CimNetwork Network { get; set; }

		/// <summary>
		/// The options to use when converting CIM objects into a PGO network
		/// </summary>
		[JsonRequired]
		public CimNetworkConversionOptions ConversionOptions { get; set; } = new();
	}

	/// <summary>
	/// Options for how to parse CIM objects from JSON-LD
	/// </summary>
	public class CimParsingOptions
	{
		/// <summary>
		/// The units profile to use when reading physical properties of CIM objects
		/// </summary>
		[JsonRequired]
		public CimUnitsProfile? UnitsProfile { get; set; }
	}

	/// <summary>
	/// A profile for which units are used to express physical values in a CIM JSON-LD dataset.
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public enum CimUnitsProfile
	{
		/// <summary>
		/// Digin units are used. 
		///  - Active power is expressed in MW.
		///  - Reactive power is expressed in MVAr
		///  - Voltage is expressed in kV
		///  - All other values are expressed in SI units, including m, A and Ohm.
		/// </summary>
		[EnumMember(Value = "digin")]
		Digin,

		/// <summary>
		/// All values are expressed using SI units.
		/// </summary>
		[EnumMember(Value = "si")]
		Si
	}

	/// <summary>
	/// Options for converting CIM objects into a PGO network
	/// </summary>
	public class CimNetworkConversionOptions
	{
		/// <summary>
		/// If given, each consumer is given a minimum voltage limit
		/// that is the consumer's base voltage times this factor.
		/// </summary>
		[DefaultValue(null)]
		public double? ConsumerMinVoltageFactor { get; set; }

		/// <summary>
		/// If given, each consumer is given a maximum voltage limit
		/// that is the consumer's base voltage times this factor.
		/// </summary>
		[DefaultValue(null)]
		public double? ConsumerMaxVoltageFactor { get; set; }

		/// <summary>
		/// The sources to look for operational limits in, in prioritized sequence.
		/// The default is ["value", "normalValue"].
		/// </summary>
		public List<CimOperationalLimitSource> OperationalLimitSources { get; set; }
			= new() { CimOperationalLimitSource.Value, CimOperationalLimitSource.NormalValue };

		/// <summary>
		/// If given, all line impedances are multiplied by this value. This may be used to 
		/// compensate for errors in the CIM dataset or for testing.
		/// </summary>
		[DefaultValue(null)]
		public double? LineImpedanceScaleFactor { get; set; }

		/// <summary>
		/// The sources from which PGO creates providers.
		/// The default is ["generatingUnits", "equivalentInjections"].
		/// </summary>
		public List<CimProviderSource> ProviderSources { get; set; }
			= new() { CimProviderSource.GeneratingUnits, CimProviderSource.EquivalentInjections };

		/// <summary>
		/// The sources from which PGO creates consumers.
		/// The default is ["energyConsumers", "equivalentInjections"].
		/// </summary>
		public List<CimConsumerSource> ConsumerSources { get; set; }
			= new() { CimConsumerSource.EnergyConsumers, CimConsumerSource.EquivalentInjections };

		/// <summary>
		/// Lists the types of switches that should be considered controllable in PGO, that is, PGO
		/// is allowed to open/close the switch when optimizing the network configuration.
		/// The default is ["disconnector", "loadBreakSwitch", "breaker", "disconnectingCircuitBreaker"]
		/// </summary>
		public List<CimSwitchType> ControllableSwitchTypes { get; set; } = new()
		{
			CimSwitchType.Disconnector,
			CimSwitchType.LoadBreakSwitch,
			CimSwitchType.Breaker,
			CimSwitchType.DisconnectingCircuitBreaker
		};

		/// <summary>
		/// Lists the types of switches that should be considered to have breaker function in PGO's calculation of
		/// Kile costs.
		/// The default is ["fuse", "sectionalizer", "recloser"]
		/// </summary>
		public List<CimSwitchType> BreakingSwitchTypes { get; set; } = new()
		{
			CimSwitchType.Fuse,
			CimSwitchType.Sectionalizer,
			CimSwitchType.Recloser
		};
	}

	/// <summary>
	/// A type of CIM switch
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public enum CimSwitchType
	{
		/// <summary>
		/// A <see cref="Cim.Disconnector"/>
		/// </summary>
		[EnumMember(Value = "disconnector")]
		Disconnector,

		/// <summary>
		/// A <see cref="Cim.LoadBreakSwitch"/>
		/// </summary>
		[EnumMember(Value = "loadBreakSwitch")]
		LoadBreakSwitch,

		/// <summary>
		/// A <see cref="Cim.Breaker"/>
		/// </summary>
		[EnumMember(Value = "breaker")]
		Breaker,

		/// <summary>
		/// A <see cref="Cim.DisconnectingCircuitBreaker"/>
		/// </summary>
		[EnumMember(Value = "disconnectingCircuitBreaker")]
		DisconnectingCircuitBreaker,

		/// <summary>
		/// A <see cref="Cim.Fuse"/>
		/// </summary>
		[EnumMember(Value = "fuse")]
		Fuse,

		/// <summary>
		/// A <see cref="Cim.Sectionalizer"/>
		/// </summary>
		[EnumMember(Value = "sectionalizer")]
		Sectionalizer,

		/// <summary>
		/// A <see cref="Cim.Recloser"/>
		/// </summary>
		[EnumMember(Value = "recloser")]
		Recloser,

		/// <summary>
		/// A <see cref="Cim.Jumper"/>
		/// </summary>
		[EnumMember(Value = "jumper")]
		Jumper
	}

	/// <summary>
	/// A source for an operational limit, e.g. current limits.
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public enum CimOperationalLimitSource
	{
		/// <summary>
		/// The 'value' attribute of the operational limit
		/// </summary>
		[EnumMember(Value = "value")]
		Value,

		/// <summary>
		/// The 'normalValue' attribute of the operational limit.
		/// </summary>
		[EnumMember(Value = "normalValue")]
		NormalValue
	}

	/// <summary>
	/// A source for data from which PGO can create consumers.
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public enum CimConsumerSource
	{
		/// <summary>
		/// PGO creates a consumer for each <see cref="EnergyConsumer"/>, except <see cref="StationSupply"/>
		/// </summary>
		[EnumMember(Value = "energyConsumers")]
		EnergyConsumers,

		/// <summary>
		/// PGO creates a consumer for each <see cref="EquivalentInjection"/> whose minP is positive, zero or not specified
		/// </summary>
		[EnumMember(Value = "equivalentInjections")]
		EquivalentInjections
	}

	/// <summary>
	/// A source for data from which PGO can create providers.
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public enum CimProviderSource
	{
		/// <summary>
		/// PGO creates a provider for each <see cref="GeneratingUnit"/>
		/// </summary>
		[EnumMember(Value = "generatingUnits")]
		GeneratingUnits,

		/// <summary>
		/// PGO creates a provider for each <see cref="EquivalentInjection"/> whose maxP is negative or zero
		/// </summary>
		[EnumMember(Value = "equivalentInjections")]
		EquivalentInjections
	}
}