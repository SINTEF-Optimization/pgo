using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;
using System.Reflection;

#if NET7_0_OR_GREATER
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
#endif

namespace Sintef.Pgo.REST.Client
{
	/// <summary>
	/// Provides a client-side interface for communicating with a PGO server.
	/// Hides the details of the HTTP REST protocol.
	/// Error responses over HTTP cause an exception to be thrown.
	/// 
	/// This class exists primarily for simplifying writing tests for PGO.
	/// It does not have the stability requirements of an official API.
	/// Nevertheless, it is expected to be stable enough that it is preferable
	/// to write applications using this interface rather than more directly
	/// using <see cref="HttpClient"/>.
	/// </summary>
	public class PgoRestClient
	{
		/// <summary>
		/// The ID of the user that the client represents, or null if none has been set
		/// </summary>
		public string UserId => _cookieContainer.GetCookies(_httpClient.BaseAddress)[_userIdCookieName]?.Value;

		/// <summary>
		/// The session type used by this client
		/// </summary>
		public SessionType SessionType { get; }

		/// <summary>
		/// The http client used to communicate with the server
		/// </summary>
		private HttpClient _httpClient;

		/// <summary>
		/// The HttpClient's cookie container
		/// </summary>
		private CookieContainer _cookieContainer;

		/// <summary>
		/// The HttpClient's message handler
		/// </summary>
		private HttpMessageHandler _handler;

		private const string _userIdCookieName = "X-PgoUserId";

		/// <summary>
		/// The base URI of the API
		/// </summary>
		private string BaseUri { get; }

		/// <summary>
		/// Initializes a <see cref="PgoRestClient"/>
		/// </summary>
		/// <param name="sessionType">The type of sessions to use</param>
		/// <param name="httpClient">The http client to use to communicate with the server</param>
		/// <param name="userId">If not null, uses this user ID.
		///   If null, no user ID is configured at first, but may be set by the server (in a cookie)</param>
		public PgoRestClient(SessionType sessionType, HttpClient httpClient, string userId)
		{
			SessionType = sessionType;
			_httpClient = httpClient;
			_handler = FindMessageHandler(httpClient);
			_cookieContainer = FindCookieContainer(_handler);

			if (userId != null)
				_cookieContainer.Add(new Cookie(_userIdCookieName, userId, null, _httpClient.BaseAddress.Host));

			BaseUri = sessionType switch
			{
				SessionType.Json => "/api",
				SessionType.Cim => "/api/cim",
				_ => throw new ArgumentException($"Unknown session type {sessionType}")
			}; ;
		}

		/// <summary>
		/// Initializes a <see cref="PgoRestClient"/>
		/// </summary>
		/// <param name="sessionType">The type of sessions to use</param>
		/// <param name="baseAddress">The base address of the server</param>
		/// <param name="userId">If not null, uses this user ID.
		///   If null, no user ID is configured at first, but may be set by the server (in a cookie)</param>
		public PgoRestClient(SessionType sessionType, string baseAddress, string userId)
			:this(sessionType, CreateHandler(baseAddress),userId)
		{
		}

		/// <summary>
		/// Creates and starts a connection to a SignalR hub on the server
		/// </summary>
		/// <param name="hubName">The hub's name</param>
		public async Task<HubConnection> ConnectToHub(string hubName)
		{
			string url = _httpClient.BaseAddress + hubName;
			var connection = new HubConnectionBuilder()
				.WithUrl(url, options =>
				{
						options.HttpMessageHandlerFactory = _ => _handler;
				})
				.Build();

			await connection.StartAsync();
	
			return connection;
		}

		/// <summary>
		/// Retrieves the server status
		/// </summary>
		public ServerStatus GetServerStatus()
		{
			return Get<ServerStatus>(ServerUri);
		}

		/// <summary>
		/// Retrieves the quotas for the user that the client represents
		/// </summary>
		public UserQuotas GetQuotas()
		{
			return Get<UserQuotas>(QuotasUri);
		}

