using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml.Linq;
using LibreHardwareMonitor.Hardware;

static class CpuSensorBridge {
    static int Score(string name) {
        if (name.IndexOf("Tctl/Tdie", StringComparison.OrdinalIgnoreCase) >= 0) return 50;
        if (name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0) return 40;
        if (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0) return 30;
        if (name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0) return 20;
        return 0;
    }
    static void Write(string destination, XElement result) {
        string temporary = destination + ".tmp";
        result.Save(temporary);
        if (File.Exists(destination)) File.Replace(temporary, destination, null);
        else File.Move(temporary, destination);
    }
    [STAThread] static void Main(string[] args) {
        if (args.Length != 1) return;
        int parentId; if (!int.TryParse(args[0], out parentId)) return;
        string destination = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cpu-readings.xml");
        using (var parent = Process.GetProcessById(parentId)) {
            var computer = new Computer { IsCpuEnabled = true };
            try {
                computer.Open();
                while (!parent.HasExited) {
                    double temperature = -1, power = -1; int best = 0; string source = "";
                    foreach (var hardware in computer.Hardware) {
                        if (hardware.HardwareType != HardwareType.Cpu) continue;
                        hardware.Update();
                        foreach (var sensor in hardware.Sensors) {
                            if (!sensor.Value.HasValue) continue;
                            if (sensor.SensorType == SensorType.Temperature && Score(sensor.Name) > best) { best = Score(sensor.Name); temperature = sensor.Value.Value; source = sensor.Name; }
                            if (sensor.SensorType == SensorType.Power && sensor.Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0) power = sensor.Value.Value;
                        }
                    }
                    Write(destination, new XElement("CpuReading", new XElement("TimestampUtc", DateTime.UtcNow.ToString("o")), new XElement("Temperature", temperature), new XElement("Power", power), new XElement("Sensor", source), new XElement("ParentId", parentId)));
                    Thread.Sleep(1000);
                }
            } catch (Exception ex) { try { Write(destination, new XElement("CpuReading", new XElement("TimestampUtc", DateTime.UtcNow.ToString("o")), new XElement("Temperature", -1), new XElement("Power", -1), new XElement("Error", ex.Message))); } catch { } }
            finally { computer.Close(); }
        }
    }
}
