using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Distributor
{
	public class Server
	{
		public static bool Verbose = false;

		string InputFileName, OutputFileName;

		string _LocalDir = Directory.GetCurrentDirectory() + Path.PathSeparator;
		public string LocalDir
		{
			get { return _LocalDir; }
			set
			{
				if (Directory.Exists(value))
				{
					_LocalDir = value;
					if (_LocalDir.Last() != Path.PathSeparator) _LocalDir += Path.PathSeparator;
				}
				else
					throw new DirectoryNotFoundException();
			}
		}

		Node ExecutionNode;
		CancellationTokenSource ExecutionCTS;

		public Server() { }

		public Server(string localDir)
		{
			Directory.CreateDirectory(localDir);
			LocalDir = localDir;
		}

		public async Task Listen(int port = Node.DefaultPort)
		{
			if (ExecutionCTS != null)
				throw new InvalidOperationException();
			else
				ExecutionCTS = new CancellationTokenSource();

			try
			{

			}
			finally
			{
				ExecutionCTS = null;
			}			
		}

		public void Stop()
		{
			if (ExecutionCTS != null) ExecutionCTS.Cancel();
		}
	}
}
