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
				else if (ipAddressStr == "localhost")
				{
					tcpListener = new TcpListener(IPAddress.Loopback, port);
					tcpListenerIPv6 = new TcpListener(IPAddress.IPv6Loopback, port);
				}
				else
					tcpListener = new TcpListener(IPAddress.Parse(ipAddressStr), port);

				tcpListener.Start();
				if (tcpListenerIPv6 != null) tcpListenerIPv6.Start();
				while (true)
				{
					Task<TcpClient> listening, listeningIPv6 = null;
					listening = tcpListener.AcceptTcpClientAsync();
					if (tcpListenerIPv6 != null) listeningIPv6 = tcpListenerIPv6.AcceptTcpClientAsync();

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
						else
							await Task.Delay(500, ListeningCTS.Token);
					}

					using (var stream = tcpClient.GetStream())
					{
						ushort inputMessageId = ushort.MaxValue, executionMessageId = ushort.MaxValue;
						Node node;
						Task execution = null;
						CancellationTokenSource executionCTS = null;

						while (true)
						{
							while (!stream.DataAvailable)
							{
								if (execution != null && (execution.IsCompleted || execution.IsFaulted)) break;
								await Task.Delay(500, ListeningCTS.Token);
							}
							if (execution != null && execution.IsCompleted)
							{
								try
								{
									var outputMessage = GetOutputMessage(inputMessageId);
									stream.Write(outputMessage, 0, outputMessage.Length);
								}
								catch
								{
									var responseMessage = GetResponseMessage(executionMessageId, Message.Failed);
									stream.Write(responseMessage, 0, responseMessage.Length);
								}
								break;
							}
							else if (execution != null && execution.IsFaulted)
							{
								var responseMessage = GetResponseMessage(executionMessageId, Message.Failed);
								stream.Write(responseMessage, 0, responseMessage.Length);
								break;
							}

							var message = await Message.GetMessage(stream, ListeningCTS.Token);
							if (message[0] == Message.InputHeader)
							{
								inputMessageId = message[1];
								try
								{
									var messageStr = Message.ReadMessage(message);
									var index1 = messageStr.IndexOf(Message.Separator);
									var index2 = messageStr.IndexOf(Message.Separator, index1 + 1);
									InputFileName = messageStr.Substring(0, index1);
									OutputFileName = messageStr.Substring(index1 + 1, index2 - index1 - 1);
									var inputFileContent = messageStr.Substring(index2 + 1);
									File.WriteAllText(LocalDir + InputFileName, inputFileContent);
									var responseMessage = GetResponseMessage(inputMessageId, Message.Successful);
									stream.Write(responseMessage, 0, responseMessage.Length);
									executionCTS = new CancellationTokenSource();
								}
								catch
								{
									var responseMessage = GetResponseMessage(inputMessageId, Message.Failed);
									stream.Write(responseMessage, 0, responseMessage.Length);
								}
							}
							else if (message[0] == Message.ExecutionHeader && execution == null && executionCTS != null)
							{
								executionMessageId = message[1];
								node = new Node(Message.ReadMessage(message));
								node.IpEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
								File.Delete(LocalDir + OutputFileName);
								execution = node.Execute(executionCTS.Token, ExeSecondsTimeout, LocalDir);
							}
							else if (message[0] == Message.CancellationHeader && execution != null)
							{
								executionCTS.Cancel();
							}
						}
					}
					tcpClient.Close();
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
