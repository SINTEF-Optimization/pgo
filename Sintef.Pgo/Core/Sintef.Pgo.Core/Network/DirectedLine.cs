using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A direction along a <see cref="Line"/>
	/// </summary>
	public enum LineDirection
	{
		/// <summary>
		/// From Node1 to Node2
		/// </summary>
		Forward,

		/// <summary>
		/// From Node2 to Node1
		/// </summary>
		Reverse
	}

	/// <summary>
	/// A line and a direction along the line
	/// </summary>
	[DebuggerDisplay("DirectedLine {StartNode}->{EndNode} ({Line} {Direction})")]
	public struct DirectedLine
	{
		/// <summary>
		/// The line
		/// </summary>
		public readonly Line Line;

		/// <summary>
		/// The direction
		/// </summary>
		public readonly LineDirection Direction;

		/// <summary>
		/// The node that the direction points from
		/// </summary>
		public Bus StartNode
		{
			get
			{
				if (Direction == LineDirection.Forward)
					return Line.Node1;
				else
					return Line.Node2;
			}
		}

		/// <summary>
		/// The node that the direction points to
		/// </summary>
		public Bus EndNode
		{
			get
			{
				if (Direction == LineDirection.Forward)
					return Line.Node2;
				else
					return Line.Node1;
			}
		}

		/// <summary>
		/// The opposite of <see cref="Direction"/>
		/// </summary>
		private LineDirection OppositeDirection
		{
			get
			{
				if (Direction == LineDirection.Forward)
					return LineDirection.Reverse;
				else
					return LineDirection.Forward;
			}
		}

		/// <summary>
		/// The same line in the opposite direction
		/// </summary>
		public DirectedLine Reversed => new DirectedLine(Line, OppositeDirection);

		/// <summary>
		/// Initializes a directed line
		/// </summary>
		/// <param name="line">The line</param>
		/// <param name="direction">The direction</param>
		public DirectedLine(Line line, LineDirection direction)
		{
			Line = line;
			Direction = direction;
		}

		/// <summary>
		/// Deconstructs into a (line, direction) pair
		/// </summary>
		public void Deconstruct(out Line line, out LineDirection direction)
		{
			line = Line;
			direction = Direction;
		}
	}

	/// <summary>
	/// Extensions for <see cref="DirectedLine"/>
	/// </summary>
	public static partial class Extensions
	{
		/// <summary>
		/// Returns the input <paramref name="value"/> if <paramref name="direction"/> is Forward,
		/// otherwise returns its negative.
		/// </summary>
		public static Complex AdjustForDirection(this Complex value, LineDirection direction)
		{
			if (direction == LineDirection.Forward)
				return value;
			else
				return -value;
		}

		/// <summary>
		/// Reverses the given path of directed lines, and also the direction of each line in the path.
		/// </summary>
		/// <param name="path"></param>
		/// <returns></returns>
		public static IEnumerable<DirectedLine> ReverseDirectedPath(this IEnumerable<DirectedLine> path)
		{
			return path.Reverse().Select(dl => dl.Reversed);
		}

		/// <summary>
		/// Returns this line in the forward direction
		/// </summary>
		public static DirectedLine Forward(this Line line)
		{
			return new DirectedLine(line, LineDirection.Forward);
		}

		/// <summary>
		/// Returns this line in the direction pointing from the given bus
		/// </summary>
		public static DirectedLine InDirectionFrom(this Line line, Bus bus)
		{
			if (bus == line.Node1)
				return new DirectedLine(line, LineDirection.Forward);
			if (bus == line.Node2)
				return new DirectedLine(line, LineDirection.Reverse);

			throw new ArgumentException("Line does not start/end in that bus");
		}

		/// <summary>
		/// Returns this line in the direction pointing to the given bus
		/// </summary>
		public static DirectedLine InDirectionTo(this Line line, Bus bus)
		{
			if (bus == line.Node2)
				return new DirectedLine(line, LineDirection.Forward);
			if (bus == line.Node1)
				return new DirectedLine(line, LineDirection.Reverse);

			throw new ArgumentException("Line does not start/end in that bus");
		}
	}
}
