using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ShaiyaServerHub.Core
{
    /// <summary>Parsing dell'XML di un task (schtasks /Query /XML) per il modale Edit.</summary>
    public static class TaskXml
    {
        private static readonly XNamespace NS = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        public static TaskDefinition Parse(string xml, string name)
        {
            var def = new TaskDefinition { Name = name };
            try
            {
                int i = xml.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase);
                if (i > 0) xml = xml.Substring(i);
                var root = XDocument.Parse(xml).Root;

                var exec = root.Element(NS + "Actions")?.Element(NS + "Exec");
                if (exec != null)
                {
                    def.Program = (string)exec.Element(NS + "Command") ?? "";
                    def.Arguments = (string)exec.Element(NS + "Arguments") ?? "";
                    def.WorkingDir = (string)exec.Element(NS + "WorkingDirectory") ?? "";
                }

                var trg = root.Element(NS + "Triggers");
                if (trg != null) ParseTrigger(trg, def.Schedule);
            }
            catch { /* XML inatteso → def coi default */ }
            return def;
        }

        private static void ParseTrigger(XElement triggers, ScheduleSpec s)
        {
            var cal = triggers.Element(NS + "CalendarTrigger");
            if (cal != null)
            {
                s.Time = TimeFrom(cal);
                var wk = cal.Element(NS + "ScheduleByWeek");
                if (wk != null)
                {
                    s.Type = "WEEKLY";
                    var dow = wk.Element(NS + "DaysOfWeek");
                    if (dow != null)
                        s.Days = string.Join(",", dow.Elements()
                            .Select(e => DayCode(e.Name.LocalName)).Where(x => x != null));
                }
                else s.Type = "DAILY";
                return;
            }
            var tt = triggers.Element(NS + "TimeTrigger");
            if (tt != null)
            {
                s.Time = TimeFrom(tt);
                var rep = tt.Element(NS + "Repetition")?.Element(NS + "Interval")?.Value;
                var m = rep != null ? Regex.Match(rep, @"PT(\d+)([HM])") : Match.Empty;
                if (m.Success)
                {
                    s.Interval = int.Parse(m.Groups[1].Value);
                    s.Type = m.Groups[2].Value == "H" ? "HOURLY" : "MINUTE";
                }
                else s.Type = "ONCE";
                return;
            }
            if (triggers.Element(NS + "LogonTrigger") != null) { s.Type = "LOGON"; return; }
            if (triggers.Element(NS + "BootTrigger") != null) { s.Type = "BOOT"; return; }
            if (triggers.Element(NS + "EventTrigger") != null) { s.Type = "SHUTDOWN"; return; }
        }

        private static string TimeFrom(XElement trg)
        {
            var sb = (string)trg.Element(NS + "StartBoundary");
            return (sb != null && sb.Length >= 16) ? sb.Substring(11, 5) : "04:00"; // HH:mm
        }

        private static string DayCode(string dayName)
        {
            switch (dayName)
            {
                case "Monday": return "MON";
                case "Tuesday": return "TUE";
                case "Wednesday": return "WED";
                case "Thursday": return "THU";
                case "Friday": return "FRI";
                case "Saturday": return "SAT";
                case "Sunday": return "SUN";
                default: return null;
            }
        }
    }
}
