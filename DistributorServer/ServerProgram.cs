using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NDesk.Options;

namespace Distributor
{
	class ServerProgram
	{
		const string Version = "0.1.0";

		static void Main(string[] args)
		{
			string bind = "";
			var localDir = Directory.GetCurrentDirectory();
			var secondsTimeout = -1;
			var showHelp = false;

			var options = new OptionSet()
			{
				{ "b|bind=", "the {IP:PORT} this server binds to", v => bind = v },
				{ "d|dir=", "local working {DIR}", v => localDir = v },
				{ "t|timeout=", "execution timeout in {SECONDS}", (int v) => secondsTimeout = v },
				{ "v|verbose", "verbose mode", v => Server.Verbose = v != null },
				{ "h|help", "show help message", v => showHelp = v != null },
			};

			try
			{
				options.Parse(args);
			}
			catch (OptionException ex)
			{
				Console.Write("{0}: ", Server.ProcessName);
				Console.WriteLine(ex.Message);
				Console.WriteLine("Try `{0} --help' for more information.", Server.ProcessName);
				return;
			}

			if (showHelp)
			{
				ShowHelp(options);
				return;
			}

			try
			{
				var server = new Server(localDir);
				server.ExeSecondsTimeout = secondsTimeout;
				var task = Task.Run(() => server.Listen(bind));
				task.Wait();
			}
			catch (AggregateException ex)
			{
				Console.Write("{0}: ", Server.ProcessName);
				Console.WriteLine(ex.InnerException.Message);
			}
			catch (Exception ex)
			{
				Console.Write("{0}: ", Server.ProcessName);
				Console.WriteLine(ex.Message);
			}
		}

		static void ShowHelp(OptionSet options)
		{
			Console.WriteLine("Distributor server version {0}", Version);
			Console.WriteLine();
			Console.WriteLine("Options:");
			options.WriteOptionDescriptions(Console.Out);
		}
	}
}
