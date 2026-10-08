using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace PaswordChService
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        private readonly ServiceProcessInstaller _processInstaller;
        private readonly ServiceInstaller _serviceInstaller;

        public ProjectInstaller()
        {
            _processInstaller = new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalSystem,
                Username = null,
                Password = null
            };

            _serviceInstaller = new ServiceInstaller
            {
                ServiceName = PaswordChService.ServiceNameConst,
                DisplayName = "PaswordCh Service",
                Description = "Периодическая смена паролей пользователей (Local/AD) с email-уведомлениями и архивацией.",
                StartType = ServiceStartMode.Automatic,
                DelayedAutoStart = false
            };

            Installers.Add(_processInstaller);
            Installers.Add(_serviceInstaller);
        }
    }
}