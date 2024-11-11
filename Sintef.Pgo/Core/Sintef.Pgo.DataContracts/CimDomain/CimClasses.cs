using System.Collections.Generic;
using UnitsNet;
using System;

using Voltage = UnitsNet.ElectricPotential;
using Resistance = UnitsNet.ElectricResistance;
using Conductance = UnitsNet.ElectricConductance;
using CurrentFlow = UnitsNet.ElectricCurrent;
using ActivePower = UnitsNet.Power;
using ReactivePower = UnitsNet.ReactivePower;
using ApparentPower = UnitsNet.ApparentPower;

using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.Serialization;

namespace Sintef.Pgo.Cim
{
	// This namespace contains classes that represent CIM concepts.
	// CIM is an object model for modelling energy management system. It is
	// described in standard EN 61970-301:2017.
	//
	// Each class in this namespace corresponds to a model type in CIM.
	// Each property in the class corresponds to an attribute or association
	// of the CIM model type, and the name is the same, except possibly for
	// different capitalization or an added 's' to denote plural.
	//
	// This namespace contains only a small subset of all CIM types, and most classes
	// contain only a subset of all properties defined for the respective CIM type.
	// The selection has been made based on what is needed by PGO.
	// You may add other classes and properties if needed.
	//
	// The documentation for each class and property has been copied directly 
	// from the documentation in the CIM standard. In a few cases, comments for
	// clarification have been added.
	//
	// CIM has domain types with electrical units, e.g. Voltage (V/kV/...), CurrentFlow (A/mA/...) etc.
	// We represent such types with types from the "UnitsNet" nuget package, and give them aliases
	// that match the CIM types.

	#region Package Core

	/// <summary>
	/// This is a root class to provide common identification for all classes needing identification and
	/// naming attributes.
	/// </summary>
	public class IdentifiedObject
	{
		/// <summary>
		/// Master resource identifier issued by a model
		/// authority. The mRID is globally unique within an
		/// exchange context. Global uniqueness is easily
		/// achieved by using a UUID, as specified in
		/// RFC 4122, for the mRID. The use of UUID is
		/// strongly recommended.
		/// </summary>
		public string MRID { get; set; }

		/// <summary>
		/// The name is any free human readable and
		/// possibly non unique text naming the object.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The description is a free human readable text
		/// describing or naming the object. It may be non
		/// unique and may not correlate to a naming
		/// hierarchy.
		/// </summary>
		public string Description { get; set; }
	}

	/// <summary>
	/// Defines a system base voltage which is referenced.
	/// </summary>
	public class BaseVoltage : IdentifiedObject
	{
		/// <summary>
		/// The power system resource's base voltage.
		/// </summary>
		public Voltage? NominalVoltage { get; set; }

		/// <summary>
		/// All conducting equipment with this base voltage. Use only when there
		/// is no voltage level container used and only one base voltage applies.
		/// For example, not used for transformers.
		/// </summary>
		public List<ConductingEquipment> ConductingEquipments { get; set; }

		/// <summary>
		/// The voltage levels having this base voltage.
		/// </summary>
		public List<VoltageLevel> VoltageLevels { get; set; }
	}

	/// <summary>
	/// A power system resource can be an item of equipment such as a switch, an equipment
	/// container containing many individual items of equipment such as a substation, or an
	/// organizational entity such as sub-control area. Power system resources can have
	/// measurements associated.
	/// </summary>
	public class PowerSystemResource : IdentifiedObject
	{ }

	/// <summary>
	/// The parts of a power system that are physical devices, electronic or mechanical.
	/// </summary>
	public class Equipment : PowerSystemResource
	{
		/// <summary>
		/// Container of this equipment.
		/// </summary>
		public EquipmentContainer EquipmentContainer { get; set; }

		/// <summary>
		/// The operational limit sets associated with this equipment.
		/// </summary>
		public List<OperationalLimitSet> OperationalLimitSets { get; set; }
	}

	/// <summary>
	/// The parts of the AC power system that are designed to carry current or that are conductively
	/// connected through terminals.
	/// </summary>
	public class ConductingEquipment : Equipment
	{
		/// <summary>
		/// Base voltage of this conducting
		/// equipment. Use only when there is no
		/// voltage level container used and only
		/// one base voltage applies. For example,
		/// not used for transformers.
		/// </summary>
		public BaseVoltage BaseVoltage { get; set; }

