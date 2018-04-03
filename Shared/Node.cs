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

		public IPEndPoint IpEndPoint;
		string ExecutorPath, ArgStr = "";

		public Node(string nodeLine)
		{
			if (nodeLine == null)
				throw new ArgumentNullException();
			else if (String.IsNullOrWhiteSpace(nodeLine))
				throw new ArgumentException();

			var args = nodeLine.Split(Message.Separator);
			string ipAddressStr;
			int port = Tools.ParseIPEndPoint(args[0], out ipAddressStr);
			if (port == 0) port = DefaultPort;
			if (ipAddressStr == "localhost")
				IpEndPoint = new IPEndPoint(IPAddress.Loopback, port);
			else
				IpEndPoint = new IPEndPoint(Dns.GetHostAddresses(ipAddressStr)[0], port);
			
			if (String.IsNullOrWhiteSpace(args[1])) throw new FormatException();
			ExecutorPath = args[1];
			if (args.Length > 2) ArgStr = args[2];
		}

		public Node(byte[] message) : this(Message.ReadMessage(message)) { }

		public byte[] GetExecutionMessage(ushort id = 0)
		{
			if (String.IsNullOrEmpty(ExecutorPath)) throw new InvalidOperationException();

			var argsStr = String.IsNullOrEmpty(ArgStr) ? "" : Message.Separator + ArgStr;
			var messageStr = String.Format("{0}{1}{2}{3}{4}{5}{6}",
				Message.ExecutionHeader, Convert.ToChar(id),
				IpEndPoint.ToString(), Message.Separator,
				ExecutorPath, argsStr, Message.MessageEnd);
			return Encoding.Unicode.GetBytes(messageStr);
		}

		public void Execute(int secondsTimeout = -1, string workingDir = "")
		{
			var process = new Process();
			process.StartInfo.FileName = ExecutorPath;
			process.StartInfo.Arguments = ArgStr;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.WorkingDirectory = workingDir.TrimEnd(Path.DirectorySeparatorChar);

			process.Start();
			if (!process.WaitForExit(secondsTimeout > 0 ? 1000 * secondsTimeout : -1))
			{
				process.Kill();
				throw new TimeoutException("Local execution timeout");
			}
		}

		public async Task Execute(CancellationToken ct, int secondsTimeout = -1, string workingDir = "")
		{
			var process = new Process();
			process.StartInfo.FileName = ExecutorPath;
			process.StartInfo.Arguments = ArgStr;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.WorkingDirectory = workingDir.TrimEnd(Path.DirectorySeparatorChar);

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
					throw new TimeoutException("Local execution timeout");
				}
			} while (!process.HasExited);
		}
	}

}
