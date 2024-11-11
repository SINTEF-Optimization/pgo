using Sintef.Scoop.Kernel;
using System;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A solution to a <see cref="FlowProblem"/>
	/// </summary>
	public class FlowSolution : Solution
	{
		/// <summary>
		/// The problem
		/// </summary>
		public FlowProblem Problem => Encoding as FlowProblem;

		/// <summary>
		/// The flow that defines the solution
		/// </summary>
		public IPowerFlow Flow { get; }

		/// <summary>
		/// Initializes a solution
		/// </summary>
		/// <param name="problem">The problem</param>
		/// <param name="flow">The flow</param>
		public FlowSolution(FlowProblem problem, IPowerFlow flow)
			: base(problem)
		{
			Flow = flow;
		}

		/// <summary>
		/// Clones the solution; not implemented
		/// </summary>
		public override ISolution Clone()
		{
			throw new NotImplementedException();
		}
	}
}
