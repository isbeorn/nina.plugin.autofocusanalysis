using Moq;
using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Utility.AutoFocus;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace AutofocusAnalysis.Tests {

    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public class AutofocusAnalysisBehaviorTests {
        private const string AllDates = "(All Dates)";
        private global::AutofocusAnalysis.AutofocusAnalysis sut;
        private string testRoot;

        [SetUp]
        public void SetUp() {
            EnsureWpfApplication();
            DisableSettingsUpgrade();

            testRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(AutofocusAnalysisBehaviorTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);

            var profileService = new Mock<IProfileService>();
            var pluginSettings = new PluginSettings();
            profileService
                .SetupGet(service => service.ActiveProfile.PluginSettings)
                .Returns(pluginSettings);

            sut = new global::AutofocusAnalysis.AutofocusAnalysis(
                profileService.Object,
                Mock.Of<IOptionsVM>(),
                Mock.Of<IImageSaveMediator>());
        }

        [TearDown]
        public async Task TearDown() {
            if (sut != null) {
                await sut.Teardown();
            }

            if (!string.IsNullOrWhiteSpace(testRoot) && Directory.Exists(testRoot)) {
                Directory.Delete(testRoot, recursive: true);
            }
        }

        [Test]
        public async Task LoadingValidReports_PopulatesAnalysisState() {
            DateTime firstDate = new DateTime(2026, 7, 12);
            DateTime secondDate = firstDate.AddDays(1);
            await WriteReport("first.json", CreateReport(firstDate, temperature: 10, position: 1000));
            await WriteReport("second.json", CreateReport(secondDate, temperature: 20, position: 1200));

            await LoadReports();

            Assert.That(sut.AutoFocusReports, Has.Count.EqualTo(2));
            Assert.That(sut.Filters, Is.EqualTo(new[] { "L" }));
            Assert.That(sut.Dates, Is.EquivalentTo(new DateTime?[] { null, firstDate, secondDate }));
            Assert.That(sut.SelectedFilter, Is.EqualTo("L"));
            Assert.That(sut.SelectedDateFrom, Is.Null);
            Assert.That(sut.SelectedDateThru, Is.Null);
            Assert.That(sut.FilteredAutoFocusReports.Count(), Is.EqualTo(2));
            Assert.That(sut.Trend, Is.Not.Null);
            Assert.That(sut.Trend.Slope, Is.EqualTo(20).Within(0.000001));
            Assert.That(sut.Trend.Offset, Is.EqualTo(800).Within(0.000001));
            Assert.That(sut.Trend.RSquared, Is.EqualTo(1).Within(0.000001));
        }

        [Test]
        public async Task TemperatureRange_IncludesBothEndpoints() {
            DateTime timestamp = new DateTime(2026, 7, 12);
            await WriteReport("below.json", CreateReport(timestamp, temperature: 9, position: 900));
            await WriteReport("from.json", CreateReport(timestamp, temperature: 10, position: 1000));
            await WriteReport("through.json", CreateReport(timestamp, temperature: 20, position: 1200));
            await WriteReport("above.json", CreateReport(timestamp, temperature: 21, position: 1300));
            await LoadReports();

            sut.TemperatureFrom = 10;
            sut.TemperatureThrough = 20;

            Assert.That(
                sut.FilteredAutoFocusReports.Select(report => report.Temperature),
                Is.EquivalentTo(new[] { 10d, 20d }));
        }

        [Test]
        public async Task PositionRange_IncludesBothEndpoints() {
            DateTime timestamp = new DateTime(2026, 7, 12);
            await WriteReport("below.json", CreateReport(timestamp, temperature: 9, position: 999));
            await WriteReport("from.json", CreateReport(timestamp, temperature: 10, position: 1000));
            await WriteReport("through.json", CreateReport(timestamp, temperature: 20, position: 1200));
            await WriteReport("above.json", CreateReport(timestamp, temperature: 21, position: 1201));
            await LoadReports();

            sut.PositionFrom = 1000;
            sut.PositionThrough = 1200;

            Assert.That(
                sut.FilteredAutoFocusReports.Select(report => report.CalculatedFocusPoint.Position),
                Is.EquivalentTo(new[] { 1000d, 1200d }));
        }

        [Test]
        public async Task LoadingEmptyFolder_ExposesNoTrend() {
            await LoadReports();

            Assert.That(sut.FilteredAutoFocusReports, Is.Empty);
            Assert.That(sut.Trend, Is.Null);
        }

        [Test]
        public async Task LoadingSingleMatchingReport_ExposesNoTrend() {
            await WriteReport(
                "only.json",
                CreateReport(new DateTime(2026, 7, 12), temperature: 10, position: 1000));

            await LoadReports();

            Assert.That(sut.FilteredAutoFocusReports.Count(), Is.EqualTo(1));
            Assert.That(sut.Trend, Is.Null);
        }

        [Test]
        public async Task LoadingFolder_WithLockedFile_ContinuesWithValidReports() {
            await WriteReport(
                "valid.json",
                CreateReport(new DateTime(2026, 7, 12), temperature: 10, position: 1000));
            string lockedFilePath = Path.Combine(testRoot, "locked.json");

            using (FileStream lockedFile = File.Open(
                lockedFilePath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None)) {
                await LoadReports();
            }

            Assert.That(sut.AutoFocusReports, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task LoadingMissingFolder_LeavesStableEmptyState() {
            Directory.Delete(testRoot);

            await LoadReports();

            Assert.That(sut.AutoFocusReports, Is.Empty);
            Assert.That(sut.Filters, Is.Empty);
            Assert.That(sut.Dates, Is.EqualTo(new DateTime?[] { null }));
            Assert.That(sut.SelectedFilter, Is.Null);
            Assert.That(sut.FilteredAutoFocusReports, Is.Empty);
            Assert.That(sut.Trend, Is.Null);
        }

        [Test]
        public async Task LoadingReport_WithoutCalculatedFocusPoint_SkipsInvalidReport() {
            DateTime timestamp = new DateTime(2026, 7, 12);
            AutoFocusReport invalidReport = CreateReport(timestamp, temperature: 10, position: 1000);
            invalidReport.CalculatedFocusPoint = null;
            await WriteReport("invalid.json", invalidReport);
            await WriteReport("valid.json", CreateReport(timestamp, temperature: 20, position: 1200));

            await LoadReports();

            Assert.That(sut.AutoFocusReports, Has.Count.EqualTo(1));
            Assert.That(sut.FilteredAutoFocusReports.Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task LoadingFolder_WithMalformedJson_ContinuesWithValidReports() {
            await File.WriteAllTextAsync(Path.Combine(testRoot, "malformed.json"), "{ not valid json");
            await WriteReport(
                "valid.json",
                CreateReport(new DateTime(2026, 7, 12), temperature: 10, position: 1000));

            await LoadReports();

            Assert.That(sut.AutoFocusReports, Has.Count.EqualTo(1));
            Assert.That(sut.FilteredAutoFocusReports.Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task FilterAndDateRange_SelectOnlyMatchingReportsAndIncludeDateEndpoints() {
            DateTime fromDate = new DateTime(2026, 7, 12);
            DateTime throughDate = fromDate.AddDays(1);
            await WriteReport("before.json", CreateReport(fromDate.AddDays(-1), temperature: 9, position: 900));
            await WriteReport("from.json", CreateReport(fromDate, temperature: 10, position: 1000));
            await WriteReport("through.json", CreateReport(throughDate, temperature: 20, position: 1200));
            await WriteReport("after.json", CreateReport(throughDate.AddDays(1), temperature: 21, position: 1300));
            await WriteReport("other-filter.json", CreateReport(fromDate, temperature: 15, position: 1100, filter: "R"));
            await LoadReports();

            sut.SelectedFilter = "L";
            sut.SelectedDateFrom = fromDate;
            sut.SelectedDateThru = throughDate;

            Assert.That(
                sut.FilteredAutoFocusReports.Select(report => report.Timestamp.Date),
                Is.EquivalentTo(new[] { fromDate, throughDate }));
        }

        [TestCase(AFCurveFittingEnum.HYPERBOLIC)]
        [TestCase(AFCurveFittingEnum.TRENDHYPERBOLIC)]
        [TestCase(AFCurveFittingEnum.PARABOLIC)]
        [TestCase(AFCurveFittingEnum.TRENDPARABOLIC)]
        [TestCase(AFCurveFittingEnum.TRENDLINES)]
        public async Task RSquaredFilter_UsesSelectedFittingAndStrictThreshold(AFCurveFittingEnum fitting) {
            DateTime timestamp = new DateTime(2026, 7, 12);
            await WriteReport(
                "at-threshold.json",
                CreateReportWithFittingScore(timestamp, temperature: 10, position: 1000, fitting, score: 0.9));
            await WriteReport(
                "above-threshold.json",
                CreateReportWithFittingScore(timestamp, temperature: 20, position: 1200, fitting, score: 0.91));
            await LoadReports();

            sut.RSquaredAbove = 0.9;

            Assert.That(
                sut.FilteredAutoFocusReports.Select(report => report.CalculatedFocusPoint.Position),
                Is.EqualTo(new[] { 1200d }));
        }

        [Test]
        public async Task RSquaredFilter_DoesNotRejectGaussianReport() {
            AutoFocusReport report = CreateReport(
                new DateTime(2026, 7, 12),
                temperature: 10,
                position: 1000);
            report.Fitting = "GAUSSIAN";
            report.RSquares = null;
            await WriteReport("gaussian.json", report);
            await LoadReports();

            sut.RSquaredAbove = 0.99;

            Assert.That(sut.FilteredAutoFocusReports.Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task ReloadingFolder_ReplacesPreviousAnalysisState() {
            DateTime firstDate = new DateTime(2026, 7, 12);
            await WriteReport("first.json", CreateReport(firstDate, temperature: 10, position: 1000));
            await LoadReports();

            string secondRoot = Path.Combine(testRoot, "second");
            Directory.CreateDirectory(secondRoot);
            DateTime secondDate = firstDate.AddDays(1);
            await WriteReport(
                secondRoot,
                "second.json",
                CreateReport(secondDate, temperature: 20, position: 1200, filter: "R"));

            await LoadReports(secondRoot);

            Assert.That(sut.AutoFocusReports, Has.Count.EqualTo(1));
            Assert.That(sut.AutoFocusReports.Single().Filter, Is.EqualTo("R"));
            Assert.That(sut.Filters, Is.EqualTo(new[] { "R" }));
            Assert.That(sut.Dates, Is.EquivalentTo(new DateTime?[] { null, secondDate }));
            Assert.That(sut.SelectedFilter, Is.EqualTo("R"));
            Assert.That(sut.FilteredAutoFocusReports.Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task LoadingReportsFromWorkerThread_UpdatesBothDateBindingsWithoutDispatcherException() {
            DateTime timestamp = new DateTime(2026, 7, 12);
            await WriteReport("report.json", CreateReport(timestamp, temperature: 10, position: 1000));
            ComboBox dateFrom = CreateDateComboBox(nameof(sut.SelectedDateFrom));
            ComboBox dateThru = CreateDateComboBox(nameof(sut.SelectedDateThru));
            Exception dispatcherException = null;
            DispatcherUnhandledExceptionEventHandler exceptionHandler = (_, args) => {
                dispatcherException = args.Exception;
                args.Handled = true;
            };
            Dispatcher.CurrentDispatcher.UnhandledException += exceptionHandler;

            try {
                await LoadReports();
            } finally {
                Dispatcher.CurrentDispatcher.UnhandledException -= exceptionHandler;
            }

            Assert.That(dispatcherException, Is.Null);
            Assert.That(sut.SelectedDateFrom, Is.Null);
            Assert.That(sut.SelectedDateThru, Is.Null);
            Assert.That(dateFrom.Items.Cast<object>(), Is.EquivalentTo(new object[] { AllDates, timestamp }));
            Assert.That(dateThru.Items.Cast<object>(), Is.EquivalentTo(new object[] { AllDates, timestamp }));
        }

        private ComboBox CreateDateComboBox(string selectedDateProperty) {
            var comboBox = new ComboBox { DataContext = sut };

            BindingOperations.SetBinding(
                comboBox,
                ItemsControl.ItemsSourceProperty,
                new Binding(nameof(sut.Dates)) {
                    Converter = CreateConverter("EnumerableNullReplaceConverter"),
                    ConverterParameter = AllDates
                });
            BindingOperations.SetBinding(
                comboBox,
                ComboBox.SelectedItemProperty,
                new Binding(selectedDateProperty) {
                    Converter = CreateConverter("NullReplaceConverter"),
                    ConverterParameter = AllDates,
                    Mode = BindingMode.TwoWay
                });

            return comboBox;
        }

        private static IValueConverter CreateConverter(string typeName) {
            Type converterType = typeof(global::AutofocusAnalysis.AutofocusAnalysis).Assembly.GetType(
                $"AutofocusAnalysis.{typeName}",
                throwOnError: true);

            return (IValueConverter)Activator.CreateInstance(converterType, nonPublic: true);
        }

        private async Task LoadReports(string path = null) {
            Task loadTask = Task.Run(() => sut.LoadAutofocusReports(path ?? testRoot));
            await WaitWithDispatcher(loadTask);
        }

        private async Task WriteReport(string fileName, AutoFocusReport report) {
            await WriteReport(testRoot, fileName, report);
        }

        private static async Task WriteReport(string directory, string fileName, AutoFocusReport report) {
            string json = JsonConvert.SerializeObject(report);
            await File.WriteAllTextAsync(Path.Combine(directory, fileName), json);
        }

        private static AutoFocusReport CreateReport(
            DateTime timestamp,
            double temperature,
            double position,
            string filter = "L",
            AFCurveFittingEnum fitting = AFCurveFittingEnum.HYPERBOLIC,
            double hyperbolicRSquared = 0.95,
            double quadraticRSquared = 0.95,
            double leftTrendRSquared = 0.95,
            double rightTrendRSquared = 0.95) {
            return new AutoFocusReport {
                Filter = filter,
                Timestamp = timestamp,
                Temperature = temperature,
                Fitting = fitting.ToString(),
                CalculatedFocusPoint = new FocusPoint {
                    Position = position
                },
                RSquares = new RSquares {
                    Hyperbolic = hyperbolicRSquared,
                    Quadratic = quadraticRSquared,
                    LeftTrend = leftTrendRSquared,
                    RightTrend = rightTrendRSquared
                }
            };
        }

        private static AutoFocusReport CreateReportWithFittingScore(
            DateTime timestamp,
            double temperature,
            double position,
            AFCurveFittingEnum fitting,
            double score) {
            switch (fitting) {
                case AFCurveFittingEnum.HYPERBOLIC:
                case AFCurveFittingEnum.TRENDHYPERBOLIC:
                    return CreateReport(
                        timestamp,
                        temperature,
                        position,
                        fitting: fitting,
                        hyperbolicRSquared: score,
                        quadraticRSquared: 0,
                        leftTrendRSquared: 0,
                        rightTrendRSquared: 0);

                case AFCurveFittingEnum.PARABOLIC:
                case AFCurveFittingEnum.TRENDPARABOLIC:
                    return CreateReport(
                        timestamp,
                        temperature,
                        position,
                        fitting: fitting,
                        hyperbolicRSquared: 0,
                        quadraticRSquared: score,
                        leftTrendRSquared: 0,
                        rightTrendRSquared: 0);

                case AFCurveFittingEnum.TRENDLINES:
                    return CreateReport(
                        timestamp,
                        temperature,
                        position,
                        fitting: fitting,
                        hyperbolicRSquared: 0,
                        quadraticRSquared: 0,
                        leftTrendRSquared: score,
                        rightTrendRSquared: score);

                default:
                    throw new ArgumentOutOfRangeException(nameof(fitting), fitting, null);
            }
        }

        private static void EnsureWpfApplication() {
            if (Application.Current == null) {
                _ = new Application {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
            }
        }

        private static void DisableSettingsUpgrade() {
            Type settingsType = typeof(global::AutofocusAnalysis.AutofocusAnalysis).Assembly.GetType(
                "AutofocusAnalysis.Properties.Settings",
                throwOnError: true);
            object settings = settingsType
                .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            settingsType.GetProperty("UpdateSettings").SetValue(settings, false);
        }

        private static async Task WaitWithDispatcher(Task task) {
            DateTime timeout = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted) {
                DrainDispatcher();
                if (DateTime.UtcNow > timeout) {
                    throw new TimeoutException("Timed out while loading autofocus reports.");
                }

                await Task.Delay(10);
            }

            await task;
            DrainDispatcher();
        }

        private static void DrainDispatcher() {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
