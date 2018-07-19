using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NDesk.Options;

namespace Distributor
{
	class ClientProgram
	{
		const string Version = "0.1.4";

		static void Main(string[] args)
		{
			string nodePoolFilePath = null;
			string inputFileName = null;
			string outputFileName = null;
			var localDir = Directory.GetCurrentDirectory();
			var secondsTimeout = -1;
			var showHelp = false;

			var options = new OptionSet()
			{
				{ "n|nodes=", "the {PATH} of node pool configuration file", v => nodePoolFilePath = v },
				{ "i|input=", "the {NAME} of input text file", v => inputFileName = v },
				{ "o|output=", "the {NAME} of output text file", v => outputFileName = v },
				{ "d|dir=", "local working {DIR}", v => localDir = v },
				{ "t|timeout=", "execution timeout in {SECONDS}", (int v) => secondsTimeout = v },
				{ "v|verbose", "verbose mode", v => Client.Verbose = v != null },
				{ "h|help", "show help message", v => showHelp = v != null },
			};

			try
			{
				options.Parse(args);
			}
			catch (OptionException ex)
			{
				Console.Write("{0}: ", Client.ProcessName);
				Console.WriteLine(ex.Message);
				Console.WriteLine("Try `{0} --help' for more information.", Client.ProcessName);
				return;
			}

			if (showHelp)
			{
				ShowHelp(options);
				return;
			}

			try
			{
				var client = new Client(nodePoolFilePath, inputFileName, outputFileName);
				client.LocalDir = localDir;
				var task = Task.Run(() => client.Connect(secondsTimeout));
				task.Wait();
			}
			catch (AggregateException ex)
			{
				Console.Write("{0}: ", Client.ProcessName);
				Console.WriteLine(ex.InnerException.Message);
				throw ex.InnerException;
			}
			catch (Exception ex)
			{
				Console.Write("{0}: ", Client.ProcessName);
				Console.WriteLine(ex.Message);
				throw;
			}
			finally
			{
				Client.ClearNodePool();
			}
		}

		static void ShowHelp (OptionSet options)
		{
			Console.WriteLine("Distributor client version {0}", Version);
			Console.WriteLine();
			Console.WriteLine("Options:");
			options.WriteOptionDescriptions(Console.Out);
		}
	}
}
