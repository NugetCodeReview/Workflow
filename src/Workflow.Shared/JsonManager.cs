using Newtonsoft.Json;

using Workflow.Shared;

namespace Workflow
{
    public static class JsonManager
    {
        private static Timer? _timer;

        static JsonManager()
        {
            var config = HostAppBuilder.AppHost.Services.GetRequiredService<PackagesConfig>();

            ConfigFolder = new(config.ConfigFolder);

            SetupTime();
            SetupTimer();

            static void TimerAction(object o)
            {
                _timer?.Dispose();

                SetupTime();

                SetupTimer();
            }

            static void SetupTime()
            {
                Today = DateTimeOffset.UtcNow.Date;
                var todayFilename = Path.Combine(
                        ConfigFolder.FullName,
                        $"top-packages-{TodayString}.json");

                TodayFile = new FileInfo(todayFilename);
            }

            static void SetupTimer()
            {
                TimeSpan tillMidnight = DateTime.Now.Date.AddDays(1) - DateTime.Now;
                _timer = new Timer(TimerAction, null, tillMidnight, TimeSpan.Zero);
            }
        }
        public static DirectoryInfo ConfigFolder { get; set; }
        public static FileInfo TodayFile { get; set; }
        public static DateTimeOffset Today { get; set; }
        public static string TodayString => Today.ToString("yyyy-MM-dd");
        public static Task<bool> Save(this IEnumerable<PackageListing> list)
        {
            try
            {
                if (list?.Any() ?? false)
                {
                    return list.SerializeObject(TodayFile.FullName);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Handled in JsonManager.Save()");
                ex.Data.Add(nameof(list), list);
                ex.Data.Add(nameof(TodayFile), TodayFile.FullName);
                throw;
            }

            return Task.FromResult(false);
        }

        public static JsonSerializerSettings JsonSettings { get; set; } =
            new()
            {
                TypeNameHandling = TypeNameHandling.All,
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            };

        public static async Task<TData?> DeserializeObject<TData>(this string filename)
        {
            var file = new FileInfo(filename);

            if (!file.Exists) return default;

            using var stream = file.OpenText();
            var json = await stream.ReadToEndAsync();

            try
            {
                return JsonConvert.DeserializeObject<TData>(json, JsonSettings);
            }
            catch(Exception ex)
            {
                ex.Data.Add(nameof(filename), filename);
                ex.Data.Add(nameof(json), json);
                throw;
            }
        }

        public static async Task<bool> SerializeObject<TData>(this TData data, string filename)
        {
            try
            {
                var json = JsonConvert.SerializeObject(data, JsonSettings);

                var file = new FileInfo(filename);

                if (!file.Directory.Exists)
                {
                    file.Directory.Create();
                }

                using var stream = file.OpenWrite();
                using var writer = new StreamWriter(stream);
                await writer.WriteAsync(json);
                await writer.FlushAsync();
                writer.Close();
                return true;
            }
            catch (Exception ex)
            {
                ex.Data.Add(nameof(data), data);
                ex.Data.Add(nameof(filename), filename);
                throw;
            }
        }
    }
}