		/// <summary>
		/// Conducting equipment have terminals
		/// that may be connected to other
		/// conducting equipment terminals via
		/// connectivity nodes or topological nodes.
		/// </summary>
		public List<Terminal> Terminals { get; set; }
	}

	/// <summary>
	/// An electrical connection point (AC or DC) to a piece of conducting equipment. Terminals are
	/// connected at physical connection points called connectivity nodes.
	/// </summary>
	public class ACDCTerminal : IdentifiedObject
	{
	}

	/// <summary>
	/// An AC electrical connection point to a piece of conducting equipment. Terminals are
	/// connected at physical connection points called connectivity nodes.
	/// </summary>
	public class Terminal : ACDCTerminal
	{
		/// <summary>
		/// The conducting equipment of the
		/// terminal. Conducting equipment have
		/// terminals that may be connected to other
		/// conducting equipment terminals via
		/// connectivity nodes or topological nodes.
		/// </summary>
		public ConductingEquipment ConductingEquipment { get; set; }

		/// <summary>
		/// The connectivity node to which this
		/// terminal connects with zero impedance.
		/// </summary>
		public ConnectivityNode ConnectivityNode { get; set; }

		/// <summary>
		/// All transformer ends connected at this terminal.
		/// </summary>
		public List<TransformerEnd> TransformerEnds { get; set; }

		/// <summary>
		/// The operational limit sets at the terminal.
		/// </summary>
		public List<OperationalLimitSet> OperationalLimitSets { get; set; }
	}

	/// <summary>
	/// Connectivity nodes are points where terminals of AC conducting equipment are connected
	/// together with zero impedance.
	/// </summary>
	public class ConnectivityNode : IdentifiedObject
	{
		/// <summary>
		/// Terminals interconnected with zero
		/// impedance at a this connectivity node.
		/// </summary>
		public List<Terminal> Terminals { get; set; }
	}

	/// <summary>
	/// A base class for all objects that may contain connectivity nodes or topological nodes
	/// </summary>
	public class ConnectivityNodeContainer : PowerSystemResource
	{
		/// <summary>
		/// Connectivity nodes which belong to this connectivity node container.
		/// </summary>
		public List<ConnectivityNode> ConnectivityNodes { get; set; }
	}

	/// <summary>
	/// A modeling construct to provide a root class for containing equipment.
	/// </summary>
	public class EquipmentContainer : ConnectivityNodeContainer
	{
		/// <summary>
		/// Contained equipment.
		/// </summary>
		public List<Equipment> Equipments { get; set; }
	}

	/// <summary>
	/// A collection of equipment for purposes other than generation or utilization, through which
	/// electric energy in bulk is passed for the purposes of switching or modifying its characteristics.
	/// </summary>
	public class Substation : EquipmentContainer
	{
	}

	/// <summary>
	/// A collection of equipment at one common system voltage forming a switchgear. The
	/// equipment typically consist of breakers, busbars, instrumentation, control, regulation and
	/// protection devices as well as assemblies of all these.
	/// </summary>
	public class VoltageLevel : EquipmentContainer
	{
		/// <summary>
		/// The substation of the voltage level.
		/// </summary>
		public Substation Substation { get; set; }

		/// <summary>
		/// The base voltage used for all equipment within the voltage level.
		/// </summary>
		public BaseVoltage BaseVoltage { get; set; }
	}

	/// <summary>
	/// A collection of power system resources (within a given substation) including conducting
	/// equipment, protection relays, measurements, and telemetry. A bay typically represents a
	/// physical grouping related to modularization of equipment.
	/// </summary>
	public class Bay : EquipmentContainer
	{
		/// <summary>
		/// The voltage level containing this bay.
		/// </summary>
		public VoltageLevel VoltageLevel { get; set; }
	}

	#endregion

	#region Package OperationalLimits

