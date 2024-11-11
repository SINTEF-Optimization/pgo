using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Server;

namespace Sintef.Pgo.Api.Impl
{
	/// <summary>
	/// The implementation of <see cref="IServer"/>.
	/// 
	/// This is mostly a thin adaptation layer on top of <see cref="ISession"/>.
	/// </summary>
	internal class Session : ISession
	{
		/// <summary>
		/// The ID of the session.
		/// 
		/// Null if the session has been removed.
		/// </summary>
		public string Id { get; private set; }

		/// <inheritdoc/>
		public string NetworkId { get; }

		/// <inheritdoc/>
		public SessionStatus Status => InternalSession.GetStatus();

		/// <inheritdoc/>
		public Solution BestJsonSolution => GetJsonSolution("best");

		/// <inheritdoc/>
		public CimSolution BestCimSolution => GetCimSolution("best");

		/// <inheritdoc/>
		public CimJsonLdSolution BestCimJsonLdSolution => GetCimJsonLdSolution("best");

		/// <inheritdoc/>
		public SolutionInfo BestSolutionInfo => GetSolutionInfo("best");

		/// <summary>
		/// Returns the internal session that we adapt
		/// </summary>
		internal Sintef.Pgo.Server.ISession InternalSession
		{
			get
			{
				if (Id != null && _server.GetSession(Id) is Sintef.Pgo.Server.ISession internalSession)
					return internalSession;

				throw new InvalidOperationException("The session has been removed from the server and may not be used");
			}
		}

		/// <summary>
		/// The server containing the <see cref="ISession"/> that we adapt for.
		/// </summary>
		private Pgo.Server.Server _server;

		/// <inheritdoc/>
		public event EventHandler<EventArgs>? BestSolutionFound;

		/// <summary>
		/// Initializes a session
		/// </summary>
		public Session(Pgo.Server.Server server, string id, string networkId)
		{
			_server = server;
			Id = id;
			NetworkId = networkId;

			InternalSession.BestSolutionValueFound += (s, e) => BestSolutionFound?.Invoke(this, new());
		}

		/// <inheritdoc/>
		public void AddSolution(string id, Solution solution)
		{
			AddOrUpdateSolution(
				id ?? throw new ArgumentNullException(nameof(id)),
				solution ?? throw new ArgumentNullException(nameof(solution)), 
				add: true);
		}

		/// <inheritdoc/>
		public void AddSolution(string id, CimSolution solution)
		{
			AddOrUpdateSolution(
				id ?? throw new ArgumentNullException(nameof(id)),
				solution ?? throw new ArgumentNullException(nameof(solution)),
				add: true);
		}

		/// <inheritdoc/>
		public void AddSolution(string id, CimJsonLdSolution solution)
		{
			AddOrUpdateSolution(
				id ?? throw new ArgumentNullException(nameof(id)),
				solution ?? throw new ArgumentNullException(nameof(solution)),
				add: true);
		}

		/// <inheritdoc/>
		public void UpdateSolution(string id, Solution solution)
		{
			AddOrUpdateSolution(
				id ?? throw new ArgumentNullException(nameof(id)),
				solution ?? throw new ArgumentNullException(nameof(solution)),
				add: false);
		}

		/// <inheritdoc/>
		public void UpdateSolution(string id, CimSolution solution)
		{
			AddOrUpdateSolution(
				id ?? throw new ArgumentNullException(nameof(id)),
				solution ?? throw new ArgumentNullException(nameof(solution)),
				add: false);
		}

		/// <inheritdoc/>
		public void UpdateSolution(string id, CimJsonLdSolution solution)
		{
			AddOrUpdateSolution(
				id ?? throw new ArgumentNullException(nameof(id)),
				solution ?? throw new ArgumentNullException(nameof(solution)),
				add: false);
		}

		/// <inheritdoc/>
		public Solution GetJsonSolution(string id)
		{
			RequireJson();

			var pgoSolution = GetSolution(id);
			return PgoJsonParser.ConvertToJson(pgoSolution, (pgoSolution.Encoding as PgoProblem)?.FlowProvider);
		}

		/// <inheritdoc/>
		public CimSolution GetCimSolution(string id)
		{
			RequireCim();

			var pgoSolution = GetSolution(id);
			var converter = new CimSolutionConverter(_server.GetCimNetworkConverter(NetworkId));

			return converter.ConvertToCim(pgoSolution);
		}

