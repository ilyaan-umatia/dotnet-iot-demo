using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Device.Gateway.Sparkplug;

public sealed class BirthDeathSequenceStore(string clientId)
{
    // Keep the counter across process restarts; use a safe filename for each client.
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "state",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientId))) + ".txt");

    public ulong ReserveNext()
    {
        var next = File.Exists(_path)
            ? (byte)((byte.Parse(File.ReadAllText(_path), CultureInfo.InvariantCulture) + 1) % 256)
            : (byte)0;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path + ".tmp", next.ToString(CultureInfo.InvariantCulture));
        File.Move(_path + ".tmp", _path, overwrite: true);
        return next;
    }
}