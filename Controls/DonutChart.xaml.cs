using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;

namespace BlueSapphire.Controls;

public sealed partial class DonutChart : UserControl
{
    private const double Circumference = 2 * Math.PI * 34;

    public DonutChart()
    {
        InitializeComponent();
    }

    public enum DonutDisplayMode
    {
        Idle,
        NoResults,
        Data
    }

    public void SetDisplayMode(DonutDisplayMode mode)
    {
        switch (mode)
        {
            case DonutDisplayMode.Idle:
                DataPanel.Visibility = Visibility.Collapsed;
                IdlePanel.Visibility = Visibility.Visible;
                NoResultsPanel.Visibility = Visibility.Collapsed;
                LegendPanel.Visibility = Visibility.Collapsed;
                NoResultsLabel.Visibility = Visibility.Collapsed;
                SafeRing.StrokeDashArray = new DoubleCollection { 0, Circumference };
                ReviewRing.StrokeDashArray = new DoubleCollection { 0, Circumference };
                ViewOnlyRing.StrokeDashArray = new DoubleCollection { 0, Circumference };
                break;

            case DonutDisplayMode.NoResults:
                DataPanel.Visibility = Visibility.Collapsed;
                IdlePanel.Visibility = Visibility.Collapsed;
                NoResultsPanel.Visibility = Visibility.Visible;
                LegendPanel.Visibility = Visibility.Collapsed;
                NoResultsLabel.Visibility = Visibility.Visible;
                SafeRing.StrokeDashArray = new DoubleCollection { 0, Circumference };
                ReviewRing.StrokeDashArray = new DoubleCollection { 0, Circumference };
                ViewOnlyRing.StrokeDashArray = new DoubleCollection { 0, Circumference };
                break;

            case DonutDisplayMode.Data:
                DataPanel.Visibility = Visibility.Visible;
                IdlePanel.Visibility = Visibility.Collapsed;
                NoResultsPanel.Visibility = Visibility.Collapsed;
                LegendPanel.Visibility = Visibility.Visible;
                NoResultsLabel.Visibility = Visibility.Collapsed;
                break;
        }
    }

    public void Update(long safeBytes, long reviewBytes, long viewOnlyBytes,
                       string totalText, string safeText, string reviewText, string viewOnlyText)
    {
        TotalText.Text = totalText;
        SafeLabel.Text = safeText;
        ReviewLabel.Text = reviewText;
        ViewOnlyLabel.Text = viewOnlyText;

        long total = safeBytes + reviewBytes + viewOnlyBytes;
        if (total <= 0)
        {
            SafeRing.StrokeDashArray = new DoubleCollection { 0, Circumference };
            ReviewRing.StrokeDashArray = new DoubleCollection { 0, Circumference };
            ViewOnlyRing.StrokeDashArray = new DoubleCollection { 0, Circumference };
            return;
        }

        double safeRatio = (double)safeBytes / total;
        double reviewRatio = (double)reviewBytes / total;
        double viewRatio = (double)viewOnlyBytes / total;

        double safeDash = safeRatio * Circumference;
        SafeRing.StrokeDashArray = new DoubleCollection { safeDash, Circumference - safeDash };

        double reviewDash = reviewRatio * Circumference;
        ReviewRing.StrokeDashArray = new DoubleCollection { reviewDash, Circumference - reviewDash };
        ReviewRing.StrokeDashOffset = -safeRatio * Circumference;

        double viewDash = viewRatio * Circumference;
        ViewOnlyRing.StrokeDashArray = new DoubleCollection { viewDash, Circumference - viewDash };
        ViewOnlyRing.StrokeDashOffset = -(safeRatio + reviewRatio) * Circumference;
    }
}
