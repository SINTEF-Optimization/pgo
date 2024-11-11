using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A distance measure that counts the number of switches that are different in the
	/// two solutions
	/// </summary>
	public class DifferentSwitchSettingsDistanceMeasure : IDistanceMeasure
	{
		#region Public properties 

		/// <summary>
		/// A short description of the distance measure.
		/// </summary>
		public string Name => "Number of different switches.";



		#endregion

		#region Private data members

		#endregion

		#region Construction

		/// <summary>
		/// Constructor
		/// </summary>
		public DifferentSwitchSettingsDistanceMeasure()
		{
			
		}

		#endregion

		#region Public methods

		/// <summary>
		/// The difference in distance resulting from the given move away from the given solution.
		/// Not yet implemented.
		/// </summary>
		/// <param name="m"></param>
		/// <param name="away_from"></param>
		/// <returns></returns>
		public double DeltaDistance(Move m, ISolution away_from)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// The distance between the two given solutions, i.e. the number of
		/// switches that have a different setting in the two.
		/// Assumes that both solutions are for the same encoding, and that both are of type <see cref="PgoSolution"/>.
		/// </summary>
		/// <param name="from"></param>
		/// <param name="to"></param>
		/// <returns></returns>
		public double Distance(ISolution from, ISolution to)
		{
			if (from == to)
				return 0;

			if (from.Encoding != to.Encoding)
				throw new ArgumentException("The solutions must have the same encoding");

			if (from is PgoSolution fMsol)
			{
				PgoSolution tMsol = to as PgoSolution;
				Debug.Assert(tMsol != null, "Wrong kind of solution given to DifferentSwitchSettingsDistanceMeasure.Distance");

				return fMsol.Problem.Periods.Sum(period =>
					fMsol.GetPeriodSolution(period).NumberOfDifferentSwitches(tMsol.GetPeriodSolution(period)));
			}
			else
				throw new Exception("DifferentSwitchSettingsDistanceMeasure: Unknown solution type");
		}

		/// <summary>
		/// The maximum distance that you can have for any pair of feasible solutions.
		/// Returns the number of switches, minus the number of switches that must
		/// be opened in any solution to get a radial solution with one sub station in each tree.
		/// We here assume that all components are connected if all switches are closed (since otherwise this would actually be more than one independent problem).
		/// </summary>
		/// <param name="encoding"></param>
		/// <returns></returns>
		public double MaxDistance(Encoding encoding)
		{
			PgoProblem prob = encoding as PgoProblem;
			Debug.Assert(prob != null, "Wrong kind of encoding given to DifferentSwitchSettingsDistanceMeasure.MaxDistance");
			PowerNetwork network = prob.Network;
			return network.SwitchableLines.Count() - network.Providers.Count() + 1;

			//TODO: We can probably do a better analysis to lower this number.
		}


		#endregion

		#region Private methods

		#endregion
	}
}
