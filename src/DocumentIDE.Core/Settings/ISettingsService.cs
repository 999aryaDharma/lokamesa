using System.Threading.Tasks;

namespace DocumentIDE.Core.Settings;

public interface ISettingsService
{
    Task<AppSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
}
