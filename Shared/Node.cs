using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Distributor
{	
	public enum NodeState:byte
	{
		Idel = 0,
		Busy = 1,
		
	}

	public class Node
	{
		public const int DefaultPort = 57220;

		public IPEndPoint EndPoint;
		string ExecutorPath, ArgStr;

		public Node(string nodeLine)
		{
			if (String.IsNullOrEmpty(nodeLine)) throw new ArgumentNullException();

			var tabIndex1 = nodeLine.IndexOf(Message.Separator);
			var endPointStr = nodeLine.Substring(0, tabIndex1);
			var colonIndex = endPointStr.IndexOf(':');

			string ipStr;
			int port;
			if (colonIndex >= 0)
			{
				ipStr = endPointStr.Substring(0, colonIndex);
				port = Convert.ToInt32(endPointStr.Substring(colonIndex + 1));
			}
			else
			{
				ipStr = endPointStr;
				port = DefaultPort;
			}

			IPAddress ip;
			try
			{
				ip = Dns.GetHostAddresses(ipStr)[0];
			}
			catch (System.Net.Sockets.SocketException ex)
			{
				throw ex;
			}
			EndPoint = new IPEndPoint(ip, port);

			var tabIndex2 = nodeLine.LastIndexOf(Message.Separator);
			if (tabIndex2 == tabIndex1)
			{
				ExecutorPath = nodeLine.Substring(tabIndex2 + 1);
				ArgStr = "";
			}
			else
			{
				ExecutorPath = nodeLine.Substring(tabIndex1 + 1, tabIndex2 - tabIndex1 - 1);
				ArgStr = nodeLine.Substring(tabIndex2 + 1);
			}
		}

		public Node(byte[] message) : this(Message.ReadMessage(message)) { }

		public byte[] GetExecutionMessage(ushort id = 0)
		{
			if (String.IsNullOrEmpty(ExecutorPath)) throw new InvalidOperationException();

			var argsStr = String.IsNullOrEmpty(ArgStr) ? "" : Message.Separator + ArgStr;
			var messageStr = Message.ExecutionHeader + Convert.ToChar(id) + EndPoint.ToString() + Message.Separator + ExecutorPath + argsStr + Message.MessageEnd;
			return Encoding.Unicode.GetBytes(messageStr);
		}

		public void Execute(int secondsTimeout = -1, string workingDir = "")
		{
			var process = new Process();
			process.StartInfo.FileName = ExecutorPath;
			process.StartInfo.Arguments = ArgStr;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.WorkingDirectory = workingDir;

			process.Start();
			if (!process.WaitForExit(secondsTimeout > 0 ? 1000 * secondsTimeout : -1))
			{
				process.Kill();
				throw new TimeoutException();
			}
		}

		public async Task Execute(CancellationToken ct, int secondsTimeout = -1, string workingDir = "")
		{
			var process = new Process();
			process.StartInfo.FileName = ExecutorPath;
			process.StartInfo.Arguments = ArgStr;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.WorkingDirectory = workingDir;

			process.Start();
			do
			{
				try
				{
					await Task.Delay(1000, ct);
				}
				catch (TaskCanceledException ex)
				{
					process.Kill();
					throw ex;
				}
				if (--secondsTimeout == 0)
				{
					process.Kill();
					throw new TimeoutException();
				}
			} while (!process.HasExited);
		}
	}

}
