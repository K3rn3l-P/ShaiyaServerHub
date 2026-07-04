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
                if (trg != null)
                {
                    // Parsa TUTTI i trigger, in ordine, ciascuno come suo ScheduleSpec.
                    foreach (var el in trg.Elements())
                    {
                        var spec = ParseOneTrigger(el);
                        if (spec != null) def.Triggers.Add(spec);
                    }
                }
                if (def.Triggers.Count == 0) def.Triggers.Add(new ScheduleSpec());
            }
            catch { /* XML inatteso → def coi default */ if (def.Triggers.Count == 0) def.Triggers.Add(new ScheduleSpec()); }
            return def;
        }

        private static ScheduleSpec ParseOneTrigger(XElement el)
        {
            var s = new ScheduleSpec();
            switch (el.Name.LocalName)
            {
                case "CalendarTrigger":
                    s.Time = TimeFrom(el);
                    var wk = el.Element(NS + "ScheduleByWeek");
                    if (wk != null)
                    {
                        s.Type = "WEEKLY";
                        var dow = wk.Element(NS + "DaysOfWeek");
                        if (dow != null)
                            s.Days = string.Join(",", dow.Elements()
                                .Select(e => DayCode(e.Name.LocalName)).Where(x => x != null));
                    }
                    else s.Type = "DAILY";
                    return s;
                case "TimeTrigger":
                    s.Time = TimeFrom(el);
                    var rep = el.Element(NS + "Repetition")?.Element(NS + "Interval")?.Value;
                    var m = rep != null ? Regex.Match(rep, @"PT(\d+)([HM])") : Match.Empty;
                    if (m.Success)
                    {
                        s.Interval = int.Parse(m.Groups[1].Value);
                        s.Type = m.Groups[2].Value == "H" ? "HOURLY" : "MINUTE";
                    }
                    else s.Type = "ONCE";
                    return s;
                case "LogonTrigger": s.Type = "LOGON"; return s;
                case "BootTrigger": s.Type = "BOOT"; return s;
                case "EventTrigger":
                    string sub = (string)el.Element(NS + "Subscription") ?? "";
                    ParseSubscription(sub, s);
                    // Il filtro 1074/User32 è il nostro preset "SHUTDOWN"; il resto è "EVENT" generico.
                    s.Type = (s.EventXPath.Contains("1074") && s.EventXPath.Contains("User32")) ? "SHUTDOWN" : "EVENT";
                    return s;
                default: return null;
            }
        }

        private static void ParseSubscription(string subscription, ScheduleSpec s)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(subscription)) return;
                var ql = XDocument.Parse(subscription).Root;      // <QueryList>
                var sel = ql.Descendants().FirstOrDefault(e => e.Name.LocalName == "Select");
                if (sel != null)
                {
                    var path = sel.Attribute("Path");
                    if (path != null) s.EventChannel = path.Value;
                    s.EventXPath = (sel.Value ?? "").Trim();
                }
            }
            catch { /* Subscription non parsabile → resta il default */ }
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
