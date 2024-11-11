using Newtonsoft.Json.Linq;
using Sintef.Pgo.Cim;
using System;
using System.Collections.Generic;
using System.Linq;
using Sintef.Pgo.DataContracts;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.IO
{
	/// <summary>
	/// Converts between <see cref="PgoSolution"/>, <see cref="CimSolution"/>
	/// and <see cref="CimJsonLdSolution"/>
	/// </summary>
	public class CimSolutionConverter
	{
		/// <summary>
		/// The CIM network
		/// </summary>
		private CimNetworkConverter _networkConverter;

		/// <summary>
		/// Initializes the converter
		/// </summary>
		/// <param name="cimNetworkConverter">The converter that created the network to use</param>
		public CimSolutionConverter(CimNetworkConverter cimNetworkConverter)
		{
			_networkConverter = cimNetworkConverter;
		}

		/// <summary>
		/// Convert the given solution to the corresponding <see cref="PgoSolution"/>
		/// </summary>
		/// <param name="encoding">The encoding to create a solution for</param>
		/// <param name="solution">The solution to convert</param>
		public PgoSolution ConvertToPgo(CimJsonLdSolution solution, PgoProblem encoding)
		{
			CimSolution cimSolution = ConvertToCim(solution);

			return ConvertToPgo(cimSolution, encoding);
		}

		/// <summary>
		/// Convert the given solution to the corresponding <see cref="PgoSolution"/>
		/// </summary>
		/// <param name="encoding">The encoding to create a solution for</param>
		/// <param name="cimSolution">The solution to convert</param>
		public PgoSolution ConvertToPgo(CimSolution cimSolution, PgoProblem encoding)
		{
			if (cimSolution.PeriodSolutions == null)
				throw new ArgumentException("The list if period solutions is null");
			if (cimSolution.PeriodSolutions.Contains(null))
				throw new ArgumentException("The list if period solutions contains null");

			if (cimSolution.PeriodSolutions.Count != encoding.PeriodCount)
				throw new ArgumentException($"The problem has {encoding.PeriodCount} periods, but the list of period solutions has {cimSolution.PeriodSolutions.Count} elements");

			var configurationConverter = new CimConfigurationConverter(_networkConverter);

			Dictionary<Period, NetworkConfiguration> periodConfigurations = new();

			foreach (var (period, periodSolution) in encoding.Periods.Zip(cimSolution.PeriodSolutions))
			{
				try
				{
					periodConfigurations[period] = configurationConverter.ToNetworkConfiguration(periodSolution.Switches);
				}
				catch (Exception ex)
				{
					throw new ArgumentException($"Period '{period.Id}': {ex.Message}");
				}
			}

			return new PgoSolution(encoding, periodConfigurations);
		}

		/// <summary>
		/// Convert the given solution to the corresponding <see cref="CimSolution"/>
		/// </summary>
		public CimSolution ConvertToCim(IPgoSolution solution)
		{
			var cimSolution = new CimSolution();

			cimSolution.PeriodSolutions = solution.SinglePeriodSolutions
				.Select(periodSolution => ConvertToCim(periodSolution))
				.ToList();

			return cimSolution;
		}

		/// <summary>
		/// Convert the given solution to the corresponding <see cref="CimJsonLdSolution"/>
		/// </summary>
		/// <param name="cimSolution">The solution to convert</param>
		/// <param name="metadata">The metadata to give to the solution in each period</param>
		public CimJsonLdSolution ConvertToCimJsonLd(CimSolution cimSolution, IEnumerable<CimJsonExporter.SolutionMetadata> metadata)
		{
			if (_networkConverter.NetworkParser == null)
				throw new InvalidOperationException("Cannot export solution to JSON-LD: URIs are unknown since the network was not created from JSON-LD data.");

			var exporter = new CimJsonExporter(_networkConverter.NetworkParser);

			CimJsonLdSolution result = new();

			foreach (var (periodSolution, meta) in cimSolution.PeriodSolutions.Zip(metadata, (x, y) => (x, y)))
			{
				JObject solutionAsJObject = exporter.ToJson(periodSolution, meta);

				result.PeriodSolutions.Add(solutionAsJObject);
			}

			return result;
		}

		/// <summary>
		/// Convert the given solution to the corresponding <see cref="CimSolution"/>
		/// </summary>
		private CimSolution ConvertToCim(CimJsonLdSolution solution)
		{
			CimSolution cimSolution = new();

			foreach (var periodSolution in solution.PeriodSolutions)
			{
				var periodParser = new CimJsonParser(_networkConverter.NetworkParser.Units);
				periodParser.Parse(periodSolution);
				periodParser.CreateCimObjects();
				cimSolution.PeriodSolutions.Add(CreateCimPeriodSolution(periodParser));
			}

			return cimSolution;
		}

		/// <summary>
		/// Creates a <see cref="CimPeriodSolution"/> from the CIM objects in the
		/// given parser.
		/// </summary>
		private CimPeriodSolution CreateCimPeriodSolution(CimJsonParser parser)
		{
			foreach (var consumer in parser.CreatedObjects<IdentifiedObject>())
			{
				parser.FillMridIfMissing(consumer, _networkConverter.NetworkParser);
			}

			return new CimPeriodSolution
			{
				Switches = parser.CreatedObjects<Switch>().ToList(),
			};
		}

		/// <summary>
		/// Convert the given period solution to the corresponding <see cref="CimPeriodSolution"/>
		/// </summary>
		private CimPeriodSolution ConvertToCim(PeriodSolution periodSolution)
		{
			var network = periodSolution.Network;

			var result = new CimPeriodSolution();

			// For each switch in the CIM network, create a corresponding
			// switch object, with the Open flag set to the solution value

			foreach (var theSwitch in _networkConverter.CimNetwork.Switches)
			{
				var solutionSwitch = theSwitch.Clone();

				var line = network.GetLine(theSwitch.MRID);

				solutionSwitch.Open = line.IsSwitchable && periodSolution.SwitchSettings.IsOpen(line);

				result.Switches.Add(solutionSwitch);
			}

			return result;
		}
	}
}