using System.IO;
using System.Management;
using System.Text;

namespace Htxb.Launcher;

/// <summary>
/// Hidden background task: collects the machine's unique hardware identifiers
/// (system, motherboard, BIOS, CPU, RAM modules, physical disks) via WMI and
/// stores them in <c>configuration\pc_info.ini</c> next to the EXEs. Runs
/// detached so the launcher UI is never blocked, and never surfaces any UI or
/// error even when WMI is unavailable.
/// </summary>
internal static class PcInfo
{
    public static void RunDetached()
    {
        // Run on a dedicated STA thread: several WMI providers (e.g. the disk
        // provider behind Win32_DiskDrive) are COM apartment-aware and silently
        // fail from MTA thread-pool threads. The thread is backgrounded so a
        // hung WMI call can never keep the launcher alive after it closes.
        try
        {
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    Collect();
                }
                catch
                {
                    // hidden task - never surface
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }
        catch
        {
            // ignore
        }
    }

    private static void Collect()
    {
        var sb = new StringBuilder();
        sb.AppendLine("; TTI HTXB Utilities - unique PC hardware information");
        sb.AppendLine("; Collected automatically each time the Utilities Launcher starts.");
        sb.AppendLine(string.Format("; Generated: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now));
        sb.AppendLine();

        AppendSystem(sb);
        AppendMotherboard(sb);
        AppendBios(sb);
        AppendCpus(sb);
        AppendRam(sb);
        AppendDisks(sb);

        var dir = Path.Combine(AppContext.BaseDirectory, "configuration");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "pc_info.ini");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void AppendSystem(StringBuilder sb)
    {
        var rows = Query("SELECT Manufacturer,Model,SystemFamily,TotalPhysicalMemory FROM Win32_ComputerSystem", "Manufacturer", "Model", "SystemFamily", "TotalPhysicalMemory");
        var product = Query("SELECT UUID,Vendor,Name,IdentifyingNumber FROM Win32_ComputerSystemProduct", "UUID", "Vendor", "Name", "IdentifyingNumber");
        sb.AppendLine("[System]");
        if (rows.Count > 0)
        {
            sb.AppendLine("Manufacturer=" + Clean(rows[0][0]));
            sb.AppendLine("Model=" + Clean(rows[0][1]));
            sb.AppendLine("SystemFamily=" + Clean(rows[0][2]));
            var mem = rows[0][3];
            string memGb = "0";
            if (ulong.TryParse(mem, out var bytes)) memGb = (bytes / 1073741824.0).ToString("0.#");
            sb.AppendLine("TotalPhysicalMemoryGB=" + memGb);
        }
        if (product.Count > 0)
        {
            sb.AppendLine("ProductUUID=" + Clean(product[0][0]));
            sb.AppendLine("ProductVendor=" + Clean(product[0][1]));
            sb.AppendLine("ProductName=" + Clean(product[0][2]));
            sb.AppendLine("ChassisSerial=" + Clean(product[0][3]));
        }
        sb.AppendLine();
    }

    private static void AppendMotherboard(StringBuilder sb)
    {
        var rows = Query("SELECT Manufacturer,Product,Version,SerialNumber FROM Win32_BaseBoard", "Manufacturer", "Product", "Version", "SerialNumber");
        sb.AppendLine("[Motherboard]");
        if (rows.Count > 0)
        {
            sb.AppendLine("Manufacturer=" + Clean(rows[0][0]));
            sb.AppendLine("Product=" + Clean(rows[0][1]));
            sb.AppendLine("Version=" + Clean(rows[0][2]));
            sb.AppendLine("SerialNumber=" + Clean(rows[0][3]));
        }
        else
        {
            sb.AppendLine("Manufacturer=(none)");
            sb.AppendLine("Product=(none)");
            sb.AppendLine("Version=(none)");
            sb.AppendLine("SerialNumber=(none)");
        }
        sb.AppendLine();
    }

    private static void AppendBios(StringBuilder sb)
    {
        var rows = Query("SELECT Manufacturer,SMBIOSBIOSVersion,SerialNumber,ReleaseDate FROM Win32_BIOS", "Manufacturer", "SMBIOSBIOSVersion", "SerialNumber", "ReleaseDate");
        sb.AppendLine("[BIOS]");
        if (rows.Count > 0)
        {
            sb.AppendLine("Manufacturer=" + Clean(rows[0][0]));
            sb.AppendLine("SMBIOSVersion=" + Clean(rows[0][1]));
            sb.AppendLine("SerialNumber=" + Clean(rows[0][2]));
            var release = rows[0][3];
            if (release.Length >= 8)
            {
                var ds = release.Substring(0, 8);
                if (ds.All(char.IsDigit))
                    release = ds.Substring(0, 4) + "-" + ds.Substring(4, 2) + "-" + ds.Substring(6, 2);
            }
            sb.AppendLine("ReleaseDate=" + (release.Length == 0 ? "(none)" : release));
        }
        else
        {
            sb.AppendLine("Manufacturer=(none)");
            sb.AppendLine("SMBIOSVersion=(none)");
            sb.AppendLine("SerialNumber=(none)");
            sb.AppendLine("ReleaseDate=(none)");
        }
        sb.AppendLine();
    }

    private static void AppendCpus(StringBuilder sb)
    {
        var rows = Query("SELECT Manufacturer,Name,ProcessorId,MaxClockSpeed,NumberOfCores,NumberOfLogicalProcessors,DeviceID FROM Win32_Processor", "Manufacturer", "Name", "ProcessorId", "MaxClockSpeed", "NumberOfCores", "NumberOfLogicalProcessors", "DeviceID");
        sb.AppendLine("[CPUs]");
        int n = 0;
        foreach (var r in rows)
        {
            n++;
            sb.AppendLine();
            sb.AppendLine("[CPU" + n + "]");
            sb.AppendLine("DeviceID=" + Clean(r[6]));
            sb.AppendLine("Manufacturer=" + Clean(r[0]));
            sb.AppendLine("Name=" + Clean(r[1]));
            sb.AppendLine("ProcessorId=" + Clean(r[2]));
            sb.AppendLine("MaxClockSpeedMHz=" + Clean(r[3]));
            sb.AppendLine("Cores=" + Clean(r[4]));
            sb.AppendLine("LogicalProcessors=" + Clean(r[5]));
        }
        if (n == 0)
        {
            sb.AppendLine("(no processor information available)");
        }
        sb.AppendLine();
    }

    private static void AppendRam(StringBuilder sb)
    {
        var rows = Query("SELECT DeviceLocator,Manufacturer,PartNumber,SerialNumber,Capacity,Speed FROM Win32_PhysicalMemory", "DeviceLocator", "Manufacturer", "PartNumber", "SerialNumber", "Capacity", "Speed");
        sb.AppendLine("[RAM]");
        int n = 0;
        foreach (var r in rows)
        {
            n++;
            sb.AppendLine();
            sb.AppendLine("[RAM" + n + "]");
            sb.AppendLine("Slot=" + Clean(r[0]));
            sb.AppendLine("Manufacturer=" + Clean(r[1]));
            sb.AppendLine("PartNumber=" + Clean(r[2]));
            sb.AppendLine("SerialNumber=" + Clean(r[3]));
            string capacityGb = "0";
            if (ulong.TryParse(r[4], out var bytes)) capacityGb = (bytes / 1073741824.0).ToString("0.#");
            sb.AppendLine("CapacityGB=" + capacityGb);
            sb.AppendLine("SpeedMHz=" + Clean(r[5]));
        }
        if (n == 0)
        {
            sb.AppendLine("(no memory information available)");
        }
        sb.AppendLine();
    }

    private static void AppendDisks(StringBuilder sb)
    {
        var rows = Query("SELECT * FROM Win32_DiskDrive", "Index", "Manufacturer", "Model", "PartNumber", "SerialNumber", "InterfaceType", "MediaType", "Size");
        sb.AppendLine("[Disks]");
        int n = 0;
        foreach (var r in rows)
        {
            n++;
            sb.AppendLine();
            sb.AppendLine("[Disk" + n + "]");
            sb.AppendLine("Index=" + Clean(r[0]));
            sb.AppendLine("Manufacturer=" + Clean(r[1]));
            sb.AppendLine("Model=" + Clean(r[2]));
            sb.AppendLine("PartNumber=" + Clean(r[3]));
            sb.AppendLine("SerialNumber=" + Clean(r[4]));
            sb.AppendLine("InterfaceType=" + Clean(r[5]));
            sb.AppendLine("MediaType=" + Clean(r[6]));
            string sizeGb = "0";
            if (ulong.TryParse(r[7], out var bytes)) sizeGb = (bytes / 1073741824.0).ToString("0.#");
            sb.AppendLine("SizeGB=" + sizeGb);
        }
        if (n == 0)
        {
            sb.AppendLine("(no disk information available)");
        }
        sb.AppendLine();
    }

    /// <summary>Runs a CIM query (default root\cimv2) and returns each row's requested properties.</summary>
    private static List<string[]> Query(string query, params string[] props)
    {
        var rows = new List<string[]>();
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementBaseObject o in searcher.Get())
            {
                try
                {
                    var values = new string[props.Length];
                    for (int i = 0; i < props.Length; i++)
                    {
                        try { values[i] = Clean(o[props[i]]); }
                        catch { values[i] = "(none)"; /* tolerate lazy provider properties */ }
                    }
                    rows.Add(values);
                }
                catch
                {
                    // skip row
                }
                finally
                {
                    try { o.Dispose(); } catch { /* ignore */ }
                }
            }
        }
        catch
        {
            // WMI unavailable -> empty result
        }
        return rows;
    }

    private static string Clean(object? value)
    {
        var s = value?.ToString()?.Trim() ?? "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length == 0 ? "(none)" : s;
    }
}