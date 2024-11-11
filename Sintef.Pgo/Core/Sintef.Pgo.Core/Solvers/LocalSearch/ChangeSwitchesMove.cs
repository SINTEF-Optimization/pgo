using System;
using System.Collections.Generic;
using System.Linq;
using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A move that changes the status of one or more switches in one period
	/// </summary>
	public class ChangeSwitchesMove : Move
	{
		#region Public properties 

		/// <summary>
		/// The period that the move operates on
		/// </summary>
		public Period Period { get; }

		/// <summary>
		/// The Lines/Switches to open
		/// </summary>
		public IEnumerable<Line> SwitchesToOpen { get; }

		/// <summary>
		/// The Lines/Switches to close
		/// </summary>
		public IEnumerable<Line> SwitchesToClose { get; }

		/// <summary>
		/// Enumeratre the switches to open or close
		/// </summary>
		public IEnumerable<Line> SwitchesToChange => SwitchesToOpen.Concat(SwitchesToClose);

		/// <summary>
		/// The solution that this move operates on
		/// </summary>
		public new PgoSolution Solution => base.Solution as PgoSolution;

		/// <summary>
		/// Returns true if the move will actually change the solution, false otherwise.
		/// </summary>
		public bool DoesSomething => SwitchesToClose.Any() || SwitchesToOpen.Any();

		/// <summary>
		/// The network configuration the move is applied to
		/// </summary>
		public NetworkConfiguration Configuration => Solution.GetPeriodSolution(Period).NetConfig;

		#endregion

		#region Construction

		/// <summary>
		/// Initializes a move
		/// </summary>
		/// <param name="solution">The solution to modify</param>
		/// <param name="period">The period to modify the solution in</param>
		/// <param name="switchesToOpen">The switches to open</param>
		/// <param name="switchesToClose">The switches to close</param>
		public ChangeSwitchesMove(PgoSolution solution, Period period, IEnumerable<Line> switchesToOpen, IEnumerable<Line> switchesToClose)
			: base(solution)
		{
			SwitchSettings switchSettings = solution.GetPeriodSolution(period).SwitchSettings;

			if (switchesToOpen.Any(s => switchSettings.IsOpen(s)))
				throw new ArgumentException("A switch to open is already open");
			if (switchesToClose.Any(s => switchSettings.IsClosed(s)))
				throw new ArgumentException("A switch to close is already closed");

			Period = period;
			SwitchesToOpen = switchesToOpen.ToList();
			SwitchesToClose = switchesToClose.ToList();
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Applies the move.
		/// </summary>
		/// <param name="propagate"></param>
		protected override void DoApply(bool propagate)
		{
			foreach (var s in SwitchesToClose)
				Solution.SetSwitch(Period, s, false);

			foreach (var s in SwitchesToOpen)
				Solution.SetSwitch(Period, s, true);
		}

		/// <summary>
		/// Applies the move's switch changes to the given configuration
		/// </summary>
		public void ApplyTo(NetworkConfiguration configuration)
		{
			foreach (var s in SwitchesToClose)
				configuration.SetSwitch(s, false);

			foreach (var s in SwitchesToOpen)
				configuration.SetSwitch(s, true);
		}

		/// <summary>
		/// Gets the reverse move
		/// </summary>
		/// <returns></returns>
		public override Move GetReverse()
		{
			return new ChangeSwitchesMove(Solution, Period, SwitchesToClose, SwitchesToOpen);
		}

		/// <summary>
		/// Returns a move that makes the same changes to the given solution as this move
		/// makes to its solution
		/// </summary>
		public override Move GetCloneFor(ISolution solution)
		{
			return new ChangeSwitchesMove((PgoSolution)solution, Period, SwitchesToOpen, SwitchesToClose);
		}

		/// <summary>
		/// Returns a description of the move
		/// </summary>
		public override string ToString()
		{
			string open = SwitchesToOpen.Select(s => s.Name).Concatenate(", ");
			string close = SwitchesToClose.Select(s => s.Name).Concatenate(", ");

			string period = "";
			if (Solution.PeriodCount > 1)
				period = $", period {Period.Index}";

			if (!SwitchesToClose.Any())
				return $"Open {open}{period}";

			if (!SwitchesToOpen.Any())
				return $"Close {close}{period}";

			return $"Open {open}, close {close}{period}";
		}

		#endregion
	}
}
