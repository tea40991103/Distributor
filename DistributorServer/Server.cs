using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Net;
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

		CancellationTokenSource ListeningCTS;

		public Server() { }

		public Server(string localDir)
		{
			Directory.CreateDirectory(localDir);
			LocalDir = localDir;
		}

		public async Task Listen(string ipEndPointStr = null)
		{
			if (ListeningCTS != null)
				throw new InvalidOperationException();
			else
				ListeningCTS = new CancellationTokenSource();

			TcpListener tcpListener = null, tcpListenerIPv6 = null;
			TcpClient tcpClient = null;
			try
			{
				string ipAddressStr;
				int port = Tools.ParseIPEndPoint(ipEndPointStr, out ipAddressStr);
				if (port == 0) port = Node.DefaultPort;

				if (ipAddressStr == "")
				{
					tcpListener = new TcpListener(IPAddress.Any, port);
					tcpListenerIPv6 = new TcpListener(IPAddress.IPv6Any, port);
				}
				else
					tcpListener = new TcpListener(IPAddress.Parse(ipAddressStr), port);

				while (true)
				{
					tcpListener.Start();
					if (tcpListenerIPv6 != null) tcpListenerIPv6.Start();

					Task<TcpClient> task, taskIPv6 = null;
					task = tcpListener.AcceptTcpClientAsync();
					if (tcpListenerIPv6 != null) taskIPv6 = tcpListenerIPv6.AcceptTcpClientAsync();

					while (true)
					{
						if (task.IsCompleted)
						{
							tcpClient = task.Result;
							if (tcpListenerIPv6 != null) tcpListenerIPv6.Stop();
							break;
						}
						else if (taskIPv6 != null && taskIPv6.IsCompleted)
						{
							tcpClient = taskIPv6.Result;
							tcpListener.Stop();
							break;
						}
						else if ((task.IsFaulted && taskIPv6 == null)
							|| (task.IsFaulted && taskIPv6 != null && taskIPv6.IsFaulted))
							throw new SocketException();
						else
							await Task.Delay(500, ListeningCTS.Token);
					}

					using (var stream = tcpClient.GetStream())
					{

					}
				}
			}
			finally
			{
				if (tcpClient != null) tcpClient.Close();
				if (tcpListener != null) tcpListener.Stop();
				if (tcpListenerIPv6 != null) tcpListenerIPv6.Stop();
				ListeningCTS = null;
			}			
		}

		public void Stop()
		{
			if (ListeningCTS != null) ListeningCTS.Cancel();
		}
	}
}
