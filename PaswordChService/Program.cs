using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace PaswordChService
{
    public class PaswordChService : ServiceBase
    {
        public const string ServiceNameConst = "PaswordChService";

        private Thread _worker;
        private ManualResetEvent _stopEvent;
        private ServiceConfig _config;
        private DateTime _lastLogArchive = DateTime.MinValue;
        private PipeServer _pipeServer;

        public PaswordChService()
        {
            ServiceName = ServiceNameConst;
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                Paths.EnsureDataDirs();
                Logger.Initialize(Paths.LogsDir);

                Logger.Info("Служба PaswordChService запускается...");
                Logger.Info("InstallDir = " + Paths.InstallDir);
                Logger.Info("DataDir    = " + Paths.DataDir);
                Logger.Info("IniPath    = " + Paths.IniPath);

                // Автошифрование открытых паролей в my.ini
                try
                {
                    var sealedCount = ServiceConfig.SealSecrets(Paths.IniPath);
                    if (sealedCount > 0)
                        Logger.Info($"Автоматически зашифровано секретов в my.ini: {sealedCount}");
                }
                catch (Exception ex)
                {
                    Logger.Warn("Не удалось автоматически зашифровать секреты в my.ini: " + ex.Message);
                }

                _config = ServiceConfig.Load(Paths.IniPath);

                // Логгер больше не переключаем — всё пишется в Paths.LogsDir.
                // Значение _config.LogPath используется только для справки
                // (если кто-то задал его вручную в my.ini — программа воспримет это
                //  как настройку логики, но физически каталог логов остаётся Paths.LogsDir).

                Logger.Info($"Конфигурация загружена. Интервал={_config.IntervalDays} дн., " +
                            $"проверка каждые {_config.CheckIntervalMinutes} мин, режим={_config.Mode}");

                _stopEvent = new ManualResetEvent(false);

                _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "PaswordChWorker" };
                _worker.Start();

                _pipeServer = new PipeServer(_config);
                _pipeServer.Start();

                Logger.Info("Служба успешно запущена.");
            }
            catch (Exception ex)
            {
                Logger.Error("Критическая ошибка запуска службы", ex);
                throw;
            }
        }

        protected override void OnStop()
        {
            Logger.Info("Служба останавливается...");
            try { _pipeServer?.Stop(); } catch (Exception ex) { Logger.Error("Pipe stop", ex); }
            try
            {
                _stopEvent?.Set();
                _worker?.Join(TimeSpan.FromSeconds(30));
                Logger.Info("Служба остановлена.");
            }
            catch (Exception ex) { Logger.Error("Ошибка остановки", ex); }
        }

        // ---------------- Счётчик дней ----------------

        private string LastRunFilePath
        {
            get
            {
                var dir = _config != null && !string.IsNullOrEmpty(_config.LogPath)
                    ? _config.LogPath
                    : Paths.LogsDir;
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "lastrun.txt");
            }
        }

        private DateTime GetLastRun()
        {
            try
            {
                var path = LastRunFilePath;
                if (!File.Exists(path)) return DateTime.MinValue;
                var text = File.ReadAllText(path).Trim();
                return DateTime.TryParse(text, out var dt) ? dt : DateTime.MinValue;
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось прочитать lastrun.txt", ex);
                return DateTime.MinValue;
            }
        }

        private bool IsTimeToRun()
        {
            var last = GetLastRun();
            if (last == DateTime.MinValue) return true;
            return (DateTime.Now - last).TotalDays >= _config.IntervalDays;
        }

        private void SaveLastRun(DateTime when)
        {
            try
            {
                File.WriteAllText(LastRunFilePath, when.ToString("o"));
                Logger.Info("Файл lastrun.txt обновлён: " + when.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось записать lastrun.txt", ex);
            }
        }

        private void WorkerLoop()
        {
            var processor = new EventProcessor(_config);

            Logger.Info($"Служба запущена. Смена паролей раз в {_config.IntervalDays} дн. " +
                        $"Проверка — каждые {_config.CheckIntervalMinutes} мин.");

            while (!_stopEvent.WaitOne(0))
            {
                try
                {
                    if (IsTimeToRun())
                    {
                        Logger.Info($"Наступил срок смены паролей (интервал {_config.IntervalDays} дн.). Запускаю цикл.");
                        try
                        {
                            processor.RunOnce();
                            SaveLastRun(DateTime.Now);
                            Logger.Info($"Дата следующей смены: {DateTime.Now.AddDays(_config.IntervalDays):yyyy-MM-dd HH:mm}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Ошибка цикла обработки (продолжаем работу)", ex);
                        }
                    }
                    else
                    {
                        var next = GetLastRun().AddDays(_config.IntervalDays);
                        Logger.Info($"Срок смены ещё не наступил. Следующая попытка не раньше {next:yyyy-MM-dd HH:mm}.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Ошибка в WorkerLoop (продолжаем работу)", ex);
                }

                if ((DateTime.Now - _lastLogArchive).TotalHours >= _config.LogArchiveHours)
                {
                    Logger.ArchiveOldLogs(_config.LogPath, _config.LogArchiveHours);
                    _lastLogArchive = DateTime.Now;
                }

                if (_stopEvent.WaitOne(TimeSpan.FromMinutes(_config.CheckIntervalMinutes)))
                    break;
            }
        }
    }
}