	/// <summary>
	/// A value associated with a specific kind of limit.
	/// 
	/// The sub class value attribute shall be positive.
	/// 
	/// The sub class value attribute is inversely proportional to
	/// OperationalLimitType.acceptableDuration (acceptableDuration for short). A pair of value_x
	/// and acceptableDuration_x are related to each other as follows:
	/// if value_1 > value_2 > value_3 > ..., then
	/// acceptableDuration_1 &lt; acceptableDuration_2 &lt; acceptableDuration_3 &lt; ...
	/// 
	/// A value_x with direction="high" shall be greater than a value_y with direction="low".
	/// </summary>
	public class OperationalLimit : IdentifiedObject
	{
		/// <summary>
		/// The limit set to which the limit values belong.
		/// </summary>
		public OperationalLimitSet OperationalLimitSet { get; set; }

		/// <summary>
		/// The limit type associated with this limit.
		/// </summary>
		public OperationalLimitType OperationalLimitType { get; set; }
	}

	/// <summary>
	/// Operational limit on current.
	/// </summary>
	public class CurrentLimit : OperationalLimit
	{
		/// <summary>
		/// Limit on current flow. The attribute shall be a positive value or zero.
		/// </summary>
		public CurrentFlow? Value { get; set; }

		/// <summary>
		/// The normal value for limit on current flow. The attribute shall be a positive value or zero.
		/// </summary>
		public CurrentFlow? NormalValue { get; set; }
	}

	/// <summary>
	/// Operational limit applied to voltage.
	/// </summary>
	public class VoltageLimit : OperationalLimit
	{
		/// <summary>
		/// Limit on voltage. High or low limit nature of the
		/// limit depends upon the properties of the
		/// operational limit type.
		/// The attribute shall be a positive value or zero.
		/// </summary>
		public Voltage? Value { get; set; }

		/// <summary>
		/// The normal limit on voltage. High or low limit nature of the
		/// limit depends upon the properties of the operational limit type. 
		/// The attribute shall be a positive value or zero.
		/// </summary>
		public Voltage? NormalValue { get; set; }
	}

	/// <summary>
	/// Limit on active power flow.
	/// </summary>
	public class ActivePowerLimit : OperationalLimit
	{
		/// <summary>
		/// Value of active power limit. The attribute shall be a positive value or zero.
		/// </summary>
		public ActivePower? Value { get; set; }

		/// <summary>
		/// The normal value of active power limit. The attribute shall be a positive value or zero.
		/// </summary>
		public ActivePower? NormalValue { get; set; }
	}

	/// <summary>
	/// Apparent power limit.
	/// </summary>
	public class ApparentPowerLimit : OperationalLimit
	{
		/// <summary>
		/// The apparent power limit. The attribute shall be a positive value or zero.
		/// </summary>
		public ApparentPower? Value { get; set; }

		/// <summary>
		/// The normal apparent power limit. The attribute shall be a positive value or zero.
		/// </summary>
		public ApparentPower? NormalValue { get; set; }
	}

	/// <summary>
	/// The operational meaning of a category of limits.
	/// </summary>
	public class OperationalLimitType : IdentifiedObject
	{
		/// <summary>
		/// The nominal acceptable duration of the limit.
		/// Limits are commonly expressed in terms of the
		/// time limit for which the limit is normally
		/// acceptable. The actual acceptable duration of a
		/// specific limit may depend on other local factors
		/// such as temperature or wind speed.
		/// 
		/// Unit: seconds
		/// </summary>
		public double? AcceptableDuration { get; set; }

		/// <summary>
		/// The direction of the limit.
		/// </summary>
		public OperationalLimitDirectionKind? Direction { get; set; }

		/// <summary>
		/// The operational limits associated with this type of limit.
		/// </summary>
		public List<OperationalLimit> OperationalLimits { get; set; }
	}

	/// <summary>
	/// The direction attribute describes the side of a limit that is a violation.
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public enum OperationalLimitDirectionKind
	{
		/// <summary>
		/// The default value, not specified in the data
		/// </summary>
		[EnumMember(Value = "notSpecified")]
		NotSpecified,

		/// <summary>
		/// High means that a monitored value above the
		/// limit value is a violation. If applied to a terminal
		/// flow, the positive direction is into the terminal.
		/// </summary>
		[EnumMember(Value = "high")]
		High,

		/// <summary>
		/// Low means a monitored value below the limit is
		/// a violation. If applied to a terminal flow, the
		/// positive direction is into the terminal.
		/// </summary>
		[EnumMember(Value = "low")]
		Low,

		/// <summary>
		/// An absoluteValue limit means that a monitored
		/// absolute value above the limit value is a violation.
		/// </summary>
		[EnumMember(Value = "absoluteValue")]
		AbsoluteValue
	}

