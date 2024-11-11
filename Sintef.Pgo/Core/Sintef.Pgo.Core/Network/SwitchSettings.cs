using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Class keeping switch settings
	/// </summary>
	public class SwitchSettings
	{
		/// <summary>
		/// For each switchable line, whether is it is open or not.
		/// When open (value == true), no current can pass
		/// </summary>
		private Dictionary<Line, bool> _lineIsOpen;

		/// <summary>
		/// For debugging. Persistent under cloning.
		/// </summary>
		private int _id;

		/// <summary>
		/// For debugging, used in producing unique _id's.
		/// </summary>
		static int _idCounter = 0;

		/// <summary>
		/// Construct a switchsetting where all switches are closed.
		/// </summary>
		/// <param name="network"></param>
		public SwitchSettings(PowerNetwork network)
			: this(network, (l) => false)
		{
		}

		/// <summary>
		/// Construct a switchsetting using the values provided by the given function.
		/// </summary>
		/// <param name="network"></param>
		/// <param name="isOpenFunc">A function that returns true for open (switchable) 
		///   lines and false for closed lines</param>
		public SwitchSettings(PowerNetwork network, Func<Line, bool> isOpenFunc)
		{
			_id = _idCounter++;

			_lineIsOpen = network.SwitchableLines.ToDictionary(l => l, l => isOpenFunc(l));
		}

		/// <summary>
		/// Copy constructor.
		/// </summary>
		/// <param name="settings"></param>
		public SwitchSettings(SwitchSettings settings)
		{
			_id = settings._id;
			_lineIsOpen = settings._lineIsOpen.Clone();
		}

		internal SwitchSettings Clone()
		{
			return new SwitchSettings(this);
		}

		/// <summary>
		/// Copies all settings from <paramref name="other"/>. Switches are identified by name.
		/// Any switch not in <paramref name="other"/> is set to closed.
		/// </summary>
		/// <param name="other">The settings to copy from</param>
		/// <param name="myNetwork">The network these settings are for</param>
		/// <param name="aggregator">The aggregation that gives the mapping between <paramref name="myNetwork"/>
		///   and the network of <paramref name="other"/></param>
		/// <param name="missingLineValue">A function that returns the setting for a switch that is not present in the source network.</param>
		internal void CopyFrom(SwitchSettings other, PowerNetwork myNetwork, NetworkAggregation aggregator, Func<Line, bool> missingLineValue)
		{
			var otherNetwork = aggregator.OtherNetwork(myNetwork);

			foreach (var line in _lineIsOpen.Keys.ToList())
			{
				if (otherNetwork.TryGetLine(line.Name, out Line otherLine))
					_lineIsOpen[line] = other._lineIsOpen[otherLine];
				else
					_lineIsOpen[line] = missingLineValue(line);
			}
		}

		/// <summary>
		/// Sets switch state of line
		/// </summary>
		/// <param name="line">The line to switch</param>
		/// <param name="open">True if line should be open, false if line should be closed</param>
		/// <returns>True if the switch setting changed, false if it was already at the indicated value.</returns>
		public bool SetSwitch(Line line, bool open)
		{
			if (_lineIsOpen[line] != open)
			{
				_lineIsOpen[line] = open;
				return true;
			}
			else
				return false;
		}

		/// <summary>
		/// Checks if the given line is set to be open.
		/// </summary>
		public bool IsOpen(Line line) => _lineIsOpen[line];

		/// <summary>
		/// Checks if the given line is set to be closed.
		/// </summary>
		public bool IsClosed(Line line) => !_lineIsOpen[line];

		/// <summary>
		/// Returns the number of switches that are different in the given configuration 
		/// from this one.
		/// </summary>
		/// <param name="other"></param>
		/// <returns></returns>
		public int NumberOfDifferentSwitches(SwitchSettings other)
		{
			return _lineIsOpen.Count(kvp => other._lineIsOpen[kvp.Key] != kvp.Value);
		}

		/// <summary>
		/// Returns the switches (switchable <see cref="Line"/>'s) that are different in the given configuration 
		/// from this one.
		/// </summary>
		/// <param name="other"></param>
		/// <returns></returns>
		public IEnumerable<Line> DifferentSwitches(SwitchSettings other)
		{
			return _lineIsOpen.Where(kvp => other._lineIsOpen[kvp.Key] != kvp.Value).Select(kvp => kvp.Key);
		}

		/// <summary>
		/// Enumerates the switches that are closed.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<Line> ClosedSwitches => _lineIsOpen.Where(kvp => !kvp.Value).Select(l => l.Key);

		/// <summary>
		/// Enumerates the switches that are closed.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<Line> OpenSwitches => _lineIsOpen.Where(kvp => kvp.Value).Select(l => l.Key);

		/// <summary>
		/// 
		/// </summary>
		/// <param name="other"></param>
		/// <returns></returns>
		public bool Equals(SwitchSettings other)
		{
			if (_lineIsOpen.Count != other._lineIsOpen.Count)
				return false;
			if (_lineIsOpen.Keys.Except(other._lineIsOpen.Keys).Any())
				return false;
			foreach (var kvp in _lineIsOpen)
			{
				if (kvp.Value != other._lineIsOpen[kvp.Key])
					return false;
			}
			return true;
		}

		/// <summary>
		/// Returns a new switch settings object based on the settings given in the input Stream.
		/// Assumes the format given in TODO.
		/// </summary>
		/// <param name="currentConfiguration"></param>
		/// <returns></returns>
		internal static SwitchSettings ParseCIM(Stream currentConfiguration)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Writes the id. Used for debugging.
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			return _id.ToString();
		}
	}
}
