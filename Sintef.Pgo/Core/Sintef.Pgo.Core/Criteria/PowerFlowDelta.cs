using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Roslyn.Utilities;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Data on the differences between two Flow's, in terms of the lines on which
	/// the power flow and current are different, and the values "before" and "after".
	/// Typically used to cache the effect on flow associated with a local search Move.
	/// </summary>
	/// <remarks>
	/// <see cref="PowerFlowDelta"/> objects are allocated from and returned to an object 
	/// pool, in order to reuse their memory without involving garbage collection.
	/// This is beneficial because they can be fairly large, and many are created and used
	/// for just a short time during neighborhood exploration.
	/// </remarks>
	public class PowerFlowDelta
	{
		#region Public properties 

		/// <summary>
		/// The power flow delta per line at which the flow changed/would change.
		/// </summary>
		public Dictionary<Line, LinePowerFlowDelta> LineDeltas { get; private set; } = new Dictionary<Line, LinePowerFlowDelta>();

		/// <summary>
		/// The delta values per bus which the flow changed/would change.
		/// </summary>
		public Dictionary<Bus, BusPowerFlowDelta> BusDeltas { get; private set; } = new Dictionary<Bus, BusPowerFlowDelta>();

		#endregion

		/// <summary>
		/// The pool from which power flow deltas are allocated
		/// </summary>
		static ObjectPool<PowerFlowDelta> _pool = new ObjectPool<PowerFlowDelta>(() => new PowerFlowDelta(), Environment.ProcessorCount * 5);

		#region Public methods

		/// <summary>
		/// Allocates a power flow delta from the pool
		/// </summary>
		public static PowerFlowDelta Allocate() => _pool.Allocate();

		/// <summary>
		/// Returns this power flow delta to the pool
		/// </summary>
		public void Free()
		{
			// Clear dictionaries. This does not release their memory.
			LineDeltas.Clear();
			BusDeltas.Clear();

			_pool.Free(this);
		}

		/// <summary>
		/// Private constructor
		/// </summary>
		private PowerFlowDelta() { }

		/// <summary>
		/// Adds a delta value for the given line, with power insertion values before and after, 
		/// both taken in the direction from the line's Node1 to the line's Node2.
		/// </summary>
		/// <param name="line">The line. You can only add for one line once.</param>
		/// <param name="oldPowerFromNode1">The power flowing into the line from Node1, "before"</param>
		/// <param name="newPowerFromNode1">The power flowing into the line from Node1, "after"</param>
		/// <param name="oldPowerFromNode2">The power flowing into the line from Node2, "before"</param>
		/// <param name="newPowerFromNode2">The power flowing into the line from Node2, "after"</param>
		/// <param name="oldCurrent"></param>
		/// <param name="newCurrent"></param>
		public void AddLineDelta(Line line,
			Complex oldPowerFromNode1, Complex newPowerFromNode1,
			Complex oldPowerFromNode2, Complex newPowerFromNode2,
			Complex oldCurrent, Complex newCurrent)
		{
			Debug.Assert(!LineDeltas.ContainsKey(line), $"{nameof(PowerFlowDelta.AddLineDelta)}: Each line can only be added once, at most.");
			LineDeltas[line] = new LinePowerFlowDelta(line,
				oldPowerFromNode1, newPowerFromNode1, oldPowerFromNode2, newPowerFromNode2,
				oldCurrent, newCurrent);
		}

		/// <summary>
		/// Adds delta values for the given bus.
		/// </summary>
		/// <param name="bus"></param>
		/// <param name="voltage"></param>
		/// <param name="nominalVoltage"></param>
		/// <param name="provider"></param>
		public BusPowerFlowDelta AddBusDelta(Bus bus, Complex voltage, double nominalVoltage, Bus provider)
		{
			Debug.Assert(!BusDeltas.ContainsKey(bus), $"{nameof(PowerFlowDelta.AddBusDelta)}: Each bus can only be added once, at most.");
			var busDelta = new BusPowerFlowDelta(bus, voltage, nominalVoltage, provider);
			BusDeltas[bus] = busDelta;
			return busDelta;
		}

		#endregion

		/// <summary>
		/// The updated voltage, nominal voltage, and provider for a bus.
		/// </summary>
		public struct BusPowerFlowDelta
		{
			/// <summary>
			/// The bus that this delta is for.
			/// </summary>
			public Bus Bus { get; }

			/// <summary>
			/// The voltage at the bus after the update.
			/// </summary>
			public Complex NewVoltage { get; }

			/// <summary>
			/// The nominal voltage at the bus after the update.
			/// </summary>
			public double NewNominalVoltage { get; }

			/// <summary>
			/// The provider for the bus after the update.
			/// </summary>
			public Bus NewProvider { get; set; }

			/// <summary>
			/// Constructor
			/// </summary>
			/// <param name="bus"></param>
			/// <param name="voltage"></param>
			/// <param name="nominalVoltage"></param>
			/// <param name="provider"></param>
			public BusPowerFlowDelta(Bus bus, Complex voltage, double nominalVoltage, Bus provider)
			{
				Bus = bus;
				NewVoltage = voltage;
				NewNominalVoltage = nominalVoltage;
				NewProvider = provider;
			}
		}

		/// <summary>
		/// The "before" and "after" power injections and current on a line.
		/// </summary>
		public struct LinePowerFlowDelta
		{
			/// <summary>
			/// The line this delta is for
			/// </summary>
			public Line Line { get; }

			/// <summary>
			/// The current on the line, "before". From Node1 to Node2.
			/// </summary>
			public Complex OldCurrent { get; private set; }

			/// <summary>
			/// The current on the line, "after". From Node1 to Node2.
			/// </summary>
			public Complex NewCurrent { get; private set; }

			/// <summary>
			/// The power flowing from <paramref name="bus"/> into the line, "before"
			/// </summary>
			public Complex OldPowerFlowFrom(Bus bus)
			{
				if (bus == Line.Node1)
					return _oldPowerFromNode1;
				else
					return _oldPowerFromNode2;
			}

			/// <summary>
			/// The power flowing from <paramref name="bus"/> into the line, "after"
			/// </summary>
			public Complex NewPowerFlowFrom(Bus bus)
			{
				if (bus == Line.Node1)
					return _newPowerFromNode1;
				else
					return _newPowerFromNode2;
			}

			/// <summary>
			/// The change in power flowing from <paramref name="bus"/> into the line, "after" - "before"
			/// </summary>
			public Complex DeltaPowerFlowFrom(Bus bus) => NewPowerFlowFrom(bus) - OldPowerFlowFrom(bus);

			private Complex _oldPowerFromNode1;
			private Complex _newPowerFromNode1;
			private Complex _oldPowerFromNode2;
			private Complex _newPowerFromNode2;

			#region Construction

			/// <summary>
			/// Constructor.
			/// </summary>
			/// <param name="line">The line the delta is for</param>
			/// <param name="oldPowerFromNode1">The power flowing into the line from Node1, "before"</param>
			/// <param name="newPowerFromNode1">The power flowing into the line from Node1, "after"</param>
			/// <param name="oldPowerFromNode2">The power flowing into the line from Node2, "before"</param>
			/// <param name="newPowerFromNode2">The power flowing into the line from Node2, "after"</param>
			/// <param name="oldCurrent">The current on the line, "before"</param>
			/// <param name="newCurrent">The current on the line, "after"</param>
			public LinePowerFlowDelta(Line line,
				Complex oldPowerFromNode1, Complex newPowerFromNode1,
				Complex oldPowerFromNode2, Complex newPowerFromNode2,
				Complex oldCurrent, Complex newCurrent)
			{
				Line = line;

				_oldPowerFromNode1 = oldPowerFromNode1;
				_newPowerFromNode1 = newPowerFromNode1;
				_oldPowerFromNode2 = oldPowerFromNode2;
				_newPowerFromNode2 = newPowerFromNode2;

				OldCurrent = oldCurrent;
				NewCurrent = newCurrent;
			}

			#endregion
		}
	}
}
