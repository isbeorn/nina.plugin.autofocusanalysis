using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;

namespace AutofocusAnalysis.Tests {

    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class DateSelectionBindingTests {
        private const string AllDates = "(All Dates)";

        [TestCase(nameof(DateSelectionModel.SelectedDateFrom))]
        [TestCase(nameof(DateSelectionModel.SelectedDateThru))]
        public void ClearingDateSelection_DoesNotThrow(string selectedDateProperty) {
            var model = new DateSelectionModel();
            var comboBox = CreateBoundComboBox(model, selectedDateProperty);

            Assert.That(comboBox.SelectedItem, Is.EqualTo(AllDates));

            Assert.DoesNotThrow(() => comboBox.SelectedItem = null);
            Assert.That(model.GetSelectedDate(selectedDateProperty), Is.Null);
        }

        [TestCase(nameof(DateSelectionModel.SelectedDateFrom))]
        [TestCase(nameof(DateSelectionModel.SelectedDateThru))]
        public void SelectingDateAndAllDates_UpdatesDateFilter(string selectedDateProperty) {
            var selectedDate = new DateTime(2026, 7, 12);
            var model = new DateSelectionModel();
            var comboBox = CreateBoundComboBox(model, selectedDateProperty);

            comboBox.SelectedItem = selectedDate;

            Assert.That(model.GetSelectedDate(selectedDateProperty), Is.EqualTo(selectedDate));

            comboBox.SelectedItem = AllDates;

            Assert.That(model.GetSelectedDate(selectedDateProperty), Is.Null);
        }

        private static ComboBox CreateBoundComboBox(DateSelectionModel model, string selectedDateProperty) {
            var comboBox = new ComboBox { DataContext = model };

            BindingOperations.SetBinding(
                comboBox,
                ItemsControl.ItemsSourceProperty,
                new Binding(nameof(DateSelectionModel.Dates)) {
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
            var converterType = typeof(global::AutofocusAnalysis.AutofocusAnalysis).Assembly.GetType(
                $"AutofocusAnalysis.{typeName}",
                throwOnError: true);

            return (IValueConverter)Activator.CreateInstance(converterType, nonPublic: true);
        }

        private sealed class DateSelectionModel : INotifyPropertyChanged {
            private DateTime? selectedDateFrom;
            private DateTime? selectedDateThru;

            public event PropertyChangedEventHandler PropertyChanged;

            public IEnumerable<DateTime?> Dates { get; private set; } = new DateTime?[] {
                null,
                new DateTime(2026, 7, 12)
            };

            public DateTime? SelectedDateFrom {
                get => selectedDateFrom;
                set {
                    selectedDateFrom = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDateFrom)));
                }
            }

            public DateTime? SelectedDateThru {
                get => selectedDateThru;
                set {
                    selectedDateThru = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDateThru)));
                }
            }

            public DateTime? GetSelectedDate(string propertyName) {
                return propertyName == nameof(SelectedDateFrom) ? SelectedDateFrom : SelectedDateThru;
            }

        }
    }
}