		#region Network management

		/// <summary>
		/// Retrieves the network with the given ID
		/// </summary>
		public PowerGrid GetNetwork(string id)
		{
			return Get<PowerGrid>(NetworkUri(id));
		}

		/// <summary>
		/// Retrieves the network with the given ID, as a Json string
		/// </summary>
		public string GetNetworkString(string id)
		{
			return Get<string>(NetworkUri(id));
		}

		/// <summary>
		/// Adds a network to the server from a stream.
		/// The stream may contain PGO Json data or CIM Json-Ld data, according to <see cref="SessionType"/>.
		/// </summary>
		/// <param name="id">The network's ID</param>
		/// <param name="networkStream">The stream with network data</param>
		/// <param name="filename">The network filename</param>
		/// <param name="parsingOptions">The options to use for parsing JSON-LD into CIM objects</param>
		/// <param name="conversionOptions">The options to use when converting CIM objects into a PGO network</param>
		public void LoadNetwork(string id, Stream networkStream, string filename,
			CimParsingOptions parsingOptions = null, 
			CimNetworkConversionOptions conversionOptions = null)
		{
			switch (SessionType)
			{
				case SessionType.Json:

					using (var content = new MultipartFormDataContent())
					{
						content.Add(new StreamContent(networkStream), "networkDescription", filename);

						Post(NetworkUri(id), content);
					}

					break;

#if NET7_0_OR_GREATER

				case SessionType.Cim:

					var networkData = new CimJsonLdNetworkData
					{
						ParsingOptions = parsingOptions ?? throw new ArgumentNullException(nameof(parsingOptions)),
						ConversionOptions = conversionOptions ?? new(),
						Network = Deserialize<JObject>(networkStream)
					};

					Post(NetworkUri(id), networkData);

					break;

#endif

				default:
					throw new NotImplementedException();
			};
		}

		/// <summary>
		/// Adds a network to the server from a file.
		/// The file may contain PGO Json data or CIM Json-Ld data, according to <see cref="SessionType"/>.
		/// </summary>
		/// <param name="id">The network's ID</param>
		/// <param name="path">The path of the network file</param>
		/// <param name="parsingOptions">The options to use for parsing JSON-LD into CIM objects</param>
		/// <param name="conversionOptions">The options to use when converting CIM objects into a PGO network</param>
		public void LoadNetworkFromFile(string id, string path,
			CimParsingOptions parsingOptions = null,
			CimNetworkConversionOptions conversionOptions = null)
		{
			using (Stream inputStream = File.OpenRead(path))
			{
				LoadNetwork(id, inputStream, Path.GetFileName(path), parsingOptions, conversionOptions);
			}
		}

		/// <summary>
		/// Adds a network to the server from a string.
		/// The string may contain PGO Json data or CIM Json-Ld data, according to <see cref="SessionType"/>.
		/// </summary>
		/// <param name="id">The network's ID</param>
		/// <param name="jsonData">A string containing the network</param>
		/// <param name="parsingOptions">The options to use for parsing JSON-LD into CIM objects</param>
		/// <param name="conversionOptions">The options to use when converting CIM objects into a PGO network</param>
		public void LoadNetworkFromString(string id, string jsonData,
			CimParsingOptions parsingOptions = null,
			CimNetworkConversionOptions conversionOptions = null)
		{
			using (Stream networkStream = jsonData.ToMemoryStream())
			{
				LoadNetwork(id, networkStream, "dummyFilename", parsingOptions, conversionOptions);
			}
		}

		/// <summary>
		/// Loads the server with the network from the given builder's network.
		/// This only works for <see cref="SessionType"/> Json.
		/// </summary>
		/// <param name="id">The network's ID</param>
		public void LoadNetworkFromBuilder(string id, NetworkBuilder builder)
		{
			var jsonObject = PgoJsonParser.ConvertToJson(builder.Network);
			var json = JsonConvert.SerializeObject(jsonObject);

			LoadNetworkFromString(id, json);
		}