	/// <summary>
	/// A set of limits associated with equipment. Sets of limits might apply to a specific temperature,
	/// or season for example. A set of limits may contain different severities of limit levels that would
	/// apply to the same equipment. The set may contain limits of different types such as apparent
	/// power and current limits or high and low voltage limits that are logically applied together as a
	/// set.
	/// </summary>
	public class OperationalLimitSet : IdentifiedObject
	{
		/// <summary>
		/// The terminal where the operational limit set apply.
		/// </summary>
		public ACDCTerminal Terminal { get; set; }

		/// <summary>
		/// The equipment to which the limit set applies.
		/// </summary>
		public Equipment Equipment { get; set; }

		/// <summary>
		/// Values of equipment limits.
		/// </summary>
		public List<OperationalLimit> OperationalLimits { get; set; }
	}

	#endregion

	#region Package Wires

	#region Switches

	/// <summary>
	/// A generic device designed to close, or open, or both, one or more electric circuits. All
	/// switches are two terminal devices including grounding switches.
	/// </summary>
	public class Switch : ConductingEquipment
	{
		/// <summary>
		/// The maximum continuous current carrying
		/// capacity in amps governed by the device
		/// material and construction.
		/// </summary>
		public CurrentFlow? RatedCurrent { get; set; }

		/// <summary>
		/// The attribute tells if the switch is considered	open when used as input to topology processing.
		/// </summary>
		public bool? Open { get; set; }
	}

	/// <summary>
	/// An overcurrent protective device with a circuit opening fusible part that is heated and severed
	/// by the passage of overcurrent through it. A fuse is considered a switching device because it
	/// breaks current.
	/// </summary>
	public class Fuse : Switch
	{
	}

	/// <summary>
	/// A short section of conductor with negligible impedance which can be manually removed and
	/// replaced if the circuit is de-energized. Note that zero-impedance branches can potentially be
	/// modeled by other equipment types.
	/// </summary>
	public class Jumper : Switch
	{
	}

	/// <summary>
	/// Automatic switch that will lock open to isolate a faulted section. It may, or may not, have load
	/// breaking capability. Its primary purpose is to provide fault sectionalising at locations where
	/// the fault current is either too high, or too low, for proper coordination of fuses.
	/// </summary>
	public class Sectionalizer : Switch
	{
	}

	/// <summary>
	/// A manually operated or motor operated mechanical switching device used for changing the
	/// connections in a circuit, or for isolating a circuit or equipment from a source of power. It is
	/// required to open or close circuits when negligible current is broken or made.
	/// </summary>
	public class Disconnector : Switch
	{
	}

	/// <summary>
	/// A manually operated or motor operated mechanical switching device used for isolating a
	/// circuit or equipment from ground.
	/// </summary>
	public class GroundDisconnector : Switch
	{
	}

	/// <summary>
	/// A ProtectedSwitch is a switching device that can be operated by ProtectionEquipment.
	/// </summary>
	public class ProtectedSwitch : Switch
	{
	}

	/// <summary>
	/// A mechanical switching device capable of making, carrying, and breaking currents under
	/// normal operating conditions.
	/// </summary>
	public class LoadBreakSwitch : ProtectedSwitch
	{
	}

	/// <summary>
	/// A mechanical switching device capable of making, carrying, and breaking currents under
	/// normal circuit conditions and also making, carrying for a specified time, and breaking currents
	/// under specified abnormal circuit conditions e.g.those of short circuit.
	/// </summary>
	public class Breaker : ProtectedSwitch
	{
	}

	/// <summary>
	/// A circuit breaking device including disconnecting function, 
	/// eliminating the need for separate disconnectors.
	/// </summary>
	public class DisconnectingCircuitBreaker : Breaker
	{
	}

	/// <summary>
	/// Pole-mounted fault interrupter with built-in phase and ground relays, current transformer (CT),
	/// and supplemental controls.
	/// </summary>
	public class Recloser : ProtectedSwitch
	{
	}

	#endregion

