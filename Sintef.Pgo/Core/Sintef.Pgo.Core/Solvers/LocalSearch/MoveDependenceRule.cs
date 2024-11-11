using Sintef.Scoop.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A move dependence rule for <see cref="SwapSwitchStatusMove"/>s
	/// </summary>
	internal class MoveDependenceRule : IDependenceRule
	{
		/// <summary>
		/// Returns true if applying all moves (first each of <paramref name="earlierMoves"/> and then <paramref name="lastMove"/>)
		/// cannot be done or produces a non-radial solution.
		/// Otherwise returns false.
		/// </summary>
		public bool AreDependent(IEnumerable<MoveInfo> earlierMoves, MoveInfo lastMove)
		{
			var allMoves = earlierMoves.Concat(lastMove).Select(i => i.Move);

			return !ProduceARadialSolution(allMoves);

			// We could make the moves dependent in more cases, e.g. when they affect related
			// parts of the solution. However, we need empirical data to support the decision.
		}

		/// <summary>
		/// Returns <paramref name="moveToUpdate"/> if both moves can be applied together and this gives
		/// a radial solution, null otherwise.
		/// </summary>
		public Func<Move> Update(Move moveToUpdate, Move moveToBeApplied)
		{
			if (!ProduceARadialSolution(moveToUpdate, moveToBeApplied))
				return null;

			return () =>
			{
				SwapSwitchStatusMove swapMoveToBeApplied = (SwapSwitchStatusMove)moveToBeApplied;
				SwapSwitchStatusMove swapMoveToUpdate = (SwapSwitchStatusMove)moveToUpdate;

				if (swapMoveToBeApplied.Period == swapMoveToUpdate.Period)
					// Delta flows may change when another move is applied in the same period,
					// so clear them
					swapMoveToUpdate.ClearCachedPowerFlowDelta();

				return moveToUpdate;
			};
		}

		/// <summary>
		/// Returns true if applying the given sequence of moves produces a radial solution
		/// </summary>
		private bool ProduceARadialSolution(params Move[] moves) => ProduceARadialSolution((IEnumerable<Move>)moves);

		/// <summary>
		/// Returns true if applying the given sequence of moves produces a radial solution
		/// </summary>
		private bool ProduceARadialSolution(IEnumerable<Move> moves)
		{
			var groups = moves.Cast<SwapSwitchStatusMove>().GroupBy(m => m.Period);

			return groups.All(g => ProduceARadialSolutionInOnePeriod(g));
		}

		/// <summary>
		/// Returns true if applying the given sequence of moves, which are for the same period,
		/// produces a radial solution
		/// </summary>
		private bool ProduceARadialSolutionInOnePeriod(IEnumerable<SwapSwitchStatusMove> moves)
		{
			if (moves.SelectMany(m => m.SwitchesToChange).Distinct().Count() != moves.Count() * 2)
				// Moves involving the same switches cannot be applied together
				return false;

			var configuration = moves.First().Configuration;

			// Initialize one component with the whole network
			ComponentSet components = new ComponentSet(configuration);

			// Split components due to opening switches
			foreach (var move in moves)
			{
				components.Split(move.SwitchToOpen);
			}

			// Initialize one cluster per component
			ClusterSet clusters = new ClusterSet(components);

			// Merge clusters due to closing switches
			foreach (var move in moves)
			{
				if (!clusters.Merge(move.SwitchToClose))
					// Configuration became non-radial
					return false;
			}

			return true;
		}

		#region Inner types

		/// <summary>
		/// A partition of (the buses of) the network into connected components (with respect
		/// to a given configuration).
		/// The 'main' component contains all producers and is not represented explicitly.
		/// </summary>
		private class ComponentSet
		{
			/// <summary>
			/// The configuration
			/// </summary>
			public NetworkConfiguration Configuration { get; }

			/// <summary>
			/// The components that do not contain any producer
			/// </summary>
			public List<Component> Components { get; } = new();

			/// <summary>
			/// Initializes the set with one component, containing the whole of the network
			/// </summary>
			public ComponentSet(NetworkConfiguration configuration)
			{
				Configuration = configuration;
			}

			/// <summary>
			/// Splits the component that contains <paramref name="line"/> in two:
			/// one upstream and one downstream of <paramref name="line"/>
			/// </summary>
			internal void Split(Line line)
			{
				foreach (var c in Components)
				{
					if (c.Contains(line))
					{
						Components.Remove(c);
						Components.AddRange(c.Split(line));
						return;
					}
				}

				// Line is in the main component

				// The downstream boundary of the component consists of the upstream lines for
				// existing components below line,
				var downstreamLines = Components
					.Select(c => c.UpstreamLine)
					.Where(upstream => Configuration.IsAncestor(line, upstream))
					.ToList();

				// ...except those that are downstream of another such line
				downstreamLines = downstreamLines
					.Where(downstreamLine => !downstreamLines.Any(l => Configuration.IsAncestor(l, downstreamLine)))
					.ToList();

				Components.Add(new Component(Configuration, line, downstreamLines));
			}
		}

		/// <summary>
		/// A connected component of the network. It is delimited by one upstream line and zero
		/// to many downstream lines (and also the lines that are open in the configuration).
		/// </summary>
		private class Component
		{
			/// <summary>
			/// The upstream line. The component does not contain this line, or anything upstream of it.
			/// </summary>
			public Line UpstreamLine { get; }

			/// <summary>
			/// The configuration
			/// </summary>
			private NetworkConfiguration _configuration;

			/// <summary>
			/// The upstream lines. The component does not contain these lines, or anything upstream of them.
			/// </summary>
			private List<Line> _downstreamLines;

			/// <summary>
			/// Initializes a component
			/// </summary>
			public Component(NetworkConfiguration configuration, Line upstreamLine, List<Line> downstreamLines)
			{
				_configuration = configuration;
				UpstreamLine = upstreamLine;
				_downstreamLines = downstreamLines;
			}

			/// <summary>
			/// Returns true if <paramref name="line"/> is in the component
			/// (connects two buses in the component).
			/// </summary>
			internal bool Contains(Line line)
			{
				return _configuration.IsAncestor(UpstreamLine, line) &&
					!_downstreamLines.Any(down => _configuration.IsAncestor(down, line));
			}

			/// <summary>
			/// Returns the sub-components that are created by removing the given line,
			/// which is in this component
			/// </summary>
			internal IEnumerable<Component> Split(Line line)
			{
				var downdown = _downstreamLines.Where(l => _configuration.IsAncestor(line, l)).ToList();

				// Sub-component downstream of line
				yield return new Component(_configuration, line, downdown);
				// Sub-component upstream of line
				yield return new Component(_configuration, UpstreamLine, _downstreamLines.Except(downdown).Concat(new[] { line }).ToList());
			}

			/// <summary>
			/// Returns true if <paramref name="line"/> is on the border of the component, i.e. it connects one
			/// bus is the component with one outside it.
			/// </summary>
			internal bool IsOnBorder(Line line)
			{
				if (_configuration.IsAncestorOfOneEnd(UpstreamLine, line))
				{
					// Option 1:
					// Line is on the border of the subtree below UpstreamLine, but not
					// on the border of the subtree below any of _downstreamLines
					return !_downstreamLines.Any(down => _configuration.IsAncestorOfOneEnd(down, line));
				}
				else
				{
					// Option 2:
					// Line connects two buses in the subtree below UpstreamLine, and *one* of its ends is on the
					// border of a subtree below one of _downstreamLines
					return _configuration.IsAncestor(UpstreamLine, line.Node1) &&
							_downstreamLines.Count(down => _configuration.IsAncestorOfOneEnd(down, line)) == 1;
				}
			}

			public override string ToString()
			{
				string description = $"Below {UpstreamLine}";
				if (_downstreamLines.Any())
					description += $"; above {_downstreamLines.Select(l => l.ToString()).Join(", ")}";
				return description;
			}
		}

		/// <summary>
		/// A partition of the network into clusters. Each cluster contains one or more non-main components.
		/// In addition, the main component may belong to a cluster: the one where Cluster.ContainsMainComponent is true.
		/// </summary>
		private class ClusterSet
		{
			/// <summary>
			/// The clusters
			/// </summary>
			private List<Cluster> _clusters;

			/// <summary>
			/// Initializes a cluster set with one cluster for each component in
			/// the given set. 
			/// </summary>
			public ClusterSet(ComponentSet components)
			{
				_clusters = components.Components.Select(c => new Cluster(c)).ToList();
			}

			/// <summary>
			/// Merges the two clusters that are connected by the given line
			/// </summary>
			/// <returns>True if two clusters were merged, false if the merge could not be done
			///   because the line does not connect two distinct clusters</returns>
			internal bool Merge(Line line)
			{
				if (_clusters.Any(c => c.ConnectsInternal(line)))
					// The line connects two components in the same cluster
					return false;

				// Find the clusters to be connected
				List<Cluster> cs = _clusters.Where(c => c.ConnectsExternal(line)).ToList();

				if (cs.Count == 1)
				{
					// There is only one. We must merge it with the main component
					Cluster cluster = cs[0];

					if (cluster.ContainsMainComponent)
						// Cluster already contains main compoent. Fail.
						return false;

					cluster.ContainsMainComponent = true;
					return true;
				}

				// There are two
				var (c1, c2) = (cs[0], cs[1]);

				if (c1.ContainsMainComponent && c2.ContainsMainComponent)
					// Both contain the main component. Fail.
					return false;

				// Do the merge

				_clusters.Remove(c1);
				_clusters.Remove(c2);
				_clusters.Add(c1.MergedWith(c2));

				return true;
			}
		}

		/// <summary>
		/// A cluster of one or more <see cref="Component"/>s (and possibly including the main component)
		/// </summary>
		private class Cluster
		{
			/// <summary>
			/// True if the cluster contains the main component
			/// </summary>
			public bool ContainsMainComponent { get; set; } = false;

			/// <summary>
			/// The (non-main) components in the cluster
			/// </summary>
			private List<Component> _components;

			/// <summary>
			/// Initializes a cluster that contains just the given component
			/// </summary>
			public Cluster(Component c)
			{
				_components = new List<Component> { c };
			}

			/// <summary>
			/// Initializes a cluster that contains the given components
			/// </summary>
			/// <param name="components">The non-main components</param>
			/// <param name="containsMainComponent">True if the cluster contains the main component</param>
			public Cluster(IEnumerable<Component> components, bool containsMainComponent)
			{
				_components = components.ToList();
				ContainsMainComponent = containsMainComponent;
			}

			/// <summary>
			/// Returns true if the given line connects two (non-main) components in
			/// the cluster
			/// </summary>
			internal bool ConnectsInternal(Line line)
			{
				return _components.Count(c => c.IsOnBorder(line)) == 2;
			}

			/// <summary>
			/// Returns true if the given line connects one (non-main) component in the
			/// cluster with a component outside the cluster (or with the main
			/// component)
			/// </summary>
			internal bool ConnectsExternal(Line line)
			{
				return _components.Count(c => c.IsOnBorder(line)) == 1;
			}

			/// <summary>
			/// Returns the cluster created by merging this cluster with <paramref name="other"/>
			/// </summary>
			internal Cluster MergedWith(Cluster other)
			{
				return new Cluster(_components.Concat(other._components), ContainsMainComponent || other.ContainsMainComponent);
			}

			public override string ToString()
			{
				string description = _components.Select(c => $"[{c}]").Join(", ");
				if (ContainsMainComponent)
					description += ", [main]";
				return description;
			}
		}

		#endregion
	}
}