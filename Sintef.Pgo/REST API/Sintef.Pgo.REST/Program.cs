﻿using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Sintef.Pgo.REST;

namespace Sintef.Pgo.REST
{
#pragma warning disable CS1591
	public class Program
	{
		public static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
				Host.CreateDefaultBuilder(args)
						.ConfigureWebHostDefaults(webBuilder =>
						{
							webBuilder.UseStartup<Startup>();
						});
	}
#pragma warning restore CS1591
}
