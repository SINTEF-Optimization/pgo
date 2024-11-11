using System.IO;
using Moq;
using System.Collections.Generic;
using System;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.Server;

namespace Sintef.Pgo.REST.Tests
{
	internal class TestServer : IServer
	{
		public ISession CreateCIMSession(string id, string problemName, Stream powerDemands, Stream currentConfiguration)
		{
			using (var demandsReader = new StreamReader(powerDemands, System.Text.Encoding.UTF8, true, 1024, true))
			using (var configReader = new StreamReader(currentConfiguration, System.Text.Encoding.UTF8, true, 1024, true))
			{
				string powerDemandsContent = demandsReader.ReadToEnd();
				string currentConfigurationContent = configReader.ReadToEnd();
				var session = new CIMSessionInformation(id, problemName, powerDemandsContent, currentConfigurationContent);
				LastCreatedCIMSession = session;
				return new Mock<ISession>().Object;
			}
		}

		public ISession CreateJsonSession(string id, string networkName, Stream problemDefinition,
			Stream currentConfiguration = null, bool allowUnspecifiedConsumerDemands = false)
		{
			using (var problemReader = new StreamReader(problemDefinition, System.Text.Encoding.UTF8, true, 1024, true))
			{
				string currentConfigurationContent = null;
				if (currentConfiguration != null)
				{
					using (var configReader = new StreamReader(currentConfiguration, System.Text.Encoding.UTF8, true, 1024, true))
					{
						currentConfigurationContent = configReader.ReadToEnd();
					}
				}

				string problemContent = problemReader.ReadToEnd();
				var session = new JsonSessionInformation(id, problemContent, currentConfigurationContent);
				LastCreatedJsonSession = session;
				return new Mock<ISession>().Object;
			}
		}

		public void DeleteSession(string id)
		{
			throw new System.NotImplementedException();
		}

		public ISession GetSession(string id)
		{
			throw new System.NotImplementedException();
		}

		public void LoadNetworkFromJson(string id, Stream inputFile, Action<PowerNetwork> action)
		{
			using (var reader = new StreamReader(inputFile, System.Text.Encoding.UTF8, true, 1024, true))
			{
				Network = reader.ReadToEnd();
			}
		}

		public void LoadNetworkFromRDF(string networkName, Stream networkDescriptionCimXml, Stream limitStream = null)
		{
			using (var reader = new StreamReader(networkDescriptionCimXml, System.Text.Encoding.UTF8, true, 1024, true))
			{
				NetworkName = networkName;
				Network = reader.ReadToEnd();
			}
		}

		/// <summary>
		/// Returns an already loaded network.
		/// </summary>
		/// <returns></returns>
		public PowerGrid GetNetwork() => throw new NotImplementedException();


		public string AnalyseNetwork(string id, bool verbose)
		{
			throw new NotImplementedException();
		}

		public PowerGrid GetNetwork(string id)
		{
			throw new NotImplementedException();
		}

		public void DeleteNetwork(string id)
		{
			throw new NotImplementedException();
		}

		public NetworkConnectivityStatus GetNetworkConnectivityStatus(string id)
		{
			throw new NotImplementedException();
		}

		public void CleanUpSessions()
		{
			throw new NotImplementedException();
		}

		public void LoadCimNetworkFromJsonLd(string id, CimJsonLdNetworkData networkData, Action<PowerNetwork> action = null)
		{
			throw new NotImplementedException();
		}

		public ISession CreateCimSession(string sessionId, CimJsonLdSessionParameters parameters)
		{
			throw new NotImplementedException();
		}

		public CimNetworkConverter GetCimNetworkConverter(string networkId)
		{
			throw new NotImplementedException();
		}

		internal CIMSessionInformation LastCreatedCIMSession { get; private set; }

		internal JsonSessionInformation LastCreatedJsonSession { get; private set; }

		public string Network { get; private set; }

		public IEnumerable<string> SessionIds => new string[0];

		public string NetworkName { get; set; }

		public IEnumerable<string> NetworkIds { get; set; } = new string[0];
	}

	internal class TestMultiServer : IMultiUserServer
	{
		private IServer _server;

		public TestMultiServer(IServer server)
		{
			_server = server;
		}

		public void CleanUpSessions()
		{
			throw new NotImplementedException();
		}

		public void Clear()
		{
			throw new NotImplementedException();
		}

		public IServer ServerFor(string value, Server.Server.SessionType type) => _server;
	}
}
