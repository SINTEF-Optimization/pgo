using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Linq;
using System.Numerics;
using System.IO;
using Newtonsoft.Json;
using System;
using Sintef.Scoop.Utilities;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core.Test;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.REST.Client;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for catching errors in the input data (network, forecast etc.) and that
	/// we produce helpful error messages
	/// </summary>
	public class InputConsistencyTests_RestApi : LiveServerFixture
	{
		RestAPIVariationTester _tester;
		JsonSerializerSettings _jsonOptions;

		public InputConsistencyTests_RestApi(ITestOutputHelper output)
			: base(output)
		{
			CreateDefaultClient();

			_tester = new RestAPIVariationTester() { WriteLine = output.WriteLine };

			_jsonOptions = new JsonSerializerSettings();
			_jsonOptions.ContractResolver = new IgnoreRequiredContractResolver();
			_jsonOptions.MissingMemberHandling = MissingMemberHandling.Ignore;
		}

		[Fact]
		public void ErrorsInNetworkDefinitionAreReported()
		{
			PowerGrid network = null;

			_tester.RunVariationChecks(Setup, Test, "An error occurred while parsing the network data: "
				//, variationToRun: 46
				);


			VariationTester.ActionSets Setup()
			{
				network = InputConsistencyTests.CreateTestNetwork(out var actions);
				return actions;
			}

			void Test()
			{
				var json = JsonConvert.SerializeObject(network, _jsonOptions);

				Client.LoadNetworkFromString(NetworkId, json);
				Client.DeleteNetwork(NetworkId);
			}
		}

		[Fact]
		public void ErrorsInSessionDefinitionAreReported()
		{
			var builder = new NetworkBuilder();
			builder.Add("Prod[generatorVoltage=100000] -- l1 -- Consumer1[consumption=1]");
			builder.Add("Prod -- l2[open] -- Consumer2[consumption=1]");
			builder.Add("Prod -- l3[open] -- Consumer3[consumption=1]");

			var network = PgoJsonParser.ConvertToJson(builder.Network);
			var json = JsonConvert.SerializeObject(network);

			Client.LoadNetworkFromString(NetworkId, json);

			Demand forecast = null;
			SinglePeriodSettings startConfig = null;
			Func<bool> allowUnspecifiedConsumerDemands = null;

			_tester.RunVariationChecks(Setup, Test, "An error occurred while parsing the session data:"
				//, variationToRun: 25
				);


			VariationTester.ActionSets Setup()
			{
				(forecast, startConfig, allowUnspecifiedConsumerDemands) = InputConsistencyTests.CreateTestSession(out var actions);
				return actions;
			}

			void Test()
			{
				var jsonDemand = JsonConvert.SerializeObject(forecast, _jsonOptions).ToMemoryStream();
				var jsonConfiguration = JsonConvert.SerializeObject(startConfig, _jsonOptions).ToMemoryStream();

				Client.CreateJsonSession(SessionId, NetworkId, jsonDemand, jsonConfiguration, allowUnspecifiedConsumerDemands());
				Client.DeleteSession(SessionId);
			}
		}

		[Fact]
		public void ErrorsInSolutionDefinitionAreReported()
		{
			var builder = new NetworkBuilder();
			builder.Add("Prod[generatorVoltage=100000] -- l1 -- Consumer1[consumption=1]");
			builder.Add("Prod -- l2[open] -- Consumer2[consumption=1]");
			builder.Add("Prod -- l3[open] -- Consumer3[consumption=1]");

			var network = PgoJsonParser.ConvertToJson(builder.Network);
			string json = JsonConvert.SerializeObject(network);

			Client.LoadNetworkFromString(NetworkId, json);

			var demands = PgoJsonParser.ConvertToJson(builder.RepeatedPeriodData(2));
			var jsonDemand = JsonConvert.SerializeObject(demands).ToMemoryStream();

			Client.CreateJsonSession(SessionId, NetworkId, jsonDemand, jsonDemand);

			Solution solution = null;

			_tester.RunVariationChecks(Setup, Test, "An error occurred adding the solution:"
				//, variationToRun: 25
				);


			VariationTester.ActionSets Setup()
			{
				solution = InputConsistencyTests.CreateSolution(out var actions);
				return actions;
			}

			void Test()
			{
				string json = JsonConvert.SerializeObject(solution, _jsonOptions);

				Client.AddSolution(SessionId, "solution", json);
				Client.RemoveSolution(SessionId, "solution");
			}
		}
	}

	/// <summary>
	/// A contract resolver that makes all attributes not Required.
	/// This enables us to create illegal Json data by serialization.
	/// </summary>
	internal class IgnoreRequiredContractResolver : DefaultContractResolver
	{
		protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
		{
			JsonProperty jsonProperty = base.CreateProperty(member, memberSerialization);
			jsonProperty.Required = Required.Default;
			return jsonProperty;
		}

		protected override JsonProperty CreatePropertyFromConstructorParameter(JsonProperty matchingMemberProperty, ParameterInfo parameterInfo)
		{
			JsonProperty jsonProperty = base.CreatePropertyFromConstructorParameter(matchingMemberProperty, parameterInfo);
			jsonProperty.Required = Required.Default;
			return jsonProperty;
		}
	}

	/// <summary>
	/// Helper for testing variations in input data, with adaptations to the
	/// PGO Rest API
	/// </summary>
	internal class RestAPIVariationTester : VariationTester
	{
		public RestAPIVariationTester()
		{
			Fail = (s) => Assert.True(false, s);
			ClassifyException = ClassifyRestException;
		}

		/// <summary>
		/// Determines whether an exception was thrown in the service or in the client,
		/// or by (de)serialization.
		/// </summary>
		private (ExceptionLocation, Exception) ClassifyRestException(Exception exception)
		{
			string message = exception.Message;
			if (message.Contains(". Path '") && message.Contains("', line ") && message.Contains(", position "))
				// Looks like an error from Json deserialization
				return (ExceptionLocation.InSerialization, exception);

			if (exception is HttpCodeException ex && ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
				return (ExceptionLocation.InService, exception);

			return (ExceptionLocation.InClient, exception);
		}
	}
}
