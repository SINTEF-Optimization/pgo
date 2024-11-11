using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// An interface for mechanisms that compute the differences in flow based
	/// on changes in switch settings (e.g. based on Move's)
	/// </summary>
	interface IDeltaFlowProvider 
	{
		/// <summary>
		/// Returns the flow that will result from applying the given move,
		/// in the relevant approximation.
		/// </summary>
		/// <param name="move"></param>
		/// <returns></returns>
		NodeBasedFlow GetFlow(Move move);
	}
}
