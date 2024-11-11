using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// Represents a power grid as a collection of nodes (buses), lines (which may be switchable)
	/// and transformers.
	/// </summary>
	public class PowerGrid
	{
		/// <summary>
		/// The name of the network.
		/// </summary>
		[JsonProperty(PropertyName = "name")]
		public string Name { get; set; }

		/// <summary>
		/// The version number of the PGO network file format.
		/// You should always set it to 1, which is the current version.
		/// </summary>
		[JsonProperty(PropertyName = "@schema_version")]
		[DefaultValue(-1)]
		public int SchemaVersion { get; set; }

		/// <summary>
		/// The nodes in the network.
		/// </summary>
		[JsonProperty(PropertyName = "nodes"), JsonRequired]
		public List<Node> Nodes { get; set; }

		/// <summary>
		/// The lines in the network.
		/// </summary>
		[JsonProperty(PropertyName = "lines"), JsonRequired]
		public List<Line> Lines { get; set; }

		/// <summary>
		/// The transformers in the network.
		/// </summary>
		[JsonProperty(PropertyName = "transformers")]
		public List<Transformer> Transformers { get; set; }
	}

	/// <summary>
	/// Results of checking a network's connectivity.
	/// </summary>
	public class NetworkConnectivityStatus
	{
		/// <summary>
		/// True if no network connectivity error was detected.
		/// If false, at least one of the error type fields will be non-empty.
		/// </summary>
		[JsonProperty(PropertyName = "ok")]
		public bool Ok { get; set; }

		/// <summary>
		/// If not null, contains the line IDs of an unbreakable cycle in the network.
		/// This means that the network has no radial configuration, since it contains one or more cycles that cannot be broken.
		/// A cycle is unbreakable if none of its lines are switchable.
		/// A an unbreakable path between two providers also counts as a cycle.
		/// </summary>
		[JsonProperty(PropertyName = "unbreakable_cycle")]
		public List<string> UnbreakableCycle { get; set; }

		/// <summary>
		/// If not null, contains the IDs of nodes that cannot be connected to any provider.
		/// This is not an error in itself.
		/// The network can still be used for optimization, but only if all demands for
		/// consumers in this list are set to zero.
		/// </summary>
		[JsonProperty(PropertyName = "unconnectable_buses")]
		public List<string> UnconnectableNodes { get; set; }

		/// <summary>
		/// If not null, it was not possible to find a network configuration where all transformers
		/// are used consistently with their specified input/output directions on terminals.
		/// If so, this property contains the IDs of an example set of transformers that could not be
		/// used consistently with their modes while keeping a radial configuration, and without 
		/// misconfiguring other transformers.
		/// </summary>
		[JsonProperty(PropertyName = "invalid_transformers")]
		public List<string> InvalidTransformers { get; set; }

		/// <summary>
		/// Returns a text description of the connectivity status.
		/// </summary>
		public override string ToString()
		{
			string result = Ok ? $"The connectivity checks out OK\r\n" : "The network has connectivity problems:\r\n";
			if (!Ok)
			{
				if (UnbreakableCycle != null)
				{
					result += $"\r\nUnbreakable cycle (or path between two providers) found through the following lines:\r\n";
					UnbreakableCycle.Do(l => result += $"\t{l}\r\n");
				}
				if (UnconnectableNodes != null)
				{
					result += $"\r\nThe following nodes cannot possibly be connected with any provider:\r\n";
					UnconnectableNodes.Do(b => result += $"\t{b}\r\n");
				}
				if (InvalidTransformers != null)
				{
					result += $"\r\nSome transformers had such mode definitions that they cannot be " +
						$"used consistently while keeping a radial configuration:\r\n" +
						$"{InvalidTransformers.Join(", ")}\r\n";
				}
			}
			return result;
		}
	}

	/// <summary>
	/// A way that a transformer may operate.
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public enum TransformerOperation
	{
		/// <summary>
		/// The transformer converts voltages using a fixed ratio.
		/// </summary>
		[EnumMember(Value = "fixed_ratio")]
		FixedRatio,

		/// <summary>
		/// The transformer will always provide the expected secondary voltage (as long as the voltage on the primary side respects the corresponding
		/// node voltage limits).
		/// </summary>
		[EnumMember(Value = "automatic")]
		Automatic
	}

	/// <summary>
	/// A type of node (bus).
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public enum NodeType
	{
		/// <summary>
		/// A connection node, neither a consumer nor a provider.
		/// </summary>
		[EnumMember(Value = "transition")]
		Connection,

		/// <summary>
		/// A provider node.
		/// </summary>
		[EnumMember(Value = "provider")]
		PowerProvider,

		/// <summary>
		/// Consumer node.
		/// </summary>
		[EnumMember(Value = "consumer")]
		PowerConsumer
	}

	/// <summary>
	/// A consumer category/type.
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public enum ConsumerCategory
	{
		/// <summary>
		/// 'Jordbruk'
		/// </summary>
		[EnumMember(Value = "agriculture")]
		Agriculture,

		/// <summary>
		/// 'Husholdning'
		/// </summary>
		[EnumMember(Value = "household")]
		Household,

		/// <summary>
		/// 'Industri'
		/// </summary>
		[EnumMember(Value = "industry")]
		Industry,

		/// <summary>
		/// 'Handel og tjenester'
		/// </summary>
		[EnumMember(Value = "services")]
		Services,

		/// <summary>
		/// 'Offentlig virksomhet'
		/// </summary>
		[EnumMember(Value = "public")]
		Public,

		/// <summary>
		/// 'Industri med eldrevne prosesser'
		/// </summary>
		[EnumMember(Value = "elind")]
		ElectricIndustry,

		/// <summary>
		/// Default value, not to be used. 
		/// </summary>
		[EnumMember(Value = "undefined")]
		Undefined
	}


	/// <summary>
	/// A consumer category and a fraction.
	/// Used to express that a consumer can represent an aggregate of consumers
	/// of different categories.
	/// </summary>
	public class ConsumerTypeFracton
	{
		/// <summary>
		/// The consumer category.
		/// </summary>
		[DefaultValue(null)]//To avoid using default values even if serializer settings are set to Ignore.
		[JsonProperty(PropertyName = "consumer_type")]
		public ConsumerCategory ConsumerType { get; set; }

		/// <summary>
		/// The fraction of the consumers (aggregated) demand that is from consumers of the given category.
		/// The sum of power_fraction's over all consumer categories in the aggregate must sum to 1.
		/// </summary>
		[JsonProperty(PropertyName = "power_fraction")]
		public double PowerFraction { get; set; }
	}

	/// <summary>
	/// A node in the power grid.
	/// </summary>
	public class Node
	{
		/// <summary>
		/// The node's unique ID.
		/// </summary>
		[JsonProperty(PropertyName = "id"), JsonRequired]
		public string Id { get; set; }

		/// <summary>
		/// The node's type.
		/// </summary>
		[JsonProperty(PropertyName = "type"), JsonRequired]
		public NodeType Type { get; set; }

		/// <summary>
		/// The type of consumer that the node represents.
		/// This is used to compute reliability
		/// measures, such as "cost of energy not delivered", and is only
		/// used if the node type is consumer.
		/// If the consumer actually represents an aggregate of consumers of different types, use 
		/// the property "consumer_type_fractions" instead.
		/// </summary>
		[DefaultValue("undefined")]
		[JsonProperty(PropertyName = "consumer_type", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
		public ConsumerCategory ConsumerType { get; set; } = ConsumerCategory.Undefined;

		/// <summary>
		/// Fractions of consumer types for aggregate ("composite") type consumers. This is used to compute reliability
		/// measures, such as "cost of energy not delivered". Should be given only for consumers, and
		/// only if consumer_type is not given or has the value "undefined".
		/// For each consumer type,
		/// a fraction of the total demand must be given. These power fractions must sum up to one.
		/// </summary>
		[DefaultValue(null)]
		[JsonProperty(PropertyName = "consumer_type_fractions")]
		public List<ConsumerTypeFracton> ConsumerTypeFractions { get; set; }

		/// <summary>
		/// Minimum voltage in kV. This is only used for non-provider nodes.  Default value is 0.
		/// </summary>
		[JsonProperty(PropertyName = "v_min")]
		public double MinimumVoltage { get; set; }

		/// <summary>
		/// Maximum voltage in kV. This is only used for non-provider nodes. Default value is infinity.
		/// </summary>
		[DefaultValue(double.PositiveInfinity)]
		[JsonProperty(PropertyName = "v_max")]
		public double MaximumVoltage { get; set; } = double.PositiveInfinity;

		/// <summary>
		/// Reference voltage in the generator, given in kV. Default value is 0. Used only for providers.
		/// </summary>
		[DefaultValue(0.0)]
		[JsonProperty(PropertyName = "v_gen")]
		public double GeneratorVoltage { get; set; }

		/// <summary>
		/// Max active generation in MW. Default value is infinity. Used only for providers.
		/// </summary>
		[JsonProperty(PropertyName = "p_gen_max")]
		[DefaultValue(double.PositiveInfinity)]
		public double MaximumActiveGeneration { get; set; } = double.PositiveInfinity;

		/// <summary>
		/// Min active generation in MW. Default value is 0. Used only for providers.
		/// </summary>
		[JsonProperty(PropertyName = "p_gen_min")]
		[DefaultValue(0.0)]
		public double MinimumActiveGeneration { get; set; }

		/// <summary>
		/// Max reactive generation in MW. Default value is infinity. Used only for providers.
		/// </summary>
		[JsonProperty(PropertyName = "q_gen_max")]
		[DefaultValue(double.PositiveInfinity)]
		public double MaximumReactiveGeneration { get; set; } = double.PositiveInfinity;

		/// <summary>
		/// Min reactive generation in MW. Default value is 0. Used only for providers.
		/// </summary>
		[JsonProperty(PropertyName = "q_gen_min")]
		[DefaultValue(0.0)]
		public double MinimumReactiveGeneration { get; set; }

		/// <summary>
		/// Coordinates, used for visualization. Optional. Should be a two-element array [x,y]. The coordinates are not assumed to have any graphical meaning.
		/// </summary>
		[JsonProperty(PropertyName = "coordinates")]
		public List<double> Location;

	}

	/// <summary>
	/// A line in the power grid. The line may optionally be switchable.
	/// It is also possible to provide reliability information.
	/// </summary>
	public class Line
	{
		/// <summary>
		/// The line's unique ID.
		/// </summary>
		[DefaultValue("")]
		[JsonProperty(PropertyName = "id")]
		[JsonRequired]
		public string Id { get; set; }

		/// <summary>
		/// The ID of the node at the first endpoint of the line (arbitrarily called the source).
		/// </summary>
		[JsonRequired]
		[JsonProperty(PropertyName = "source")]
		public string SourceNode { get; set; }

		/// <summary>
		/// The ID of the node at the second endpoint of the line (arbitrarily called the target).
		/// </summary>
		[JsonRequired]
		[JsonProperty(PropertyName = "target")]
		public string TargetNode { get; set; }

		/// <summary>
		/// The resistance of the line, given in Ω. Default value is 0.
		/// </summary>
		[JsonProperty(PropertyName = "r")]
		[DefaultValue(0.0)]
		public double Resistance { get; set; }

		/// <summary>
		/// The reactance of the line, given in Ω. Default value is 0.
		/// </summary>
		[JsonProperty(PropertyName = "x")]
		[DefaultValue(0.0)]
		public double Reactance { get; set; }

		/// <summary>
		/// Maximum current on the line, given in A. Default value is infinity.
		/// </summary>
		[DefaultValue(double.PositiveInfinity)]
		[JsonProperty(PropertyName = "imax")]
		public double CurrentLimit { get; set; } = double.PositiveInfinity;

		/// <summary>
		/// Maximum voltage allowed on this line, given in kV. Default value is infinity.
		/// </summary>
		[DefaultValue(double.PositiveInfinity)]
		[JsonProperty(PropertyName = "vmax")]
		public double VoltageLimit { get; set; } = double.PositiveInfinity;

		/// <summary>
		/// Indicates whether the line is switchable, i.e. PGO chooses whether it should be open
		/// or closed as part of optimization.
		/// </summary>
		[DefaultValue(false)]
		[JsonProperty(PropertyName = "switchable")]
		public bool IsSwitchable { get; set; }

		/// <summary>
		/// If the line is switchable, the associated switching cost. Omit otherwise.
		/// </summary>
		[JsonProperty(PropertyName = "switching_cost")]
		public double? SwitchingCost { get; set; }

		/// <summary>
		/// Indicates whether the line acts as a breaker (i.e. a breaker is on the line). Optional; used in reliability measure.
		/// </summary>
		[DefaultValue(false)]
		[JsonProperty(PropertyName = "breaker")]
		public bool IsBreaker;

		/// <summary>
		/// Fault frequency of the line per year. Optional; used in reliability measure.
		/// </summary>
		[JsonProperty(PropertyName = "fault_frequency", DefaultValueHandling = DefaultValueHandling.Populate)]
		public double? FaultsPerYear { get; set; }

		/// <summary>
		/// Time needed to sectionalize the line in case of a fault. Optional; used in reliability measure.
		/// </summary>
		[JsonProperty(PropertyName = "sectioning_time"), JsonConverter(typeof(TimeSpanConverter))]
		public TimeSpan? SectioningTime { get; set; }

		/// <summary>
		/// Time needed to repair the line in case of a fault (after sectionalizing). Used in reliability measure.
		/// </summary>
		[JsonProperty(PropertyName = "repair_time"), JsonConverter(typeof(TimeSpanConverter))]
		public TimeSpan? RepairTime { get; set; }
	}

	/// <summary>
	/// A transformer in the power grid.
	/// </summary>
	public class Transformer
	{
		/// <summary>
		/// The tranformer's unique ID.
		/// </summary>
		[DefaultValue("")]
		[JsonProperty(PropertyName = "id")]
		[JsonRequired]
		public string Id { get; set; }

		/// <summary>
		/// The connections of the transformer to nodes in the grid.
		/// This list must contain 2 or 3 elements referring to distinct nodes.
		/// </summary>
		[JsonProperty(PropertyName = "connections"), JsonRequired]
		public List<TransformerConnection> Connections = new List<TransformerConnection>();

		/// <summary>
		/// The operating modes of the transformer.
		/// </summary>
		[JsonProperty(PropertyName = "modes"), JsonRequired]
		public List<TransformerMode> Modes = new List<TransformerMode>();

		/// <summary>
		/// The location of the center of the transformer. Used for visualization. Optional. 
		/// Should be a two-element array [x,y]. The coordinates are not assumed to have any graphical meaning.
		/// </summary>
		[JsonProperty(PropertyName = "coordinates")]
		public List<double> CenterLocation;
	}

	/// <summary>
	/// Identifies a node to which the transformer is connected and the expected voltage at the node.
	/// </summary>
	public class TransformerConnection
	{
		/// <summary>
		/// The ID of the node at which the connection is made.
		/// </summary>
		[JsonProperty(PropertyName = "node_id"), JsonRequired]
		public string NodeId;

		/// <summary>
		/// The magnitude of the expected voltage at this end of the transformer. Given in kV.
		/// </summary>
		[JsonProperty(PropertyName = "end_voltage"), JsonRequired]
		public double Voltage;
	}

	/// <summary>
	/// A transformer mode specifies a pair of nodes that the transformer can convert between
	/// and the related parameters.
	/// Note that the direction indicated by the choice of "source" and "target" defines
	/// the direction for which the "ratio" is defined. If the mode does not include `bidirectional`
	/// set to false, it is assumed that the transformer can also be used in the other direction,
	/// with the inverse ratio.
	/// </summary>
	public class TransformerMode
	{
		/// <summary>
		/// The ID of the node that is the source (primary/input/upstream) in this mode.
		/// </summary>
		[JsonProperty(PropertyName = "source"), JsonRequired]
		public string Source;

		/// <summary>
		/// The ID of the node that is the target (secondary/output/downstream) in this mode.
		/// </summary>
		[JsonProperty(PropertyName = "target"), JsonRequired]
		public string Target;

		/// <summary>
		/// Specifies how the transformer is operated in this mode.
		/// </summary>
		[JsonProperty(PropertyName = "operation"), JsonRequired]
		public TransformerOperation Operation;

		/// <summary>
		/// The voltage (or winding) ratio of the transformer mode. Should be (at least nearly) equal to the ratio 
		/// of expected voltages, V_in / V_out, on the corresponding connections. 
		/// Use only if fixed operation is specified.
		/// </summary>
		[JsonProperty(PropertyName = "ratio")]
		public double? Ratio;

		/// <summary>
		/// The mode's power factor, which is a measure of power loss. The output power is assumed to equal PowerFactor * Input Power.
		/// Consequently, the tranformer loss (active and reactive) is (1 - PowerFactor) * Input Power. 
		/// If not given, 1.0 is used (i.e. the transformer is lossless).
		/// </summary>
		[DefaultValue(1.0)]
		[JsonProperty(PropertyName = "power_factor")]
		public double PowerFactor = 1.0;

		/// <summary>
		/// If false, this mode can only be used when the source node is upstream from the target node.
		/// If true, this mode can be used with power flowing in any of the directions. 
		/// Defaults to true if omitted.
		/// </summary>
		[DefaultValue(true)]
		[JsonProperty(PropertyName = "bidirectional")]
		public bool Bidirectional = true;
	}

	/// <summary>
	/// A forecast of consumer demands (loads) in the power network 
	/// over one or more time periods.
	/// </summary>
	public class Demand
	{
		/// <summary>
		/// The version number of the PGO demands file format.
		/// You should always set it to 1, which is the current version.
		/// </summary>
		[JsonProperty(PropertyName = "@schema_version")]
		[DefaultValue(-1)]
		public int SchemaVersion { get; set; }

		/// <summary>
		/// The periods for which the loads are given.
		/// </summary>
		[JsonProperty(PropertyName = "periods")]
		[JsonRequired]
		public List<Period> Periods { get; set; }

		/// <summary>
		/// Loads for the consumers. If loads are not given for a consumer, a default
		/// value of zero load is used.
		/// When loads are given for a consumer, they must be given for each period in <see cref="Periods"/>,
		/// with both active and reactive load.
		/// </summary>
		[JsonProperty(PropertyName = "loads")]
		[JsonRequired]
		public List<LoadSeries> Loads { get; set; }
	}

	/// <summary>
	/// Describes a period for which the problem should be solved.
	/// </summary>
	public class Period
	{
		/// <summary>
		/// The unique ID of the period.
		/// </summary>
		[JsonProperty(PropertyName = "id")]
		[JsonRequired]
		public string Id { get; set; }

		/// <summary>
		/// Start time of the period.
		/// </summary>
		[JsonProperty(PropertyName = "start_time")]
		[JsonRequired]
		public DateTime StartTime { get; set; }

		/// <summary>
		/// End time of the period.
		/// </summary>
		[JsonProperty(PropertyName = "end_time")]
		[JsonRequired]
		public DateTime EndTime { get; set; }
	}

	/// <summary>
	/// The loads for a given consumer over time.
	/// </summary>
	public class LoadSeries
	{
		/// <summary>
		/// The ID of the consumer node that this load series is for.
		/// </summary>
		[JsonProperty(PropertyName = "node_id")]
		[JsonRequired]
		public string NodeId { get; set; }

		/// <summary>
		/// Active loads per period, in MW. Same order as the <see cref="Period"/>s are listed in the <see cref="Demand"/>.
		/// </summary>
		[JsonProperty(PropertyName = "p_loads")]
		[JsonRequired]
		public List<double> ActiveLoads { get; set; }

		/// <summary>
		/// Reactive loads per period, in MW. Same order as the <see cref="Period"/>s are listed in the <see cref="Demand"/>.
		/// </summary>
		[JsonProperty(PropertyName = "q_loads")]
		[JsonRequired]
		public List<double> ReactiveLoads { get; set; }
	}

	/// <summary>
	/// An open/closed state for a switchable line.
	/// </summary>
	public class SwitchState
	{
		/// <summary>
		/// The ID of the switchable line.
		/// </summary>
		[JsonProperty(PropertyName = "line_id")]
		[JsonRequired]
		public string Id { get; set; }

		/// <summary>
		/// true if the switch is open, false if it is closed.
		/// </summary>
		[JsonProperty(PropertyName = "open")]
		[JsonRequired]
		public bool Open { get; set; }
	}

	/// <summary>
	/// Settings defining the configuration of the network in a single period.
	/// </summary>
	public class SinglePeriodSettings
	{
		/// <summary>
		/// The switch settings.
		/// </summary>
		[JsonProperty(PropertyName = "switch_settings")]
		[JsonRequired]
		public List<SwitchState> SwitchSettings { get; set; }

		/// <summary>
		/// The ID of the period that the settings apply to. 
		/// Not used if the settings represent a start configuration.
		/// </summary>
		[JsonProperty(PropertyName = "period")]
		public string Period { get; set; }

		/// <summary>
		/// Constructor
		/// </summary>
		public SinglePeriodSettings()
		{
			SwitchSettings = new List<SwitchState>();
		}
	}

	/// <summary>
	/// A solution to the network configuration optimization problem.
	/// The solution is defined by switch settings for each period in the problem.
	/// 
	/// A solution returned from PGO also contains power flow and KILE cost information for each
	/// period. When giving a solution to PGO, these parts of the solution do not need to be specified.
	/// </summary>
	public class Solution
	{
		/// <summary>
		/// The single-period switch settings making up the solution.
		/// </summary>
		[JsonProperty(PropertyName = "period_solutions")]
		[JsonRequired]
		public List<SinglePeriodSettings> PeriodSettings { get; set; }

		/// <summary>
		/// The flows (i.e. power injections, currents and voltages), for each single period solution.
		/// Only periods where the solution has a radial configuration, are included.
		/// This property is ignored in input solutions given to the service.
		/// </summary>
		[JsonProperty(PropertyName = "flows")]
		public List<PowerFlow> Flows { get; set; }

		/// <summary>
		/// The KILE costs, for each single period solution.
		/// Only periods where the solution has a radial configuration, are included.
		/// This property is ignored in input solutions given to the service.
		/// </summary>
		[JsonProperty(PropertyName = "kile_costs")]
		public List<KileCosts> KileCosts { get; set; }

		/// <summary>
		/// Constructor
		/// </summary>
		public Solution()
		{
			PeriodSettings = new List<SinglePeriodSettings>();
			Flows = new List<PowerFlow>();
			KileCosts = new List<KileCosts>();
		}
	}

	/// <summary>
	/// Status for the result of a flow calculation.
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public enum FlowStatus
	{
		/// <summary>
		/// A flow calculation was attempted, but failed. The flow that was produced may contain very
		/// inconsistent values. This code is used e.g. if IteratedDistFlow diverges.
		/// </summary>
		[EnumMember(Value = "failed")]
		Failed,

		/// <summary>
		/// The flow is approximate.
		/// </summary>
		[EnumMember(Value = "approximate")]
		Approximate,

		/// <summary>
		/// The flow is exact (up to numerical tolerances)
		/// </summary>
		[EnumMember(Value = "exact")]
		Exact,
	}

	/// <summary>
	/// KILE costs for a single period.
	/// </summary>
	public class KileCosts
	{
		/// <summary>
		/// The ID of the period.
		/// </summary>
		[JsonProperty(PropertyName = "period_id")]
		public string PeriodId { get; set; }

		/// <summary>
		/// The expected KILE cost in each consumer node.
		/// </summary>
		[JsonProperty(PropertyName = "expected_costs")]
		public Dictionary<string, double> ExpectedCosts;

		/// <summary>
		/// Constructor
		/// </summary>
		public KileCosts(string periodId)
		{
			PeriodId = periodId;
			ExpectedCosts = new Dictionary<string, double>();
		}
	}

	/// <summary>
	/// Electrical quantities for a single period.
	/// </summary>
	public class PowerFlow
	{
		/// <summary>
		/// The ID of the period.
		/// </summary>
		[JsonProperty(PropertyName = "period_id")]
		public string PeriodId { get; set; }

		/// <summary>
		/// Status for the result of the flow calculation.
		/// The field has one of the following values:
		/// 
		/// - `exact` if the load flow computation converged and is correct.
		/// - `approxmiate` if the load flow computation was aborted before converging, and is therefore only an approximation.
		/// -	`failed` if the load flow computation failed to converge.
		/// 
		/// The last two values indicate that the flow may be imprecise, and not in full accordance with the actual physical laws.
		/// </summary>
		[JsonProperty(PropertyName = "status")]
		public FlowStatus Status { get; set; }

		/// <summary>
		/// A description of the flow's status.
		/// </summary>
		[JsonProperty(PropertyName = "status_details")]
		public string StatusDetails { get; set; }

		/// <summary>
		/// The voltage level in each node. Given in kV.
		/// </summary>
		[JsonProperty(PropertyName = "voltages")]
		public Dictionary<string, double> Voltages;

		/// <summary>
		/// The outgoing currents from various nodes. Keyed by originating node, 
		/// and for each a list of targets with the current injected into the line is provided. Given in kA.
		/// </summary>
		[JsonProperty(PropertyName = "currents")]
		public Dictionary<string, List<OutgoingCurrent>> Currents;

		/// <summary>
		/// The power injected into various lines. Keyed by originating node,
		/// and for each node a list of targets with the power injected into the line is provided. Given in MW.
		/// </summary>
		[JsonProperty(PropertyName = "injected_power")]
		public Dictionary<string, List<OutgoingPower>> Powers;

		/// <summary>
		/// Constructor
		/// </summary>
		public PowerFlow(string periodId)
		{
			PeriodId = periodId;
			Voltages = new Dictionary<string, double>();
			Currents = new Dictionary<string, List<OutgoingCurrent>>();
			Powers = new Dictionary<string, List<OutgoingPower>>();
		}
	}

	/// <summary>
	/// A current with a target node.
	/// </summary>
	public class OutgoingCurrent
	{
		/// <summary>
		/// The ID of the target node, which the current flows toward (when it has positive sign).
		/// </summary>
		[JsonProperty(PropertyName = "target")]
		public string Target;

		/// <summary>
		/// The ID of the line carrying the current.
		/// </summary>
		[JsonProperty(PropertyName = "line_id")]
		public string LineId;

		/// <summary>
		/// The magnitude of the current in the line (in kA), in the direction toward the target.
		/// </summary>
		[JsonProperty(PropertyName = "current")]
		public double Current;
	}

	/// <summary>
	/// Active and reactive power with a target.
	/// </summary>
	public class OutgoingPower
	{
		/// <summary>
		/// The ID of the target node.
		/// </summary>
		[JsonProperty(PropertyName = "target")]
		public string Target;

		/// <summary>
		/// The ID of the line carrying the power.
		/// </summary>
		[JsonProperty(PropertyName = "line_id")]
		public string LineId;

		/// <summary>
		/// The active power flowing towards the target (in MW).
		/// </summary>
		[JsonProperty(PropertyName = "active")]
		public double ActivePower;

		/// <summary>
		/// The reactive power flowing towards the target (in MVAr).
		/// </summary>
		[JsonProperty(PropertyName = "reactive")]
		public double ReactivePower;

		/// <summary>
		/// Thermal power loss in the line (in MW).
		/// </summary>
		[JsonProperty(PropertyName = "thermal_loss")]
		public double PowerLoss;
	}
}