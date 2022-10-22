using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin. Generate a fresh one for your plugin!
[assembly: Guid("97021132-0C25-4443-B947-FE5EFBE0A3D6")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Autofocus Report Analysis")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Analyse temperature slope of autofocus runs from the autofocus logs")]

// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("Stefan Berg")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Autofocus Report Analysis")]
[assembly: AssemblyCopyright("Copyright © 2022 Stefan Berg")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.0")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://bitbucket.org/Isbeorn/nina.plugin.autofocusanalysis")]

// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "https://bitbucket.org/Isbeorn/nina.plugin.autofocusanalysis")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "Autofocus")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://bitbucket.org/Isbeorn/nina.plugin.autofocusanalysis/CHANGELOG.md")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "https://bitbucket.org/Isbeorn/nina.plugin.autofocusanalysis/downloads/AutofocusReportAnalysisIcon.png")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "https://bitbucket.org/Isbeorn/nina.plugin.autofocusanalysis/downloads/Screenshot1.png")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"This plugin can help you determining the temperature slope and offset of your equipment by analyzing past autofocus reports that were created by N.I.N.A.

Simply hit the button with the three dots and point to the folder that contains your autofocus report files in JSON format.
When a folder is selected the plugin will load in all reports, plot a chart and determine the slope and offset via linear regression.
Further filters can be applied afterwards, like only having a specific date range, temperature range or focuser position range.

*Please note that you should not blindly follow the suggested slope and offset without reviewing how well the data could be fitted. A low R² value for example should not be relied on and it should be as close to 1 as possible.*")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]