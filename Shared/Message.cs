using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Distributor
{
	public static class Message
	{
		public const char InputHeader = '\u0080';
		public const char OutputHeader = '\u0081';
		public const char ExecutionHeader = '\u0091';
		public const char CancellationHeader = '\u0092';
		public const char ResponseHeader = '\u0010';
		public const char Successful = '\u0011';
		public const char Failed = '\u0012';
		public const char NodeIsIdel = '\u0013';
		public const char NodeIsBusy = '\u0014';
		public const char Separator = '\t';
		public const char MessageEnd = '\u0099';

		public static string ReadMessage(string message, out char header, out ushort id)
		{
			if (message == null)
				throw new ArgumentNullException();
			else if (String.IsNullOrEmpty(message))
				throw new ArgumentException();

			header = message[0];
			id = message[1];

			var i = message.IndexOf(MessageEnd);
			if (i >= 2)
				message = message.Substring(2, i - 2);
			else if (i < 0)
				message = message.Substring(2);
			else
				throw new FormatException();

			return message;
		}

		public static string ReadMessage(string message)
		{
			char header;
			ushort id;
			return ReadMessage(message, out header, out id);
		}

		public static string ReadMessage(byte[] message, out char header, out ushort id)
		{
			var messageStr = Encoding.Unicode.GetString(message);
			return ReadMessage(messageStr, out header, out id);
		}

		public static string ReadMessage(byte[] message)
		{
			char header;
			ushort id;
			return ReadMessage(message, out header, out id);
		}

		public static async Task<string> GetMessage(NetworkStream stream, CancellationToken ct)
		{
			var buffer = new byte[1500];
			var message = "";
			int i;

			do
			{
				i = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
				if (ct.IsCancellationRequested) throw new TaskCanceledException();
				message += Encoding.Unicode.GetString(buffer, 0, i);
			} while (message.Last() != MessageEnd);

			return message;
		}
	}
	
}
