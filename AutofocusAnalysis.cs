using AutofocusAnalysis.Properties;
using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.ImageData;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Utility.AutoFocus;
using OxyPlot.Series;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using Settings = AutofocusAnalysis.Properties.Settings;

namespace AutofocusAnalysis {

    /// <summary>
    /// This class exports the IPluginManifest interface and will be used for the general plugin information and options
    /// The base class "PluginBase" will populate all the necessary Manifest Meta Data out of the AssemblyInfo attributes. Please fill these accoringly
    ///
    /// An instance of this class will be created and set as datacontext on the plugin options tab in N.I.N.A. to be able to configure global plugin settings
    /// The user interface for the settings will be defined by a DataTemplate with the key having the naming convention "AutofocusAnalysis_Options" where AutofocusAnalysis corresponds to the AssemblyTitle - In this template example it is found in the Options.xaml
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class AutofocusAnalysis : PluginBase, INotifyPropertyChanged {
        private readonly IPluginOptionsAccessor pluginSettings;
        private readonly IProfileService profileService;
        private string defaultFolder;

        [ImportingConstructor]
        public AutofocusAnalysis(IProfileService profileService, IOptionsVM options, IImageSaveMediator imageSaveMediator) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }

            // This helper class can be used to store plugin settings that are dependent on the current profile
            this.pluginSettings = new PluginOptionsAccessor(profileService, Guid.Parse(this.Identifier));
            this.profileService = profileService;
            // React on a changed profile
            profileService.ProfileChanged += ProfileService_ProfileChanged;

            defaultFolder = pluginSettings.GetValueString("DefaultFolder", Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\NINA\Autofocus"));

            positionFrom = 0;
            positionThrough = 1000000;
            temperatureFrom = -50;
            temperatureThrough = 50;
            rSquaredAbove = 0.7;
            Dates = new AsyncObservableCollection<DateTime?>();
            Filters = new AsyncObservableCollection<string>();
            AutoFocusReports = new AsyncObservableCollection<AutoFocusReport>();
            LoadFolderCommand = new AsyncCommand<bool>(LoadFolder);
        }

        public override Task Teardown() {
            // Make sure to unregister an event when the object is no longer in use. Otherwise garbage collection will be prevented.
            profileService.ProfileChanged -= ProfileService_ProfileChanged;

            return base.Teardown();
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
        }