		/// <summary>
		/// Analyzes a loaded network and returns the result as a string.
		/// </summary>
		/// <param name="id">The network's ID</param>
		public string AnalyseNetwork(string id)
		{
			return Get<string>(NetworkUri(id, "analysis"));
		}

		/// <summary>
		/// Get a network connectivity report.
		/// </summary>
		/// <param name="id">The network's ID</param>
		public NetworkConnectivityStatus NetworkConnectivityStatus(string id)
		{
			return Get<NetworkConnectivityStatus>(NetworkUri(id, "connectivityStatus"));
		}

		/// <summary>
		/// Delete the network with the given ID from the server.
		/// </summary>
		public void DeleteNetwork(string id)
		{
			Delete(NetworkUri(id));
		}

		#endregion

		#region Session management

		/// <summary>
		/// Adds a Json session to the server
		/// </summary>
		/// <param name="id">The session's ID</param>
		/// <param name="demandsFile">The file with Json power demand data</param>
		/// <param name="configurationFile">The file with the current Json configuration data</param>
		public void CreateJsonSession(string sessionId, string networkId, string demandsFile, string configurationFile = null)
		{
			using (Stream demandsStream = File.OpenRead(demandsFile))
			{
				if (configurationFile != null)
				{
					using (Stream configStream = File.OpenRead(configurationFile))
					{
						CreateJsonSession(sessionId, networkId, demandsStream, configStream);
					}
				}
				else
				{
					CreateJsonSession(sessionId, networkId, demandsStream, null);
				}
			}
		}

		/// <summary>
		/// Adds a single-period Json session with demands and configuration from the builder.
		/// </summary>
		public void CreateJsonSession(string sessionId, string networkId, NetworkBuilder builder)
		{
			var demands = PgoJsonParser.ConvertToJson(new[] { builder.PeriodData });
			var json = JsonConvert.SerializeObject(demands);

			using (var stream = new MemoryStream())
			{
				StreamWriter writer = new StreamWriter(stream);
				writer.Write(json);
				writer.Flush();

				stream.Seek(0, SeekOrigin.Begin);
				CreateJsonSession(sessionId, networkId, stream);
			}
		}

		/// <summary>
		/// Adds a Json session to the server
		/// </summary>
		/// <param name="id">The session's ID</param>
		/// <param name="demandsStream">The stream with Json power demand data</param>
		/// <param name="currentConfigurationStream">The stream with the current Json configuration data</param>
		public void CreateJsonSession(string id, string networkId, Stream demandsStream, Stream currentConfigurationStream = null,
			bool allowUnspecifiedConsumerDemands = false)
		{
			if (SessionType != SessionType.Json)
				throw new InvalidOperationException();

			using (var content = new MultipartFormDataContent())
			{
				content.Add(new StringContent(networkId), "networkId");
				content.Add(new StreamContent(demandsStream), "demands", "demandsFilename");
				if (currentConfigurationStream != null)
					content.Add(new StreamContent(currentConfigurationStream), "startConfiguration", "currentConfigurationFilename");
				content.Add(new StringContent(allowUnspecifiedConsumerDemands.ToString()), "allowUnspecifiedConsumerDemands");

				Post(SessionUri(id), content);
			}
		}

#if NET7_0_OR_GREATER

		/// <summary>
		/// Adds a single-period CIM session to the server
		/// </summary>
		/// <param name="id">The session ID</param>
		/// <param name="networkId">The ID of the network to use</param>
		/// <param name="singleDemandsFile">The file containing demands, as a CIM SSH dataset serialized to JSON-LD</param>
		/// <param name="startConfigFile">The file containing the start configuration, as a CIM dataset serialized to JSON-LD,
		///   or null if no start config is given</param>
		public void CreateCimSession(string id, string networkId, string singleDemandsFile, string startConfigFile = null)
		{
			string demandsJsonLd = File.ReadAllText(singleDemandsFile);

			List<string> demands = new() { demandsJsonLd };

			CreateCimSession(id, networkId, demands, startConfigFile);
		}

