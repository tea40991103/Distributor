using System;
using System.Collections.Generic;
using static System.Convert;
using System.Linq;
using System.IO;
using static System.IO.File;
using System.Text;
using System.Threading.Tasks;

namespace Distributor
{
	public static class Tools
	{
		public static bool IsAnsiEncoding(string filePath)
		{
			return ReadAllText(filePath).IndexOf('�') >= 0;
		}

		public static ushort ParseIPEndPoint(string ipEndPointStr, out string ipAddressStr)
		{
			if (ipEndPointStr == null) throw new ArgumentNullException();
			ipEndPointStr.Trim();

			var colonIndex = ipEndPointStr.LastIndexOf(':');
			ushort port = 0;
			if (colonIndex > 0)
			{
				if (colonIndex == ipEndPointStr.IndexOf(':') || ipEndPointStr[colonIndex - 1] == ']')
				{
					ipAddressStr = ipEndPointStr.Substring(0, colonIndex);
					port = ToUInt16(ipEndPointStr.Substring(colonIndex + 1));
				}
				else
					ipAddressStr = ipEndPointStr;
			}
			else if (colonIndex == 0)
			{
				ipAddressStr = "";
				port = ToUInt16(ipEndPointStr.Substring(colonIndex + 1));
			}
			else
			{
				ipAddressStr = ipEndPointStr;
			}

			return port;
		}
	}	
}
