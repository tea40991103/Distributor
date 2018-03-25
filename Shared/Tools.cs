using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Distributor
{
	public static class Tools
	{
		public static bool IsAnsiEncoding(string filePath)
		{
			return File.ReadAllText(filePath).IndexOf('�') >= 0;
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
					port = Convert.ToUInt16(ipEndPointStr.Substring(colonIndex + 1));
				}
				else
					ipAddressStr = ipEndPointStr;
			}
			else if (colonIndex == 0)
			{
				ipAddressStr = "";
				port = Convert.ToUInt16(ipEndPointStr.Substring(colonIndex + 1));
			}
			else
			{
				ipAddressStr = ipEndPointStr;
			}

			return port;
		}
	}	
}