		/// <summary>
		/// Adds a CIM session to the server.
		/// A 1-hour period is created for each demand dataset.
		/// </summary>
		/// <param name="id">The session ID</param>
		/// <param name="networkId">The ID of the network to use</param>
		/// <param name="cimDemands">The demands for each period, as CIM SSH datasets serialized to JSON-LD</param>
		/// <param name="startConfigFile">The file containing the start configuration, as a CIM dataset serialized to JSON-LD,
		///   or null if no start config is given</param>
		public void CreateCimSession(string id, string networkId, IEnumerable<string> cimDemands, string startConfigFile = null)
		{
			if (startConfigFile != null)
			{
				using (Stream configStream = File.OpenRead(startConfigFile))
				{
					CreateCimSession(id, networkId, cimDemands, configStream);
				}
			}
			else
			{
				CreateCimSession(id, networkId, cimDemands, (Stream)null);
			}
		}

		/// <summary>
		/// Adds a CIM session to the server.
		/// A 1-hour period is created for each demand dataset.
		/// </summary>
		/// <param name="id">The session ID</param>
		/// <param name="networkId">The ID of the network to use</param>
		/// <param name="cimDemands">The demands for each period, as CIM SSH datasets serialized to JSON-LD</param>
		/// <param name="startConfigStream">The stream containing the start configuration, as a CIM dataset serialized to JSON-LD,
		///   or null if no start config is given</param>
		private void CreateCimSession(string id, string networkId, IEnumerable<string> cimDemands, Stream startConfigStream = null)
		{
			if (SessionType != SessionType.Cim)
				throw new InvalidOperationException();

			var demands = CimDemandsConverter.ToRestDemands(cimDemands);

			// Build and send request

			var sessionParameters = new CimJsonLdSessionParameters
			{
				NetworkId = networkId,
				PeriodsAndDemands = demands
			};

			if (startConfigStream != null)
				sessionParameters.StartConfiguration = Deserialize<JObject>(startConfigStream);

			Post(SessionUri(id), sessionParameters);
		}

#endif

		/// <summary>
		/// Retrieves a session status
		/// </summary>
		public SessionStatus GetSessionStatus(string sessionId)
		{
			return Get<SessionStatus>(SessionUri(sessionId));
		}

		/// <summary>
		/// Retrieves a session's demands
		/// </summary>
		public Demand GetSessionDemands(string sessionId)
		{
			return Get<Demand>(SessionUri(sessionId, "demands"));
		}

		/// <summary>
		/// Registers a handler that will be called whenever a new best solution is found
		/// in a specific session
		/// </summary>
		/// <param name="sessionId">The ID of the session</param>
		/// <param name="handler">The handler</param>
		/// <returns>A subscription that can be disposed to stop receiving messages</returns>
		public IDisposable ReceiveBestSolutionUpdates(string sessionId, Action<SolutionInfo> handler)
		{
			HubConnection connection = ConnectToHub("solutionStatusHub").Result;

			connection.InvokeAsync("AddToGroup", sessionId).Wait();

			return connection.On("newSolutionStatus", (string msg) => handler(JsonConvert.DeserializeObject<SolutionInfo>(msg)));
		}

		/// <summary>
		/// Starts optimizing in a session
		/// </summary>
		public void StartOptimizing(string sessionId)
		{
			Put(SessionUri(sessionId, "runOptimization"), true);
		}

		/// <summary>
		/// Stops optimizing in a session
		/// </summary>
		public void StopOptimizing(string sessionId)
		{
			Put(SessionUri(sessionId, "runOptimization"), false);
		}

		/// <summary>
		/// Delete the session with the given ID from the server.
		/// </summary>
		public void DeleteSession(string id)
		{
			Delete(SessionUri(id));
		}

		#endregion

		#region Solution management

