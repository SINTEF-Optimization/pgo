using Sintef.Pgo.Cim;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// A fixture for tests that build their model using <see cref="CimBuilder"/>
	/// </summary>
	public class CimBuilderTestFixture
	{
		/// <summary>
		/// The builder from whose data the network is created
		/// </summary>
		protected CimBuilder _networkBuilder { get; set; } = new CimBuilder();

		/// <summary>
		/// Options for creating the network
		/// </summary>
		protected CimNetworkConversionOptions _networkOptions = new();

		/// <summary>
		/// The created network
		/// </summary>
		protected PowerNetwork _network;

		/// <summary>
		/// The builder from whose data the demands are created
		/// </summary>
		protected CimBuilder _demandsBuilder { get; set; } = new CimBuilder();

		/// <summary>
		/// The created demands
		/// </summary>
		protected PowerDemands _demands;

		/// <summary>
		/// Creates the network, from the data in _networkBuilder
		/// </summary>
		protected void ConvertNetwork()
		{
			var converter = new CimNetworkConverter(_networkBuilder.Network, _networkOptions);
			_network = converter.CreateNetwork();
		}

		/// <summary>
		/// Creates the demands, from the data in _demandsBuilder
		/// </summary>
		protected void ConvertDemands()
		{
			var converter = new CimDemandsConverter(_network, null);
			_demands = converter.ToPowerDemands(_demandsBuilder.Demands);
		}
	}
}