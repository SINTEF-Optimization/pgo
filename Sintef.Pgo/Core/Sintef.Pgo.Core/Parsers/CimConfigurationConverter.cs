using Newtonsoft.Json.Linq;
using Sintef.Pgo.Cim;
using Sintef.Pgo.DataContracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sintef.Pgo.Core.IO
{
	/// <summary>
	/// Creates a <see cref="NetworkConfiguration"/> from CIM data that has been parsed by
	/// a <see cref="CimJsonParser"/>
	/// </summary>
	public class CimConfigurationConverter
	{
		/// <summary>
		/// The converter that created the power network
		/// </summary>
		private CimNetworkConverter _networkConverter;

		/// <summary>
		/// Initializes the converter
		/// </summary>
		/// <param name="networkConverter">The converter that created the power network</param>
		/// 
		public CimConfigurationConverter(CimNetworkConverter networkConverter)
		{
			_networkConverter = networkConverter;
		}

		/// <summary>
		/// Converts the given SSH CIM dataset to a <see cref="NetworkConfiguration"/>
		/// </summary>
		public NetworkConfiguration ToNetworkConfiguration(JObject startConfiguration)
		{
			// Read CIM data
			var parser = new CimJsonParser(_networkConverter.NetworkParser.Units);
			parser.Parse(startConfiguration);
			parser.CreateCimObjects();

			return ToNetworkConfiguration(parser);
		}

		/// <summary>
		/// Creates and returns the network configuration from the CIM objects in the given parser
		/// </summary>
		public NetworkConfiguration ToNetworkConfiguration(CimJsonParser parser)
		{
			return ToNetworkConfiguration(ToCimConfiguration(parser));
		}

		/// <summary>
		/// Extracts a <see cref="CimConfiguration"/> from the CIM objects in the given parser
		/// </summary>
		public CimConfiguration ToCimConfiguration(CimJsonParser parser)
		{
			foreach (var consumer in parser.CreatedObjects<IdentifiedObject>())
			{
				parser.FillMridIfMissing(consumer, _networkConverter.NetworkParser);
			}

			var cimConfig = CimConfiguration.FromObjects(parser.CreatedObjects<IdentifiedObject>());
			return cimConfig;
		}

		/// <summary>
		/// Converts the given configuration to a <see cref="NetworkConfiguration"/>
		/// </summary>
		public NetworkConfiguration ToNetworkConfiguration(CimConfiguration configuration)
		{
			return ToNetworkConfiguration(configuration.Switches);
		}

		/// <summary>
		/// Converts the given switch settings to a <see cref="NetworkConfiguration"/>
		/// </summary>
		public NetworkConfiguration ToNetworkConfiguration(List<Switch> switches)
		{
			// Check input

			if (switches == null)
				throw new ArgumentException("The list of switches is null");
			if (switches.Contains(null))
				throw new ArgumentException("The list of switches contains null");

			foreach (var theSwitch in switches)
			{
				if (theSwitch.MRID == null)
					throw new ArgumentException("A switch has null MRID");
			}

			if (switches.Select(x => x.MRID).Distinct().Count() < switches.Count)
			{
				var objectWithDuplicateMrid = switches.GroupBy(x => x.MRID).First(g => g.Count() > 1).First();
				throw new Exception($"{objectWithDuplicateMrid.Describe()}: Occurs more than once");
			}

			var network = _networkConverter.Network;

			foreach (var theSwitch in switches)
			{
				if (!network.TryGetLine(theSwitch.MRID, out var line))
					throw new Exception($"{theSwitch.Describe()}: Not found in the network");

				if (theSwitch.Open == null)
					throw new Exception($"{theSwitch.Describe()}: The 'open' property is missing");

				if (!line.IsSwitchable && theSwitch.Open == true)
					throw new Exception($"{theSwitch.Describe()}: Opening a non-controllable switch is not supported");
			}

			if (network.SwitchableLines.Select(s => s.Name)
				.Except(switches.Select(s => s.MRID))
				.FirstOrDefault() is string missingMrid)
			{
				throw new Exception($"No setting is given for controllable switch with MRID '{missingMrid}'");
			}

			// Ok, create switch settings

			var switchSettings = new SwitchSettings(network);

			foreach (var theSwitch in switches)
			{
				var line = network.GetLine(theSwitch.MRID);
				if (line.IsSwitchable)
					switchSettings.SetSwitch(line, theSwitch.Open.Value);
			}

			return new NetworkConfiguration(network, switchSettings);
		}
	}
}
