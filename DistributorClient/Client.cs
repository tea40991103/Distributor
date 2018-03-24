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
	public class Client
	{
		public static bool Verbose = false;
		public readonly static string ProcessName = Process.GetCurrentProcess().ProcessName;

		static string NodePoolFilePath;
		static List<Node> NodePool = new List<Node>();
		public static int NodeCount
		{
			get { return NodePool.Count(); }
		}

		static string NodeStatesFilePath;
		static byte[] NodeStates;

		static string InputFileName, OutputFileName;
		static Random PidRandom = new Random(Process.GetCurrentProcess().Id);

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

		CancellationTokenSource ExecutionCTS;

		public Client(string localDir)
		{
			Directory.CreateDirectory(localDir);
			LocalDir = localDir;
		}

		public Client(string nodePoolFilePath, string inputFileName, string outputFileName)
		{
			if (String.IsNullOrEmpty(nodePoolFilePath) || String.IsNullOrEmpty(inputFileName) || String.IsNullOrEmpty(outputFileName))
				throw new ArgumentNullException();
			if (NodeCount == 0)
			{
				if (File.Exists(nodePoolFilePath))
				{
					NodePoolFilePath = nodePoolFilePath;
					InputFileName = inputFileName;
					OutputFileName = outputFileName;
				}
				else
					throw new FileNotFoundException();

				try
				{
					var nodeLines = Tools.IsAnsiEncoding(NodePoolFilePath) ? File.ReadAllLines(NodePoolFilePath, Encoding.Default) : File.ReadAllLines(NodePoolFilePath);
					File.SetAttributes(NodePoolFilePath, FileAttributes.ReadOnly);
					foreach (var nodeLine in nodeLines) NodePool.Add(new Node(nodeLine));
				}
				catch
				{
					ClearNodePool();
					throw new FormatException();
				}

				NodeStatesFilePath = NodePoolFilePath + ".states";
				try
				{
					if (File.Exists(NodeStatesFilePath) && File.GetLastWriteTime(NodeStatesFilePath) > File.GetLastWriteTime(NodePoolFilePath))
					{
						ReadNodeStates();
					}
					else
					{
						NodeStates = new byte[NodeCount];
						var idel = (byte)NodeState.Idel;
						for (var i = 0; i < NodeCount; ++i) NodeStates[i] = idel;
					}
					WriteNodeStates();
				}
				catch
				{
					ClearNodePool();
					throw new System.Security.SecurityException();
				}
			}
			else if (nodePoolFilePath != NodePoolFilePath || inputFileName != InputFileName || outputFileName != OutputFileName)
			{
				throw new ArgumentException();
			}
		}

		public async Task<int> Connect(int secondsTimeout = -1)
		{
			if (NodeCount > 0)
			{
				if (ExecutionCTS != null)
					throw new InvalidOperationException();
				else
					ExecutionCTS = new CancellationTokenSource();

				try
				{
					var sw = new Stopwatch();
					Node node = null;
					int nodeIndex;
					var idel = (byte)NodeState.Idel;

					await Task.Delay(PidRandom.Next(1000 * NodeCount), ExecutionCTS.Token);
					sw.Start();
					do
					{
						ReadNodeStates();
						for (nodeIndex = 0; nodeIndex < NodeCount; ++nodeIndex)
							if (NodeStates[nodeIndex] == idel)
							{
								node = NodePool[nodeIndex];
								NodeStates[nodeIndex] = (byte)NodeState.Busy;
								WriteNodeStates();
								break;
							}
						if (node != null)
							break;
						else if (secondsTimeout > 0 && sw.Elapsed.Seconds > secondsTimeout)
							throw new TimeoutException();
						else
							await Task.Delay(5000, ExecutionCTS.Token);
					} while (true);

					using (var tcpClient = new TcpClient())
					{
						tcpClient.ConnectAsync(node.EndPoint.Address, node.EndPoint.Port);
						do
						{
							if (secondsTimeout > 0 && sw.Elapsed.Seconds > secondsTimeout)
								throw new TimeoutException();
							else
								await Task.Delay(500, ExecutionCTS.Token);
						} while (!tcpClient.Connected);

						using (var stream = tcpClient.GetStream())
						{
							var reader = new StreamReader(stream, Encoding.Unicode);
							string message;

							var inputMessageId = Convert.ToUInt16(PidRandom.Next(1, ushort.MaxValue));
							var inputMessage = GetInputMessage(inputMessageId);
							stream.WriteAsync(inputMessage, 0, inputMessage.Length);

							var executionMessageId = ushort.MaxValue;
							do
							{
								while (!stream.DataAvailable)
								{
									if (secondsTimeout > 0 && sw.Elapsed.Seconds > secondsTimeout)
										throw new TimeoutException();
									else
										await Task.Delay(500, ExecutionCTS.Token);
								}

								message = "";
								do
								{
									message += reader.ReadToEnd();
									if (secondsTimeout > 0 && sw.Elapsed.Seconds > secondsTimeout)
										throw new TimeoutException();
									else
										await Task.Delay(500, ExecutionCTS.Token);
								} while (message.Last() != Message.MessageEnd);

								if (message[0] == Message.ResponseHeader)
								{
									if (message[1] == inputMessageId)
									{
										if (message[2] == Message.Successful)
										{
											executionMessageId = Convert.ToUInt16(PidRandom.Next(1, ushort.MaxValue));
											var executionMessage = node.GetExecutionMessage(executionMessageId);
											stream.Write(executionMessage, 0, executionMessage.Length);
										}
										else
											throw new ApplicationException();
									}
									else if (message[1] == executionMessageId && message[2] == Message.Failed)
									{
										throw new ApplicationException();
									}
								}
							} while (message[0] != Message.OutputHeader);

							File.WriteAllText(LocalDir + OutputFileName, Message.ReadMessage(message));
						}
					}

					ReadNodeStates();
					NodeStates[nodeIndex] = idel;
					WriteNodeStates();
					return nodeIndex;
				}
				finally
				{
					ExecutionCTS = null;
				}
			}
			else
				return -1;
		}

		public byte[] GetInputMessage(ushort id = 0)
		{
			if (String.IsNullOrEmpty(InputFileName)) throw new InvalidOperationException();

			var inputFilePath = LocalDir + InputFileName;
			var inputFileContent = Tools.IsAnsiEncoding(inputFilePath) ? File.ReadAllText(inputFilePath, Encoding.Default) : File.ReadAllText(inputFilePath);
			var messageStr = Message.InputHeader + Convert.ToChar(id) + InputFileName + Message.Separator + OutputFileName + Message.Separator + inputFileContent + Message.MessageEnd;
			return Encoding.Unicode.GetBytes(messageStr);
		}

		public void Cancel()
		{
			if (ExecutionCTS != null) ExecutionCTS.Cancel();
		}

		public static void ClearNodePool()
		{
			NodePool.Clear();
			if (File.Exists(NodePoolFilePath) && Process.GetProcessesByName(ProcessName).Length == 1)
			{
				File.SetAttributes(NodePoolFilePath, FileAttributes.Normal);
				File.Delete(NodeStatesFilePath);
			}
		}

		static void ReadNodeStates()
		{
			try
			{
				NodeStates = File.ReadAllBytes(NodeStatesFilePath);
			}
			catch
			{
				Thread.Sleep(500);
				NodeStates = File.ReadAllBytes(NodeStatesFilePath);
			}
		}

		static void WriteNodeStates()
		{
			try
			{
				File.WriteAllBytes(NodeStatesFilePath, NodeStates);
			}
			catch
			{
				Thread.Sleep(500);
				File.WriteAllBytes(NodeStatesFilePath, NodeStates);
			}
		}
	}

}