		/// <summary>
		/// Retrieves info about the best solution of a session
		/// </summary>
		public SolutionInfo GetBestSolutionInfo(string sessionId)
		{
			return Get<SolutionInfo>(SessionUri(sessionId, "bestSolutionInfo"));
		}

		/// <summary>
		/// Retrieves the best solution of a Json session
		/// </summary>
		public Solution GetBestSolution(string sessionId)
		{
			return Get<Solution>(SessionUri(sessionId, "bestSolution"));
		}

		/// <summary>
		/// Retrieves the best solution of a CIM session
		/// </summary>
		public CimJsonLdSolution GetBestCimSolution(string sessionId)
		{
			return Get<CimJsonLdSolution>(SessionUri(sessionId, "bestSolution"));
		}

		/// <summary>
		/// Retrieves the best solution of a session, serialized to a string
		/// </summary>
		public string GetBestSolutionAsString(string sessionId)
		{
			return Get<string>(SessionUri(sessionId, "bestSolution"));
		}

		/// <summary>
		/// Retrieves info about the solution with the given ID (including 'best')
		/// </summary>
		public SolutionInfo GetSolutionInfo(string sessionId, string solutionId)
		{
			return Get<SolutionInfo>(SolutionUri(sessionId, solutionId, "info"));
		}

		/// <summary>
		/// Retrieves the solution with the given ID (including 'best')
		/// from a Json session
		/// </summary>
		public Solution GetSolution(string sessionId, string solutionId)
		{
			return Get<Solution>(SolutionUri(sessionId, solutionId));
		}

		/// <summary>
		/// Retrieves the solution with the given ID (including 'best')
		/// from a CIM session
		/// </summary>
		public CimJsonLdSolution GetCimSolution(string sessionId, string solutionId)
		{
			return Get<CimJsonLdSolution>(SolutionUri(sessionId, solutionId));
		}

		/// <summary>
		/// Saves the identified solution to the given file
		/// </summary>
		public void SaveSolutionToFile(string sessionId, string solutionId, string solutionFileName)
		{
			using (var solutionFileStream = File.Open(solutionFileName, FileMode.Create))
			{
				SaveSolutionToStream(sessionId, solutionId, solutionFileStream);
			}
		}

		/// <summary>
		/// Saves the identified solution to the given stream
		/// </summary>
		public void SaveSolutionToStream(string sessionId, string solutionId, Stream solutionStream)
		{
			switch (SessionType)
			{
				case SessionType.Json:

					Solution sol = GetSolution(sessionId, solutionId);
					PgoJsonParser.Serialize(sol, solutionStream, true);
					break;

				case SessionType.Cim:

					CimJsonLdSolution cimSol = GetCimSolution(sessionId, solutionId);
					PgoJsonParser.Serialize(cimSol, solutionStream, true);
					break;

				default:
					throw new NotImplementedException();
			}
		}

		/// <summary>
		/// Adds a solution in a Json session
		/// </summary>
		public void AddSolution(string sessionId, string solutionId, Solution solution)
		{
			Post(SolutionUri(sessionId, solutionId), solution);
		}

		/// <summary>
		/// Adds a solution in a CIM session
		/// </summary>
		public void AddSolution(string sessionId, string solutionId, CimJsonLdSolution solution)
		{
			Post(SolutionUri(sessionId, solutionId), solution);
		}

		/// <summary>
		/// Adds a solution (serialized to json) in a session
		/// </summary>
		public void AddSolution(string sessionId, string solutionId, string solutionJson)
		{
			Post(SolutionUri(sessionId, solutionId), ToJsonContent(solutionJson));
		}

		/// <summary>
		/// Updates a solution in a Json session
		/// </summary>
		public void UpdateSolution(string sessionId, string solutionId, Solution solution)
		{
			Put(SolutionUri(sessionId, solutionId), solution);
		}

		/// <summary>
		/// Updates a solution in a CIM session
		/// </summary>
		public void UpdateSolution(string sessionId, string solutionId, CimJsonLdSolution solution)
		{
			Put(SolutionUri(sessionId, solutionId), solution);
		}

