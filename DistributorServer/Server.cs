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
		public readonly static string ProcessName = Process.GetCurrentProcess().ProcessName;

		public int ExeSecondsTimeout = -1;
		string InputFileName, OutputFileName;

		string _LocalDir = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar;
		public string LocalDir
		{
			get { return _LocalDir; }
			set
			{
				if (Directory.Exists(value))
				{
					_LocalDir = value;
					if (_LocalDir.Last() != Path.DirectorySeparatorChar) _LocalDir += Path.DirectorySeparatorChar;
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

		public async Task Listen(string ipEndPointStr = "")
		{
			if (ListeningCTS != null)
				throw new InvalidOperationException();
			else
				ListeningCTS = new CancellationTokenSource();

			TcpListener tcpListener = null, tcpListenerIPv6 = null;
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
				else if (ipAddressStr == "localhost")
				{
					tcpListener = new TcpListener(IPAddress.Loopback, port);
					tcpListenerIPv6 = new TcpListener(IPAddress.IPv6Loopback, port);
				}
				else
					tcpListener = new TcpListener(IPAddress.Parse(ipAddressStr), port);

				tcpListener.Start();
				if (tcpListenerIPv6 != null) tcpListenerIPv6.Start();

				var inputMessageId = ushort.MaxValue;
				var executionMessageId = ushort.MaxValue;
				var response = Message.NodeIsIdelResponse;
				Node node;
				Task execution = null;
				CancellationTokenSource executionCTS = null;
				var sw = new Stopwatch();
				var sw2 = new Stopwatch();

				TcpClient tcpClient = null;
				while (true)
				{
					try
					{
						Task<TcpClient> listening, listeningIPv6 = null;
						listening = tcpListener.AcceptTcpClientAsync();
						if (tcpListenerIPv6 != null) listeningIPv6 = tcpListenerIPv6.AcceptTcpClientAsync();

						sw.Restart();
						while (true)
						{
							if (listening.IsCompleted)
							{
								tcpClient = listening.Result;
								break;
							}
							else if (listeningIPv6 != null && listeningIPv6.IsCompleted)
							{
								tcpClient = listeningIPv6.Result;
								break;
							}
							else if ((listening.IsFaulted && listeningIPv6 == null)
								|| (listening.IsFaulted && listeningIPv6.IsFaulted))
								throw new SocketException();
							else if ((ExeSecondsTimeout <= 0 && sw.Elapsed.TotalSeconds >= 60)
								|| (ExeSecondsTimeout > 0 && sw2.Elapsed.TotalSeconds >= ExeSecondsTimeout))
								throw new TimeoutException();
							else
								await Task.Delay(500, ListeningCTS.Token);
						}

						var stream = tcpClient.GetStream();
						while (true)
						{
							while (!stream.DataAvailable)
							{
								stream.Write(response, 0, response.Length);
								await Task.Delay(2000, ListeningCTS.Token);
								if (execution != null && (execution.IsCompleted || execution.IsFaulted)) break;
							}
							if (execution != null && execution.IsCompleted)
							{
								try
								{
									response = GetOutputMessage(inputMessageId);
								}
								catch
								{
									response = GetResponseMessage(executionMessageId, Message.Failed);
								}
								execution = null;
								continue;
							}
							else if (execution != null && execution.IsFaulted)
							{
								response = GetResponseMessage(executionMessageId, Message.Failed);
								execution = null;
								continue;
							}

							var message = await Message.GetMessage(stream, ListeningCTS.Token);
							if (message[0] == Message.InputHeader && executionCTS == null)
							{
								inputMessageId = message[1];
								try
								{
									ReadInputMessage(message);
									response = GetResponseMessage(inputMessageId, Message.Successful);
									executionCTS = new CancellationTokenSource();
								}
								catch
								{
									response = GetResponseMessage(inputMessageId, Message.Failed);
								}
							}
							else if (message[0] == Message.ExecutionHeader && execution == null && executionCTS != null)
							{
								executionMessageId = message[1];
								try
								{
									node = new Node(Message.ReadMessage(message));
									node.IpEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
									File.Delete(LocalDir + OutputFileName);
									execution = node.Execute(executionCTS.Token, ExeSecondsTimeout, LocalDir);
									sw2.Start();
									response = Message.NodeIsBusyResponse;
								}
								catch
								{
									response = GetResponseMessage(executionMessageId, Message.Failed);
								}
							}
							else if (message[0] == Message.TerminationHeader)
							{
								if (execution != null && executionCTS != null) executionCTS.Cancel();
								sw2.Reset();
								inputMessageId = executionMessageId = ushort.MaxValue;
								response = Message.NodeIsIdelResponse;
								execution = null;
								executionCTS = null;
								break;
							}
						}
					}
					catch (Exception ex)
					{
						if (ex is TimeoutException)
						{
							if (execution != null && executionCTS != null) executionCTS.Cancel();
							sw2.Reset();
							inputMessageId = executionMessageId = ushort.MaxValue;
							response = Message.NodeIsIdelResponse;
							execution = null;
							executionCTS = null;
						}
						else if (ex is SocketException || ex is TaskCanceledException)
						{
							if (execution != null && executionCTS != null) executionCTS.Cancel();
							throw ex;
						}
					}
					finally
					{
						tcpClient.Close();
					}
				}
			}
			finally
			{
				if (tcpListener != null) tcpListener.Stop();
				if (tcpListenerIPv6 != null) tcpListenerIPv6.Stop();
				ListeningCTS = null;
			}			
		}

		public void ReadInputMessage(string message)
		{
			var input = Message.ReadMessage(message);
			var index1 = input.IndexOf(Message.Separator);
			var index2 = input.IndexOf(Message.Separator, index1 + 1);
			InputFileName = input.Substring(0, index1);
			OutputFileName = input.Substring(index1 + 1, index2 - index1 - 1);
			var inputFileContent = input.Substring(index2 + 1);
			File.WriteAllText(LocalDir + InputFileName, inputFileContent);
		}

		public byte[] GetOutputMessage(ushort id = 0)
		{
			if (String.IsNullOrEmpty(OutputFileName)) throw new InvalidOperationException();

			var outputFilePath = LocalDir + OutputFileName;
			var outputFileContent = Tools.IsAnsiEncoding(outputFilePath) ? File.ReadAllText(outputFilePath, Encoding.Default) : File.ReadAllText(outputFilePath);
			var messageStr = String.Format("{0}{1}{2}{3}",
				Message.OutputHeader, Convert.ToChar(id),
				outputFileContent, Message.MessageEnd);
			return Encoding.Unicode.GetBytes(messageStr);
		}

		public static byte[] GetResponseMessage(ushort id, char state)
		{
			var messageStr = String.Format("{0}{1}{2}{3}",
				Message.ResponseHeader, Convert.ToChar(id),
				state, Message.MessageEnd);
			return Encoding.Unicode.GetBytes(messageStr);
		}

		public void Stop()
		{
			if (ListeningCTS != null) ListeningCTS.Cancel();
		}
	}
}
