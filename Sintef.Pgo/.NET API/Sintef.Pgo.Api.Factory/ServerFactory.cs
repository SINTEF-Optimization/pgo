namespace Sintef.Pgo.Api.Factory
{
	/// <summary>
	/// Contains the static function for creating an <see cref="IServer"/>
	/// </summary>
	public class ServerFactory
	{
		/// <summary>
		/// Creates and returns a new <see cref="IServer"/>
		/// </summary>
		public static IServer CreateServer()
		{
			return new Impl.Server();
		}
	}
}