		/// <summary>
		/// Updates a solution (serialized to json) in a session
		/// </summary>
		public void UpdateSolution(string sessionId, string solutionId, string solution)
		{
			Put(SolutionUri(sessionId, solutionId), ToJsonContent(solution));
		}

		/// <summary>
		/// Loads the identified solution from the given file
		/// </summary>
		/// <param name="sessionId"></param>
		/// <param name="solutionId"></param>
		/// <param name="solutionFileName"></param>
		public void LoadSolutionFromFile(string sessionId, string solutionId, string solutionFileName)
		{
			FileInfo fi = new FileInfo(solutionFileName);
			if (fi.Extension == ".json")
			{
				AddSolution(sessionId, solutionId, ParseSolutionFromJson(solutionFileName));
			}
		}

		/// <summary>
		/// Repair a solution.
		/// </summary>
		public string RepairSolution(string sessionId, string solutionId, string newSolutionId)
		{
			return Post<string>(SolutionUri(sessionId, solutionId, $"repair/{newSolutionId}"));
		}

		/// <summary>
		/// Removes a solution from a session
		/// </summary>
		public void RemoveSolution(string sessionId, string solutionId)
		{
			Delete(SolutionUri(sessionId, solutionId));
		}

		/// <summary>
		/// Get objective weights from a session.
		/// </summary>
		/// <param name="sessionId"></param>
		public List<ObjectiveWeight> GetObjectiveWeights(string sessionId)
		{
			return Get<List<ObjectiveWeight>>(SessionUri(sessionId, "objectiveWeights"));
		}

