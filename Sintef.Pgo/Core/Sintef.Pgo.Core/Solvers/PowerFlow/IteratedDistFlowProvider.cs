using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A flow solver for radial network configurations.
	/// Applies the DistFlow equations iteratively on an approximate solution until
	/// it converges.
	/// 
	/// In detail, the algorithm is, for the tree below each generator:
	///  1. Initialize all line currents to zero and all bus voltages to the generator's voltage
	///  2. Update the power injected into each line, as the total consumed power plus
	///     line power losses in the subtree under the line. Line power losses are computed
	///     using the line currents: S_loss = z * I * I*
	///  3. Update all line currents and bus voltages from the generator downward, using the
	///     equations I = S_injected / V_parent; V_child = V_parent - z * I
	///  4. Repeat from step 2 until convergence
	///  
	/// The first run through steps 1-3 is the same as running the SimplifiedDistFlow algorithm, which
	/// produces a fairly good solution.
	/// The algorithms converges well because line power losses are small compared to the consumed
	/// power, so adjusting for line power losses is a small change to the total power
	/// injected into each line.
	/// 
	/// If the flow problem cannot be solved, due to too large resistances, the algorithm will diverge.
	/// This is detected, causing the flow status to be <see cref="FlowStatus.Failed"/>.
	/// </summary>
	public class IteratedDistFlowProvider : IFlowProvider
	{
		#region Public properties 

		/// <summary>
		/// The flow approximation that the flow provider uses.
		/// </summary>
		public FlowApproximation FlowApproximation => FlowApproximation.IteratedDF;

		/// <summary>
		/// Defaults options for <see cref="IteratedDistFlowProvider"/> 
		/// </summary>
		public static Options DefaultOptions => new Options();

		#endregion

		/// <summary>
		/// The options used by the provider
		/// </summary>
		private Options _options;

		/// <summary>
		/// Constructor
		/// </summary>
		public IteratedDistFlowProvider(Options options)
		{
			_options = options;
		}

		#region Public methods

		/// <summary>
		/// Computes a flow for the given flow problem
		/// </summary>
		public IPowerFlow ComputeFlow(FlowProblem flowProblem)
		{
			NetworkConfiguration configuration = flowProblem.NetworkConfig;

			if (!configuration.AllowsRadialFlow(requireConnected: false))
			{
				throw new ArgumentException("Cannot compute flow with IteratedDistFlow because the network is not radial or is missing a transformer mode");
			}

			PowerDemands demands = flowProblem.Demands;
			var flow = new RadialFlow(configuration, demands, false);

			List<FlowStatus> statuses = new List<FlowStatus>();
			int maxIterations = 0; // Used for any generator

			// Treat each generator

			foreach (var generator in configuration.Network.Providers)
			{
				int iterations = ComputeFlow(flow, generator, demands);
				maxIterations = Math.Max(maxIterations, iterations);

				// Record status and clear for next generator
				statuses.Add(flow.Status);
				flow.Status = FlowStatus.None;
			}

			// Select the worst status for any generator as the overall status
			if (statuses.Any())
				flow.Status = statuses.Min();
			else
				flow.Status = FlowStatus.Exact;

			// Finalize the status string

			string iterationInfo = $"{maxIterations} iterations";
			if (configuration.Network.Providers.Count() > 1)
				iterationInfo = $"up to {iterationInfo}";

			if (flow.StatusDetails == null)
				flow.StatusDetails = iterationInfo;
			else
				flow.StatusDetails += $" ({iterationInfo})";

			return flow;
		}

		/// <summary>
		/// Computes the flow for a single generator
		/// </summary>
		/// <param name="flow">The flow to update</param>
		/// <param name="generator">The generator to compute for</param>
		/// <param name="demands">The demands to satisfy</param>
		/// <returns>The number of iterations completed</returns>
		private int ComputeFlow(RadialFlow flow, Bus generator, PowerDemands demands)
		{
			// Initialize (step 1)

			SetInitialVoltageAndCurrent(flow, generator);

			Complex prevGeneratorPower = 0;
			int iteration = 0;

			while (flow.Status == FlowStatus.None)
			{
				if (iteration == _options.MaxIterations)
				{
					flow.Status = FlowStatus.Approximate;
					flow.StatusDetails = "Max iterations reached";
					break;
				}

				++iteration;

				// Update (steps 2 and 3)

				UpdatePower(flow, demands, generator);

				UpdateVoltageAndCurrent(flow, generator);

				// Check for convergence

				Complex generatorPower = flow.PowerInjection(generator);

				double relativeChange = 0;
				if (generatorPower != 0)
					relativeChange = ((generatorPower - prevGeneratorPower) / generatorPower).Magnitude;

				if (relativeChange.IsNanOrInfinity())
					throw new Exception("Relative change is not a real number");

				if (relativeChange < 1e-10)
					// The change in this iteration was small. Accept the solution for this generator.
					flow.Status = FlowStatus.Exact;

				prevGeneratorPower = generatorPower;

				//Console.WriteLine(generatorPower);
			}

			return iteration;
		}

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
		public IPowerFlow DisaggregateFlow(IPowerFlow aggregateFlow, NetworkAggregation aggregation,
			NetworkConfiguration originalConfiguration, PowerDemands originalDemands)
		{
			// Check input

			if (!(aggregateFlow is RadialFlow flow))
				throw new ArgumentException("This is not a flow from this provider");

			// Forward to common algorithm in SimplifiedDistFlowProvider
			return SimplifiedDistFlowProvider.Disaggregate(flow, aggregation, originalConfiguration, originalDemands);
		}

		/// <summary>
		/// Compute power flow delta for the move. Throws an exception, as deltas are not supported for iterated dist flow computations.
		/// </summary>
		/// <param name="move"></param>
		/// <returns></returns>
		public PowerFlowDelta ComputePowerFlowDelta(Move move)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Returns a description of the flow provider
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			return "Iterated DF";
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Initializes all currents to zero and all voltages to the closest upstrean tranformer or provider's voltage, in
		/// the subtree under the <paramref name="provider"/>.
		/// </summary>
		/// <param name="flow">The flow to initialize</param>
		/// <param name="provider">The provider bus</param>
		private void SetInitialVoltageAndCurrent(RadialFlow flow, Bus provider)
		{
			NetworkConfiguration configuration = flow.NetworkConfig;

			// Update in each bus, from root to leaves
			configuration.Traverse(provider, topDownAction: Update);


			void Update(Bus bus)
			{
				if (bus.IsProvider)
				{
					flow.SetVoltage(bus, bus.GeneratorVoltage);
					return;
				}

				var line = configuration.UpstreamLine(bus);
				flow.SetDownstreamCurrent(line, 0);

				if (bus.IsTransformer)
					// Voltage is undefined
					return;

				var upstream = configuration.UpstreamEnd(line);

				Complex voltage;
				if (line.IsTransformerConnection)
				{
					// Line is a transformer output line
					var mode = configuration.TransformerModeForOutputLine(line);
					voltage = mode.OutputVoltage(flow.Voltage(mode.InputBus));
				}
				else
				{
					// Regular line
					voltage = flow.Voltage(upstream);
				}

				flow.SetVoltage(bus, voltage);
			}
		}

		/// <summary>
		/// Updates the injected power in each line in the subtree starting with 
		/// the upstream line of <paramref name="startBus"/>
		/// </summary>
		/// <param name="flow">The flow to update</param>
		/// <param name="demands">The consumer demands</param>
		/// <param name="startBus"></param>
		private void UpdatePower(RadialFlow flow, PowerDemands demands, Bus startBus)
		{
			NetworkConfiguration configuration = flow.NetworkConfig;

			// Update in each bus, from leaves to root
			configuration.Traverse(startBus, bottomUpAction: Update);


			void Update(Bus bus)
			{
				// Sum up the power injected to downstream lines
				var power = new Complex();
				foreach (var line in configuration.DownstreamLines(bus))
					power += flow.PowerFlow(bus, line);

				if (bus.IsProvider)
				{
					// Set the generated power
					flow.SetGeneratedPower(bus, power);
					return;
				}

				// Add the bus' own consumption
				if (bus.IsConsumer)
					power += demands.PowerDemand(bus);

				// Add the power loss in the upstream line
				var upstreamLine = configuration.UpstreamLine(bus);

				if (upstreamLine.IsTransformerConnection)
				{
					if (!bus.IsTransformer)
					{
						// Transformer output line
						var mode = configuration.TransformerModeForOutputLine(upstreamLine);
						power /= mode.PowerFactor;
					}
				}
				else
				{
					// Normal line
					var current = flow.Current(upstreamLine);
					Complex powerLoss = upstreamLine.Impedance * current * Complex.Conjugate(current);
					if (powerLoss.Real > power.Real)
					{
						flow.Status = FlowStatus.Failed;
						flow.StatusDetails = "IteratedDistFlow diverged; power loss in line exceeds output power";
					}

					power += powerLoss;
				}


				// Set the power injected into the upstream line
				flow.SetInjectedPower(upstreamLine, power);
			}
		}

		/// <summary>
		/// Updates the bus voltages and line currents in the subtree starting
		/// at <paramref name="startBus"/>
		/// </summary>
		/// <param name="flow">The flow to update</param>
		/// <param name="startBus"></param>
		private void UpdateVoltageAndCurrent(RadialFlow flow, Bus startBus)
		{
			NetworkConfiguration configuration = flow.NetworkConfig;

			// Update in each bus, from root to leaves
			configuration.Traverse(startBus, topDownAction: Update);


			void Update(Bus upstreamBus)
			{
				// For each downstream line

				foreach (var line in flow.NetworkConfig.DownstreamLines(upstreamBus))
				{
					var downstreamBus = line.OtherEnd(upstreamBus);

					// Update the current (except in transformer output lines)

					Complex current = 0;
					if (!upstreamBus.IsTransformer)
					{
						current = Complex.Conjugate(flow.PowerFlow(upstreamBus, line) / flow.Voltage(upstreamBus));
						flow.SetDownstreamCurrent(line, current);

						if (_options.StopOnIMaxViolation && current.Magnitude > line.IMax)
						{
							flow.Status = FlowStatus.Approximate;
							flow.StatusDetails = "IMax was exceeded";
						}
					}
					// Update the voltage

					if (downstreamBus.IsTransformer)
						// Voltage is undefined
						return;

					Complex voltage;
					if (line.IsTransformerConnection)
					{
						var mode = configuration.TransformerModeForOutputLine(line);
						voltage = mode.OutputVoltage(flow.Voltage(mode.InputBus));
					}
					else
					{
						var voltageDrop = current * line.Impedance;
						Complex voltageIn = flow.Voltage(upstreamBus);
						voltage = voltageIn - voltageDrop;

						if (voltageDrop.Magnitude > voltageIn.Magnitude)
						{
							flow.Status = FlowStatus.Failed;
							flow.StatusDetails = "IteratedDistFlow diverged; line voltage drop exceeds upstream voltage";
						}
					}

					flow.SetVoltage(downstreamBus, voltage);

					if (_options.StopOnVMinViolation && voltage.Magnitude < downstreamBus.VMin)
					{
						flow.Status = FlowStatus.Approximate;
						flow.StatusDetails = "Voltage dropped below VMin";
					}

					// Update the current for transformer output lines

					if (upstreamBus.IsTransformer)
					{
						current = Complex.Conjugate(-flow.PowerFlow(downstreamBus, line) / flow.Voltage(downstreamBus));
						flow.SetDownstreamCurrent(line, current);
					}
				}
			}
		}

		/// <summary>
		/// Options for <see cref="IteratedDistFlowProvider"/>
		/// </summary>
		public class Options
		{
			/// <summary>
			/// If true, the solver stops when it detects a violation of an IMax limit,
			/// and sets <see cref="FlowStatus.Approximate"/>.
			/// </summary>
			public bool StopOnIMaxViolation { get; set; } = false;

			/// <summary>
			/// If true, the solver stops when it detects a violation of a VMin limit,
			/// and sets <see cref="FlowStatus.Approximate"/>.
			/// </summary>
			public bool StopOnVMinViolation { get; set; } = false;

			/// <summary>
			/// If true, the solver stops when it has completed this many iterations,
			/// and sets <see cref="FlowStatus.Approximate"/>.
			/// </summary>
			public int MaxIterations { get; set; } = int.MaxValue;
		}

		#endregion
	}
}
