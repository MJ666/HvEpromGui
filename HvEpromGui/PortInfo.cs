using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace HvEpromGui
{
    /// <summary>A serial port with the friendly name Windows shows in the Device Manager.</summary>
    public sealed class PortInfo
    {
        public string PortName;
        public string Description;

        public override string ToString()
        {
            return string.IsNullOrEmpty(Description) ? PortName : PortName + " - " + Description;
        }

        /// <summary>
        /// Lists the available COM ports. With <paramref name="withDescriptions"/> the Device Manager
        /// names (e.g. "Arduino Mega 2560") are looked up through WMI, which can take a second or two.
        /// </summary>
        public static List<PortInfo> Enumerate(bool withDescriptions)
        {
            var names = SerialPort.GetPortNames()
                .OrderBy(p => { int n; return int.TryParse(p.Replace("COM", ""), out n) ? n : 9999; })
                .ThenBy(p => p)
                .ToArray();

            var map = withDescriptions ? ReadDescriptions() : new Dictionary<string, string>();
            var list = new List<PortInfo>();
            foreach (var n in names)
            {
                string d;
                map.TryGetValue(n, out d);
                list.Add(new PortInfo { PortName = n, Description = d });
            }
            return list;
        }

        // Win32_PnPEntity names look like "Arduino Mega 2560 (COM3)" - the same text the Device Manager shows.
        private static Dictionary<string, string> ReadDescriptions()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%)'"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject o in results)
                    {
                        using (o)
                        {
                            string name = o["Name"] as string;
                            if (name == null) continue;
                            var m = Regex.Match(name, @"^(.*?)\s*\((COM\d+)\)\s*$");
                            if (m.Success) map[m.Groups[2].Value] = m.Groups[1].Value;
                        }
                    }
                }
            }
            catch
            {
                // WMI unavailable: fall back to plain port names
            }
            return map;
        }
    }
}
