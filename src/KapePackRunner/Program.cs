using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace KapePackRunner;

internal static class Program
{
    public const string Magic = "KAPEPACK";
    private const int FooterSize = 8 + 8 + 8; // zipStart + zipLen + magic

    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            var self = Environment.ProcessPath
                       ?? throw new InvalidOperationException("Не удалось определить путь к EXE.");

            Console.WriteLine("KAPE Pack Runner");
            Console.WriteLine($"EXE: {self}");

            if (!TryReadPayload(self, out var zipStart, out var zipLen))
            {
                Console.Error.WriteLine("Это не автономный пакет KAPE (нет payload KAPEPACK).");
                Console.Error.WriteLine("Соберите пакет через KAPE Pack Builder → «Собрать автономный EXE».");
                Pause();
                return 2;
            }

            var outDir = Path.Combine(
                Path.GetDirectoryName(self)!,
                Path.GetFileNameWithoutExtension(self));

            Console.WriteLine($"Распаковка в: {outDir}");
            Console.WriteLine($"Размер архива: {zipLen:N0} байт");

            if (Directory.Exists(outDir))
            {
                Console.WriteLine("Очистка предыдущей распаковки…");
                Directory.Delete(outDir, true);
            }

            Directory.CreateDirectory(outDir);
            ExtractPayload(self, zipStart, zipLen, outDir);

            var kape = Path.Combine(outDir, "kape.exe");
            var ps1 = Path.Combine(outDir, "run_collection.ps1");
            var bat = Path.Combine(outDir, "run_collection.bat");
            if (!File.Exists(kape))
            {
                Console.Error.WriteLine("В пакете нет kape.exe — автономный запуск невозможен.");
                Console.Error.WriteLine($"Проверьте содержимое: {outDir}");
                Pause();
                return 3;
            }

            Console.WriteLine("Запуск сбора (нужны права администратора)…");

            ProcessStartInfo psi;
            if (File.Exists(ps1))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\"",
                    WorkingDirectory = outDir,
                    UseShellExecute = true,
                    Verb = "runas"
                };
            }
            else if (File.Exists(bat))
            {
                psi = new ProcessStartInfo
                {
                    FileName = bat,
                    WorkingDirectory = outDir,
                    UseShellExecute = true,
                    Verb = "runas"
                };
            }
            else
            {
                Console.Error.WriteLine("В пакете нет run_collection.ps1 / .bat");
                Pause();
                return 4;
            }

            try
            {
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
                return proc?.ExitCode ?? 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UAC/runas не удался ({ex.Message}), обычный запуск…");
                psi.Verb = null;
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
                return proc?.ExitCode ?? 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Ошибка: " + ex.Message);
            Pause();
            return 1;
        }
    }

    public static bool TryReadPayload(string exePath, out long zipStart, out long zipLen)
    {
        zipStart = 0;
        zipLen = 0;
        var fi = new FileInfo(exePath);
        if (fi.Length < FooterSize + 64) return false;

        using var fs = File.OpenRead(exePath);
        fs.Seek(-FooterSize, SeekOrigin.End);
        using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
        zipStart = br.ReadInt64();
        zipLen = br.ReadInt64();
        var magic = Encoding.ASCII.GetString(br.ReadBytes(8));
        if (!string.Equals(magic, Magic, StringComparison.Ordinal)) return false;
        if (zipStart < 0 || zipLen <= 0) return false;
        if (zipStart + zipLen + FooterSize > fi.Length) return false;
        return true;
    }

    private static void ExtractPayload(string exePath, long zipStart, long zipLen, string outDir)
    {
        var tmpZip = Path.Combine(Path.GetTempPath(), "kapepack_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var fs = File.OpenRead(exePath))
            using (var outFs = File.Create(tmpZip))
            {
                fs.Seek(zipStart, SeekOrigin.Begin);
                CopyExactly(fs, outFs, zipLen);
            }

            ZipFile.ExtractToDirectory(tmpZip, outDir, overwriteFiles: true);
        }
        finally
        {
            try { File.Delete(tmpZip); } catch { /* ignore */ }
        }
    }

    private static void CopyExactly(Stream input, Stream output, long count)
    {
        var buffer = new byte[1024 * 256];
        long left = count;
        while (left > 0)
        {
            var n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (n <= 0) throw new EndOfStreamException("Неожиданный конец файла при чтении payload.");
            output.Write(buffer, 0, n);
            left -= n;
        }
    }

    private static void Pause()
    {
        Console.WriteLine("Нажмите Enter для выхода…");
        try { Console.ReadLine(); } catch { /* ignore */ }
    }
}
