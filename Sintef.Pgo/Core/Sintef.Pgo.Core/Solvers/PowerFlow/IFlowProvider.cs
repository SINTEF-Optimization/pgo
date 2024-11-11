using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Computes a flow based on switch settings and problem parameters,
	/// and optionally also based on a previously existing flow.
	/// </summary>
	public interface IFlowProvider
	{
		/// <summary>
		/// Computes a flow based on switch settings and problem parameters taken from the given solution..
		/// </summary>
		/// <param name="flowProblem">The flow problem to create a flow for</param>
		/// <returns>The resulting flow</returns>
		IPowerFlow ComputeFlow(FlowProblem flowProblem);

		/// <summary>
		/// The flow approximation that the flow provider uses.
		/// </summary>
		FlowApproximation FlowApproximation { get; }

		/// <summary>
		/// Computes the changes in flow that will result from the given move. 
		/// </summary>
		PowerFlowDelta ComputePowerFlowDelta(Move move);

		/// <summary>
		/// Disaggregates a power flow on an aggregated network, to produce
		/// an equivalent flow on the original network
		/// </summary>
		/// <param name="aggregateFlow">The flow to disaggregate</param>
		/// <param name="aggregation">The network aggregation</param>
		/// <param name="originalConfiguration">The network configuration to make a flow for, on
		///   the original network</param>
		/// <param name="originalDemands">The power demands to make a flow for, on
		///   the original network</param>
		/// <returns>The disagreggated flow, on the original network</returns>
		IPowerFlow DisaggregateFlow(IPowerFlow aggregateFlow, NetworkAggregation aggregation,
			NetworkConfiguration originalConfiguration, PowerDemands originalDemands);
	}
}