	/// <summary>
	/// Combination of conducting material with consistent electrical characteristics, building a single
	/// electrical system, used to carry current between points in the power system.
	/// </summary>
	public class Conductor : ConductingEquipment
	{
		/// <summary>
		/// Segment length for calculating line section
		/// capabilities
		/// </summary>
		public Length? Length { get; set; }
	}

	/// <summary>
	/// A wire or combination of wires, with consistent electrical characteristics, building a single
	/// electrical system, used to carry alternating current between points in the power system.
	/// 
	/// For symmetrical, transposed three phase lines, it is sufficient to use attributes of the line
	/// segment, which describe impedances and admittances for the entire length of the segment.
	/// Additionally impedances can be computed by using length and associated per length
	/// impedances.
	/// 
	/// The BaseVoltage at the two ends of ACLineSegments in a Line shall have the same
	/// BaseVoltage.nominalVoltage. However, boundary lines may have slightly different
	/// BaseVoltage.nominalVoltages and variation is allowed. Larger voltage difference in general
	/// requires use of an equivalent branch.
	/// </summary>
	public class ACLineSegment : Conductor
	{
		/// <summary>
		/// Positive sequence series resistance of the entire
		/// line section.
		/// </summary>
		public Resistance? R { get; set; }

		/// <summary>
		/// Positive sequence series reactance of the entire
		/// line section.
		/// 
		/// CIM type is Reactance: imaginary part of impedance, at rated frequency, unit Ohm
		/// </summary>
		public Resistance? X { get; set; }

		/// <summary>
		/// Positive sequence shunt (charging)
		/// conductance, uniformly distributed, of the entire
		/// line section.
		/// </summary>
		public Conductance? Gch { get; set; }

		/// <summary>
		/// Positive sequence shunt (charging)
		/// susceptance, uniformly distributed, of the entire
		/// line section. This value represents the full
		/// charging over the full length of the line.
		/// 
		/// CIM type is Susceptance: Imaginary part of admittance, unit Siemens
		/// We assume that this is at rated frequency.
		/// </summary>
		public Conductance? Bch { get; set; }
	}

	/// <summary>
	/// Generic user of energy – a point of consumption on the power system model.
	/// </summary>
	public class EnergyConsumer : ConductingEquipment
	{
		/// <summary>
		/// Number of individual customers represented by
		/// this demand.
		/// </summary>
		public int? CustomerCount { get; set; }

		/// <summary>
		/// Active power of the load. Load sign convention is used, i.e. positive sign means flow out from a node.
		/// 
		/// For voltage dependent loads the value is at rated voltage.
		/// 
		/// Starting value for a steady state solution.
		/// </summary>
		public ActivePower? P { get; set; }

		/// <summary>
		/// Reactive power of the load. Load sign convention is used, i.e. positive sign means flow out from a node.
		/// 
		/// For voltage dependent loads the value is at rated voltage.
		/// 
		/// Starting value for a steady state solution.
		/// </summary>
		public ReactivePower? Q { get; set; }
	}

	/// <summary>
	/// A type of conducting equipment that can regulate a quantity (i.e. voltage or flow) at a specific
	/// point in the network.
	/// </summary>
	public class RegulatingCondEq : ConductingEquipment
	{
	}

	/// <summary>
	/// A rotating machine which may be used as a generator or motor.
	/// </summary>
	public class RotatingMachine : RegulatingCondEq
	{
		/// <summary>
		/// A synchronous machine may operate as
		/// a generator and as such becomes a
		/// member of a generating unit.
		/// </summary>
		public GeneratingUnit GeneratingUnit { get; set; }

		/// <summary>
		/// Rated voltage (nameplate data, Ur in IEC 60909-0). 
		/// It is primarily used for short circuit data exchange according to IEC 60909.
		/// </summary>
		public Voltage? RatedU { get; set; }
	}

	/// <summary>
	/// An electromechanical device that operates with shaft rotating synchronously with the network.
	/// It is a single machine operating either as a generator or synchronous condenser or pump.
	/// </summary>
	public class SynchronousMachine : RotatingMachine
	{
		/// <summary>
		/// Maximum reactive power limit. This is the
		/// maximum(nameplate) limit for the unit.
		/// </summary>
		public ReactivePower? MaxQ { get; set; }

