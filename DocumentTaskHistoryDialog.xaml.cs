using BlueSapphire.Models;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace BlueSapphire
{
    public sealed partial class DocumentTaskHistoryDialog : ContentDialog
    {
        public DocumentConversionBatchReport? SelectedReport { get; private set; }

        public DocumentTaskHistoryDialog(IReadOnlyList<DocumentConversionBatchReport> reports)
        {
            this.InitializeComponent();
            HistoryList.ItemsSource = reports;
            IsPrimaryButtonEnabled = reports.Count > 0;

            if (reports.Count > 0)
            {
                HistoryList.SelectedIndex = 0;
            }
        }

        private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedReport = HistoryList.SelectedItem as DocumentConversionBatchReport;
            IsPrimaryButtonEnabled = SelectedReport != null;
        }
    }
}
