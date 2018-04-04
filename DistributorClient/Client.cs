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

		CancellationTokenSource ExecutionCTS;

		public Client(string localDir)
		{
			Directory.CreateDirectory(localDir);
			LocalDir = localDir;
		}

		public Client(string nodePoolFilePath, string inputFileName, string outputFileName)
		{
			if (nodePoolFilePath == null || inputFileName == null || outputFileName == null)
				throw new ArgumentNullException();
			else if (String.IsNullOrWhiteSpace(nodePoolFilePath) || String.IsNullOrWhiteSpace(inputFileName) || String.IsNullOrWhiteSpace(outputFileName))
				throw new ArgumentException();

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
					foreach (var nodeLine in nodeLines) try { NodePool.Add(new Node(nodeLine)); } catch { }
				}
				catch (Exception ex)
				{
					ClearNodePool();
					throw ex;
				}

				NodeStatesFilePath = NodePoolFilePath + ".states";
				try
				{
					if (File.Exists(NodeStatesFilePath)
						&& File.GetLastWriteTime(NodeStatesFilePath) > File.GetLastWriteTime(NodePoolFilePath))
						//&& Process.GetProcessesByName(ProcessName).Length > 1)
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
				catch (Exception ex)
				{
					ClearNodePool();
					throw ex;
				}
			}
			else if (nodePoolFilePath != NodePoolFilePath || inputFileName != InputFileName || outputFileName != OutputFileName)
			{
				throw new InvalidOperationException();
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

				var nodeIndex = -1;
				try
				{
					await Task.Delay(PidRandom.Next(1000 * NodeCount), ExecutionCTS.Token);

					Node node = null;
					while (true)
					{
						ReadNodeStates();
						for (int i = 0; i < NodeCount; ++i)
							if (NodeStates[i] == (byte)NodeState.Idel)
							{
								nodeIndex = i;
								node = NodePool[i];
								NodeStates[i] = (byte)NodeState.Busy;
								WriteNodeStates();
								break;
							}
						if (node != null)
							break;
						else
							await Task.Delay(5000, ExecutionCTS.Token);
					}

					var outputFilePath = LocalDir + OutputFileName;
					if (node.IpEndPoint.Address == IPAddress.Loopback)
					{
						try
						{
							File.Delete(outputFilePath);
							await node.Execute(ExecutionCTS.Token, secondsTimeout, LocalDir);
							if (!File.Exists(outputFilePath)) throw new ApplicationException("Local execution failure");
							SetNodeState(nodeIndex, NodeState.Idel);
						}
						catch (Exception ex)
						{
							if (!(ex is InvalidOperationException) && !(ex is System.ComponentModel.Win32Exception))
								SetNodeState(nodeIndex, NodeState.Idel);
							throw ex;
						}
					}
					else
					{
						var inputMessageId = Convert.ToUInt16(PidRandom.Next(1, ushort.MaxValue));
						var executionMessageId = Convert.ToUInt16(PidRandom.Next(1, ushort.MaxValue));
						byte[] inputMessage = null, executionMessage = null;
						try
						{
							inputMessage = GetInputMessage(inputMessageId);
							executionMessage = node.GetExecutionMessage(executionMessageId);
						}
						catch (Exception ex)
						{
							SetNodeState(nodeIndex, NodeState.Idel);
							throw ex;
						}
						var sw = new Stopwatch();
						var sw2 = new Stopwatch();
						var done = false;

						TcpClient tcpClient;
						while (!done)
						{
							tcpClient = new TcpClient(AddressFamily.InterNetworkV6);
							tcpClient.Client.DualMode = true;
							var connect = tcpClient.ConnectAsync(node.IpEndPoint.Address, node.IpEndPoint.Port);
							sw.Restart();
							do
							{
								if (((secondsTimeout <= 0 || !sw2.IsRunning) && sw.Elapsed.TotalSeconds >= 60)
									|| (secondsTimeout > 0 && sw2.Elapsed.TotalSeconds >= secondsTimeout))
									throw new TimeoutException("Connection timeout");
								else
								{
									await Task.Delay(500, ExecutionCTS.Token);
									if (connect.IsFaulted) connect = tcpClient.ConnectAsync(node.IpEndPoint.Address, node.IpEndPoint.Port);
								}
							} while (!connect.IsCompleted);

							var stream = tcpClient.GetStream();
							try
							{
								while (true)
								{
									var getMessage = Message.GetMessage(stream, ExecutionCTS.Token);
									sw.Restart();
									while (!getMessage.IsCompleted && !getMessage.IsFaulted)
									{
										if (sw.Elapsed.TotalSeconds >= 30)
											throw new SocketException();
										else if (secondsTimeout > 0 && sw2.Elapsed.TotalSeconds >= secondsTimeout)
											throw new TimeoutException("Remote execution timeout");
										else
											await Task.Delay(100);
									}
									if (getMessage.IsCanceled)
										throw new TaskCanceledException();
									else if (getMessage.IsFaulted)
										throw new SocketException();

									var message = getMessage.Result;
									if (message[1] != Message.DefaultId && message[1] != inputMessageId && message[1] != executionMessageId)
										throw new ApplicationException("Unexpected response");
									else if (message[0] == Message.ResponseHeader)
									{
										if (message[2] == Message.NodeIsIdel)
										{
											stream.Write(inputMessage, 0, inputMessage.Length);
										}
										else if (message[1] == inputMessageId)
										{
											if (message[2] == Message.Successful)
											{
												stream.Write(executionMessage, 0, executionMessage.Length);
												sw2.Start();
											}
											else
												throw new ApplicationException("Remote input file creation failure");
										}
										else if (message[1] == executionMessageId && message[2] == Message.Failed)
											throw new ApplicationException("Remote execution failure");
									}
									else if (message[0] == Message.OutputHeader)
									{
										File.WriteAllText(outputFilePath, Message.ReadMessage(message));
										stream.Write(Message.TerminationMessage, 0, Message.TerminationMessage.Length);
										SetNodeState(nodeIndex, NodeState.Idel);
										done = true;
										break;
									}
								}
							}
							catch (Exception ex)
							{
								if (!(ex is SocketException))
								{
									try
									{
										stream.Write(Message.TerminationMessage, 0, Message.TerminationMessage.Length);
										SetNodeState(nodeIndex, NodeState.Idel);
									} catch { }
									throw ex;
								}
							}
							finally
							{
								tcpClient.Close();
							}
						}
					}
				}
				finally
				{
					ExecutionCTS = null;
				}
				return nodeIndex;
			}
			else
				throw new InvalidOperationException();
		}

		public byte[] GetInputMessage(ushort id = 0)
		{
			if (String.IsNullOrEmpty(InputFileName)) throw new InvalidOperationException();

			var inputFilePath = LocalDir + InputFileName;
			var inputFileContent = Tools.IsAnsiEncoding(inputFilePath) ? File.ReadAllText(inputFilePath, Encoding.Default) : File.ReadAllText(inputFilePath);
			var messageStr = String.Format("{0}{1}{2}{3}{4}{5}{6}{7}",
				Message.InputHeader, Convert.ToChar(id),
				InputFileName, Message.Separator,
				OutputFileName, Message.Separator,
				inputFileContent, Message.MessageEnd);
			return Encoding.Unicode.GetBytes(messageStr);
		}

		public void Cancel()
		{
			if (ExecutionCTS != null) ExecutionCTS.Cancel();
		}

		public static void ClearNodePool()
		{
			NodePool.Clear();
			if (File.Exists(NodeStatesFilePath) && Process.GetProcessesByName(ProcessName).Length == 1)
			{
				//File.Delete(NodeStatesFilePath);
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

		static void SetNodeState(int nodeIndex, NodeState nodeState)
		{
			if (nodeIndex < 0 || nodeIndex >= NodeCount) throw new ArgumentOutOfRangeException();

			ReadNodeStates();
			NodeStates[nodeIndex] = (byte)nodeState;
			WriteNodeStates();
		}
	}

}
