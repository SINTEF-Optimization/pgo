using Sintef.Scoop.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// An annotation for a Power solution
	/// </summary>
	public class PowerAnnotation : TextAnnotation
	{
		/// <summary>
		/// The period the annotation is for
		/// </summary>
		public Period Period { get; }

		/// <summary>
		/// Initializes an annotation
		/// </summary>
		public PowerAnnotation(Period period, string text)
			: base(text)
		{
			Period = period;
		}

		/// <summary>
		/// Text representation of the power annotation.
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			return $"{Period}: {Text}";
		}
	}

	/// <summary>
	/// An annotation indicating one or more buses
	/// </summary>
	public class BusAnnotation : PowerAnnotation
	{
		/// <summary>
		/// The buses
		/// </summary>
		public List<Bus> Buses { get; }

		/// <summary>
		/// Annotation for a set of buses.
		/// </summary>
		public BusAnnotation(IEnumerable<Bus> buses, Period period, string text)
			: base(period, text)
		{
			Buses = buses.ToList();
		}

		/// <summary>
		/// Annotation for a single bus.
		/// </summary>
		public BusAnnotation(Bus bus, Period period, string text)
			: this(new[] { bus }, period, text)
		{
		}
	}

	/// <summary>
	/// An annotation indicating one or more lines
	/// </summary>
	public class LineAnnotation : PowerAnnotation
	{
		/// <summary>
		/// The lines
		/// </summary>
		public List<Line> Lines { get; }

		/// <summary>
		/// Constructor for annotating a set of lines.
		/// </summary>
		/// <param name="lines"></param>
		/// <param name="period"></param>
		/// <param name="text"></param>
		public LineAnnotation(IEnumerable<Line> lines, Period period, string text)
			: base(period, text)
		{
			Lines = lines.ToList();
		}

		/// <summary>
		/// Constructor for annotating a single line.
		/// </summary>
		/// <param name="line"></param>
		/// <param name="period"></param>
		/// <param name="text"></param>
		public LineAnnotation(Line line, Period period, string text)
			: this(new[] { line }, period, text)
		{
		}
	}
}
