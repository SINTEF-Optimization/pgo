using Sintef.Scoop.Utilities.GeoCoding;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A bus in the power network. May connect multiple lines. May have consumption or production of power, and may have a voltage transformer <see cref="BusTypes"/>.
	/// </summary>
	public class Bus
	{
		#region Public properties

		/// <summary>
		/// Name of this bus.
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// The type of this bus.
		/// </summary>
		public BusTypes Type { get; private set; }

		/// <summary>
		/// The location of this bus.
		/// </summary>
		public Coordinate Location { get; private set; }

		/// <summary>
		/// Returns the generator voltage, in V.
		/// Throws an exception if the bus is not a generator.
		/// </summary>
		public double GeneratorVoltage
		{
			get
			{
				if (Type != BusTypes.PowerProvider)
					throw new InvalidOperationException("This bus is not a power provider!");
				return _generatorVoltage;
			}
			internal set
			{
				_generatorVoltage = value;
			}
		}

		/// <summary>
		/// Complex generation capacity in the bus. Throws if the bus is not a provider.
		/// </summary>
		public Complex GenerationCapacity
		{
			get
			{
				if (Type != BusTypes.PowerProvider)
					throw new InvalidOperationException("This bus is not a power provider!");
				return _generationCapacity;
			}
			set => _generationCapacity = value;
		}

		/// <summary>
		/// Active generation capacity in the bus. Throws if the bus is not a provider.
		/// </summary>
		public double ActiveGenerationCapacity
		{
			get => GenerationCapacity.Real;
			set => GenerationCapacity = new Complex(value, GenerationCapacity.Imaginary);
		}

		/// <summary>
		/// Reactive generation capacity in the bus. Throws if the bus is not a provider.
		/// </summary>
		public double ReactiveGenerationCapacity
		{
			get => GenerationCapacity.Imaginary;
			set => GenerationCapacity = new Complex(GenerationCapacity.Real, value);
		}

		/// <summary>
		/// Lower bound on complex generation in the bus. Throws if the bus is not a provider.
		/// </summary>
		public Complex GenerationLowerBound
		{
			get
			{
				if (Type != BusTypes.PowerProvider)
					throw new InvalidOperationException("This bus is not a power provider!");
				return _minimumGeneration;
			}
			set => _minimumGeneration = value;
		}

		/// <summary>
		/// Lower bound on active generation in the bus. Throws if the bus is not a provider.
		/// </summary>
		public double ActiveGenerationLowerBound
		{
			get => GenerationLowerBound.Real;
			set => GenerationLowerBound = new Complex(value, GenerationLowerBound.Imaginary);
		}

		/// <summary>
		/// Lower bound on reactive generation in the bus. Throws if the bus is not a provider.
		/// </summary>
		public double ReactiveGenerationLowerBound
		{
			get => GenerationLowerBound.Imaginary;
			set => GenerationLowerBound = new Complex(GenerationLowerBound.Real, value);
		}

		/// <summary>
		/// Gets the associated transformer from the bus. 
		/// </summary>
		public Transformer Transformer
		{
			get
			{
				if (Type != BusTypes.PowerTransformer)
					throw new InvalidOperationException("This bus does not represent a transformer!");
				if (_transformer == null)
					throw new InvalidOperationException("This bus is in an inconsistent state!");
				return _transformer;
			}
		}

		/// <summary>
		/// Returns true if the bus is a connection bus
		/// </summary>
		public bool IsConnection => Type == BusTypes.Connection;

		/// <summary>
		/// Returns true if the bus is a generator.
		/// </summary>
		public bool IsProvider => Type == BusTypes.PowerProvider;

		/// <summary>
		/// Returns true if the bus is a consumer.
		/// </summary>
		public bool IsConsumer => Type == BusTypes.PowerConsumer;

		/// <summary>
		/// Returns true if the bus represents a transformer
		/// </summary>
		public bool IsTransformer => Type == BusTypes.PowerTransformer;

		/// <summary>
		/// The number of lines incident to the bus
		/// </summary>
		public int IncidentLinesCount => IncidentLines.Count();

		/// <summary>
		/// All lines incident to the bus.
		/// </summary>
		public IEnumerable<Line> IncidentLines => _incidentLines;

		/// <summary>
		/// All lines incident to the bus.
		/// </summary>
		public Line[] IncidentLinesArray => _incidentLinesArray;

		/// <summary>
		/// Enumerates the buses that are connected to this bus with a line (including switchable lines
		/// that may be open)
		/// </summary>
		public IEnumerable<Bus> Neighbors => IncidentLines.Select(l => l.OtherEnd(this));

		/// <summary>
		/// The index of the bus. This is an integer in the range [0, network.BusIndexBound - 1] that is
		/// unique within the network.
		/// </summary>
		public int Index { get; private set; }

		/// <summary>
		/// The minimum allowed voltage at the bus, in volts
		/// </summary>
		public double VMin
		{
			get
			{
				if (Type == BusTypes.PowerProvider)
					throw new InvalidOperationException("Providers don't have voltage limits!");
				return _vMinVolts;
			}
			set => _vMinVolts = value;
		}

		/// <summary>
		/// The maximum allowed voltage at the bus, in volts
		/// </summary>
		public double VMax
		{
			get
			{
				if (Type == BusTypes.PowerProvider)
					throw new InvalidOperationException("Providers don't have voltage limits!");
				return _vMaxVolts;
			}
			set => _vMaxVolts = value;
		}
		#endregion

		#region Private data members

		private List<Line> _incidentLines = new List<Line>();
		private double _generatorVoltage;
		private Complex _generationCapacity;
		private Complex _minimumGeneration;

		private Transformer _transformer = null; // 

		/// <summary>
		/// Caches the value of <see cref="IncidentLines"/> for efficiency
		/// </summary>
		private Line[] _incidentLinesArray = new Line[0];
		private double _vMinVolts;
		private double _vMaxVolts;

		#endregion

		#region Constructors

		/// <summary>
		/// Create a transition bus or consumer bus.
		/// </summary>
		/// <param name="index">The index of the bus</param>
		/// <param name="vMax">Max voltage in bus, in V.</param>
		/// <param name="vMin">Min voltage in bus, in V.</param>
		/// <param name="type">The type of bus (Connection or PowerConsumer)</param>
		/// <param name="name">Optional user-supplied name. If none is given, a string representation of the index is used.</param>
		/// <param name="location">Optional location of the bus.</param>
		internal Bus(int index, double vMin, double vMax, BusTypes type, string name = null, Coordinate location = null)
		{
			Index = index;

			VMin = vMin;
			VMax = vMax;

			Name = string.IsNullOrEmpty(name) ? Index.ToString() : name;
			Type = type;
			Location = location;
		}

		/// <summary>
		/// Create a power-providing bus.
		/// </summary>
		/// <param name="index">The index of the bus</param>
		/// <param name="generatorVoltage">The generator voltage, in V</param>
		/// <param name="maxPower">Max power generation, in VA</param>
		/// <param name="minPower">Min power generation, in VA</param>
		/// <param name="name">Optional user-supplied name. If none is given, a string representation of the index is used.</param>
		/// <param name="location">Optional location of the bus.</param>
		internal Bus(int index, double generatorVoltage, Complex maxPower, Complex minPower, string name = null, Coordinate location = null)
			: this(index, generatorVoltage, generatorVoltage, BusTypes.PowerProvider, name, location)
		{
			_generatorVoltage = generatorVoltage;
			_generationCapacity = maxPower;
			_minimumGeneration = minPower;
		}

		/// <summary>
		/// Create a power-transforming bus, representing a transformer in the power grid.
		/// </summary>
		/// <param name="index">The index of the bus</param>
		/// <param name="transformer">The <see cref="Transformer"/> this bus represents.</param>
		/// <param name="name">Optional user-supplied name. If none is given, a string representation of the index is used.</param>
		/// <param name="location">Optional location of the bus.</param>
		internal Bus(int index, Transformer transformer, string name = null, Coordinate location = null)
			: this(index, 0.0, double.MaxValue, BusTypes.PowerTransformer, name, location)
		{
			_transformer = transformer ?? throw new ArgumentNullException($"Bus {name} initialized as transformer bus with null transformer.");
		}

		#endregion

		#region Methods

		/// <summary>
		/// Register an incident line which has this bus as an endpoint.
		/// </summary>
		/// <param name="line"></param>
		public void RegisterLine(Line line)
		{
			_incidentLines.Add(line);
			_incidentLinesArray = IncidentLines.ToArray();
		}
		#endregion

		/// <summary>
		/// String representation using name, or index if the name is missing or too long.
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			if (string.IsNullOrEmpty(Name) || Name.Length > 20)
				return $"Bus {Index}";

			return Name;
		}

		/// <summary>
		/// Describes the properties of the Bus as a string.
		/// </summary>
		/// <param name="tab">The tab to put first in each line. Optional.</param>
		public string Description(string tab = null)
		{
			tab = tab ?? string.Empty;
			string descr = tab + $"Index: {Index}.\n\r";
			descr += tab + $"Name: {Name}.\n\r";
			descr += tab + $"Type: {Type}.\n\r";

			switch (Type)
			{
				case BusTypes.PowerProvider:
					descr += tab + $"Generation Capacity (active): {ActiveGenerationCapacity} W.\n\r";
					descr += tab + $"Generation lower limit (active): {ActiveGenerationLowerBound} W.\n\r";
					if (ReactiveGenerationCapacity != 0)
					{
						descr += tab + $"Generation Capacity (reactive): {ReactiveGenerationCapacity} W.\n\r";
						descr += tab + $"Generation lower limit (reactive): {ReactiveGenerationLowerBound} W.\n\r";
					}
					descr += tab + $"Generator Voltage: {GeneratorVoltage} V.\n\r";
					descr += tab + $"Number of incident lines: {this.IncidentLinesCount}.\n\r";
					descr += tab + $"Coordinates: {this.Location}.\n\r";
					break;
				case BusTypes.Connection:
				case BusTypes.PowerConsumer:
				case BusTypes.PowerTransformer:
				default:
					descr += tab + $"VMin: {VMin} V.\n\r";
					descr += tab + $"VMax: {VMax} V.\n\r";
					break;
			}
			return descr;
		}
	}

	/// <summary>
	/// A type of bus used in the model.
	/// </summary>
	public enum BusTypes
	{
		/// <summary>
		/// Nodes that have no consumption or production, but can be used to connect lines.
		/// </summary>
		Connection = 0,

		/// <summary>
		/// Nodes that provide power to the network.
		/// </summary>
		PowerProvider,

		/// <summary>
		/// Nodes that consumer power from the network.
		/// </summary>
		PowerConsumer,

		/// <summary>
		/// Nodes that represent voltage transformers.
		/// </summary>
		PowerTransformer
	}
}
