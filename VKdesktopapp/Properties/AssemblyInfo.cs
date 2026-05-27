using System.Windows;

// File properties (Title, Description, Company, Product, Copyright, version)
// are now sourced from the .csproj <PropertyGroup> + per-agency overrides
// passed by tools/build_wpf_local.py at publish time. See <GenerateAssemblyInfo>
// in VRASDesktopApp.csproj — that flag tells MSBuild to emit those attributes
// for us, so the build can vary the file description per agency without
// hand-editing this file.
//
// ThemeInfo stays here because it isn't one of the auto-generated attributes
// and WPF needs it to locate theme resources.
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]
