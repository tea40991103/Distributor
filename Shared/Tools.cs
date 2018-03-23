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

	}	
}