		/// <summary>
		/// Set objective weights in a session.
		/// </summary>
		/// <param name="sessionId"></param>
		public void SetObjectiveWeights(string sessionId, List<ObjectiveWeight> value)
		{
			Put(SessionUri(sessionId, "objectiveWeights"), value);
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Executes GET on the given URI.
		/// </summary>
		/// <param name="requestUri">The URI to access</param>
		/// <typeparam name="TResult">The type of result to return</typeparam>
		private TResult Get<TResult>(string requestUri)
		{
			return Execute<TResult>(_httpClient.GetAsync, requestUri);
		}

		/// <summary>
		/// Sends a POST requests to the given URI, with the given content.
		/// </summary>
		/// <param name="requestUri">The URI to access</param>
		/// <param name="content">The request body content</param>
		private void Post(string requestUri, object content = null) => Post<EmptyResult>(requestUri, content);

		/// <summary>
		/// Sends a PUT requests to the given URI, with the given content.
		/// </summary>
		/// <param name="requestUri">The URI to access</param>
		/// <param name="content">The request body content</param>
		private void Put(string requestUri, object content = null) => Put<EmptyResult>(requestUri, content);

		/// <summary>
		/// Sends a POST requests to the given URI, with the given content.
		/// </summary>
		/// <param name="requestUri">The URI to access</param>
		/// <param name="content">The request body content</param>
		private TResult Post<TResult>(string requestUri, object content = null)
		{
			HttpContent httpContent = ToHttpContent(content);

			return Execute<TResult>((s) => _httpClient.PostAsync(s, httpContent), requestUri);
		}

		/// <summary>
		/// Sends a PUT requests to the given URI, with the given content.
		/// </summary>
		/// <param name="requestUri">The URI to access</param>
		/// <param name="content">The request body content</param>
		private TResult Put<TResult>(string requestUri, object content = null)
		{
			HttpContent httpContent = ToHttpContent(content);

			return Execute<TResult>((s) => _httpClient.PutAsync(s, httpContent), requestUri);
		}

		/// <summary>
		/// Executes DELETE on the given URI.
		/// </summary>
		/// <param name="requestUri">The URI to access</param>
		private void Delete(string requestUri)
		{
			Execute<EmptyResult>(_httpClient.DeleteAsync, requestUri);
		}

		/// <summary>
		/// Executes the given http method on the given URI.
		/// </summary>
		/// <param name="httpMethod">The http method to use (e.g. <see cref="HttpClient.GetAsync"/>)</param>
		/// <param name="requestUri">The URI to access</param>
		/// <typeparam name="TResult">The type of result to return</typeparam>
		private static TResult Execute<TResult>(Func<string, Task<HttpResponseMessage>> httpMethod, string requestUri)
		{
			using (var response = httpMethod(requestUri).GetAwaiter().GetResult())
			{
				// Return the status code, if requested
				if (response.StatusCode is TResult)
					return (TResult)(object)response.StatusCode;

				if (!response.IsSuccessStatusCode)
					ThrowErrorResult(response);


				// Read the response
				var responseString = response.Content.ReadAsStringAsync().Result;

				if (responseString is TResult)
					// Return raw json, as requested
					return (TResult)(object)responseString;

				// Deserialise and return 
				return JsonConvert.DeserializeObject<TResult>(responseString);
			}
		}

		/// <summary>
		/// Throw an exception appropriate for the given response.
		/// </summary>
		private static void ThrowErrorResult(HttpResponseMessage response)
		{
			var errorMessage = response.Content.ReadAsStringAsync().Result;
			if (response.StatusCode == HttpStatusCode.NotFound)
				errorMessage = $"Not found: {response.RequestMessage.RequestUri}";

			if (errorMessage == "")
				errorMessage = $"{response.StatusCode}";

			throw new HttpCodeException(response.StatusCode, errorMessage);
		}

		/// <summary>
		/// Utility to support configurations given on json file, on the standard configuration data format. 
		/// Builds a single period solution
		/// based on the switch settings in the given (json) file, for the given session.
		/// </summary>
		/// <param name="filename">Name of the json file.</param>
		/// <param name="sessionId">Session Id.</param>
		/// <returns>A new solution, with the parsed configuration, but with no flow.</returns>
		private Solution ParseSolutionFromJson(string filename)
		{
			using (var solFileStream = new FileStream(filename, FileMode.Open))
			{
				return ParseSolutionFromJsonStream(solFileStream);
			}
		}

		/// <summary>
		/// Utility to support configurations given on json stream, on the standard configuration data format. 
		/// Builds a single period solution
		/// based on the switch settings in the given (json) stream, for the given session.
		/// </summary>
		/// <param name="solFileStream">Name of the json solution stream.</param>
		/// <returns>A new solution, with the parsed configuration, but with no flow.</returns>
		private Solution ParseSolutionFromJsonStream(Stream solFileStream)
		{
			using (StreamReader reader = new StreamReader(solFileStream))
			{
				string configuration = reader.ReadToEnd();
				var sol = PgoJsonParser.Deserialize<Solution>(configuration);
				return sol;
			}
		}

		/// <summary>
		/// Converts the given content to <see cref="HttpContent"/>, if necessary.
		/// Strings and null are passed unchanged; other objects are serialized to json.
		/// </summary>
		private static HttpContent ToHttpContent(object content)
		{
			if (!(content is HttpContent httpContent))
			{
				if (content is string s)
					httpContent = new StringContent(s);
				else if (content == null)
					httpContent = new StringContent("");
				else
					httpContent = new StringContent(PgoJsonParser.Serialize(content, false), Encoding.UTF8, "application/json");
			}

			return httpContent;
		}

		/// <summary>
		/// Converts the given string to Json http content
		/// </summary>
		private HttpContent ToJsonContent(string json)
		{
			return new StringContent(json, Encoding.UTF8, "application/json");
		}

		/// <summary>
		/// Serializes the given object to Json http content
		/// </summary>
		private HttpContent ToJsonContent(object theObject)
		{
			return ToJsonContent(JsonConvert.SerializeObject(theObject));
		}

		/// <summary>
		/// The URI of the 'server' endpoint
		/// </summary>
		private string ServerUri => $"{BaseUri}/server";

		/// <summary>
		/// The URI of the 'quotas' endpoint
		/// </summary>
		private string QuotasUri => $"{BaseUri}/identity/quotas";

		/// <summary>
		/// The URI of the network with the given ID, or its property with the given name
		/// </summary>
		private string NetworkUri(string id, string propertyName = null) =>
			propertyName switch
			{
				null => $"{BaseUri}/networks/{id}",
				_ => $"{NetworkUri(id)}/{propertyName}"
			};

		/// <summary>
		/// The URI of the session with the given ID, or its property with the given name
		/// </summary>
		private string SessionUri(string id, string propertyName = null) =>
			propertyName switch
			{
				null => $"{BaseUri}/sessions/{id}",
				_ => $"{SessionUri(id)}/{propertyName}"
			};

		/// <summary>
		/// The URI of the solution with the given ID in the session with the given ID,
		/// or its property with the given name
		/// </summary>
		private string SolutionUri(string sessionId, string solutionId, string propertyName = null) =>
			propertyName switch
			{
				null => $"{SessionUri(sessionId)}/solutions/{solutionId}",
				_ => $"{SolutionUri(sessionId, solutionId)}/{propertyName}"
			};

		private static void Serialize(object value, Stream s)
		{
			using (StreamWriter writer = new StreamWriter(s))
			using (JsonTextWriter jsonWriter = new JsonTextWriter(writer))
			{
				JsonSerializer ser = new JsonSerializer();
				ser.Serialize(jsonWriter, value);
				jsonWriter.Flush();
			}
		}

		private static T Deserialize<T>(Stream s)
		{
			using (StreamReader reader = new StreamReader(s))
			using (JsonTextReader jsonReader = new JsonTextReader(reader))
			{
				JsonSerializer ser = new JsonSerializer();
				return ser.Deserialize<T>(jsonReader);
			}
		}

		private static HttpClient CreateHandler(string baseAddress)
		{
			var cookieContainer = new CookieContainer();
			var handler = new HttpClientHandler() { CookieContainer = cookieContainer };
			return new HttpClient(handler)
			{
				BaseAddress = new Uri(baseAddress)
			};
		}

		private static CookieContainer FindCookieContainer(HttpMessageHandler handler)
		{
			while (true)
			{
				switch (handler)
				{
#if NET7_0_OR_GREATER
					case CookieContainerHandler cookieHandler:
						return cookieHandler.Container;
#endif
					case HttpClientHandler clientHandler:
						return clientHandler.CookieContainer;

					case DelegatingHandler delegating:
						handler = delegating.InnerHandler;
						break;

					default:
						throw new Exception("HttpClient has no cookie handler");
				}
			}
		}

		private static HttpMessageHandler FindMessageHandler(HttpClient httpClient)
		{
#if NET7_0_OR_GREATER
			string fieldName = "_handler";
#else
			string fieldName = "handler";
#endif

			Type type = httpClient.GetType().BaseType;
			FieldInfo fieldInfo = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new Exception($"Unable to find HttpClient's {fieldName} field");

			HttpMessageHandler handler = fieldInfo.GetValue(httpClient) as HttpMessageHandler;
			return handler;
		}

#endregion

		#region Inner types

		private class EmptyResult
		{
		}
	}
	#endregion

	/// <summary>
	/// A type of session
	/// </summary>
	public enum SessionType
	{
		/// <summary>
		/// A session based on data expressed in PGO's JSON format
		/// </summary>
		Json,

		/// <summary>
		/// A session based on CIM data
		/// </summary>
		Cim
	}

	/// <summary>
	/// An exception containing a HTTP status code
	/// </summary>
	public class HttpCodeException : Exception
	{
		/// <summary>
		/// The status code
		/// </summary>
		public HttpStatusCode StatusCode { get; }

		public HttpCodeException(HttpStatusCode statusCode, string message)
			: base(message)
		{
			StatusCode = statusCode;
		}
	}
}
