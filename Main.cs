using AOSharp.Core;
using AOSharp.Core.UI;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Messaging;
using System.Text;

namespace PacketLogger
{
    public class Main : AOPluginEntry
    {
        private static readonly object _fileLock = new object();
        private static string _logFilePath;
        private static StreamWriter _writer;

        public override void Run()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string logDir = Path.Combine(pluginDir, "Logs");

                Directory.CreateDirectory(logDir);

                string fileName = $"packetlog_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
                _logFilePath = Path.Combine(logDir, fileName);

                _writer = new StreamWriter(_logFilePath, false, Encoding.UTF8)
                {
                    AutoFlush = true
                };

                Chat.WriteLine($"[PacketLogger] Plugin dir: {pluginDir}");
                Chat.WriteLine($"[PacketLogger] Log dir: {logDir}");
                Chat.WriteLine($"[PacketLogger] Log file path: {_logFilePath}");
                Chat.WriteLine($"[PacketLogger] Log file exists right now: {File.Exists(_logFilePath)}");

                Log("Logger ready.");
                Log($"Log file: {_logFilePath}");

                Network.PacketSent += OnPacketSent;
                Network.PacketReceived += OnPacketReceived;
                Network.N3MessageSent += OnN3MessageSent;
                Network.N3MessageReceived += OnN3MessageReceived;
            }
            catch (Exception ex)
            {
                Chat.WriteLine($"[PacketLogger] Failed to start logger: {ex}");
            }
        }

        private void OnPacketSent(object sender, object packet)
        {
            //DumpObject("PACKET SENT", packet);
        }

        private void OnPacketReceived(object sender, object packet)
        {
            //DumpObject("PACKET RECEIVED", packet);
        }

        private void OnN3MessageSent(object sender, N3Message message)
        {
            if (ShouldIgnoreN3Message(message))
                return;

            DumpObject("N3 SENT", message);
        }

        private void OnN3MessageReceived(object sender, N3Message message)
        {
            if (ShouldIgnoreN3Message(message))
                return;

            DumpObject("N3 RECEIVED", message);
        }

        private bool ShouldIgnoreN3Message(N3Message message)
        {
            if (message == null)
                return false;

            return message is StatMessage
                || message is FollowTargetMessage;
        }

        private void DumpObject(string direction, object obj)
        {
            try
            {
                string header = $"=== {direction}: {obj?.GetType().Name ?? "<null>"} ===";
                Log(header);

                if (obj == null)
                {
                    Log("  <null>");
                    return;
                }

                var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
                DumpMembers(obj, 1, visited, 0);
            }
            catch (Exception ex)
            {
                Log($"[Dump Error] {ex}");
            }
        }

        private void DumpMembers(object obj, int indent, HashSet<object> visited, int depth)
        {
            if (obj == null)
            {
                WriteIndented(indent, "null");
                return;
            }

            Type type = obj.GetType();

            if (IsSimple(type))
            {
                WriteIndented(indent, FormatSimple(obj));
                return;
            }

            if (obj is byte[] bytes)
            {
                WriteIndented(indent, $"byte[{bytes.Length}] = {BitConverter.ToString(bytes)}");
                return;
            }

            if (!type.IsValueType)
            {
                if (visited.Contains(obj))
                {
                    WriteIndented(indent, $"<circular reference: {type.Name}>");
                    return;
                }

                visited.Add(obj);
            }

            if (depth >= 3)
            {
                WriteIndented(indent, $"<{type.Name} depth limit reached>");
                return;
            }

            if (obj is IEnumerable enumerable && !(obj is string))
            {
                int i = 0;
                foreach (object item in enumerable)
                {
                    if (item == null)
                    {
                        WriteIndented(indent, $"[{i}] = null");
                    }
                    else if (IsSimple(item.GetType()))
                    {
                        WriteIndented(indent, $"[{i}] = {FormatSimple(item)}");
                    }
                    else
                    {
                        WriteIndented(indent, $"[{i}] -> {item.GetType().Name}");
                        DumpMembers(item, indent + 1, visited, depth + 1);
                    }

                    i++;
                }

                if (i == 0)
                    WriteIndented(indent, "[]");

                return;
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name);

            foreach (PropertyInfo prop in props)
            {
                object value;

                try
                {
                    value = prop.GetValue(obj);
                }
                catch (Exception ex)
                {
                    WriteIndented(indent, $"{prop.Name} = <error reading: {ex.Message}>");
                    continue;
                }

                if (value == null)
                {
                    WriteIndented(indent, $"{prop.Name} = null");
                    continue;
                }

                Type valueType = value.GetType();

                if (IsSimple(valueType))
                {
                    WriteIndented(indent, $"{prop.Name} = {FormatSimple(value)}");
                }
                else if (value is byte[] valueBytes)
                {
                    WriteIndented(indent, $"{prop.Name} = byte[{valueBytes.Length}] {BitConverter.ToString(valueBytes)}");
                }
                else if (value is IEnumerable && !(value is string))
                {
                    WriteIndented(indent, $"{prop.Name}:");
                    DumpMembers(value, indent + 1, visited, depth + 1);
                }
                else
                {
                    WriteIndented(indent, $"{prop.Name}:");
                    DumpMembers(value, indent + 1, visited, depth + 1);
                }
            }

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(f => f.Name);

            foreach (FieldInfo field in fields)
            {
                object value;

                try
                {
                    value = field.GetValue(obj);
                }
                catch (Exception ex)
                {
                    WriteIndented(indent, $"{field.Name} = <error reading: {ex.Message}>");
                    continue;
                }

                if (value == null)
                {
                    WriteIndented(indent, $"{field.Name} = null");
                    continue;
                }

                Type valueType = value.GetType();

                if (IsSimple(valueType))
                {
                    WriteIndented(indent, $"{field.Name} = {FormatSimple(value)}");
                }
                else if (value is byte[] valueBytes)
                {
                    WriteIndented(indent, $"{field.Name} = byte[{valueBytes.Length}] {BitConverter.ToString(valueBytes)}");
                }
                else if (value is IEnumerable && !(value is string))
                {
                    WriteIndented(indent, $"{field.Name}:");
                    DumpMembers(value, indent + 1, visited, depth + 1);
                }
                else
                {
                    WriteIndented(indent, $"{field.Name}:");
                    DumpMembers(value, indent + 1, visited, depth + 1);
                }
            }
        }

        private bool IsSimple(Type type)
        {
            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(TimeSpan)
                || type == typeof(Guid);
        }

        private string FormatSimple(object value)
        {
            if (value == null)
                return "null";

            if (value is string s)
                return s;

            return value.ToString();
        }

        private void WriteIndented(int indent, string text)
        {
            Log($"{new string(' ', indent * 2)}{text}");
        }

        private void Log(string text)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {text}";

            Chat.WriteLine(line);

            lock (_fileLock)
            {
                _writer?.WriteLine(line);
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}