using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Trazo
{
    internal static class StartupRegistration
    {
        private const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName="Trazo";

        internal static bool IsEnabled()
        {
            string value=ReadCommand();
            return String.Equals(value,BuildCommand(Application.ExecutablePath,true),StringComparison.OrdinalIgnoreCase)||String.Equals(value,BuildCommand(Application.ExecutablePath,false),StringComparison.OrdinalIgnoreCase);
        }
        internal static string ReadCommand()
        {
            using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RunKey,false))
            {
                return key==null?null:key.GetValue(ValueName,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
        }
        internal static void RestoreCommand(string value)
        {
            if(value==null){using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RunKey,true))if(key!=null)key.DeleteValue(ValueName,false);}
            else using(RegistryKey key=Registry.CurrentUser.CreateSubKey(RunKey))key.SetValue(ValueName,value,RegistryValueKind.String);
        }
        internal static void SetEnabled(bool enabled)
        {SetEnabled(enabled,true);}
        internal static void SetEnabled(bool enabled,bool startMinimized)
        {
            if(enabled)
            {
                using(RegistryKey key=Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if(key==null)throw new InvalidOperationException("No se pudo actualizar el inicio de Trazo.");
                    key.SetValue(ValueName,BuildCommand(Application.ExecutablePath,startMinimized),RegistryValueKind.String);
                }
            }
            else
            {
                using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RunKey,true))if(key!=null)key.DeleteValue(ValueName,false);
            }
        }
        internal static string BuildCommand(string executable)
        {return BuildCommand(executable,true);}
        internal static string BuildCommand(string executable,bool startMinimized)
        {
            if(String.IsNullOrWhiteSpace(executable)||executable.IndexOf('"')>=0)throw new ArgumentException("La ruta del programa no es válida.","executable");
            return "\""+executable+"\""+(startMinimized?" --background":String.Empty);
        }
    }
}
