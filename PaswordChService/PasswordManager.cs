using System;
using System.DirectoryServices.AccountManagement;

namespace PaswordChService
{
    /// <summary>Смена паролей: локальные пользователи или Active Directory.</summary>
    public class PasswordManager
    {
        private readonly ServiceConfig _cfg;
        public PasswordManager(ServiceConfig cfg) { _cfg = cfg; }

        public bool ChangePassword(string username, string newPassword, out string error)
        {
            error = null;
            try
            {
                if (string.Equals(_cfg.Mode, "AD", StringComparison.OrdinalIgnoreCase))
                {
                    using (var ctx = new PrincipalContext(ContextType.Domain,
                                                          _cfg.Domain,
                                                          _cfg.AdAdminUser,
                                                          _cfg.AdAdminPassword))
                    using (var user = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, username))
                    {
                        if (user == null) { error = "Пользователь AD не найден"; return false; }
                        user.SetPassword(newPassword);
                        user.Save();
                    }
                }
                else
                {
                    using (var ctx = new PrincipalContext(ContextType.Machine))
                    using (var user = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, username))
                    {
                        if (user == null) { error = "Локальный пользователь не найден"; return false; }
                        user.SetPassword(newPassword);
                        user.Save();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}