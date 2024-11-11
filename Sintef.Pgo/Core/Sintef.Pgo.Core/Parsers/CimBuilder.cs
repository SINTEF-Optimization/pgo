using System;
using System.Collections.Generic;

using Voltage = UnitsNet.ElectricPotential;
using Resistance = UnitsNet.ElectricResistance;
using Conductance = UnitsNet.ElectricConductance;
using CurrentFlow = UnitsNet.ElectricCurrent;
using ActivePower = UnitsNet.Power;
using ReactivePower = UnitsNet.ReactivePower;
using ApparentPower = UnitsNet.ApparentPower;

using System.Reflection;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Cim
{
	/// <summary>
	/// A builder for creating a CIM objects. The builder acts as a bag of
	/// CIM objects, but also allows extracting the sets of objects that define 
	/// a <see cref="CimNetwork"/>, <see cref="CimDemands"/> etc.
	/// </summary>
	public class CimBuilder
	{
		/// <summary>
		/// The network defined by the created CIM objects.
		/// The network is cleared every time a new CIM object is created and regenerated when requested.
		/// </summary>
		public CimNetwork Network
		{
			get
			{
				if (_network is null)
					_network = CimNetwork.FromObjects(CreatedObjects);

				return _network;
			}
		}

		/// <summary>
		/// The demands defined by the created CIM objects.
		/// The demands is cleared every time a new CIM object is created and regenerated when requested.
		/// </summary>
		public CimDemands Demands
		{
			get
			{
				if (_demands is null)
					_demands = CimDemands.FromObjects(CreatedObjects);

				return _demands;
			}
		}

		/// <summary>
		/// The configuration defined by the created CIM objects.
		/// The configuration is cleared every time a new CIM object is created and regenerated when requested.
		/// </summary>
		public CimConfiguration Configuration
		{
			get
			{
				if (_configuration is null)
					_configuration = CimConfiguration.FromObjects(CreatedObjects);

				return _configuration;
			}
		}

		/// <summary>
		/// The single period solution defined by the created CIM objects.
		/// The single period solution is cleared every time a new CIM object is created and regenerated when requested.
		/// </summary>
		public CimPeriodSolution PeriodSolution
		{
			get
			{
				if (_periodSolution is null)
					_periodSolution = CimPeriodSolution.FromObjects(CreatedObjects);

				return _periodSolution;
			}
		}


		/// <summary>
		/// Enumerates all <see cref="IdentifiedObject"/>s that this builder has created
		/// </summary>
		public IEnumerable<IdentifiedObject> CreatedObjects => _createdObjects;

		/// <summary>
		/// The MRID of the latest cim object to be created
		/// </summary>
		private string _lastMrid;

		/// <summary>
		/// Collects all cim objects that have been created
		/// </summary>
		private List<IdentifiedObject> _createdObjects = new();

		/// <summary>
		/// The network defined by the created CIM objects.
		/// The network is cleared every time a new CIM object is created and regenerated when requested.
		/// </summary>
		private CimNetwork _network = null;

		/// <summary>
		/// The demands defined by the created CIM objects.
		/// The demands is cleared every time a new CIM object is created and regenerated when requested.
		/// </summary>
		private CimDemands _demands = null;

		/// <summary>
		/// The configuration defined by the created CIM objects.
		/// The configuration is cleared every time a new CIM object is created and regenerated when requested.
		/// </summary>
		private CimConfiguration _configuration = null;

		/// <summary>
		/// The single period solution defined by the created CIM objects.
		/// The single period solution is cleared every time a new CIM object is created and regenerated when requested.
		/// </summary>
		private CimPeriodSolution _periodSolution = null;

		/// <summary>
		/// Initializes the builder
		/// </summary>
		public CimBuilder()
		{
		}

		/// <summary>
		/// Adds a base voltage
		/// </summary>
		/// <param name="name">The name and MRID of the new object.</param>
		/// <param name="nominalVoltageVolts">The nominal voltage, in volts</param>
		public BaseVoltage AddBaseVoltage(string name, int nominalVoltageVolts)
		{
			var baseVoltage = Create<BaseVoltage>(name);
			baseVoltage.NominalVoltage = Voltage.FromVolts(nominalVoltageVolts);

			return baseVoltage;
		}

		/// <summary>
		/// Adds a voltage level
		/// </summary>
		/// <param name="name">The name and MRID of the new object.</param>
		/// <param name="baseVoltage">The level's base voltage</param>
		public VoltageLevel AddVoltageLevel(string name, BaseVoltage baseVoltage)
		{
			var voltageLevel = Create<VoltageLevel>(name);
			Associate(voltageLevel, baseVoltage);

			return voltageLevel;
		}

		/// <summary>
		/// Adds a generating unit
		/// </summary>
		/// <param name="name">The name and MRID of the new object.</param>
		/// <param name="minPWatt">The generator's minimum operating active power, in Watts</param>
		/// <param name="maxPWatt">The generator's maximum operating active power, in Watts</param>
		/// <returns></returns>
		public GeneratingUnit AddGeneratingUnit(string name, double minPWatt, double maxPWatt)
		{
			var generatingUnit = Create<GeneratingUnit>(name);
			Network.GeneratingUnits.Add(generatingUnit);

			generatingUnit.MinOperatingP = ActivePower.FromWatts(minPWatt);
			generatingUnit.MaxOperatingP = ActivePower.FromWatts(maxPWatt);

			return generatingUnit;
		}

		/// <summary>
		/// Adds a synchronous machine with an associated terminal.
		/// </summary>
		/// <param name="name">The name and MRID of the new object.</param>
		/// <param name="generatingUnit">The generating unit the machine is part of</param>
		/// <param name="minQVar">The machine's minimum operating reactive power, in VAR</param>
		/// <param name="maxQVar">The machine's maximum operating reactive power, in VAR</param>
		/// <param name="ratedUVolts">The machine's rated voltage, in Volts</param>
		/// <param name="connectTo">If given, the machine's terminal is connected with this terminal</param>
		public SynchronousMachine AddSynchronousMachine(string name, GeneratingUnit generatingUnit, double minQVar, double maxQVar,
			double? ratedUVolts, Terminal connectTo = null)
		{
			var machine = Create<SynchronousMachine>(name, generatingUnit);
			machine.MinQ = ReactivePower.FromVoltamperesReactive(minQVar);
			machine.MaxQ = ReactivePower.FromVoltamperesReactive(maxQVar);
			machine.Type = SynchronousMachineKind.Generator;
			
			machine.RatedU = Voltage.FromVolts(ratedUVolts.Value);
			AddTerminal(machine, connectTo);

			return machine;
		}

		/// <summary>
		/// Adds an energy consumer with an associated terminal.
		/// </summary>
		/// <param name="name">The name and MRID of the new object.</param>
		/// <param name="connectTo">If given, the consumer's terminal is connected with this terminal</param>
		/// <param name="activeDemandWatt">The consumer's active power demand, in Watts</param>
		/// <param name="reactiveDemandWatt">The consumer's reactive power demand, in Watts</param>
		public EnergyConsumer AddConsumer(string name, Terminal connectTo = null,
			double? activeDemandWatt = null, double? reactiveDemandWatt = null)
		{
			var consumer = Create<EnergyConsumer>(name);
			Network.EnergyConsumers.Add(consumer);
			AddTerminal(consumer, connectTo);

			if (activeDemandWatt.HasValue)
				consumer.P = ActivePower.FromWatts(activeDemandWatt.Value);
			if (reactiveDemandWatt.HasValue)
				consumer.Q = ReactivePower.FromVoltamperesReactive(reactiveDemandWatt.Value);

			return consumer;
		}

		/// <summary>
		/// Adds a power transformer.
		/// </summary>
		/// <param name="name">The name and MRID of the new object.</param>
		public PowerTransformer AddTransformer(string name)
		{
			var transformer = Create<PowerTransformer>(name);
			Network.PowerTransformers.Add(transformer);

			return transformer;
		}

		/// <summary>
		/// Adds a power tranformer end with an associated terminal.
		/// </summary>
		/// <param name="transformer">The transformer the end belongs to</param>
		/// <param name="name">The name and MRID of the new object.</param>
		/// <param name="voltageVolts">The rated voltage of the transformer end, in Volts</param>
		/// <param name="connectTo">If given, the transformer end's terminal is connected with this terminal</param>
		public PowerTransformerEnd AddEnd(PowerTransformer transformer, string name, double voltageVolts, Terminal connectTo = null)
		{
			var transformerEnd = Create<PowerTransformerEnd>(name, transformer);
			transformerEnd.RatedU = Voltage.FromVolts(voltageVolts);
			var endTerminal = AddTerminal(transformer, connectTo);
			Associate(endTerminal, transformerEnd);

			return transformerEnd;
		}

		/// <summary>
		/// Adds a switch with two assocated terminals.
		/// </summary>
		/// <typeparam name="TSwitch">The type of switch to create</typeparam>
		/// <param name="name">The name and MRID of the new object.</param>
		/// <param name="from">If given, the switch's first terminal is connected with this terminal</param>
		/// <param name="to">If given, the switch's second terminal is connected with this terminal</param>
		public TSwitch AddSwitch<TSwitch>(string name, Terminal from = null, Terminal to = null)
			where TSwitch : Switch
		{
			var theSwitch = Create<TSwitch>(name);
			Network.Switches.Add(theSwitch);
			AddTerminal(theSwitch, connectTo: from);
			AddTerminal(theSwitch, connectTo: to);

			return theSwitch;
		}

		/// <summary>
		/// Adds an AC line segment with two associated terminals.
		/// </summary>
		/// <param name="name">The name and MRID of the new object.</param>
		/// <param name="rOhms">The line's resistance, in Ohms</param>
		/// <param name="xOhms">The line's reactance, in Ohms</param>
		/// <param name="from">If given, the line's first terminal is connected with this terminal</param>
		/// <param name="to">If given, the line's second terminal is connected with this terminal</param>
		public ACLineSegment AddLine(string name, double rOhms, double xOhms, Terminal from, Terminal to)
		{
			var line = Create<ACLineSegment>(name);
			Network.ACLineSegments.Add(line);
			line.R = Resistance.FromOhms(rOhms);
			line.X = Resistance.FromOhms(xOhms);
			AddTerminal(line, connectTo: from);
			AddTerminal(line, connectTo: to);

			return line;
		}

		/// <summary>
		/// Adds an equivalent injection
		/// </summary>
		/// <param name="name">The name and MRID of the new object.</param>
		/// <param name="connectTo">If given, the consumer's terminal is connected with this terminal</param>
		/// <param name="pWatt">The injection's active power, in Watts. Positive means the injection acts as a consumer, negative as a provider</param>
		/// <param name="qVar">The injection's reactive power, in VAR. Positive means the injection acts as a consumer, negative as a provider</param>
		/// <param name="minPWatt">The injection's minimum active power, in Watts. Positive means the injection acts as a consumer, negative as a provider</param>
		/// <param name="maxPWatt">The injection's maximum active power, in Watts. Positive means the injection acts as a consumer, negative as a provider</param>
		/// <param name="minQVar">The injection's minimum reactive power, in VAR. Positive means the injection acts as a consumer, negative as a provider</param>
		/// <param name="maxQVar">The injection's maximum reactive power, in VAR. Positive means the injection acts as a consumer, negative as a provider</param>
		public EquivalentInjection AddEquivalentInjection(string name, Terminal connectTo = null,
			double? pWatt = null, double? qVar = null,
			double? minPWatt = null, double? maxPWatt = null,
			double? minQVar = null, double? maxQVar = null)
		{
			if (pWatt.HasValue != qVar.HasValue)
				throw new Exception("Please specify both active and reactive demand, or none.");

			var injection = Create<EquivalentInjection>(name);
			Network.EquivalentInjections.Add(injection);
			AddTerminal(injection, connectTo);

			if (pWatt.HasValue)
				injection.P = ActivePower.FromWatts(pWatt.Value);
			if (qVar.HasValue)
				injection.Q = ReactivePower.FromVoltamperesReactive(qVar.Value);

			if (minPWatt.HasValue)
				injection.minP = ActivePower.FromWatts(minPWatt.Value);
			if (maxPWatt.HasValue)
				injection.maxP = ActivePower.FromWatts(maxPWatt.Value);
			if (minQVar.HasValue)
				injection.minQ = ReactivePower.FromVoltamperesReactive(minQVar.Value);
			if (maxQVar.HasValue)
				injection.maxQ = ReactivePower.FromVoltamperesReactive(maxQVar.Value);

			return injection;
		}

		/// <summary>
		/// Connects <paramref name="terminal1"/> and <paramref name="terminal2"/> (and any other terminals that are connected to one
		/// of them).
		/// The connectivity node of <paramref name="terminal2"/> (if there is one) is removed, and all terminals that were connected to
		/// it, are connected to <paramref name="terminal1"/>'s node instead.
		/// </summary>
		public void Connect(Terminal terminal1, Terminal terminal2)
		{
			var nodeToConnectAt = terminal1.ConnectivityNode;

			if (terminal2.ConnectivityNode is not ConnectivityNode nodeToRemove)
			{
				Associate(terminal2, nodeToConnectAt);
				return;
			}

			foreach (var t in nodeToRemove.Terminals)
			{
				t.ConnectivityNode = null;
				Associate(t, nodeToConnectAt);
			}

			_createdObjects.Remove(nodeToRemove);
		}

		/// <summary>
		/// Creates an association between <paramref name="object1"/> and <paramref name="object2"/>.
		/// In each object, a property with name and type matching the other class (or a base class) is set to the other object,
		/// or, the other object is added to a list with name and element type matching the other class (or a base class).
		/// </summary>
		public void Associate(IdentifiedObject object1, IdentifiedObject object2)
		{
			if (!SetObjectInProperty(object1, object2)
				&& !AddObjectToList(object1, object2))
				throw new Exception($"Did not find a property in {object1.GetType().Name} to put a {object2.GetType().Name} in");

			if (!SetObjectInProperty(object2, object1)
				&& !AddObjectToList(object2, object1))
				throw new Exception($"Did not find a property in {object2.GetType().Name} to put a {object1.GetType().Name} in");
		}

		/// <summary>
		/// Creates a new <see cref="Terminal"/> and associates it with the given <paramref name="equipment"/>.
		/// If <paramref name="connectTo"/> is given, connects the new terminal to the same connectivity node
		/// as <paramref name="connectTo"/>.
		/// Otherwise, connects it to a new connectivity node.
		/// </summary>
		public Terminal AddTerminal(ConductingEquipment equipment, Terminal connectTo = null)
		{
			var terminal = Create<Terminal>();
			Associate(equipment, terminal);

			if (connectTo != null)
				Connect(connectTo, terminal);
			else
				Create<ConnectivityNode>(associated: terminal);

			return terminal;
		}

		/// <summary>
		/// Removes the given object from the list of created objects.
		/// Note that any associations from other created objects are not removed, so it could still be reachable.
		/// </summary>
		public void Remove(IdentifiedObject idObject)
		{
			if (!_createdObjects.Remove(idObject))
				throw new Exception("Object was not present");
		}

		/// <summary>
		/// Creates a new object of type <typeparamref name="T"/>.
		/// </summary>
		/// <param name="name">If given, the name and MRID of the new object. If null, the MRID
		///   is derived from the MRID of the previous object created.</param>
		/// <param name="associated">If given, creates an association between this object and the created object</param>
		/// <returns>The created object</returns>
		private T Create<T>(string name = null, IdentifiedObject associated = null) where T : IdentifiedObject
		{
			var result = (T)Activator.CreateInstance(typeof(T));

			if (name != null)
			{
				// Use name as MRID

				result.Name = name;
				result.Description = $"{name} description";
				result.MRID = name;

				_lastMrid = name;
			}
			else
			{
				// Generate unique MRID
				_lastMrid = result.MRID = _lastMrid + "x"; ;
			}

			if (associated != null)
				Associate(associated, result);

			_createdObjects.Add(result);

			// Invalidate builder results, as they might need to include the new object
			_network = null;
			_demands = null;
			_configuration = null;

			return result;
		}

		/// <summary>
		/// If <paramref name="objectWithList"/> has a member with name
		/// (Type)s and type <code>List&lt;Type></code>, where Type is the type of
		/// <paramref name="objectToAdd"/> or one of its base types, adds <paramref name="objectToAdd"/> to that list.
		/// </summary>
		private static bool AddObjectToList(object objectWithList, object objectToAdd)
		{
			var listItemType = objectToAdd.GetType();

			while (listItemType != null)
			{
				// Look for a property named (type)s, where (type) is the type of
				// objectToAdd or (in later iterations) a base class

				string listName = $"{listItemType.Name}s";

				var listProperty = objectWithList.GetType().GetProperty(listName,
					BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);

				if (listProperty == null)
				{
					// Nope, try the base type
					listItemType = listItemType.BaseType;
					continue;
				}

				// Verify that property has type List<objectType>

				var listType = listProperty.PropertyType;

				if (!listType.IsGenericType
					|| listType.GetGenericTypeDefinition() != typeof(List<>)
					|| listType.GenericTypeArguments[0] != listItemType)
					throw new Exception($"Expected type List<{listItemType.Name}> for property {listProperty.DeclaringType.Name}.{listName}");

				// Get the list from the value object, create if null

				var list = listProperty.GetValue(objectWithList);
				if (list == null)
				{
					list = listType.GetConstructor(Array.Empty<Type>()).Invoke(Array.Empty<object>());
					listProperty.SetValue(objectWithList, list);
				}

				// Add objectToAdd to the list

				listProperty.PropertyType.GetMethod("Add").Invoke(list, new[] { objectToAdd });
				return true;
			}

			return false;
		}

		/// <summary>
		/// If <paramref name="objectWithProperty"/> has a member with name
		/// Type and type Type, where Type is the type of
		/// <paramref name="valueObject"/> or one of its base types, sets <paramref name="valueObject"/> as the value of that property.
		/// </summary>
		private static bool SetObjectInProperty(object objectWithProperty, object valueObject)
		{
			var valueType = valueObject.GetType();

			while (valueType != null)
			{
				// Look for a property named (type), where (type) is the type of
				// objectToAdd or (in later iterations) a base class

				string propertyName = valueType.Name;

				var property = objectWithProperty.GetType().GetProperty(propertyName,
					BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);

				if (property == null)
				{
					// Nope, try the base type
					valueType = valueType.BaseType;
					continue;
				}

				// Verify that property has type valueType

				if (property.PropertyType != valueType)
					throw new Exception($"Expected type {valueType.Name} for property {property.DeclaringType.Name}.{propertyName}");

				// Check that no previous value exists

				var oldValue = property.GetValue(objectWithProperty);
				if (oldValue != null)
					throw new Exception($"Property {property.DeclaringType.Name}.{propertyName} already has a value");

				// Set the value

				property.SetValue(objectWithProperty, valueObject);

				return true;
			}

			return false;
		}
	}
}