		/// <summary>
		/// Maximum voltage limit for the unit.
		/// </summary>
		public Voltage? MaxU { get; set; }

		/// <summary>
		/// Minimum voltage limit for the unit.
		/// </summary>
		public Voltage? MinU { get; set; }

		/// <summary>
		/// Minimum reactive power limit for the unit.
		/// </summary>
		public ReactivePower? MinQ { get; set; }

		/// <summary>
		/// Part of the coordinated reactive control that
		/// comes from this machine. The attribute is used
		/// as a participation factor not necessarily summing
		/// up to 100% for the devices participating in the
		/// control.
		/// </summary>
		public double? QPercent { get; set; }

		/// <summary>
		/// Equivalent resistance (RG) of generator. RG is
		/// considered for the calculation of all currents,
		/// except for the calculation of the peak current ip.
		/// Used for short circuit data exchange according
		/// to IEC 60909
		/// </summary>
		public Resistance? R { get; set; }

		/// <summary>
		/// Priority of unit for use as powerflow voltage
		/// phase angle reference bus selection. 0 = don't
		/// care (default) 1 = highest priority. 2 is less than
		/// 1 and so on.
		/// </summary>
		public int? ReferencePriority { get; set; }

		/// <summary>
		/// Modes that this synchronous machine can operate in.
		/// </summary>
		public SynchronousMachineKind? Type { get; set; }
	}

	/// <summary>
	/// Synchronous machine type.
	/// </summary>
	[Flags]
#pragma warning disable CS1591
	[JsonConverter(typeof(StringEnumConverter))]
	public enum SynchronousMachineKind
	{
		/// <summary>
		/// Indicates the synchronous machine can operate as a generator.
		/// </summary>
		[EnumMember(Value = "generator")]
		Generator = 1,

		/// <summary>
		/// Indicates the synchronous machine can operate as a condenser.
		/// </summary>
		[EnumMember(Value = "condenser")]
		Condenser = 2,

		/// <summary>
		/// Indicates the synchronous machine can operate as a generator or as a condenser.
		/// </summary>
		[EnumMember(Value = "generatorOrCondenser")]
		GeneratorOrCondenser = Generator | Condenser,

		/// <summary>
		/// Indicates the synchronous machine can operate as a motor.
		/// </summary>
		[EnumMember(Value = "motor")]
		Motor = 4,

		/// <summary>
		/// Indicates the synchronous machine can operate as a generator or as a motor.
		/// </summary>
		[EnumMember(Value = "generatorOrMotor")]
		GeneratorOrMotor = Generator | Motor,

		/// <summary>
		/// Indicates the synchronous machine can operate as a generator or as a condenser or as a motor.
		/// </summary>
		[EnumMember(Value = "motorOrCondenser")]
		MotorOrCondenser = Condenser | Motor,

		/// <summary>
		/// Indicates the synchronous machine can operate as a generator or as a condenser or as a motor.
		/// </summary>
		[EnumMember(Value = "generatorOrCondenserOrMotor")]
		GeneratorOrCondenserOrMotor = Generator | Condenser | Motor,
	}
#pragma warning restore CS1591

	/// <summary>
	/// An electrical device consisting of two or more coupled windings, with or without a magnetic
	/// core, for introducing mutual coupling between electric circuits. Transformers can be used to
	/// control voltage and phase shift (active power flow).
	/// 
	/// A power transformer may be composed of separate transformer tanks that need not be
	/// identical.
	/// 
	/// A power transformer can be modeled with or without tanks and is intended for use in both
	/// balanced and unbalanced representations. A power transformer typically has two terminals,
	/// but may have one (grounding), three or more terminals.
	/// 
	/// The inherited association ConductingEquipment.BaseVoltage should not be used. The
	/// association from TransformerEnd to BaseVoltage should be used instead.
	/// </summary>
	public class PowerTransformer : ConductingEquipment
	{
		/// <summary>
		/// The ends of this power transformer.
		/// </summary>
		public List<PowerTransformerEnd> PowerTransformerEnds { get; set; }
	}

