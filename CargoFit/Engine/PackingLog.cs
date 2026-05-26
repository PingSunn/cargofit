using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CargoFit;

/// <summary>
/// Lightweight debug logger for PackingEngine.
/// ใช้งาน:
///   - CLI mode (--input): เปิดอัตโนมัติเสมอ → เขียนไปที่ packing-debug.log ใน CWD
///   - GUI mode: เปิดเมื่อ env var CARGOFIT_DEBUG=1 → เขียนไปที่ packing-debug.log ใน CWD
///   - GUI debug export: bufferOnly=true → จับ log ไว้ใน memory เท่านั้น ไม่สร้างไฟล์
/// </summary>
internal static class PackingLog
{
    private static StreamWriter?  _writer;
    private static StringBuilder  _buffer = new();

    internal static bool IsEnabled { get; private set; }

    /// <summary>
    /// เรียกก่อน PackingEngine.Calculate() ทุกครั้ง
    /// </summary>
    /// <param name="logPath">path เต็มของ log file, null = packing-debug.log ใน CWD</param>
    /// <param name="force">true = เปิด log ไม่ว่า env var จะเป็นอะไร (ใช้ใน CLI mode)</param>
    /// <param name="bufferOnly">true = เปิด log เฉพาะ in-memory buffer, ไม่สร้างไฟล์ (ใช้ใน GUI debug export)</param>
    internal static void Init(string? logPath = null, bool force = false, bool bufferOnly = false)
    {
        _buffer.Clear();
        bool enableFile = force || Environment.GetEnvironmentVariable("CARGOFIT_DEBUG") == "1";
        IsEnabled = enableFile || bufferOnly;
        if (!IsEnabled) return;
        if (!enableFile) return; // bufferOnly: capture to memory only, no disk file

        var path = logPath ?? Path.Combine(Directory.GetCurrentDirectory(), "packing-debug.log");
        _writer = new StreamWriter(path, append: false) { AutoFlush = true };
    }

    internal static void Phase(string name) => Write($"\n=== [{name}] ===");
    internal static void Info(string msg)   => Write($"  {msg}");
    internal static void Blank()            => Write(string.Empty);

    internal static void Finish()
    {
        _writer?.Dispose();
        _writer = null;
        IsEnabled = false;
        // _buffer intentionally NOT cleared — caller reads after Finish()
    }

    /// <summary>คืน log ของ run ล่าสุด (เรียกหลัง Finish())</summary>
    internal static string GetLastRunLog() => _buffer.ToString();

    private static void Write(string msg)
    {
        if (!IsEnabled) return;
        _buffer.AppendLine(msg);      // always capture when enabled
        _writer?.WriteLine(msg);      // only writes if file is open
        Debug.WriteLine($"[PackingLog] {msg}");
    }
}
