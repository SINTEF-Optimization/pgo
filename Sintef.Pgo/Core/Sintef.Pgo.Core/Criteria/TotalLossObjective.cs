using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Computes total loss in the edges, which in practice is closely related to
	/// cost. This objective should be used for simplified DistFlow.
	/// For multi-period problems, the cost is summed over all period solutions.
	/// </summary>
	public class TotalLossObjective : FlowDependentCriterion, ICanCloneForAggregateProblem
	{
		/// <summary>
		/// Caches the total power loss per period
		/// </summary>
		PerPeriodValueCacheClient _lossCache;

		/// <summary>
		/// Default constructor
		/// </summary>
		public TotalLossObjective(IFlowProvider flowProvider)
			: base(flowProvider)
		{
			Name = "Total loss (MWh)";

			_lossCache = new PerPeriodValueCacheClient(PeriodPowerLoss);
		}

		/// <summary>
		/// Clones the criterion to be valid for an aggregate of the given
		/// <paramref name="originalProblem"/>.
		/// </summary>
		/// <param name="originalProblem">The original problem</param>
		/// <param name="aggregateProblem">The aggregate problem</param>
		public ICanCloneForAggregateProblem CloneForAggregateProblem(PgoProblem originalProblem, PgoProblem aggregateProblem)
		{
			return new TotalLossObjective(FlowProvider);
		}

		/// <summary>
		/// Creates a new criterion equivalent to this one, except that it uses the given 
		/// flow provider instead
		/// </summary>
		public override FlowDependentCriterion WithProvider(IFlowProvider flowProvider)
		{
			return new TotalLossObjective(flowProvider);
		}

		/// <summary>
		/// Compute the total loss objective value.
		/// </summary>
		/// <param name="solution"></param>
		/// <returns></returns>
		public override double Value(ISolution solution)
		{
			var mySolution = (PgoSolution)solution;

			if (!mySolution.IsComplete(FlowProvider))
				return 0;

			return _lossCache.Values(mySolution).Values.Sum();
		}

		/// <summary>
		/// Returns true.
		/// </summary>
		public override bool IsSatisfied(ISolution solution) => true;

		/// <summary>
		/// Conversion factor from SI to the output unit MWh.
		/// </summary>
		private const double UnitMWh = 1.0 / (1.0e6 * 3600.0);

		/// <summary>
		/// Approximate delta value of total loss based on "simplified DistFlow" as 
		/// presented in Baran and Wu, Network reconfiguration in distribution systems for loss reduction and load balancing", 1989.
		/// </summary>
		/// <param name="move"></param>
		/// <returns></returns>
		public override double DeltaValue(Move move)
		{
			if (!(move is SwapSwitchStatusMove swapMove))
				throw new NotImplementedException("Can only provide delta value for SwapSwitchStatusMove");

			PowerFlowDelta pfd = swapMove.GetCachedPowerFlowDelta(FlowProvider);
			var power = DeltaPowerLoss(swapMove, pfd);
			var time = swapMove.Period.Length;
			return power * time.TotalSeconds * UnitMWh;
		}

		private static double DeltaPowerLoss(SwapSwitchStatusMove swapMove, PowerFlowDelta pfd)
		{
			double powerLoss = 0;
			Dictionary<Transformer, List<PowerFlowDelta.LinePowerFlowDelta>> deltasByTransformer = null;

			// Go through the delta for each line

			foreach (var (line, flowDelta) in pfd.LineDeltas)
			{
				if (!line.IsTransformerConnection)
				{
					// For a normal line, add the change in line power loss
					powerLoss += line.Resistance * (Complex.Conjugate(flowDelta.NewCurrent) * flowDelta.NewCurrent).Real
								- line.Resistance * (Complex.Conjugate(flowDelta.OldCurrent) * flowDelta.OldCurrent).Real;
					continue;
				}

				// For a transformer, record the delta
				deltasByTransformer = deltasByTransformer ?? new Dictionary<Transformer, List<PowerFlowDelta.LinePowerFlowDelta>>();
				deltasByTransformer.AddOrNew(line.Transformer, flowDelta);
			}

			if (deltasByTransformer == null)
				// No changes in power flow through any transformer
				return powerLoss;

			// Go through each transformer whose flow changed

			foreach (var (transformer, deltas) in deltasByTransformer)
			{
				Bus bus = transformer.Bus;

				// Find the old and new upstream line
				var oldUpstream = swapMove.Configuration.UpstreamLine(bus);
				var newUpstream = swapMove.NewUpstreamLine(bus);

				foreach (var delta in deltas)
				{
					if (delta.Line != oldUpstream)
					{
						// This line was downstream before the move. Subtract the old power loss
						var oldMode = transformer.ModeFor(oldUpstream, delta.Line);
						powerLoss -= delta.OldPowerFlowFrom(bus).Real * (1 - oldMode.PowerFactor);
					}
					if (delta.Line != newUpstream)
					{
						// This line is downstream after the move. Add the new power loss
						var newMode = transformer.ModeFor(newUpstream, delta.Line);
						powerLoss += delta.NewPowerFlowFrom(bus).Real * (1 - newMode.PowerFactor);
					}
				}
			}

			return powerLoss;
		}


		/// <summary>
		/// Returns the total power loss in the given period solution
		/// </summary>
		private double PeriodPowerLoss(PeriodSolution solution)
		{
			IPowerFlow flow = solution.Flow(FlowProvider);
			var power = solution.PresentLines.Sum(l => flow.PowerLoss(l));
			var time = solution.Period.Length;
			return power * time.TotalSeconds * UnitMWh;
		}

		private static void Write(PgoSolution solution, string s, IEnumerable<Line> lines)
		{
			throw new NotImplementedException("If anyone is going to use this function, it should also write for each period.");
			//Console.WriteLine();
			//Console.WriteLine($"{s}: {lines.Count()}");
			//foreach (var line in lines)
			//{
			//	Console.WriteLine($"{line}: {solution.Flow.PowerLoss(line)}");
			//}
		}
	}
}