        public string ProfileSpecificNotificationMessage {
            get {
                return pluginSettings.GetValueString(nameof(ProfileSpecificNotificationMessage), string.Empty);
            }
            set {
                pluginSettings.SetValueString(nameof(ProfileSpecificNotificationMessage), value);
                RaisePropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async Task<bool> LoadFolder(object o) {
            using (var dialog = new FolderBrowserDialog()) {
                if (Directory.Exists(defaultFolder)) {
                    dialog.SelectedPath = defaultFolder;
                }

                if (dialog.ShowDialog() == DialogResult.OK) {
                    SelectedPath = dialog.SelectedPath;
                    defaultFolder = dialog.SelectedPath;
                    pluginSettings.SetValueString("DefaultFolder", SelectedPath);
                    await Task.Run(() => LoadAutofocusReports(SelectedPath));
                }
            }
            return true;
        }

        public ICommand LoadFolderCommand { get; }

        private string selectedPath;

        public string SelectedPath {
            get => selectedPath;
            set {
                selectedPath = value;
                RaisePropertyChanged();
            }
        }

        public AsyncObservableCollection<AutoFocusReport> AutoFocusReports { get; }
        public IEnumerable<AutoFocusReport> FilteredAutoFocusReports { get; private set; }
        private Trendline trend;

        public Trendline Trend {
            get => trend;
            set {
                trend = value;
                RaisePropertyChanged();
            }
        }

        private double temperatureFrom;

        public double TemperatureFrom {
            get => temperatureFrom;
            set {
                temperatureFrom = value;
                RaisePropertyChanged();
                RefreshChart();
            }
        }

        private double temperatureThrough;

        public double TemperatureThrough {
            get => temperatureThrough;
            set {
                temperatureThrough = value;
                RaisePropertyChanged();
                RefreshChart();
            }
        }

        private double positionFrom;

        public double PositionFrom {
            get => positionFrom;
            set {
                positionFrom = value;
                RaisePropertyChanged();
                RefreshChart();
            }
        }

        private double positionThrough;

        public double PositionThrough {
            get => positionThrough;
            set {
                positionThrough = value;
                RaisePropertyChanged();
                RefreshChart();
            }
        }

        private double rSquaredAbove;

        public double RSquaredAbove {
            get => rSquaredAbove;
            set {
                rSquaredAbove = value;
                RaisePropertyChanged();
                RefreshChart();
            }
        }

        private string selectedFilter;

        public string SelectedFilter {
            get => selectedFilter;
            set {
                selectedFilter = value;

                RaisePropertyChanged();
                RefreshChart();
            }
        }

        private DateTime? selectedDateFrom;

        public DateTime? SelectedDateFrom {
            get => selectedDateFrom;
            set {
                selectedDateFrom = value;

                RaisePropertyChanged();
                RefreshChart();
            }
        }

        private DateTime? selectedDateThru;

        public DateTime? SelectedDateThru {
            get => selectedDateThru;
            set {
                selectedDateThru = value;

                RaisePropertyChanged();
                RefreshChart();
            }
        }

        private void RefreshChart() {
            FilteredAutoFocusReports = AutoFocusReports.Where(x => {
                var couldParse = Enum.TryParse(typeof(AFCurveFittingEnum), x.Fitting, out var fitting);
                var goodR2 = true;
                if (couldParse) {
                    var hyperbolicGood = (x.RSquares?.Hyperbolic ?? 0) > RSquaredAbove;
                    var quadraticGood = (x.RSquares?.Quadratic ?? 0) > RSquaredAbove;
                    var trendlineGood = (x.RSquares?.LeftTrend ?? 0) > RSquaredAbove && (x.RSquares?.RightTrend ?? 0) > RSquaredAbove;
                    switch (fitting) {
                        case AFCurveFittingEnum.HYPERBOLIC:
                        case AFCurveFittingEnum.TRENDHYPERBOLIC:
                            goodR2 = hyperbolicGood;
                            break;

                        case AFCurveFittingEnum.PARABOLIC:
                        case AFCurveFittingEnum.TRENDPARABOLIC:
                            goodR2 = quadraticGood;
                            break;

                        case AFCurveFittingEnum.TRENDLINES:
                            goodR2 = trendlineGood;
                            break;
                    }
                } else {
                    Logger.Warning($"Unknown AFCurveFitting value ${x.Fitting}. Ignoring R Square filter");
                }

                return x.Filter == SelectedFilter
                && x.Temperature < TemperatureThrough
                && x.Temperature > TemperatureFrom
                && x.CalculatedFocusPoint.Position < PositionThrough
                && x.CalculatedFocusPoint.Position > PositionFrom
                && goodR2
                && (SelectedDateFrom == null ? true : x.Timestamp.Date >= SelectedDateFrom)
                && (SelectedDateThru == null ? true : x.Timestamp.Date <= SelectedDateThru);
            });

            try {
                Trend = new Trendline(FilteredAutoFocusReports.Select(x => new ScatterErrorPoint(x.Temperature, x.CalculatedFocusPoint.Position, 1, 1)));
            } catch (Exception ex) {
                Trend = null;
            }

            RaisePropertyChanged(nameof(FilteredAutoFocusReports));
        }

        public AsyncObservableCollection<string> Filters { get; }
        public AsyncObservableCollection<DateTime?> Dates { get; private set; }

        public async Task LoadAutofocusReports(string path) {
            AutoFocusReports.Clear();
            Filters.Clear();
            Dates.Clear();
            Dates.Add(null);
            foreach (var file in Directory.GetFiles(path, "*.json")) {
                using (var reader = File.OpenText(file)) {
                    try {
                        var text = await reader.ReadToEndAsync();
                        var report = JsonConvert.DeserializeObject<AutoFocusReport>(text);
                        if (report != null) {
                            if (!Dates.Contains(report.Timestamp.Date)) {
                                Dates.Add(report.Timestamp.Date);
                            }
                            if (!Filters.Contains(report.Filter)) {
                                Filters.Add(report.Filter);
                            }
                            AutoFocusReports.Add(report);
                        }
                    } catch (Exception ex) {
                        Logger.Error($"Failed to load json {file}", ex);
                    }
                    RaisePropertyChanged(nameof(AutoFocusReports));
                }
            }
            RaisePropertyChanged(nameof(Dates));
            SelectedDateFrom = null;
            SelectedDateThru = null;
            SelectedFilter = Filters.FirstOrDefault();
        }
    }

    internal class EnumerableNullReplaceConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            var collection = (IEnumerable)value;

            return
                collection
                .Cast<object>()
                .Select(x => x ?? parameter)
                .ToArray();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }

    internal class NullReplaceConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value ?? parameter;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            return value.Equals(parameter) ? null : value;
        }
    }
}