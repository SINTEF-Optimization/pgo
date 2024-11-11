using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sintef.Pgo.REST.Models;
using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Sintef.Pgo.REST
{
	/// <summary>
	/// Authentication handler for cookie authentication.
	/// 
	/// Expects the user ID to be supplied in a cookie and creates an authentication 
	/// ticket for that user, with the appropriate quotas.
	/// If there is no cookie, assigns a user ID and sets a cookie in the response.
	/// 
	/// If <see cref="CookieAuthenticationHandlerOptions.MultiUser"/> is true, each client gets a new
	/// unique user ID.
	/// If false, all clients share the same user ID.
	/// </summary>
	public class CookieAuthenticationHandler : AuthenticationHandler<CookieAuthenticationHandlerOptions>
	{
		/// <summary>
		/// The name of this authentication scheme
		/// </summary>
		public const string SchemeName = "PgoCookieAuthentication";

		/// <summary>
		/// The name of the user ID cookie
		/// </summary>
		public string CookieName => "X-PgoUserId";

		/// <summary>
		/// The user quota provider
		/// </summary>
		private IUserQoutaProvider _qoutaProvider;

		/// <summary>
		/// Initializes the handler
		/// </summary>
		public CookieAuthenticationHandler(
			IOptionsMonitor<CookieAuthenticationHandlerOptions> options,
			ILoggerFactory logger,
			UrlEncoder encoder,
			ISystemClock clock,
			IUserQoutaProvider qoutaProvider)
			: base(options, logger, encoder, clock)
		{
			_qoutaProvider = qoutaProvider;
		}

		/// <summary>
		/// Performs authentication
		/// </summary>
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			string userId;
			string cookieUserId = Request.Cookies[CookieName];

			if (Options.MultiUser)
			{
				userId = cookieUserId ?? Guid.NewGuid().ToString();
			}
			else
			{
				// All clients get the same user ID
				userId = "commonUser";
			}

			if (userId != cookieUserId)
			{
				// Requests setting a cookie
				var cookieOptions = new CookieBuilder
				{
					HttpOnly = true,
					SecurePolicy = CookieSecurePolicy.SameAsRequest,
					SameSite = SameSiteMode.Strict,
					Domain = Request.Host.Host
				};

				Response.Cookies.Append(CookieName, userId, cookieOptions.Build(Request.HttpContext));
			}

			// Find user's quotas
			var pgoClaims = new PgoClaimsBuilder(userId)
				.WithUserQuotas(_qoutaProvider.QuotasFor(userId))
				.Build();

			// Create identity and ticket

			var claimsIdentity = new ClaimsIdentity(pgoClaims, Scheme.Name);
			var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), Scheme.Name);

			return Task.FromResult(AuthenticateResult.Success(ticket));
		}
	}

	/// <summary>
	/// Options for <see cref="CookieAuthenticationHandler"/>
	/// </summary>
	public class CookieAuthenticationHandlerOptions : AuthenticationSchemeOptions
	{
		/// <summary>
		/// If true, each connecting client is considered a distinct user.
		/// If false, there is only one user. All clients share networks and sessions.
		/// </summary>
		public bool MultiUser { get; set; }
	}
}
