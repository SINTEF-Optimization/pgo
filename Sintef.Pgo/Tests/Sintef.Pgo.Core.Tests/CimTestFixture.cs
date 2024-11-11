using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.DataContracts;
using System.Collections.Generic;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// A fixture for processing problems read from files in CIM format, 
	/// with DIGIN as the primary example
	/// </summary>
	public class CimTestFixture
	{
		/// <summary>
		/// A CIM parser that reads all DIGIN network data
		/// </summary>
		protected static CimJsonParser _diginNetworkParser;

		/// <summary>
		/// Converts from _diginNetworkParser to _network
		/// </summary>
		protected CimNetworkConverter _networkConverter;

		/// <summary>
		/// The network created from CIM data
		/// </summary>
		protected PowerNetwork _network;

		/// <summary>
		/// The demands read from CIM SSH data
		/// </summary>
		protected PowerDemands _demands;

		/// <summary>
		/// The network configuration read from CIM SSH data
		/// </summary>
		protected NetworkConfiguration _configuration;

		/// <summary>
		/// The flow provider we use
		/// </summary>
		protected IteratedDistFlowProvider _flowProvider;

		/// <summary>
		/// The encoding for the unaggregated network
		/// </summary>
		protected PgoProblem _originalEncoding;

		/// <summary>
		/// The aggregator used
		/// </summary>
		protected NetworkAggregation _aggregator;

		/// <summary>
		/// The aggregated network
		/// </summary>
		protected PowerNetwork _aggregateNetwork;

		/// <summary>
		/// The encoding for the aggregated network
		/// </summary>
		protected PgoProblem _aggregateEncoding;

		/// <summary>
		/// The initial radial solution for the aggregated network
		/// </summary>
		protected PgoSolution _aggregateSolution;

		/// <summary>
		/// The disaggregated version of _aggregateSolution
		/// </summary>
		protected PgoSolution _disaggregatedSolution;

		[TestInitialize]
		public void Setup()
		{
			_diginNetworkParser ??= TestUtils.ParseAllDiginNetworkData();
		}

		/// <summary>
		/// Converts from the data in the given parser to _network.
		/// If <paramref name="parser"/> is null, uses  _diginNetworkParser.
		/// </summary>
		/// <param name="lineImpedanceScaleFactor"><see cref="CimNetworkConversionOptions.LineImpedanceScaleFactor"/></param>
		/// <param name="consumerMinVoltageFactor"><see cref="CimNetworkConversionOptions.ConsumerMinVoltageFactor"/></param>
		protected void ConvertNetwork(CimJsonParser parser = null, double lineImpedanceScaleFactor = 1.0,
		/// <param name="consumerMaxVoltageFactor"><see cref="CimNetworkConversionOptions.ConsumerMaxVoltageFactor"/></param>
			double? consumerMinVoltageFactor = null, double? consumerMaxVoltageFactor = null)
		{
			var options = new CimNetworkConversionOptions()
			{
				ConsumerSources = new() { CimConsumerSource.EnergyConsumers },
				LineImpedanceScaleFactor = lineImpedanceScaleFactor,
				ConsumerMinVoltageFactor = consumerMinVoltageFactor,
				ConsumerMaxVoltageFactor = consumerMaxVoltageFactor,
			};

			parser ??= _diginNetworkParser;
			_networkConverter = CimNetworkConverter.FromParser(parser, options);
			_network = _networkConverter.CreateNetwork();
		}

		/// <summary>
		/// Reads the DIGIN SSH data and converts into _demands
		/// </summary>
		protected void ReadAndConvertDiginDemands()
		{
			var steadyStateParser = TestUtils.ParseDiginSteadyStateData();
			var converter = new CimDemandsConverter(_network, _networkConverter.NetworkParser);
			_demands = converter.ToPowerDemands(steadyStateParser);
		}

		/// <summary>
		/// Reads the DIGIN SSH data and converts into _configuration
		/// </summary>
		protected void ReadAndConvertDiginConfiguration()
		{
			var steadyStateParser = TestUtils.ParseDiginSteadyStateData();
			var converter = new CimConfigurationConverter(_networkConverter);
			_configuration = converter.ToNetworkConfiguration(steadyStateParser);
		}

		/// <summary>
		/// Creates the original and the aggregated single period encoding,
		/// from the network and demands, a radial aggregated solution and
		/// the corresponding disaggregated solution.
		/// </summary>
		protected void CreateEncodingAndSolution()
		{
			var periodData = new PeriodData(_network, _demands, Period.Default);
			var periodDemands = new List<PeriodData> { periodData };

			NetworkConfiguration startConfig = null;
			_flowProvider = new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions);
			_originalEncoding = new PgoProblem(periodDemands, _flowProvider, "fromJson", startConfig);

			_aggregator = NetworkAggregation.MakeAcyclicAndConnected(_network);
			_aggregateNetwork = _aggregator.AggregateNetwork;
			_aggregateEncoding = _originalEncoding.CreateAggregatedProblem(_aggregator, true);

			_aggregateSolution = new PgoSolution(_aggregateEncoding);
			_aggregateSolution.MakeRadialFlowPossible();

			_disaggregatedSolution = PgoSolution.Disaggregate(_aggregateSolution, _originalEncoding, _aggregator);
		}
	}
}