	/// <summary>
	/// A conducting connection point of a power transformer. It corresponds to a physical
	/// transformer winding terminal. In earlier CIM versions, the TransformerWinding class served a
	/// similar purpose, but this class is more flexible because it associates to terminal but is not a
	/// specialization of ConductingEquipment.
	/// </summary>
	public class TransformerEnd : IdentifiedObject
	{
		/// <summary>
		/// Number for this transformer end, corresponding
		/// to the end's order in the power transformer
		/// vector group or phase angle clock number.
		/// Highest voltage winding should be 1. Each end
		/// within a power transformer should have a unique
		/// subsequent end number. Note the transformer
		/// end number need not match the terminal
		/// sequence number.
		/// </summary>
		public int? EndNumber { get; set; }

		/// <summary>
		/// Base voltage of the transformer end.
		/// This is essential for PU calculation.
		/// </summary>
		public BaseVoltage BaseVoltage { get; set; }

		/// <summary>
		/// Terminal of the power transformer to
		/// which this transformer end belongs.
		/// </summary>
		public Terminal Terminal { get; set; }
	}

	/// <summary>
	/// A PowerTransformerEnd is associated with each Terminal of a PowerTransformer.
	/// 
	/// The impedance values r, r0, x, and x0 of a PowerTransformerEnd represent a star equivalent
	/// as follows.
	/// 
	/// 1) For a two Terminal PowerTransformer the high voltage (TransformerEnd.endNumber=1)
	/// PowerTransformerEnd has non zero values on r, r0, x, and x0 while the low voltage
	/// (TransformerEnd.endNumber=0) PowerTransformerEnd has zero values for r, r0, x, and
	/// x0.
	/// 
	/// 2) For a three Terminal PowerTransformer the three PowerTransformerEnds represent a star
	/// equivalent with each leg in the star represented by r, r0, x, and x0 values.
	/// 
	/// 3) For a three Terminal transformer each PowerTransformerEnd shall have g, g0, b and b0
	/// values corresponding to the no load losses distributed on the three
	/// PowerTransformerEnds. The total no load loss shunt impedances may also be placed at
	/// one of the PowerTransformerEnds, preferably the end numbered 1, having the shunt
	/// values on end 1 is the preferred way.
	/// 
	/// 4) For a PowerTransformer with more than three Terminals the PowerTransformerEnd
	/// impedance values cannot be used. Instead use the TransformerMeshImpedance or split
	/// the transformer into multiple PowerTransformers.
	/// </summary>
	public class PowerTransformerEnd : TransformerEnd
	{
		/// <summary>
		/// The power transformer of this power transformer end.
		/// </summary>
		public PowerTransformer PowerTransformer { get; set; }

		/// <summary>
		/// Rated voltage (nameplate data, Ur in IEC 60909-0). 
		/// It is primarily used for short circuit data exchange according to IEC 60909.
		/// </summary>
		public Voltage? RatedU { get; set; }
	}

	/// <summary>
	/// A tunable impedance device normally used to offset line charging during single line faults in
	/// an ungrounded section of network.
	/// </summary>
	public class PetersenCoil : ConductingEquipment
	{
		// We do not model these and probably will not, as they only have a role during faults.
	}

	/// <summary>
	/// A conductor, or group of conductors, with negligible impedance, that serve to connect other
	/// conducting equipment within a single substation.
	/// 
	/// Voltage measurements are typically obtained from VoltageTransformers that are connected to
	/// busbar sections. A busbar section may have many physical terminals but for analysis is
	/// modeled with exactly one logical terminal.
	/// </summary>
	public class BusbarSection : ConductingEquipment
	{
		// We do not model these. With only one terminal, they do not connect other equipment,
		// and we don't really understand what they are for.
	}

	/// <summary>
	/// The class represents equivalent objects that are the result of a network reduction. The class
	/// is the base for equivalent objects of different types.
	/// </summary>
	public class EquivalentEquipment : ConductingEquipment
	{ 
	}

	/// <summary>
	/// This class represents equivalent injections (generation or load). Voltage regulation is allowed
	/// only at the point of connection.
	/// </summary>
	public class EquivalentInjection : EquivalentEquipment
	{
		/// <summary>
		/// Minimum active power of the injection.
		/// </summary>
		public ActivePower? minP { get; set; }

		/// <summary>
		/// Maximum active power of the injection.
		/// </summary>
		public ActivePower? maxP { get; set; }

