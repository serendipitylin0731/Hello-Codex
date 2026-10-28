using System.Text.Json;

namespace PlusQuotaLauncher.Core;

public sealed class JsonFileStore<T> where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public JsonFileStore(string path) => _path = path;

    public T Load()
    {
        if (!File.Exists(_path))
            return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(_path), Options) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new LauncherException(
                $"无法读取配置文件：{_path}",
                "检查文件是否损坏或被其他程序占用。可以备份后删除该文件以恢复默认设置。",
                ex);
        }
    }

    public void Save(T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LauncherException(
                $"无法保存文件：{_path}",
                "检查当前用户是否有写入权限，以及安全软件是否阻止了应用。",
                ex);
        }
    }
}
