using System;
using System.Collections.Generic;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Interface for classes that can create a flow provider
	/// </summary>
	public interface IFlowProviderFactory
	{
		/// <summary>
		/// Creates a flow provider using the given approximation.
		/// Throws an exception if the approximation is not supported
		/// </summary>
		IFlowProvider CreateFlowProvider(FlowApproximation approximation);
	}

	/// <summary>
	/// The default flow provider factory.
	/// Does not support LP-based providers, since they require libraries
	/// that are not .NET Standard.
	/// </summary>
	public class DefaultFlowProviderFactory : IFlowProviderFactory
	{
		/// <summary>
		/// Creates a flow provider using the given approximation.
		/// Throws an exception if the approximation is not supported
		/// </summary>
		public virtual IFlowProvider CreateFlowProvider(FlowApproximation approximation)
		{
			switch (approximation)
			{
				case FlowApproximation.SimplifiedDF:
					return new SimplifiedDistFlowProvider();
				case FlowApproximation.IteratedDF:
					return new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions);
				default:
					throw new ArgumentException($"Unknown or unsupported approximator type: {approximation}");
			}
		}
	}
}
