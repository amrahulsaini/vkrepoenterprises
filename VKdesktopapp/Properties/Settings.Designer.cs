using System.CodeDom.Compiler;
using System.Configuration;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace VRASDesktopApp.Properties
{
    [CompilerGenerated]
    [GeneratedCode("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "17.1.0.0")]
    internal sealed partial class Settings : ApplicationSettingsBase
    {
        private static Settings defaultInstance = (Settings)Synchronized(new Settings());

        public static Settings Default => defaultInstance;

        [UserScopedSetting]
        [DebuggerNonUserCode]
        [DefaultSettingValue("https://api.characterverse.tech/")]
        public string ApiBaseUrl
        {
            get => (string)this[nameof(ApiBaseUrl)];
            set => this[nameof(ApiBaseUrl)] = value;
        }

        [UserScopedSetting]
        [DebuggerNonUserCode]
        [DefaultSettingValue("")]
        public string Password
        {
            get => (string)this[nameof(Password)];
            set => this[nameof(Password)] = value;
        }

        [UserScopedSetting]
        [DebuggerNonUserCode]
        [DefaultSettingValue("VK Enterprises")]
        public string FirmName
        {
            get => (string)this[nameof(FirmName)];
            set => this[nameof(FirmName)] = value;
        }

        [UserScopedSetting]
        [DebuggerNonUserCode]
        [DefaultSettingValue("VK Maharashtra")]
        public string Address
        {
            get => (string)this[nameof(Address)];
            set => this[nameof(Address)] = value;
        }

        [UserScopedSetting]
        [DebuggerNonUserCode]
        [DefaultSettingValue("9850637363")]
        public string ContactNos
        {
            get => (string)this[nameof(ContactNos)];
            set => this[nameof(ContactNos)] = value;
        }

        [ApplicationScopedSetting]
        [DebuggerNonUserCode]
        [DefaultSettingValue("12")]
        public string ApiKey
        {
            get => (string)this[nameof(ApiKey)];
        }

        [UserScopedSetting]
        [DebuggerNonUserCode]
        [DefaultSettingValue("0")]
        public int FeedbackPortalFirmId
        {
            get => (int)this[nameof(FeedbackPortalFirmId)];
            set => this[nameof(FeedbackPortalFirmId)] = value;
        }

        [UserScopedSetting]
        [DebuggerNonUserCode]
        [DefaultSettingValue("False")]
        public bool AppDeactivated
        {
            get => (bool)this[nameof(AppDeactivated)];
            set => this[nameof(AppDeactivated)] = value;
        }
    }
}