		/// <inheritdoc/>
		public CimJsonLdSolution GetCimJsonLdSolution(string id)
		{
			var cimSolution = GetCimSolution(id);

			var network = _server.GetNetworkManager(NetworkId).OriginalNetwork;
			var networkConverter = _server.GetCimNetworkConverter(NetworkId);

			var converter = new CimSolutionConverter(networkConverter);

			var metadata = cimSolution.PeriodSolutions.Select(s => new CimJsonExporter.SolutionMetadata());

			return converter.ConvertToCimJsonLd(cimSolution, metadata);
		}

		/// <inheritdoc/>
		public void RemoveSolution(string id)
		{
			InternalSession.RemoveSolution(id ?? throw new ArgumentNullException(nameof(id)));
		}

		/// <inheritdoc/>
		public SolutionInfo GetSolutionInfo(string id)
		{
			return InternalSession.Summarize(GetSolution(id));
		}

		/// <inheritdoc/>
		public string RepairSolution(string id, string newId)
		{
			_ = newId ?? throw new ArgumentNullException(nameof(newId));

			return InternalSession.Repair(GetSolution(id), newId);
		}

		/// <summary>
		/// Causes the session to emit an exception on any future use
		/// </summary>
		internal void Die()
		{
			Id = null!;
		}

		/// <summary>
		/// Adds or updates a solution from JSON data
		/// </summary>
		private void AddOrUpdateSolution(string id, Solution solution, bool add)
		{
			RequireJson();

			var internalSession = InternalSession;

			PgoSolution pgoSolution = PgoJsonParser.ParseSolution(internalSession.Problem, solution);
			internalSession.ComputeFlow(pgoSolution);
			if (add)
				internalSession.AddSolution(pgoSolution, id);
			else
				internalSession.UpdateSolution(pgoSolution, id);
		}

		/// <summary>
		/// Adds or updates a solution from CIM data
		/// </summary>
		private void AddOrUpdateSolution(string id, CimSolution solution, bool add)
		{
			RequireCim();

			var internalSession = InternalSession;

			var converter = new CimSolutionConverter(_server.GetCimNetworkConverter(NetworkId));
			var pgoSolution = converter.ConvertToPgo(solution, internalSession.Problem);
			internalSession.ComputeFlow(pgoSolution);
			if (add)
				internalSession.AddSolution(pgoSolution, id);
			else
				internalSession.UpdateSolution(pgoSolution, id);
		}

		/// <summary>
		/// Adds or updates a solution from CIM JSON-LD data
		/// </summary>
		private void AddOrUpdateSolution(string id, CimJsonLdSolution solution, bool add)
		{
			RequireCim();

			var internalSession = InternalSession;
			var networkConverter = _server.GetCimNetworkConverter(NetworkId);

			if (networkConverter.NetworkParser == null)
				throw new InvalidOperationException("Cannot create a solution from JSON-LD: URIs are unknown since the network was not created from JSON-LD data.");

			var converter = new CimSolutionConverter(networkConverter);
			var pgoSolution = converter.ConvertToPgo(solution, internalSession.Problem);
			internalSession.ComputeFlow(pgoSolution);
			if (add)
				internalSession.AddSolution(pgoSolution, id);
			else
				internalSession.UpdateSolution(pgoSolution, id);
		}

		/// <summary>
		/// Returns the internal solutino with the given ID
		/// </summary>
		private PgoSolution GetSolution(string id)
		{
			_= id ?? throw new ArgumentNullException(nameof(id));

			if (InternalSession.GetSolution(id) is PgoSolution pgoSolution)
				return pgoSolution;

			throw new ArgumentException($"There is no solution with ID '{id}'");
		}

		/// <summary>
		/// Throws an exception if the session is not based on a JSON network
		/// </summary>
		private void RequireJson()
		{
			if (_server.GetCimNetworkConverter(NetworkId) != null)
				throw new InvalidOperationException("This function can not be used in a session based on CIM data");
		}

		/// <summary>
		/// Throws an exception if the session is not based on a CIM network
		/// </summary>
		private void RequireCim()
		{
			if (_server.GetCimNetworkConverter(NetworkId) == null)
				throw new InvalidOperationException("This function can not be used in a session based on JSON data");
		}

		/// <inheritdoc/>
		public void StartOptimization()
		{
			InternalSession.StartOptimization(new());
		}

		/// <inheritdoc/>
		public void StopOptimization()
		{
			InternalSession.StopOptimization().Wait();
		}
	}
}