using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Interface for classes that can provide fault properties for lines
	/// </summary>
	public interface ILineFaultPropertiesProvider
	{
		/// <summary>
		/// Returns the average number of permanent faults per year for the given <paramref name="line"/>
		/// </summary>
		double FaultsPerYear(Line line);

		/// <summary>
		/// Returns the time required to isolate the <paramref name="line"/> from the majority of the network in case
		/// of a fault
		/// </summary>
		TimeSpan SectioningTime(Line line);

		/// <summary>
		/// Returns the time required to repair the faulty <paramref name="line"/> after is has been isolated
		/// </summary>
		TimeSpan RepairTime(Line line);
	}

	/// <summary>
	/// Provides line fault properties by storing them in an interal dictionary
	/// </summary>
	public class LineFaultPropertiesProvider : ILineFaultPropertiesProvider
	{

		/// <summary>
		/// Get the faults per year for the line.
		/// </summary>
		/// <param name="line"></param>
		/// <returns></returns>
		public double FaultsPerYear(Line line)
		{
			if (_dataByLine.TryGetValue(line, out var data))
				return data.FaultsPerYear;
			else
				return 0;
		}

		/// <summary>
		/// Get the sectioning time of the line.
		/// </summary>
		/// <param name="line"></param>
		/// <returns></returns>
		public TimeSpan SectioningTime(Line line)
		{
			if (_dataByLine.TryGetValue(line, out var data))
				return data.SectioningTime;
			else
				return TimeSpan.Zero;
		}
		/// <summary>
		/// Get the repair time of the line.
		/// </summary>
		/// <param name="line"></param>
		/// <returns></returns>
		public TimeSpan RepairTime(Line line)
		{
			if (_dataByLine.TryGetValue(line, out var data))
				return data.RepairTime;
			else
				return TimeSpan.Zero;
		}


		/// <summary>
		/// Returns the number of lines that have a positive fault frequency
		/// </summary>
		public int FaultingLineCount => _dataByLine.Values.Count(v => v.FaultsPerYear > 0);

		/// <summary>
		/// The fault data
		/// </summary>
		private Dictionary<Line, LineFaultData> _dataByLine = new Dictionary<Line, LineFaultData>();

		/// <summary>
		/// The network 
		/// </summary>
		private PowerNetwork _network;

		/// <summary>
		/// Initializes a provider with no data.
		/// </summary>
		/// <param name="network">The network to store data for</param>
		public LineFaultPropertiesProvider(PowerNetwork network)
		{
			_network = network;
		}

		/// <summary>
		/// Creates and returns a provider with random data for all lines
		/// </summary>
		/// <param name="network">The network to consider</param>
		/// <param name="random">The random generator to use. If null, one is created</param>
		public static LineFaultPropertiesProvider RandomFor(PowerNetwork network, Random random = null)
		{
			if (random == null)
				random = new Random();

			var provider = new LineFaultPropertiesProvider(network);
			provider.SetAll(0.1, TimeSpan.FromHours(4), TimeSpan.FromHours(3));
			provider.Randomize(random);

			return provider;
		}

		/// <summary>
		/// Adds data about a line
		/// </summary>
		public void Add(Line line, double faultsPerYear, TimeSpan sectioningTime, TimeSpan repairTime)
		{
			if (line.IsBreaker)
				throw new ArgumentException("We do not support breakers that can fault");

			var data = new LineFaultData(faultsPerYear, sectioningTime, repairTime);

			_dataByLine.Add(line, data);
		}

		/// <summary>
		/// Sets the given data for all lines (that are not circuit breakers)
		/// </summary>
		public void SetAll(double faultsPerYear, TimeSpan sectioningTime, TimeSpan repairTime)
		{
			foreach (var line in _network.Lines)
			{
				if (!line.IsBreaker)
					Add(line, faultsPerYear, sectioningTime, repairTime);
			}
		}

		/// <summary>
		/// Clones the category data to use the bus objects of the given network.
		/// </summary>
		/// <param name="network"></param>
		/// <returns></returns>
		public LineFaultPropertiesProvider CloneFor(PowerNetwork network)
		{
			LineFaultPropertiesProvider ccc = new LineFaultPropertiesProvider(network);
			foreach (var d in _dataByLine)
			{
				Line nline = network.GetLine(d.Key.Name);
				ccc._dataByLine[nline] = new LineFaultData(d.Value.FaultsPerYear,d.Value.SectioningTime,d.Value.RepairTime);
			}
			return ccc;
		}


		/// <summary>
		/// Sets a zero fault frequency for any line that is not always separated from each provider
		/// by both a breaker and a switch.
		/// </summary>
		public void SuppressFaultsAroundProviders()
		{
			foreach (var provider in _network.Providers)
			{
				var configuration = NetworkConfiguration.AllClosed(_network);

				var linesCloserThanFirstBreaker = configuration.LinesAround(provider, false, stopAt: (line, _) => line.IsBreaker);
				SuppressFaults(linesCloserThanFirstBreaker);

				var linesUpToFirstSwitch = configuration.LinesAround(provider, false, stopAfter: (line, _) => line.IsSwitchable);
				SuppressFaults(linesUpToFirstSwitch);
			}
		}

		/// <summary>
		/// Randomizes all the data held by multiplying each value (including durations)
		/// by a random factor between 0.8 and 1.2.
		/// </summary>
		/// <param name="random">The random generator to use</param>
		public void Randomize(Random random)
		{
			foreach (var kv in _dataByLine)
			{
				kv.Value.Randomize(random);
			}
		}

		/// <summary>
		/// Sets a zero fault frequency for the given lines
		/// </summary>
		private void SuppressFaults(IEnumerable<Line> lines)
		{
			foreach (var line in lines)
				_dataByLine.Remove(line);
		}


		/// <summary>
		/// Fault data for one line
		/// </summary>
		private class LineFaultData
		{
			/// <summary>
			/// The average number of permanent faults per year for the line
			/// </summary>
			public double FaultsPerYear { get; private set; }

			/// <summary>
			/// The time required to isolate the line from the majority of the network in case
			/// of a fault
			/// </summary>
			public TimeSpan SectioningTime { get; private set; }

			/// <summary>
			/// The time required to repair the line after is has been isolated
			/// </summary>
			public TimeSpan RepairTime { get; private set; }

			public LineFaultData(double faultsPerYear, TimeSpan sectioningTime, TimeSpan repairTime)
			{
				FaultsPerYear = faultsPerYear;
				SectioningTime = sectioningTime;
				RepairTime = repairTime;
			}

			/// <summary>
			/// Randomizes the data held by multiplying each value (including durations)
			/// by a random factor between 0.8 and 1.2.
			/// </summary>
			/// <param name="random">The random generator to use</param>
			public void Randomize(Random random)
			{
				FaultsPerYear *= Factor(random);
				SectioningTime = SectioningTime.Times(Factor(random));
				RepairTime = RepairTime.Times(Factor(random));
			}

			private double Factor(Random random)
			{
				return 0.8 + 0.4 * random.NextDouble();
			}
		}
	}
}