		/// <summary>
		/// Used for modeling of infeed for load flow exchange. Not used for short circuit modeling. If
		/// maxQ and minQ are not used ReactiveCapabilityCurve can be used.
		/// </summary>
		public ReactivePower? minQ { get; set; }

		/// <summary>
		/// Used for modeling of infeed for load flow exchange. Not used for short circuit modeling. If
		/// maxQ and minQ are not used ReactiveCapabilityCurve can be used.
		/// </summary>
		public ReactivePower? maxQ { get; set; }

		/// <summary>
		/// Equivalent active power injection. Load sign
		/// convention is used, i.e. positive sign means flow
		/// out from a node.
		/// Starting value for steady state solutions.
		/// </summary>
		public ActivePower? P { get; set; }

		/// <summary>
		/// Equivalent reactive power injection. Load sign
		/// convention is used, i.e. positive sign means flow
		/// out from a node.
		/// Starting value for steady state solutions.
		/// </summary>
		public ReactivePower? Q { get; set; }
	}

	/// <summary>
	/// A shunt capacitor or reactor or switchable bank of shunt capacitors or reactors. A section of a
	/// shunt compensator is an individual capacitor or reactor. A negative value for
	/// reactivePerSection indicates that the compensator is a reactor. ShuntCompensator is a single
	/// terminal device. Ground is implied.
	/// </summary>
	public class ShuntCompensator : RegulatingCondEq
	{
		// We don't model these, as they are not supported by PGO at the moment.
		// We should, though.
	}

	/// <summary>
	/// A linear shunt compensator has banks or sections with equal admittance values
	/// </summary>
	public class LinearShuntCompensator : ShuntCompensator
	{
	}

	/// <summary>
	/// A non linear shunt compensator has bank or section admittance values that differ.
	/// </summary>
	public class NonlinearShuntCompensator : ShuntCompensator
	{
	}

	#endregion

	#region Package Production

	/// <summary>
	/// A single or set of synchronous machines for converting mechanical power into alternating-current
	/// power. For example, individual machines within a set may be defined for scheduling
	/// purposes while a single control signal is derived for the set. In this case there would be a
	/// GeneratingUnit for each member of the set and an additional GeneratingUnit corresponding to
	/// the set.
	/// </summary>
	public class GeneratingUnit : Equipment
	{
		/// <summary>
		/// Maximum high economic active power limit, that
		/// should not exceed the maximum operating active
		/// power limit.
		/// </summary>
		public ActivePower? MaxEconomicP { get; set; }

		/// <summary>
		/// This is the maximum operating active power limit
		/// the dispatcher can enter for this unit.
		/// </summary>
		public ActivePower? MaxOperatingP { get; set; }

		/// <summary>
		/// Low economic active power limit that shall be
		/// greater than or equal to the minimum operating
		/// active power limit.
		/// </summary>
		public ActivePower? MinEconomicP { get; set; }

		/// <summary>
		/// This is the minimum operating active power limit
		/// the dispatcher can enter for this unit.
		/// </summary>
		public ActivePower? MinOperatingP { get; set; }

		/// <summary>
		/// The nominal power of the generating unit. Used
		/// to give precise meaning to percentage based
		/// attributes such as the governor speed change
		/// droop (governorSCD attribute).
		/// The attribute shall be a positive value equal to or
		/// less than RotatingMachine.ratedS.
		/// </summary>
		public ActivePower? NominalP { get; set; }

		/// <summary>
		/// A synchronous machine may operate as
		/// a generator and as such becomes a
		/// member of a generating unit.
		/// </summary>
		public List<RotatingMachine> RotatingMachines { get; set; }
	}

	#endregion

	#region Package LoadModel

	/// <summary>
	/// ConformLoad represent loads that follow a daily load change pattern where the pattern can be
	/// used to scale the load with a system load.
	/// </summary>
	public class ConformLoad : EnergyConsumer
	{
	}

	/// <summary>
	/// NonConformLoad represent loads that do not follow a daily load change pattern and changes
	/// are not correlated with the daily load change pattern.
	/// </summary>
	public class NonConformLoad : EnergyConsumer
	{
	}

	/// <summary>
	/// Station supply with load derived from the station output.
	/// </summary>
	public class StationSupply : EnergyConsumer
	{
	}

	#endregion
}
