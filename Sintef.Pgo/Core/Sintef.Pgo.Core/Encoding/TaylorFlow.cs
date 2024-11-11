using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;


namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Represents all physical variables computed by the
	/// Taylor flow approximation.
	/// </summary>
	public class TaylorFlow : NodeBasedFlow
	{
		#region Construction

		/// <summary>
		/// Constructor
		/// </summary>
		public TaylorFlow(NetworkConfiguration networkConfig, PowerDemands demands) : base(networkConfig, demands)
		{
		}

		/// <summary>
		/// Copy constructor
		/// </summary>
		/// <param name="other"></param>
		/// <param name="configuration">The configuration to use in the new flow</param>
		public TaylorFlow(TaylorFlow other, NetworkConfiguration configuration) : base(other, configuration)
		{
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Implement Current flow using assumptions in Taylor flow
		/// </summary>
		public override Complex Current(Line line)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// The power loss in the line.
		/// </summary>
		/// <param name="line"></param>
		/// <returns></returns>
		public override double PowerLoss(Line line)
		{
			throw new NotImplementedException("TaylorFlow.GetPowerLoss not implemented");
		}

		/// <summary>
		/// Returns the power that flows from the given <paramref name="bus"/>
		/// into the given <paramref name="line"/>.
		/// If the bus actually receives power from the line, the result
		/// ('s real part) will be negative. The unit is VA.
		/// </summary>
		/// <param name="bus">The bus from which the power flows into the line</param>
		/// <param name="line">The line into which the power flows</param>
		public override Complex PowerFlow(Bus bus, Line line)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Returns a clone of this flow
		/// </summary>
		/// <param name="configuration">The configuration to use in the new flow</param>
		public override IPowerFlow Clone(NetworkConfiguration configuration) => new TaylorFlow(this, configuration);

		#endregion
	}
}
