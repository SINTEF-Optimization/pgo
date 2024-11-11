using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A transmission line in the power network. While the endpoints
	/// are numbered their treatment is symmetric.
	/// </summary>
	public class Line
	{
		#region Public properties

		/// <summary>
		/// Human-readable identifier of the line.
		/// </summary>
		public string Name { get; private set; }

		/// <summary>
		/// Max current on the line (in either direction), in A. Must be at least 0.
		/// </summary>
		public double IMax { get; private set; }

		/// <summary>
		/// Maximum allowed voltage on the line, in V. Must be at least 0.
		/// </summary>
		public double VMax { get; private set; }

		/// <summary>
		/// The complex impedance of the line.
		/// </summary>
		public Complex Impedance { get; private set; }

		/// <summary>
		/// The real component of the line impedance.
		/// </summary>
		public double Resistance => Impedance.Real;
		
		/// <summary>
		/// The imaginary component of the line impedance.
		/// </summary>
		public double Reactance => Impedance.Imaginary;

		/// <summary>
		/// The inverse of the impedance.
		/// </summary>
		public Complex Admittance => 1.0 / Impedance;
		
		/// <summary>
		/// The real component of the conductance.
		/// </summary>
		public double Conductance => Admittance.Real;

		/// <summary>
		/// The first node of the line in the given direction. I.e.,
		/// if the direction is Forward, the function returns Node1, otherwise, Node2.
		/// </summary>
		/// <param name="direction"></param>
		/// <returns></returns>
		internal Bus FirstNode(LineDirection direction) => direction == LineDirection.Forward ? Node1 : Node2;

		/// <summary>
		/// The last node of the line in the given direction. I.e.,
		/// if the direction is Forward, the function returns Node2, otherwise, Node1.
		/// </summary>
		/// <param name="direction"></param>
		/// <returns></returns>
		internal Bus LastNode(LineDirection direction) => direction == LineDirection.Forward ? Node2 : Node1;

		/// <summary>
		/// The imaginary component of the admittance.
		/// </summary>
		public double Susceptance => Admittance.Imaginary;

		/// <summary>
		/// True if the line is switchable, false if not.
		/// </summary>
		public bool IsSwitchable { get; }

		/// <summary>
		/// True if the line represents an automatic circuit breaker, false if not.
		/// An automatic circuit breaker is a switch that automatically opens in case of
		/// a short circuit in the sub-network that it supplies to.
		/// If there are more than one breaker upstream of the short circuit, normally only the
		/// closest one will open.
		/// </summary>
		public bool IsBreaker { get; }

		/// <summary>
		/// True if the line represents a connection to a transformer. In this case it will not be part
		/// of flow computations in the usual way.
		/// </summary>
		public bool IsTransformerConnection => Node1.IsTransformer || Node2.IsTransformer;

		/// <summary>
		/// If this line is a TransformerConnection return its transformer data, otherwise null.
		/// </summary>
		public Transformer Transformer => Node1.IsTransformer ? Node1.Transformer : (Node2.IsTransformer ? Node2.Transformer : null);

		/// <summary>
		/// The cost of switching a switchable line. Used e.g. in the ConfigChangeCost objective.
		/// Accessing this for a non-switchable line results in an exception.
		/// </summary>
		public double SwitchingCost
		{
			get
			{
				if (!IsSwitchable)
					throw new Exception("Line.SwitchingCost: called for non-switchable line");
				else
					return _switchingCost;
			}
			private set { _switchingCost = value; }
		}

		/// <summary>
		/// The index of the line. This is an integer in the range [0, network.LineIndexBound - 1] that is
		/// unique within the network.
		/// </summary>
		public int Index { get; private set; }

		/// <summary>
		/// An endpoint of the line.
		/// </summary>
		public Bus Node1 { get; }

		/// <summary>
		/// The other endpoint of the line.
		/// </summary>
		public Bus Node2 { get; }

		/// <summary>
		/// Enumerates both endpoints of the line
		/// </summary>
		public IEnumerable<Bus> Endpoints
		{
			get
			{
				yield return Node1;
				yield return Node2;
			}
		}

		/// <summary>
		/// Describes the properties of the Line as a string.
		/// </summary>
		/// <param name="tab">The tab to put first in each line. Optional.</param>
		public string Description(string tab = null)
		{
			tab = tab ?? string.Empty;
			string descr = tab + $"Index: {Index}.\n\r";
			descr += tab + $"Name: {Name}.\n\r";
			descr += tab + $"Nodes: {Node1.Name} (index={Node1.Index}) and {Node2.Name} (index={Node2.Index}).\n\r";

			if (IMax == double.MaxValue)
				descr += tab + $"IMax: ∞.\n\r";
			else
				descr += tab + $"IMax: {IMax} A.\n\r";

			if (VMax == double.MaxValue)
				descr += tab + $"VMax: ∞.\n\r";
			else
				descr += tab + $"VMax: {VMax} V.\n\r";

			descr += tab + $"Impedance: {Impedance}. \n\r";
			if (IsSwitchable)
				descr += tab + $"Switchable, with cost: {SwitchingCost}. \n\r";
			if (IsBreaker)
				descr += tab + $"Breaker.\n\r";
			return descr;
		}

		#endregion

		#region Private data members

		/// <summary>
		/// The cost of switching a switchable line. Used e.g. in the ConfigChangeCost objective.
		/// </summary>
		public double _switchingCost = 0;

		#endregion

		#region Constructors

		/// <summary>
		/// Constructs a line with given parameters
		/// </summary>
		/// <param name="index">The line's index</param>
		/// <param name="node1">One endpoint.</param>
		/// <param name="node2">The other endpoint.</param>
		/// <param name="impedance">Line impedance.</param>
		/// <param name="imax">Max current on the line (in either direction). Must be at least 0.</param>
		/// <param name="vmax">Maxmimum alloable voltage on the line, in V.</param>
		/// <param name="switchable">Indicates whether the line is switchable.</param>
		///<param name="switchingCost">The cost of switching the line, if it is switchable. For non-switchable lines, any value here is ignored. Optional, the default value is zero.</param>
		/// <param name="isBreaker"></param>
		/// <param name="name">Human-readable name</param>
		internal Line(int index, Bus node1, Bus node2, Complex impedance, double imax, double vmax, bool switchable = false, double switchingCost = 0,
			bool isBreaker = false, string name = null)
		{
			Index = index;
			Node1 = node1;
			Node2 = node2;
			if (imax < 0)
				throw new ArgumentException("Line.ctor: current capacity must be greater or equal to zero");
			IMax = imax;
			VMax = vmax;

			Impedance = impedance;
			IsSwitchable = switchable;
			SwitchingCost = switchingCost;
			IsBreaker = isBreaker;

			Name = string.IsNullOrEmpty(name) ? $"Line {Index}: {Node1} -- {Node2}" : name;
		}

		#endregion

		#region Public methods
		/// <summary>
		/// Given one of the endpoints, returns the other.
		/// </summary>
		/// <param name="node">The endpoint we have.</param>
		/// <returns>The point at the other end of the node</returns>
		/// <exception cref="ArgumentException">Thrown if the argument is not an endpoint of the line.</exception>
		public Bus OtherEnd(Bus node)
		{
			if (ReferenceEquals(node, Node1))
			{
				return Node2;
			}
			else if (ReferenceEquals(node, Node2))
			{
				return Node1;
			}
			else
			{
				throw new ArgumentException("OtherEnd called with non-endpoint argument.");
			}
		}

		/// <summary>
		/// Checks if the given line goes between the two given buses
		/// </summary>
		/// <param name="b1"></param>
		/// <param name="b2"></param>
		/// <returns></returns>
		public bool IsBetween(Bus b1, Bus b2)
		{
			if (b1 == Node1)
				return b2 == Node2;
			else if (b1 == Node2)
				return b2 == Node1;
			else
				return false;
		}

		/// <summary>
		/// Returns the bus that is common for the  this Line and the given one,
		/// or null if they have no bus in common.
		/// </summary>
		/// <param name="secondEdge"></param>
		/// <returns></returns>
		public Bus CommonBusWith(Line secondEdge)
		{
			if (Node1 == secondEdge.Node1 || Node1 == secondEdge.Node2)
				return Node1;
			else if (Node2 == secondEdge.Node1 || Node2 == secondEdge.Node2)
				return Node2;
			else
				return null;
		}

		/// <summary>
		/// Return Name (unless it is very long, in which case
		/// a simplified name is returned.
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			if (Name.Length > 50)
				return $"Line {Index}: {Node1} -- {Node2}";

			return Name;
		}
		#endregion
	}
}
